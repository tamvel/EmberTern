using System;
using System.Xml;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using EmberTern.App.Licensing;
using EmberTern.App.Localization;
using EmberTern.App.Security;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Core.Connections;
using EmberTern.Core.Settings;
using EmberTern.Firebird;

namespace EmberTern.App;

public class App : Application
{
    // Dark variant is the default (and the name `MainWindow` keeps as the
    // initial assignment when no DataContext is attached yet). The light
    // variant exists so the editor's foregrounds stay readable on the
    // near-white #F3F3F3 background.
    public const string FirebirdSyntaxName = "Firebird SQL";
    public const string FirebirdSyntaxLightName = "Firebird SQL Light";

    private FirebirdConnectionService? _service;
    private TransactionService? _transactionService;
    private LicenseService? _license;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        RegisterFirebirdSyntax();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // DPAPI-backed protector encrypts connection passwords at rest (and
            // migrates any legacy plaintext connections.json on first load).
            var store = new ConnectionProfileStore(DpapiSecretProtector.Create());
            _service = new FirebirdConnectionService();
            // THE user transaction — one, on the data attachment, NOWAIT. (TransactionService now
            // enforces the ReadCommitted/NOWAIT TPB itself, so there is nothing to configure here.)
            _transactionService = new TransactionService(_service);

            // ⭐⭐ THE licence service — one instance, created here, exactly as PreferencesService is (ratified
            // with the user 2026-08-15). One owner of the state, handed to whoever needs it; ⛔ a second
            // instance would be a second answer to "is this copy licensed", and the two would drift the
            // moment one of them installed a file.
            //
            // ⚠ It reads the SAME settings.dat facade every other section uses, for the clock high-water
            // mark only (§16.3) — the licence itself deliberately lives outside settings.dat (§8), so it
            // survives a settings reset and support can copy it.
            _license = new LicenseService(
                LicenseLocation.Default,
                new ApplicationSettingsStore(
                    System.IO.Path.GetDirectoryName(store.FilePath)!, store.Protector));

            // ⚠ Verified BEFORE the window is built, because the verdict decides whether the activation
            // window opens over it. Refresh never throws — a licence problem is a verdict, and startup must
            // survive every one of them.
            _license.Refresh();

            var viewModel = new MainWindowViewModel(store, _service, _transactionService, license: _license);

            // ⭐ Settings Center etap 3 — the theme is read HERE, before the window exists, and applied through
            // the one mapping in ThemePreference. Until this landed the theme was never saved at all (design
            // §2.1: App.axaml hard-codes Dark and the titlebar toggle flipped it in memory only), so "the theme
            // resets on exit" was a missing feature rather than a failing write.
            //
            // ⚠ App.axaml's RequestedThemeVariant="Dark" STAYS, and removing it is the trap. It is the value
            // the framework holds between XAML load and this line; without it that window is
            // ThemeVariant.Default, which follows the OS theme — a silent behaviour change for every existing
            // user that reads exactly like a regression. Dark is also PreferenceOptions.Theme.Default, so a
            // fresh install and the XAML fallback agree.
            ThemePreference.Apply(viewModel.Preferences.Current.Theme);

            // ⭐ The language is applied HERE for the same reason and by the same rule as the theme: one apply
            // point, fed by the one PreferencesService, with every other surface only WRITING the preference.
            // Applied before the window is built so the first frame is already in the chosen language — there
            // is no English flash to hide.
            //
            // ⭐⭐ Note what this position is NOT: it is not an ordering hazard. The restart-only design that
            // preceded the live decision had to settle the language in Program.Main, before Avalonia started,
            // because a `static readonly` string resolves on first touch and one early read would have frozen
            // the session in English. Reading live removed that constraint entirely — anything rendered before
            // this line simply re-reads when it runs.
            Loc.Apply(viewModel.Preferences.Current.Language);

            // ⭐ ONE application point for the theme, for the whole app. The titlebar toggle and the Settings
            // Center radio both only WRITE the preference; this is what paints it. Two apply sites would be two
            // answers to "what does Light mean", and the divergence would show up as a theme that applies from
            // one surface and not the other.
            //
            // ⚠ Both preferences are re-applied on every Changed notification, not only on their own. That is
            // safe by construction — each Apply is a no-op when the value has not moved (Loc.Apply compares the
            // resolved culture and returns without raising anything), so an unrelated save cannot make a
            // capture-once surface rebuild.
            viewModel.Preferences.Changed += (_, _) =>
            {
                ThemePreference.Apply(viewModel.Preferences.Current.Theme);
                Loc.Apply(viewModel.Preferences.Current.Language);
            };

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                // ⭐ The clock high-water mark is recorded on the way out, so a session that ran through
                //   midnight is recorded and moving the clock back afterwards cannot revive an expired
                //   licence (§16.3). ⛔ Advisory only — a failed write is not fatal and nothing blocks on it.
                _license?.RecordClock();
                _transactionService?.Dispose();
                _service?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterFirebirdSyntax()
    {
        RegisterIfMissing(FirebirdSyntaxName, "avares://EmberTern/Assets/FirebirdSql.xshd");
        RegisterIfMissing(FirebirdSyntaxLightName, "avares://EmberTern/Assets/FirebirdSql.Light.xshd");
    }

    private static void RegisterIfMissing(string name, string avaresUri)
    {
        if (HighlightingManager.Instance.GetDefinition(name) is not null)
        {
            return;
        }

        using var stream = AssetLoader.Open(new Uri(avaresUri));
        using var reader = XmlReader.Create(stream);
        var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        HighlightingManager.Instance.RegisterHighlighting(name, new[] { ".sql" }, definition);
    }
}

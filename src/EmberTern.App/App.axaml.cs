using System;
using System.Xml;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using EmberTern.App.Security;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Core.Connections;
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

            var viewModel = new MainWindowViewModel(store, _service, _transactionService);

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

            // ⭐ ONE application point for the theme, for the whole app. The titlebar toggle and the Settings
            // Center radio both only WRITE the preference; this is what paints it. Two apply sites would be two
            // answers to "what does Light mean", and the divergence would show up as a theme that applies from
            // one surface and not the other.
            viewModel.Preferences.Changed += (_, _) =>
                ThemePreference.Apply(viewModel.Preferences.Current.Theme);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            desktop.ShutdownRequested += (_, _) =>
            {
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

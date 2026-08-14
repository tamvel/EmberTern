using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Core.Settings;
using EmberTern.Core.Sql;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The App-side half of the casing settings: the boundary that turns a stored key into a
/// <see cref="FormatterStyle"/>, and the wiring that gets that style to every surface offering Format SQL.
///
/// <para>⚠ <b>The wiring is the part worth testing.</b> The mapping itself is two comparisons; the thing that
/// actually breaks is a Format-SQL surface left on <see cref="FormatterStyle.Default"/> — a setting that works
/// in the SQL Editor and silently does nothing in the Procedure editor, with a green build. That is what
/// <see cref="EveryFormatSqlTab_TakesItsStyleFromTheOnePreferencesService"/> pins.</para>
/// </summary>
public class FormatterStylePreferenceTests
{
    // ─── THE BOUNDARY MAPPING ────────────────────────────────────────────────────────────────

    /// <summary>Each stored key maps to its own case. The keys are Core's
    /// (<c>PreferenceOptions.Casing</c>) — named here rather than spelled as literals, so this test cannot be
    /// the place a second casing vocabulary appears.</summary>
    [Fact]
    public void StoredKeys_MapToTheFormattersOwnCases()
    {
        Assert.Equal(FormatterCase.Lower, FormatterStylePreference.CaseFor(PreferenceOptions.CaseLower));
        Assert.Equal(FormatterCase.Upper, FormatterStylePreference.CaseFor(PreferenceOptions.CaseUpper));
    }

    /// <summary>
    /// Anything unrecognised is Lower, matching <c>PreferenceOptions.Casing.Default</c>.
    /// <para>⚠ A second net, not the primary one: the store normalizes on load, so a bad value should never
    /// reach here. It matters anyway because "unknown → the shipped style" is the only answer that cannot
    /// surprise a user, and because a null slips through any path that constructs <c>Preferences</c> by hand.</para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Sentence")]
    [InlineData("UPPERCASE")]
    public void AnUnrecognisedKey_FallsBackToTheShippedCase(string? key)
    {
        Assert.Equal(FormatterCase.Lower, FormatterStylePreference.CaseFor(key));
    }

    /// <summary>A recognised key is accepted whatever its spelling — the same tolerance
    /// <c>ThemePreference.VariantFor</c> has, and for the same reason (a hand-edited file).</summary>
    [Fact]
    public void ARecognisedKey_IsCaseInsensitive()
    {
        Assert.Equal(FormatterCase.Upper, FormatterStylePreference.CaseFor("upper"));
        Assert.Equal(FormatterCase.Upper, FormatterStylePreference.CaseFor("UPPER"));
    }

    /// <summary>The two axes are read independently — a swap here would be invisible to every symmetric test.</summary>
    [Fact]
    public void TheTwoAxes_AreMappedIndependently()
    {
        var style = FormatterStylePreference.From(new Preferences
        {
            FormatterKeywordCase = PreferenceOptions.CaseUpper,
            FormatterIdentifierCase = PreferenceOptions.CaseLower,
        });

        Assert.Equal(FormatterCase.Upper, style.KeywordCase);
        Assert.Equal(FormatterCase.Lower, style.IdentifierCase);
    }

    /// <summary>A fresh <see cref="Preferences"/> yields exactly the shipped style — the link that makes
    /// "a user who never opens the settings page sees unchanged output" true end to end.</summary>
    [Fact]
    public void FreshPreferences_YieldTheShippedStyle()
    {
        Assert.Equal(FormatterStyle.Default, FormatterStylePreference.From(new Preferences()));
    }

    // ─── THE WIRING ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Every tab kind that offers Format SQL takes its style from the app's ONE
    /// <c>PreferencesService</c> — so changing the setting reaches all six formattable surfaces, not just the
    /// SQL Editor.
    ///
    /// <para>The check is behavioural on purpose: it changes the preference through the service and asserts the
    /// style each tab's editor would use follows. A test that merely asserted "the provider is not null" would
    /// pass on a provider wired to a stale snapshot, which is the failure that actually happens
    /// (apply-on-change means the value moves while tabs are open).</para>
    /// </summary>
    [Fact]
    public void EveryFormatSqlTab_TakesItsStyleFromTheOnePreferencesService()
    {
        InTempDir(dir =>
        {
            using var service = new FirebirdConnectionService();
            var main = new MainWindowViewModel(new ConnectionProfileStore(dir), service);

            var procedure = new ProcedureDetailTabViewModel("P", null, null, null);
            var function = new FunctionDetailTabViewModel("F", null, null, null);
            var trigger = new TriggerDetailTabViewModel("T", null, null, null);
            var view = new ViewDetailTabViewModel("V", null, null, null);
            var package = new PackageDetailTabViewModel("K", null, null, null);

            var obj = new MetadataObject("P", MetadataObjectKind.Procedure);
            WorkspaceTabViewModel.CreateProcedureDetail(main, obj, procedure, null);
            WorkspaceTabViewModel.CreateFunctionDetail(main, obj, function, null);
            WorkspaceTabViewModel.CreateTriggerDetail(main, obj, trigger, null);
            WorkspaceTabViewModel.CreateViewDetail(main, obj, view, null);
            WorkspaceTabViewModel.CreatePackageDetail(main, obj, package, null);

            var providers = new Func<FormatterStyle>[]
            {
                () => main.FormatterStyle,
                procedure.CurrentFormatterStyle,
                function.CurrentFormatterStyle,
                trigger.CurrentFormatterStyle,
                view.CurrentFormatterStyle,
                package.CurrentFormatterStyle,
            };

            // Default state: every surface is on the shipped style.
            Assert.All(providers, p => Assert.Equal(FormatterStyle.Default, p()));

            // Change the preference the way Settings Center does, through the one service.
            main.Preferences.Apply(main.Preferences.Current with
            {
                FormatterKeywordCase = PreferenceOptions.CaseUpper,
                FormatterIdentifierCase = PreferenceOptions.CaseUpper,
            });

            // ⭐ Every surface follows — no captured snapshot anywhere.
            Assert.All(providers, p =>
            {
                Assert.Equal(FormatterCase.Upper, p().KeywordCase);
                Assert.Equal(FormatterCase.Upper, p().IdentifierCase);
            });
        });
    }

    /// <summary>
    /// A view model built WITHOUT the tab factory (every unit test, and any future host) formats in the shipped
    /// style rather than throwing or formatting in some other case. This is <c>Preferences</c>' own
    /// self-sufficiency rule applied one level up: no "nullable meaning unset".
    /// </summary>
    [Fact]
    public void AViewModelBuiltWithoutTheFactory_FormatsInTheShippedStyle()
    {
        Assert.Equal(FormatterStyle.Default, new ProcedureDetailTabViewModel("P", null, null, null).CurrentFormatterStyle());
        Assert.Equal(FormatterStyle.Default, new ViewDetailTabViewModel("V", null, null, null).CurrentFormatterStyle());
        Assert.Equal(FormatterStyle.Default, new PackageDetailTabViewModel("K", null, null, null).CurrentFormatterStyle());
    }

    // ─── THE ANTI-DRIFT GUARD THIS ETAP OWES ─────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Every property of <see cref="Preferences"/> is either rendered by a Settings Center row or recorded
    /// below as deliberately hidden.
    ///
    /// <para><b>Why this guard exists now.</b> Etap 3 relied on <c>Compose</c> building with <c>with</c> so that
    /// an UNRENDERED preference (the formatter's two, at the time) passed through instead of being reset. Etap 4
    /// renders all four, so that invariant momentarily has no subject — which is exactly when someone deletes
    /// the <c>with</c> as redundant, and the next preference added silently stops persisting. This makes the gap
    /// a failing test instead: adding a property to <see cref="Preferences"/> fails here until the author either
    /// gives it a row or records why it has none. Same shape as etap 2's
    /// <c>EveryPreference_IsAccountedForInValidation</c>.</para>
    /// </summary>
    [Fact]
    public void EveryPreference_IsRenderedOrRecordedAsHidden()
    {
        // A preference deliberately without a Settings Center row, and the reason. An entry here is a decision,
        // not a TODO.
        var deliberatelyHidden = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(Preferences.DebuggerIrreversibleWarningAcknowledged)] =
                "Set by ticking 'do not show again' on the warning itself, which is where the user actually "
                + "makes the decision. A Settings Center row would offer to silence a data-safety warning from "
                + "a screen where the risk is not in front of the user — and it would need a way to say 'no, "
                + "ask me again' that the checkbox already gives for free by simply not being ticked.",
        };

        // ⚠ Only PREFERENCE rows map to a property. An ACTION row (etap 5b's Import / export) is a command with
        // nothing stored, and filtering it out here is what keeps PreferencePropertyFor a total function over the
        // rows that DO have a property — so a new preference row still fails below until it is mapped, while a new
        // button does not have to invent a property to satisfy a guard.
        var rendered = SettingsCatalog.Settings
            .Where(s => s.Kind == SettingKind.Preference)
            .Select(s => PreferencePropertyFor(s.Id))
            .ToHashSet(StringComparer.Ordinal);

        // And the action rows are held to their own condition, so "it is an action" cannot become a way to opt a
        // row out of the mapping: an action must have no options and no value to render.
        foreach (var action in SettingsCatalog.Settings.Where(s => s.Kind == SettingKind.Action))
        {
            Assert.Null(action.Options);
            Assert.Null(action.OptionLabels);
        }

        var all = typeof(Preferences)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        var unaccounted = all
            .Where(n => !rendered.Contains(n) && !deliberatelyHidden.ContainsKey(n))
            .ToArray();

        Assert.True(
            unaccounted.Length == 0,
            "Preferences." + string.Join(", Preferences.", unaccounted) +
            " has no Settings Center row and is not recorded as deliberately hidden. Add a row " +
            "(SettingsCatalog + ValueOf + Compose + the XAML block), or record it in this test's " +
            "'deliberatelyHidden' table with the reason.");

        // And the reverse direction: a recorded exemption that no longer names a real property is stale.
        foreach (var stale in deliberatelyHidden.Keys)
        {
            Assert.Contains(stale, all);
        }
    }

    /// <summary>The <see cref="Preferences"/> property a catalog setting id renders. Deliberately explicit, for
    /// the same reason <c>SettingsCenterViewModel.ValueOf</c> is: a reflective mapping would key on a property
    /// NAME and break silently on a rename.</summary>
    private static string PreferencePropertyFor(string settingId) => settingId switch
    {
        SettingsCatalog.SettingTheme => nameof(Preferences.Theme),
        SettingsCatalog.SettingLanguage => nameof(Preferences.Language),
        SettingsCatalog.SettingRestoreWorkspace => nameof(Preferences.RestoreWorkspaceOnStartup),
        SettingsCatalog.SettingProcedureEasyMode => nameof(Preferences.ProcedureEasyModeDefault),
        SettingsCatalog.SettingViewEasyMode => nameof(Preferences.ViewEasyModeDefault),
        SettingsCatalog.SettingTriggerEasyMode => nameof(Preferences.TriggerEasyModeDefault),
        SettingsCatalog.SettingFunctionEasyMode => nameof(Preferences.FunctionEasyModeDefault),
        SettingsCatalog.SettingPreviewRowLimit => nameof(Preferences.PreviewRowLimit),
        SettingsCatalog.SettingFullLoadPromptThreshold => nameof(Preferences.FullLoadPromptThreshold),
        SettingsCatalog.SettingDataPageSize => nameof(Preferences.DataPageSize),
        SettingsCatalog.SettingGridAutoFit => nameof(Preferences.GridAutoFitColumns),
        SettingsCatalog.SettingTabStripMode => nameof(Preferences.TabStripMode),
        SettingsCatalog.SettingTabStripMaxRows => nameof(Preferences.TabStripMaxRows),
        SettingsCatalog.SettingDebuggerIsolation => nameof(Preferences.DebuggerIsolation),
        SettingsCatalog.SettingFormatterKeywordCase => nameof(Preferences.FormatterKeywordCase),
        SettingsCatalog.SettingFormatterIdentifierCase => nameof(Preferences.FormatterIdentifierCase),
        _ => throw new ArgumentOutOfRangeException(
            nameof(settingId), settingId,
            "A new Settings Center row must be mapped to its Preferences property here as well."),
    };

    private static void InTempDir(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));
        try { body(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}

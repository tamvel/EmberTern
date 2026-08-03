using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Settings;

namespace EmberTern.App.Settings;

/// <summary>One category in Settings Center's left-hand list.</summary>
public sealed class SettingsCategoryDescriptor
{
    public SettingsCategoryDescriptor(string id, string title)
    {
        Id = id;
        Title = title;
    }

    public string Id { get; }

    /// <summary>The heading, from <c>UiStrings</c> — searchable, so "general" finds the page as well as its
    /// rows.</summary>
    public string Title { get; }
}

/// <summary>
/// What a catalog row IS — a value the user picks, or something the user does.
///
/// <para>⭐ <b>Added in etap 5b, and the alternative was worse in a specific way.</b> Import / Export are
/// buttons, not preferences: they have no <c>PreferenceOptions</c>, no arm in <c>ValueOf</c> / <c>Compose</c>,
/// and nothing to apply on change. The tempting shape was to leave them out of the catalog entirely — but
/// <b>search reads the catalog</b> (design §5.4), so a row outside it is a row the user cannot find by typing
/// "export", which is exactly the promise search makes. The other tempting shape was to reuse
/// <c>Options == null</c> as the marker; that conflates "not a preference at all" with "a preference that is not
/// enumerated", which is what a future free-text or numeric setting will be (etap 6). Two meanings on one null
/// is how a button and a number end up in the same code path.</para>
/// </summary>
public enum SettingKind
{
    /// <summary>Backed by a property on <c>Preferences</c>. Appears in <c>ValueOf</c> and <c>Compose</c>, and
    /// applies on change.</summary>
    Preference,

    /// <summary>A command the row offers. Carries no value, is never persisted, and must NOT appear in
    /// <c>ValueOf</c> / <c>Compose</c> — but is searchable exactly like any other row.</summary>
    Action,
}

/// <summary>
/// What KIND of value a preference row holds, and therefore which control renders it.
///
/// <para>⭐ <b>Added in etap 6, and it is the distinction §16.2(f) predicted would be needed.</b> That note
/// rejected reusing <c>Options == null</c> as a marker because it conflates "not a preference at all" with "a
/// preference that is not enumerated" — and etap 6 brings both of the latter: five booleans and three numbers.
/// So the two questions stay separate: <see cref="SettingKind"/> answers <i>is this a value or a command</i>,
/// this answers <i>what shape is the value</i>.</para>
///
/// <para>⚠ There is deliberately no <c>Text</c> member. A free-text preference (a font family) has no consumer
/// yet, and a mechanism without one is exactly what gotcha #233 is about.</para>
/// </summary>
public enum SettingValueKind
{
    /// <summary>One of a fixed set of keys, from a <see cref="PreferenceOptionSet"/>. Radio buttons or a
    /// ComboBox; commits on selection.</summary>
    Option,

    /// <summary>On or off. A checkbox; commits on click.</summary>
    Toggle,

    /// <summary>A number bounded by a <see cref="PreferenceRange"/>. A text field; ⚠ commits on <b>blur or
    /// Enter</b>, never per keystroke (design §5.5.1) — see <c>NumericSettingViewModel</c>.</summary>
    Number,
}

/// <summary>
/// One row on a settings page: what it is called, what it is about, whether it is a value or an action, what
/// shape the value has, and (for an enumerated or numeric preference) the Core declaration it draws its legal
/// values from.
/// </summary>
public sealed class SettingDescriptor
{
    public SettingDescriptor(
        string id,
        string categoryId,
        string label,
        string description,
        string keywords,
        PreferenceOptionSet? options = null,
        IReadOnlyDictionary<string, string>? optionLabels = null,
        SettingKind kind = SettingKind.Preference,
        SettingValueKind valueKind = SettingValueKind.Option,
        PreferenceRange? range = null)
    {
        Id = id;
        CategoryId = categoryId;
        Label = label;
        Description = description;
        Keywords = keywords
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Options = options;
        OptionLabels = optionLabels;
        Kind = kind;
        ValueKind = valueKind;
        Range = range;
    }

    public string Id { get; }

    /// <summary>Whether this row is a stored value or a command. See <see cref="SettingKind"/>.</summary>
    public SettingKind Kind { get; }

    public string CategoryId { get; }

    /// <summary>The row's caption.</summary>
    public string Label { get; }

    /// <summary>One sentence under the caption, saying what the setting does.</summary>
    public string Description { get; }

    /// <summary>
    /// Extra search terms — the words a user types when they do not know our label ("colour" for Theme,
    /// "locale" for Language).
    /// <para>⚠ They live in <c>UiStrings</c> like every other user-facing word (architecture rule #6): a search
    /// term is text the user types at the product, not an internal key.</para>
    /// </summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>
    /// The Core option set this setting is validated against, or null for a setting that is not enumerated.
    /// <para>⭐ <b>The UI generates its items from here and never types them in XAML</b> (design §5.2.2). Two
    /// copies of a legal-values list drift in the dangerous direction: a UI offering an option the validator
    /// rejects lets the user pick it, appears to work, and silently reverts on the next load.</para>
    /// </summary>
    public PreferenceOptionSet? Options { get; }

    /// <summary>
    /// Display label per option key. Core owns the keys (they are persisted and validated); App owns the
    /// words. Pinned by a test asserting every key here has a label — otherwise adding an option ships a
    /// blank row.
    /// </summary>
    public IReadOnlyDictionary<string, string>? OptionLabels { get; }

    /// <summary>What shape this row's value has, and therefore which control renders it. See
    /// <see cref="SettingValueKind"/>.</summary>
    public SettingValueKind ValueKind { get; }

    /// <summary>
    /// The Core range a numeric setting is validated against, or null for any other kind.
    /// <para>⭐ <b>The field's own bounds come from here too</b> (design §5.2.2, the numeric case): a field that
    /// accepted a value the store silently clamps on the next load is precisely the drift that rule exists to
    /// prevent — the user types it, it appears to work, and it changes by itself later.</para>
    /// </summary>
    public PreferenceRange? Range { get; }
}

/// <summary>
/// ⭐ The ONE declarative table behind Settings Center: what categories exist, what settings are in them, and
/// how each is searched. Built once at type-init — the <c>CommandCatalog</c> / <c>LanguageConstructCatalog</c>
/// pattern this project reaches for whenever one table has several readers.
///
/// <para>Two readers today: the window's category list and its search box. A third arrives with any surface
/// that has to <i>name</i> a setting.</para>
///
/// <para>⚠ <b>No string literal belongs in the tables below</b> — every word is a <c>UiStrings</c> member and
/// every option key is a <c>PreferenceOptions</c> constant. Pinned by a test, exactly as <c>CommandCatalog</c>'s
/// descriptor table is.</para>
///
/// <para>⚠ <b>A category ships WITH its settings.</b> There is one category here because etap 3 built one
/// complete page; the formatter and the rest arrive with their own etaps. An empty category is
/// indistinguishable from a defect (gotcha #233), and the standing directive (§9.1) is that nothing ships
/// because it might be wanted later.</para>
/// </summary>
public static class SettingsCatalog
{
    public const string CategoryGeneral = "general";
    public const string CategoryEditor = "editor";
    public const string CategoryGrid = "grid";
    public const string CategoryTabs = "tabs";
    public const string CategoryDebugger = "debugger";
    public const string CategoryFormatter = "formatter";

    public const string SettingTheme = "general.theme";
    public const string SettingLanguage = "general.language";
    public const string SettingRestoreWorkspace = "general.restoreWorkspace";
    public const string SettingImportExport = "general.importExport";
    public const string SettingProcedureEasyMode = "editor.procedureEasyMode";
    public const string SettingViewEasyMode = "editor.viewEasyMode";
    public const string SettingTriggerEasyMode = "editor.triggerEasyMode";
    public const string SettingFunctionEasyMode = "editor.functionEasyMode";
    public const string SettingPreviewRowLimit = "editor.previewRowLimit";
    public const string SettingFullLoadPromptThreshold = "editor.fullLoadPromptThreshold";
    public const string SettingDataPageSize = "grid.dataPageSize";
    public const string SettingGridAutoFit = "grid.autoFitColumns";
    public const string SettingTabStripMode = "tabs.stripMode";
    public const string SettingTabStripMaxRows = "tabs.stripMaxRows";
    public const string SettingDebuggerIsolation = "debugger.isolation";
    public const string SettingFormatterKeywordCase = "formatter.keywordCase";
    public const string SettingFormatterIdentifierCase = "formatter.identifierCase";

    static SettingsCatalog()
    {
        Categories =
        [
            new SettingsCategoryDescriptor(CategoryGeneral, UiStrings.SettingsCategoryGeneral),
            new SettingsCategoryDescriptor(CategoryEditor, UiStrings.SettingsCategoryEditor),
            new SettingsCategoryDescriptor(CategoryGrid, UiStrings.SettingsCategoryGrid),
            new SettingsCategoryDescriptor(CategoryTabs, UiStrings.SettingsCategoryTabs),
            new SettingsCategoryDescriptor(CategoryDebugger, UiStrings.SettingsCategoryDebugger),
            new SettingsCategoryDescriptor(CategoryFormatter, UiStrings.SettingsCategoryFormatter),
        ];

        Settings =
        [
            new SettingDescriptor(
                SettingTheme,
                CategoryGeneral,
                UiStrings.SettingsThemeLabel,
                UiStrings.SettingsThemeDescription,
                UiStrings.SettingsThemeKeywords,
                PreferenceOptions.Theme,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PreferenceOptions.ThemeDark] = UiStrings.SettingsThemeDark,
                    [PreferenceOptions.ThemeLight] = UiStrings.SettingsThemeLight,
                }),

            new SettingDescriptor(
                SettingLanguage,
                CategoryGeneral,
                UiStrings.SettingsLanguageLabel,
                UiStrings.SettingsLanguageDescription,
                UiStrings.SettingsLanguageKeywords,
                PreferenceOptions.Language,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PreferenceOptions.LanguageEnglish] = UiStrings.SettingsLanguageEnglish,
                }),

            // §7.5. ⚠ Gates RESTORE, never CAPTURE — the workspace keeps being saved either way, so turning
            // this back on restores the last session rather than whichever session last had it on. And it gates
            // the TABS only: a connection's saved queries live in the same stored workspace and are the user's
            // own content.
            new SettingDescriptor(
                SettingRestoreWorkspace,
                CategoryGeneral,
                UiStrings.SettingsRestoreWorkspaceLabel,
                UiStrings.SettingsRestoreWorkspaceDescription,
                UiStrings.SettingsRestoreWorkspaceKeywords,
                valueKind: SettingValueKind.Toggle),

            // ⚠ An ACTION row, not a preference: two buttons and a folder shortcut, with nothing stored. It is
            // in this table anyway because SEARCH reads this table — typing "export" or "backup" has to land
            // here, and a row outside the catalog is invisible to it (design §5.4). Hence SettingKind.
            new SettingDescriptor(
                SettingImportExport,
                CategoryGeneral,
                UiStrings.SettingsImportExportLabel,
                UiStrings.SettingsImportExportDescription,
                UiStrings.SettingsImportExportKeywords,
                kind: SettingKind.Action),

            // ── Editor ──────────────────────────────────────────────────────────────────────────────────
            //
            // §7.6, four rows. They replace four WorkspaceState flags that were written by whatever editor the
            // user last toggled — an invisible side effect that made "this procedure opened in Easy mode" look
            // like a bug. Toggling a mode in an editor is now a per-tab action only.
            new SettingDescriptor(
                SettingProcedureEasyMode,
                CategoryEditor,
                UiStrings.SettingsProcedureEasyModeLabel,
                UiStrings.SettingsEditorModeDescription,
                UiStrings.SettingsEditorModeKeywords,
                valueKind: SettingValueKind.Toggle),

            new SettingDescriptor(
                SettingViewEasyMode,
                CategoryEditor,
                UiStrings.SettingsViewEasyModeLabel,
                UiStrings.SettingsEditorModeDescription,
                UiStrings.SettingsEditorModeKeywords,
                valueKind: SettingValueKind.Toggle),

            new SettingDescriptor(
                SettingTriggerEasyMode,
                CategoryEditor,
                UiStrings.SettingsTriggerEasyModeLabel,
                UiStrings.SettingsEditorModeDescription,
                UiStrings.SettingsEditorModeKeywords,
                valueKind: SettingValueKind.Toggle),

            new SettingDescriptor(
                SettingFunctionEasyMode,
                CategoryEditor,
                UiStrings.SettingsFunctionEasyModeLabel,
                UiStrings.SettingsEditorModeDescription,
                UiStrings.SettingsEditorModeKeywords,
                valueKind: SettingValueKind.Toggle),

            // §7.2. ⚠ FullSafetyCeiling is deliberately absent (ratified Q9): it is a memory backstop, and a
            // user who raised it to 50 M would get an out-of-memory crash instead of a truncated grid.
            new SettingDescriptor(
                SettingPreviewRowLimit,
                CategoryEditor,
                UiStrings.SettingsPreviewRowLimitLabel,
                UiStrings.SettingsPreviewRowLimitDescription,
                UiStrings.SettingsPreviewRowLimitKeywords,
                valueKind: SettingValueKind.Number,
                range: PreferenceOptions.PreviewRowLimit),

            new SettingDescriptor(
                SettingFullLoadPromptThreshold,
                CategoryEditor,
                UiStrings.SettingsFullLoadPromptLabel,
                UiStrings.SettingsFullLoadPromptDescription,
                UiStrings.SettingsFullLoadPromptKeywords,
                valueKind: SettingValueKind.Number,
                range: PreferenceOptions.FullLoadPromptThreshold),

            // ── Grid ────────────────────────────────────────────────────────────────────────────────────
            //
            // §7.7. ⚠ The label names the two grids on purpose: this is the page size of the SERVER-PAGED data
            // grids, which is what Q9 admits. The SQL editor's results and the Procedure / Function exec grids
            // page an already-materialized result in memory and keep their own constant.
            new SettingDescriptor(
                SettingDataPageSize,
                CategoryGrid,
                UiStrings.SettingsDataPageSizeLabel,
                UiStrings.SettingsDataPageSizeDescription,
                UiStrings.SettingsDataPageSizeKeywords,
                valueKind: SettingValueKind.Number,
                range: PreferenceOptions.DataPageSize),

            // §7.4 — the last hard-coded layout default in GridLayoutBehavior. A grid the user has already
            // adjusted keeps its own stored GridProfile; this decides what an unadjusted one does.
            new SettingDescriptor(
                SettingGridAutoFit,
                CategoryGrid,
                UiStrings.SettingsGridAutoFitLabel,
                UiStrings.SettingsGridAutoFitDescription,
                UiStrings.SettingsGridAutoFitKeywords,
                valueKind: SettingValueKind.Toggle),

            // ── Tabs ────────────────────────────────────────────────────────────────────────────────────
            //
            // §8.2, ratified D5–D8. ⭐ Own category rather than two more rows in General (user, 2026-08-03):
            // the tab strip is one of the four PERSISTENT surfaces (§0.1), and this category is also where
            // M3.3c's "Tab settings…" context-menu item will jump.
            //
            // ⚠ The row order is deliberate: the MODE decides whether the row limit means anything, so it
            // comes first and the limit reads as its detail.
            new SettingDescriptor(
                SettingTabStripMode,
                CategoryTabs,
                UiStrings.SettingsTabStripModeLabel,
                UiStrings.SettingsTabStripModeDescription,
                UiStrings.SettingsTabStripModeKeywords,
                PreferenceOptions.TabStripMode,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PreferenceOptions.TabStripModeMultiRow] = UiStrings.SettingsTabStripModeMultiRow,
                    [PreferenceOptions.TabStripModeSingleRow] = UiStrings.SettingsTabStripModeSingleRow,
                }),

            // ⚠⚠ HIDDEN in single-row mode — ratified by the user (2026-08-03), and it OVERTURNS this etap's
            // first decision, which kept the row visible on the grounds that the value survives a mode round
            // trip. The user's rule is better: the interface does not show settings that do nothing in the
            // current mode. ⭐ The VALUE is still kept — it is the ROW that disappears, not the number.
            // The condition lives on the page (`SettingsCenterViewModel.ShowTabStripMaxRows`), because it is
            // an AND with the search filter's own visibility and neither may overwrite the other.
            new SettingDescriptor(
                SettingTabStripMaxRows,
                CategoryTabs,
                UiStrings.SettingsTabStripMaxRowsLabel,
                UiStrings.SettingsTabStripMaxRowsDescription,
                UiStrings.SettingsTabStripMaxRowsKeywords,
                valueKind: SettingValueKind.Number,
                range: PreferenceOptions.TabStripMaxRows),

            // ── Debugger ────────────────────────────────────────────────────────────────────────────────
            //
            // §7.3, closing a recorded D4 UX wish ("move transaction-isolation config to global Settings").
            // ⚠ The launch panel's per-launch selector STAYS; this is only the value it opens with.
            new SettingDescriptor(
                SettingDebuggerIsolation,
                CategoryDebugger,
                UiStrings.SettingsDebuggerIsolationLabel,
                UiStrings.SettingsDebuggerIsolationDescription,
                UiStrings.SettingsDebuggerIsolationKeywords,
                PreferenceOptions.DebuggerIsolation,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [PreferenceOptions.DebuggerIsolationReadCommitted] = UiStrings.DebuggerIsolationReadCommitted,
                    [PreferenceOptions.DebuggerIsolationSnapshot] = UiStrings.DebuggerIsolationSnapshot,
                }),

            // ⚠ Both formatter rows draw on the SAME Core option set (PreferenceOptions.Casing) — two
            // preferences over one declared vocabulary, which is why "Upper" cannot come to mean one thing for
            // keywords and another for identifiers. The labels are shared for the same reason.
            new SettingDescriptor(
                SettingFormatterKeywordCase,
                CategoryFormatter,
                UiStrings.SettingsFormatterKeywordCaseLabel,
                UiStrings.SettingsFormatterKeywordCaseDescription,
                UiStrings.SettingsFormatterKeywordCaseKeywords,
                PreferenceOptions.Casing,
                CasingLabels),

            new SettingDescriptor(
                SettingFormatterIdentifierCase,
                CategoryFormatter,
                UiStrings.SettingsFormatterIdentifierCaseLabel,
                UiStrings.SettingsFormatterIdentifierCaseDescription,
                UiStrings.SettingsFormatterIdentifierCaseKeywords,
                PreferenceOptions.Casing,
                CasingLabels),
        ];
    }

    /// <summary>The labels for <c>PreferenceOptions.Casing</c>, shared by both formatter rows. ⚠ Rendered as
    /// <c>lower case</c> / <c>UPPER CASE</c> deliberately: the label demonstrates the option instead of merely
    /// naming it, which is the shortest possible explanation of what the setting does.</summary>
    private static readonly IReadOnlyDictionary<string, string> CasingLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PreferenceOptions.CaseLower] = UiStrings.SettingsCaseLower,
            [PreferenceOptions.CaseUpper] = UiStrings.SettingsCaseUpper,
        };

    public static IReadOnlyList<SettingsCategoryDescriptor> Categories { get; }

    public static IReadOnlyList<SettingDescriptor> Settings { get; }

    public static IEnumerable<SettingDescriptor> SettingsIn(string categoryId)
        => Settings.Where(s => string.Equals(s.CategoryId, categoryId, StringComparison.Ordinal));

    /// <summary>
    /// Settings search: a plain case-insensitive <b>substring</b> test.
    ///
    /// <para>⚠ <b>This is deliberately NOT <c>CompletionMatcher</c>, and the note is here so nobody "unifies"
    /// them.</b> Completion is a <i>prediction</i> engine whose ratified philosophy is prefix-first with no
    /// <c>Contains</c> fallback — typing <c>cont</c> must not offer every object containing "contractor".
    /// Settings search is a <i>search</i> engine, where finding "Theme" by typing "eme" is the entire point.
    /// Sharing one implementation would have to break one of the two.</para>
    /// </summary>
    public static bool Matches(string haystack, string term)
        => string.IsNullOrWhiteSpace(term)
           || haystack.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);
}

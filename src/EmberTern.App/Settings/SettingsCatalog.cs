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
/// One setting: what it is called, what it is about, and (for an enumerated one) which Core option set it
/// draws its legal values from.
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
        IReadOnlyDictionary<string, string>? optionLabels = null)
    {
        Id = id;
        CategoryId = categoryId;
        Label = label;
        Description = description;
        Keywords = keywords
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Options = options;
        OptionLabels = optionLabels;
    }

    public string Id { get; }

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
    public const string CategoryFormatter = "formatter";

    public const string SettingTheme = "general.theme";
    public const string SettingLanguage = "general.language";
    public const string SettingFormatterKeywordCase = "formatter.keywordCase";
    public const string SettingFormatterIdentifierCase = "formatter.identifierCase";

    static SettingsCatalog()
    {
        Categories =
        [
            new SettingsCategoryDescriptor(CategoryGeneral, UiStrings.SettingsCategoryGeneral),
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

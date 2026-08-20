using System;
using System.Globalization;
using System.Resources;

namespace EmberTern.LicenseManager.Localization;

/// <summary>
/// The License Manager's ONE resolver of a localized string: key → text in the language selected now.
///
/// <para>⭐⭐ <b>A mirror of EmberTern's <c>Loc</c>, and the fourth such mirror in this application</b> —
/// <c>IconGeometryConverter</c>, <c>ThemeToggleIconConverter</c> and <c>MenuIconExtension</c> came the same
/// way, for the same reason (decision D‑1 of L8, and the same call as D‑7 = B in §49.1: <c>EmberTern.App</c>
/// stays closed). ⛔ The product's version cannot be referenced or source-linked: it depends on
/// <c>EmberTern.Core.Localization</c>, on <c>Core.Settings.PreferenceOptions</c> and on
/// <c>EmberTern.App.UiStrings</c> — linking it would drag the whole product in.</para>
///
/// <para>⭐⭐ <b>The language changes LIVE.</b> Nothing here caches a resolved string, and nothing may:
/// <see cref="Text(string)"/> is a lookup performed at the moment of the call, so a C# read after a language
/// change returns the new language, and XAML reaches it through <see cref="LocalizationSource"/> +
/// <c>{lm:Loc}</c>, which is a real binding and re-evaluates on notification.</para>
///
/// <para>⚠⚠ <b>The three member shapes differ in a way that decides whether this works at all</b>, and this
/// application already paid for the lesson once (<c>ManagerSettingsCatalog</c>'s class comment records
/// EmberTern's frozen settings vocabulary):</para>
/// <list type="bullet">
/// <item><c>const</c> — inlined by the compiler; after the build there is no field left to resolve. ⛔</item>
/// <item><c>static readonly</c> — resolved ONCE at type initialization, then frozen in the first
/// language. ⛔ Renders correctly, which is what makes it dangerous.</item>
/// <item><c>static string X =&gt; Loc.Text(nameof(X))</c> — resolved at every read. ✅ This one.</item>
/// </list>
///
/// <para>⚠ A <b>default parameter value</b> is a fourth shape and behaves like <c>const</c>: the compiler
/// copies it into every CALLER, so no lookup can ever reach it. ⛔ Never give a method a defaulted string
/// parameter carrying words.</para>
///
/// <para>⛔ <b>The language comes from the preference and from nowhere else.</b> Nothing here reads
/// <c>CurrentUICulture</c>, an environment variable or the operating system: <see cref="Apply"/> is called
/// by the composition root with the stored value, and that is the only entry point.</para>
/// </summary>
internal static class Loc
{
    /// <summary>
    /// The shipped catalog. ⚠ The manifest name is a STRING — a typo here is silent until the first read,
    /// which is why <c>TheEnglishResourceSet_Loads</c> exists.
    /// </summary>
    private static readonly ResourceManager ShippedCatalog =
        new("EmberTern.LicenseManager.Localization.Strings", typeof(Loc).Assembly);

    private static ResourceManager _catalog = ShippedCatalog;

    private static CultureInfo _culture = CultureInfo.InvariantCulture;

    private static string? _ruleSet;

    /// <summary>The culture the words are being resolved in right now.</summary>
    /// <remarks>
    /// ⚠ Starts at <see cref="CultureInfo.InvariantCulture"/>, which resolves to the neutral (English) set.
    /// So anything rendered before the composition root calls <see cref="Apply"/> is already in English, and
    /// simply re-reads when the stored preference is applied. ⭐ That is why there is no ordering hazard
    /// here and no need to settle the language before the UI framework starts.
    /// </remarks>
    public static CultureInfo Culture => _culture;

    /// <summary>
    /// Raised when the language actually changed — for consumers that CAPTURED text instead of binding it.
    /// </summary>
    /// <remarks>
    /// ⚠ Only on a REAL change: <see cref="Apply"/> compares the resolved culture first, so writing any
    /// other preference cannot make a capture-once surface rebuild.
    /// </remarks>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// Switches the language. ⭐ The ONE place a language is applied, called by the composition root.
    /// </summary>
    /// <param name="languageKey">
    /// A code from <see cref="Settings.ApplicationLanguages"/>. An unknown or missing value resolves to the
    /// default rather than throwing.
    /// </param>
    public static void Apply(string? languageKey)
    {
        var culture = LanguagePreference.CultureFor(languageKey);
        if (Equals(culture, _culture))
        {
            return;
        }

        _culture = culture;
        _ruleSet = null;
        LocalizationSource.InvalidateAll();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// The text for <paramref name="key"/>, in the language selected right now.
    /// </summary>
    /// <remarks>
    /// ⚠ Returns the KEY when the catalog has no entry, and deliberately does not throw: a missing word
    /// must not take down a window. ⭐ The build-time answer is
    /// <c>NoLocKeyInXaml_IsMissingFromTheCatalog</c> and <c>EveryLocalizedMember_MatchesItsEnglishEntry</c>;
    /// this is the runtime one.
    /// </remarks>
    public static string Text(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _catalog.GetString(key, _culture) ?? key;
    }

    /// <summary>The text for <paramref name="key"/>, or <see langword="null"/> when there is no entry.</summary>
    /// <remarks>⭐ For <see cref="LocalizedString"/>, which needs to tell "missing" from "resolved".</remarks>
    internal static string? Find(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _catalog.GetString(key, _culture);
    }

    /// <summary>
    /// The text for <paramref name="key"/> with <paramref name="arguments"/> substituted.
    /// </summary>
    /// <remarks>
    /// <para>⭐ When <paramref name="count"/> is given, the key resolves to a FAMILY of variants suffixed
    /// with a CLDR plural category — see <see cref="PluralRules"/>. ⛔ The producer never states whether a
    /// sentence needs plural forms: that is a fact about the LANGUAGE, so English may hold a flat entry
    /// where Polish declares three variants of the same key, and neither has to know what the other did.</para>
    /// <para>⚠ Arguments are formatted under <see cref="Culture"/> — the language the reader is reading —
    /// rather than under the machine's culture. ⛔ A value that is an ECHO of a technical field (a file
    /// version, an iteration count) must be formatted invariantly by its producer and handed in as a
    /// string, so that no format specifier in a resource value can touch it.</para>
    /// </remarks>
    public static string Format(string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(arguments);

        var format = Text(key);
        return arguments.Length == 0 ? format : string.Format(_culture, format, arguments);
    }

    /// <summary>
    /// The text for a COUNTED key: the variant matching <paramref name="count"/> in the rendered language.
    /// </summary>
    /// <remarks>
    /// ⚠ The count is always argument <c>{0}</c>, read in ONE place. Two readers asking "where is the
    /// number" in two ways is how a dual form drifts.
    /// </remarks>
    public static string FormatCount(string key, long count, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(arguments);

        var format = CountedFormat(key, count);

        var all = new object?[arguments.Length + 1];
        all[0] = count;
        arguments.CopyTo(all, 1);

        return string.Format(_culture, format, all);
    }

    /// <summary>
    /// Resolves a counted key to its variant, degrading in three steps and never throwing.
    /// </summary>
    /// <remarks>
    /// exact category → <c>other</c> (CLDR's own catch-all) → the flat key. ⭐ The build-time answer is
    /// <c>EveryPluralFamily_IsCompleteInEveryShippedCulture</c>; this is the runtime one, for the same
    /// reason <see cref="Text(string)"/> returns the key rather than throwing.
    /// </remarks>
    private static string CountedFormat(string key, long count)
    {
        var category = PluralRules.CategoryFor(RuleSet, count);

        return _catalog.GetString(key + "." + PluralRules.SuffixFor(category), _culture)
            ?? _catalog.GetString(key + "." + PluralRules.SuffixFor(PluralCategory.Other), _culture)
            ?? Text(key);
    }

    /// <summary>
    /// Which grammar the rendered language uses, declared by the catalog itself.
    /// </summary>
    /// <remarks>
    /// ⛔ A rule set names a GRAMMAR (<c>one-other</c>, <c>one-few-many</c>), never a language: several
    /// languages share one shape, so a language-shaped name would be false at the second consumer and would
    /// recreate the per-language branch that <c>NoCode_BranchesOnAParticularLanguage</c> forbids, one layer
    /// further out.
    /// </remarks>
    private static string RuleSet =>
        _ruleSet ??= _catalog.GetString(PluralRules.RuleSetKey, _culture) ?? PluralRules.Fallback;

    /// <summary>
    /// ⛔ TESTS ONLY. Installs a different catalog, so liveness can be measured with one shipped language.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ Without this the central claim of the whole design — "a bound string re-reads when the language
    /// changes" — is UNMEASURABLE: with only English, a live binding and a frozen one render identical
    /// text. ⛔ Never called from the application; ⛔ never ship a pseudo-language to make it measurable.
    /// </remarks>
    internal static void UseCatalogForVerification(ResourceManager? catalog, CultureInfo? culture)
    {
        _catalog = catalog ?? ShippedCatalog;
        _culture = culture ?? CultureInfo.InvariantCulture;
        _ruleSet = null;
        LocalizationSource.InvalidateAll();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// ⛔ TESTS ONLY. Detaches every <see cref="LanguageChanged"/> subscriber for the scope's lifetime.
    /// </summary>
    /// <remarks>
    /// ⚠ <see cref="LanguageChanged"/> is process-global static state, and a view model built by an earlier
    /// test stays subscribed. EmberTern's suite hid two defects for months behind exactly this
    /// (`IsolatesGlobalLanguageState`); the cheap answer here is that a test which switches languages runs
    /// with a clean subscriber list and restores the previous one.
    /// </remarks>
    internal static IDisposable IsolateSubscribersForVerification()
    {
        var saved = LanguageChanged;
        LanguageChanged = null;
        return new SubscriberScope(saved);
    }

    private sealed class SubscriberScope(EventHandler? saved) : IDisposable
    {
        public void Dispose() => LanguageChanged = saved;
    }
}

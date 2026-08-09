using System;
using System.Globalization;
using System.Resources;
using EmberTern.Core.Localization;

namespace EmberTern.App.Localization;

/// <summary>
/// The application's ONE resolver of a localized string: key → text in the language selected right now.
///
/// <para><b>Decision D‑1 (ratified 2026-08-09): the language changes LIVE.</b> Nothing here caches a resolved
/// string, and nothing may: <see cref="Text(string)"/> is a lookup performed at the moment of the call, so a
/// C# read after a language change returns the new language, and XAML reaches it through
/// <see cref="LocalizationSource"/> + <c>{app:Loc}</c>, which is a real binding and re-evaluates on
/// notification.</para>
///
/// <para>⭐ <b>The live decision REMOVED a hazard rather than adding one.</b> The earlier restart-only design
/// had to settle the language in <c>Program.Main</c>, before Avalonia started, because
/// <c>static readonly</c> members resolve at type initialization and a single early string read would have
/// frozen the session in English — silently, with a green build. Reading live has no such ordering: whatever
/// is on screen before the preference is applied simply re-reads when it is. So the wiring now sits beside
/// the theme's (<c>App.OnFrameworkInitializationCompleted</c>), which is also the only place that knows the
/// single <see cref="Settings.PreferencesService"/>.</para>
///
/// <para><b>Decision D‑2 (.resx / ResourceManager).</b> The words live in <c>Localization/Strings.resx</c>
/// (English, neutral) and, from the translation stage, in satellite files per culture. Nothing in this class
/// knows which languages exist: <see cref="LanguagePreference"/> turns the stored preference into a culture
/// and <see cref="ResourceManager"/> does the rest, including falling back to English for any entry a
/// translation has not covered. ⛔ There is therefore no place here for <c>language == "pl"</c>, and there
/// must never be one.</para>
///
/// <para>⚠ <b>What a binding cannot reach.</b> Text that C# captures ONCE — a tab header assigned on open, a
/// grid column built in code-behind, a completion row — will not change by itself, because nothing re-reads
/// it. Those surfaces subscribe to <see cref="LanguageChanged"/> and rebuild. That event is the seam for
/// exactly that class of consumer and for nothing else; a surface whose text comes from XAML needs no
/// subscription and must not take one.</para>
/// </summary>
internal static class Loc
{
    // The manifest name is <RootNamespace>.<folder>.<file>: EmberTern.App + Localization + Strings.
    // ⚠ It is a STRING, so a moved, renamed or wrongly-built .resx compiles perfectly and fails only when the
    // first lookup runs — as a MissingManifestResourceException in the user's hands. `TheEnglishResourceSet_Loads`
    // exists for that: a missing registration is silent, one layer further out than a missing value (#348).
    private static readonly ResourceManager ShippedCatalog =
        new("EmberTern.App.Localization.Strings", typeof(Loc).Assembly);

    private static ResourceManager _catalog = ShippedCatalog;
    private static CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// The language rendered right now. <see cref="CultureInfo.InvariantCulture"/> until <see cref="Apply"/>
    /// runs, which reads the neutral (English) resources — so the untouched state is English rather than
    /// "undefined", and a headless test or a design-time load needs no bootstrap at all.
    /// </summary>
    public static CultureInfo Culture => _culture;

    /// <summary>
    /// Raised after the language changes, for the consumers a binding cannot reach: anything that captured
    /// text once and must rebuild.
    ///
    /// <para>⚠ It fires only on a REAL change. Re-applying the same language raises nothing, so a surface may
    /// rebuild itself in the handler without worrying that an unrelated preference save will make it
    /// discard user state (every <c>PreferencesService.Changed</c> notification reaches this method, not just
    /// the language ones).</para>
    /// </summary>
    public static event EventHandler? LanguageChanged;

    /// <summary>
    /// Adopts the stored language for the whole application.
    /// </summary>
    /// <param name="languageKey">
    /// <see cref="Core.Settings.Preferences.Language"/> as read from <c>settings.dat</c>. Null, empty or
    /// unrecognised lands on English through <see cref="LanguagePreference.CultureFor"/> — the single
    /// fallback, shared with every other preference.
    /// </param>
    public static void Apply(string? languageKey)
    {
        var culture = LanguagePreference.CultureFor(languageKey);
        if (Equals(culture, _culture))
        {
            return;
        }

        _culture = culture;
        // Order matters: repaint the bound surfaces first, then let the capture-once consumers rebuild, so a
        // rebuild that reads a bound control cannot read the previous language.
        LocalizationSource.InvalidateAll();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// The text for <paramref name="key"/> in the current language.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>A key with no English entry returns the key itself</b> rather than throwing or returning empty.
    /// A missing entry is a build-time defect that <c>EveryLocalizedMember_MatchesItsEnglishEntry</c> catches,
    /// so it should be unreachable — and if it ever is reached, a visible key is diagnosable where an empty
    /// label is a blank control nobody can report and an exception is a crash inside a string lookup.
    /// ⛔ Do not "improve" this into a throw: failing to find a word must never end a session.
    /// </remarks>
    public static string Text(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _catalog.GetString(key, _culture) ?? key;
    }

    /// <summary>The text for a key produced by Core or Firebird (decision D‑3).</summary>
    public static string Text(MessageKey key) => Text(key.Value);

    /// <summary>
    /// Resolves a <see cref="LocalizableMessage"/> and substitutes its data.
    ///
    /// <para>⚠ Formatting uses <see cref="CultureInfo.CurrentCulture"/>, not <see cref="Culture"/>, and the
    /// difference is deliberate: <see cref="Culture"/> chooses the <i>words</i>, while numbers and dates
    /// follow the reader's machine — the convention the app already holds (<c>DateTimeDisplay</c>, ~30
    /// existing <c>string.Format(CultureInfo.CurrentCulture, …)</c> call sites). Merging the two would
    /// silently re-decide date and number presentation as a side effect of picking a language.</para>
    /// </summary>
    public static string Format(LocalizableMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var format = Text(message.Key);
        if (message.Arguments.Count == 0)
        {
            return format;
        }

        var arguments = new object?[message.Arguments.Count];
        for (var i = 0; i < arguments.Length; i++)
        {
            arguments[i] = message.Arguments[i];
        }

        return string.Format(CultureInfo.CurrentCulture, format, arguments);
    }

    /// <summary>
    /// ⚠ <b>Verification seam — swaps the catalog and the culture, and nothing in the product calls it.</b>
    ///
    /// <para>It exists because the mechanism's central claim — <i>a language change re-reads every bound
    /// string</i> — cannot be measured with one shipped language: with only English, a "changed" binding and a
    /// frozen one produce identical text, so a test would be green either way. That is the failure gotcha
    /// #336 describes (a guard must compute with the engine the product renders with) turned around: here the
    /// engine is fine and the DATA is what makes the observation impossible.</para>
    ///
    /// <para>⭐ The substitute catalog lives in the TEST assembly, so no pseudo-language ships. ⛔ Do not call
    /// this from product code and do not widen it into a plugin mechanism; when a real second language exists
    /// the liveness tests keep using it anyway, because a test must be able to flip languages without
    /// depending on which ones happen to be shipped.</para>
    /// </summary>
    internal static void UseCatalogForVerification(ResourceManager? catalog, CultureInfo? culture)
    {
        _catalog = catalog ?? ShippedCatalog;
        _culture = culture ?? CultureInfo.InvariantCulture;
        LocalizationSource.InvalidateAll();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }
}

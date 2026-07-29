using System;
using System.Collections.Generic;

namespace EmberTern.Core.Settings;

/// <summary>
/// An enumerated preference's legal values together with its default, as ONE object.
/// <para>
/// The pairing is the point. A list of legal values and a default declared separately are two facts that
/// can disagree, and the disagreement is invisible: a default outside its own option set is normalized
/// away on every load, so the preference would appear to reset itself for no reason anybody can see. Here
/// the constructor rejects that combination outright.
/// </para>
/// <para>
/// ⚠ The default is passed <b>explicitly</b> rather than taken as the first value on purpose. "The first
/// item is the default" is invisible at the call site, and these lists are the ones the UI renders — the
/// day languages are sorted alphabetically or Light is listed first, a positional convention would move
/// the default silently.
/// </para>
/// </summary>
public sealed class PreferenceOptionSet
{
    public PreferenceOptionSet(IReadOnlyList<string> values, string @default)
    {
        if (values is null || values.Count == 0)
        {
            throw new ArgumentException("An option set needs at least one value.", nameof(values));
        }

        var found = false;
        foreach (var value in values)
        {
            if (string.Equals(value, @default, StringComparison.Ordinal))
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            // Loud and immediate (type-init of PreferenceOptions), because the alternative is a preference
            // that quietly refuses to hold its own default.
            throw new ArgumentException(
                $"The default '{@default}' is not one of the option set's values.", nameof(@default));
        }

        Values = values;
        Default = @default;
    }

    /// <summary>The legal values, in the order Core declares them. The UI generates its items from this
    /// (§5.2.2) — it never types them again in XAML.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <summary>The value a fresh <see cref="Preferences"/> carries, and the one an unrecognised stored
    /// value normalizes to.</summary>
    public string Default { get; }

    /// <summary>
    /// Brings a stored value into a usable shape — <b>always</b>, and silently (§5.2.1/2).
    /// <para>
    /// A recognised value is returned in the catalog's own spelling, so <c>"dark"</c> from a hand-edited
    /// file becomes <c>"Dark"</c> rather than being thrown away: correcting a value the user clearly meant
    /// is normalization, resetting it would be data loss with extra steps. Anything unrecognised — including
    /// null, blank, and a code from a build that knew more values than this one — becomes
    /// <see cref="Default"/>.
    /// </para>
    /// <para>This never throws and never refuses. A settings file is state to be made usable, not a
    /// document to be validated at the user.</para>
    /// </summary>
    public string Normalize(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return Default;
        }

        var trimmed = stored.Trim();
        foreach (var value in Values)
        {
            if (string.Equals(value, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return Default;
    }
}

/// <summary>
/// ⭐ The ONE place an enumerated preference's legal values and default are declared (design §5.2.2).
/// <para>
/// <b>Three readers, one table.</b> <see cref="Preferences"/> takes each property's initializer from here,
/// <see cref="PreferencesStore.Validate"/> normalizes against here, and the Settings Center UI generates
/// its ComboBox / radio items from here. A second copy anywhere else drifts in the dangerous direction: a
/// UI offering an option the validator rejects lets the user pick it, appears to work, and silently reverts
/// on the next load — nothing fails. This is the same answer this codebase already gives for commands
/// (<c>CommandCatalog</c>) and language constructs (<c>LanguageConstructCatalog</c>): one declarative
/// table, many readers, plus a test.
/// </para>
/// <para>
/// <b>Layer split, so this does not collide with architecture rule #6:</b> Core owns the option
/// <i>keys</i>, because they are validated and persisted. App owns each option's <i>display label</i>,
/// which is UI text and belongs in <c>UiStrings</c>. The two are bound by a test asserting every key here
/// has a label there — otherwise adding an option ships a blank row.
/// </para>
/// <para>
/// <b>Why the values are strings and not enums</b> — the durable reason, not architecture rule #1 (which is
/// refutable here: three Core enums are already persisted in this same file through
/// <c>JsonStringEnumConverter</c>). <c>JsonStringEnumConverter</c> <b>throws</b> on a name it does not
/// know, and in this codebase that failure is total: JsonException → <c>Corrupt</c> →
/// <c>ExistingFileBlocksSave</c> → <c>Save</c> refuses, so one unknown value makes the <i>whole</i>
/// settings.dat unreadable and unwritable — connections, passwords, saved queries, workspace and watches.
/// A string in the same position normalizes to its default and everything else survives.
/// </para>
/// <para>
/// ⚠ <b>The general rule that follows, and it is worth more than the decision:</b> adding a <i>value</i> to
/// a persisted enum is NOT an additive change, even though adding a <i>property</i> is. A build that writes
/// a new enum member produces a file every older build must reject in full.
/// </para>
/// </summary>
public static class PreferenceOptions
{
    // ---- Theme -----------------------------------------------------------------------------------------
    //
    // ⚠ Dark is the default because App.axaml hard-codes RequestedThemeVariant="Dark" today, and a stored
    // value must reproduce that rather than change it. Removing that hard-coded value without a stored one
    // in hand yields ThemeVariant.Default, which follows the OS theme — a silent behaviour change for every
    // existing user that reads exactly like a regression.

    public const string ThemeDark = "Dark";
    public const string ThemeLight = "Light";

    public static PreferenceOptionSet Theme { get; } =
        new(new[] { ThemeDark, ThemeLight }, @default: ThemeDark);

    // ---- Language — the language catalog ---------------------------------------------------------------
    //
    // ⭐ ONE ROW TODAY, AND THAT IS THE WHOLE POINT. The Language preference is real storage over a real
    // catalog whose legal set happens to have one member — not a stub, not a disabled control. Adding
    // Polish is one row HERE plus its own localization milestone; no window change, no view-model change,
    // no binding change.
    //
    // ⚠ EmberTern is NOT prepared for localization, measured: 1 815 `public const string` members in
    // UiStrings (a const is inlined by the compiler, so there is no field left at runtime to reassign),
    // 42 .axaml files on {x:Static}, zero .resx. So this catalog is deliberately storage-and-validation
    // ONLY. Do not "prepare" by introducing a CultureInfo, a resource lookup for a handful of strings, or
    // a localization markup extension used in one window — that leaves the app with two string mechanisms
    // and 1 815 consts still inlined, which is the worst of both and exactly the parallel implementation
    // the reuse rules forbid.

    public const string LanguageEnglish = "en";

    public static PreferenceOptionSet Language { get; } =
        new(new[] { LanguageEnglish }, @default: LanguageEnglish);

    // ---- Formatter casing ------------------------------------------------------------------------------
    //
    // Keywords and identifiers are two preferences over ONE option set — the same two values, declared
    // once. Should they ever need different sets (an identifier-only "Preserve", say), splitting this into
    // two sets is a one-line change; declaring them twice now would be a second copy from day one.
    //
    // ⚠ For whoever implements the formatter etap: map these keys onto the formatter's own style type at
    // the boundary. Do NOT introduce a second list of casing names next to a FormatterStyle enum — the
    // persisted vocabulary is here, and one responsibility has one owner.

    public const string CaseLower = "Lower";
    public const string CaseUpper = "Upper";

    /// <summary>Legal values for both formatter casing preferences. <c>Lower</c> is the default so shipped
    /// formatter output stays byte-identical to today's.</summary>
    public static PreferenceOptionSet Casing { get; } =
        new(new[] { CaseLower, CaseUpper }, @default: CaseLower);

    /// <summary>
    /// Every option set declared here, so a test can hold all of them to the same invariants without a
    /// hand-maintained list going stale beside them.
    /// </summary>
    public static IReadOnlyList<PreferenceOptionSet> All { get; } = new[] { Theme, Language, Casing };
}

using System;
using System.Collections.Generic;
using EmberTern.Core.Query;

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
/// A numeric preference's legal bounds together with its default, as ONE object — the sibling of
/// <see cref="PreferenceOptionSet"/> for a value that is a number rather than a choice.
///
/// <para><b>Why a number needs this at all.</b> §5.2.1/2 makes normalization <i>silent and total</i>: every
/// field is valid when <c>Load</c> returns, whatever was on disk. An enumerated preference normalizes against
/// its option set; a numeric one has no option set, and "carry it over untouched" is not an answer here — a
/// hand-edited or imported <c>0</c> row limit would make the SQL editor return nothing, and a page size of
/// <c>-1</c> would break pagination arithmetic. The bounds ARE that preference's legal set, stated once.</para>
///
/// <para>⚠ <b>Out of range clamps; it never resets to the default.</b> A stored <c>50 000 000</c> means the
/// user wanted "as many as possible", and answering that with <c>5 000</c> would be data loss with extra
/// steps — the same reasoning that makes <see cref="PreferenceOptionSet.Normalize"/> correct <c>"dark"</c> to
/// <c>"Dark"</c> rather than throwing it away (§12.2).</para>
///
/// <para>⭐ <b>The UI reads <see cref="Minimum"/> / <see cref="Maximum"/> from here too</b> (§5.2.2), so the
/// bounds a field advertises and the bounds the store enforces cannot drift — which is the numeric form of the
/// drift that rule exists to prevent: a field that accepts a value the store silently corrects on the next
/// load, with nothing failing.</para>
/// </summary>
public sealed class PreferenceRange
{
    public PreferenceRange(int minimum, int maximum, int @default)
    {
        if (minimum > maximum)
        {
            throw new ArgumentException($"Minimum {minimum} is above maximum {maximum}.", nameof(minimum));
        }

        if (@default < minimum || @default > maximum)
        {
            // Loud and immediate (type-init of PreferenceOptions), for the same reason PreferenceOptionSet
            // rejects a default outside its own values: the alternative is a preference that quietly refuses
            // to hold its own default, which reads to the user as a setting that resets itself.
            throw new ArgumentException(
                $"The default {@default} is outside the range [{minimum}, {maximum}].", nameof(@default));
        }

        Minimum = minimum;
        Maximum = maximum;
        Default = @default;
    }

    public int Minimum { get; }

    public int Maximum { get; }

    /// <summary>The value a fresh <see cref="Preferences"/> carries.</summary>
    public int Default { get; }

    /// <summary>
    /// Brings a stored number into range — <b>always</b>, and silently. Never throws, never refuses.
    /// </summary>
    public int Normalize(int stored) => stored < Minimum ? Minimum : stored > Maximum ? Maximum : stored;

    /// <summary>Whether <paramref name="candidate"/> would survive <see cref="Normalize"/> unchanged. The UI
    /// uses it to tell "the user typed something out of range" from "the user typed the stored value".</summary>
    public bool Contains(int candidate) => candidate >= Minimum && candidate <= Maximum;
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

    // ---- Debugger transaction isolation (etap 6 / §7.3) ------------------------------------------------
    //
    // ⚠ The KEYS are this catalog's, deliberately spelled the way the launch panel already speaks rather
    // than as the Firebird layer's enum member names — mapping a stored key to DebugIsolation is an App-side
    // boundary job (DebuggerIsolationPreference), exactly as a casing key becomes a FormatterCase at the
    // boundary and never inside Core's formatter (§14.4a/2). Core has no opinion about FbTransactionOptions.
    //
    // ⚠ The debugger's own per-launch selector STAYS. This is the value the launch panel OPENS with, which is
    // what the recorded D4 wish asked for; it is read once when a debugger tab is built, never afterwards, so
    // changing the setting cannot move a selector a user has already touched.

    public const string DebuggerIsolationReadCommitted = "ReadCommitted";
    public const string DebuggerIsolationSnapshot = "Snapshot";

    public static PreferenceOptionSet DebuggerIsolation { get; } =
        new(new[] { DebuggerIsolationReadCommitted, DebuggerIsolationSnapshot },
            @default: DebuggerIsolationReadCommitted);

    // ---- Execution row limits (etap 6 / §7.2) ----------------------------------------------------------
    //
    // ⭐ The defaults are ExecutionDefaults' own constants, not copies of them. That class was written for
    // this sprint — "they live here (never as scattered literals) so that moving them into user settings
    // later […] is a one-line change at the call site" — so the shipped value stays declared exactly once and
    // a user who never opens the settings page gets byte-identical behaviour.
    //
    // ⚠ FullSafetyCeiling is deliberately NOT configurable (ratified Q9). It is a memory backstop, not a
    // preference: a user who raises it to 50 M gets an out-of-memory crash instead of a truncated grid, so
    // configuring the safety limit defeats it. It appears below only as the ceiling of the two that ARE
    // configurable, which is what keeps "the soft threshold sits below the hard ceiling" true by construction
    // rather than by a comment.

    public static PreferenceRange PreviewRowLimit { get; } =
        new(minimum: 1,
            maximum: (int)ExecutionDefaults.FullSafetyCeiling,
            @default: ExecutionDefaults.PreviewLimit);

    /// <summary>Row count at which a Full load stops to ask "keep loading?". Its maximum is one below
    /// <c>ExecutionDefaults.FullSafetyCeiling</c>, so the invariant <c>soft &lt; ceiling</c> — which
    /// <c>ExecutionModesTests</c> pins for the shipped values — cannot be broken by a setting.</summary>
    public static PreferenceRange FullLoadPromptThreshold { get; } =
        new(minimum: 1,
            maximum: (int)ExecutionDefaults.FullSafetyCeiling - 1,
            @default: (int)ExecutionDefaults.FullSoftThreshold);

    // ---- Table / View data page size (etap 6 / §7.7) ---------------------------------------------------
    //
    // ⚠ SCOPE, stated because it is narrower than "page size" sounds: this is the page size of the two
    // SERVER-PAGED data grids — Table Data and View Data — which is exactly what ratified Q9 admits. The
    // three client-side RESULT grids (the SQL editor's results, Procedure and Function exec results) page an
    // already-materialized, already-capped result set in memory and keep their own constant; they answer a
    // different question and are not this setting's subject. The setting's label says so.
    //
    // ⭐ The numbers move HERE from TableDetailTabViewModel / ViewDetailTabViewModel, which declared them
    // twice; those two now read this range so the value and its ceiling exist once.

    public static PreferenceRange DataPageSize { get; } =
        new(minimum: 1, maximum: 1000, @default: 200);

    // ---- Workspace tab strip (M3.3b / product-polish §8.2) ---------------------------------------------
    //
    // ⭐ Both values are RATIFIED in the design (D5/D7): two modes, MultiRow by default; 1–10 rows, 3 by
    // default. They are not open questions and this catalog is where the ratified numbers live once.
    //
    // ⚠ The KEYS are this catalog's own, spelled the way the design speaks — the same boundary rule as the
    // casing and debugger-isolation keys: Core stores a string, and turning it into whatever the view layer
    // needs is an App-side mapping. Core has no opinion about WrapPanel or ScrollViewer.
    //
    // ⚠ Additive: `UserSettings.CurrentSchemaVersion` STAYS 2 (R‑4). A bump trips downgrade protection and
    // older builds would then refuse the whole settings file, not just these two rows.

    public const string TabStripModeMultiRow = "MultiRow";
    public const string TabStripModeSingleRow = "SingleRow";

    public static PreferenceOptionSet TabStripMode { get; } =
        new(new[] { TabStripModeMultiRow, TabStripModeSingleRow },
            @default: TabStripModeMultiRow);

    /// <summary>
    /// Rows the multi-row tab strip may grow to before it scrolls (§8.2, ratified 1–10 default 3).
    /// </summary>
    /// <remarks>
    /// ⚠ The minimum is <b>1</b>, not 2, and that is deliberate rather than a loose bound: one row of a
    /// MULTI-row strip is not the same thing as <c>SingleRow</c> mode — it still wraps and scrolls
    /// vertically and still hides nothing behind a menu, whereas SingleRow scrolls sideways and moves the
    /// overflow into a list. A user who wants "one row, nothing hidden" has to be able to say so.
    /// ⚠ The maximum is 10 because the strip is chrome: eleven rows of tabs is not a tab strip any more,
    /// it is a document list, and §8.5 forbids the chrome growing without limit.
    /// </remarks>
    public static PreferenceRange TabStripMaxRows { get; } =
        new(minimum: 1, maximum: 10, @default: 3);

    /// <summary>
    /// Every option set declared here, so a test can hold all of them to the same invariants without a
    /// hand-maintained list going stale beside them.
    /// </summary>
    public static IReadOnlyList<PreferenceOptionSet> All { get; } =
        new[] { Theme, Language, Casing, DebuggerIsolation, TabStripMode };

    /// <summary>Every numeric range declared here, for the same reason as <see cref="All"/>.</summary>
    public static IReadOnlyList<PreferenceRange> AllRanges { get; } =
        new[] { PreviewRowLimit, FullLoadPromptThreshold, DataPageSize, TabStripMaxRows };
}

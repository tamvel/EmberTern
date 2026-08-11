using EmberTern.Core.Localization;

namespace EmberTern.Core.Diagnostics;

/// <summary>
/// The message keys <see cref="SessionHealthAnalyzer"/> produces — decision <b>D‑3</b>'s first producer in
/// Core.
///
/// <para>⭐ <b>The key is the contract; the words are App's.</b> This class is <c>public</c> for the same
/// reason <c>ImportDiagnosticCode</c> is: what Core publishes is the identity of a message, and a consumer
/// (or a test) must be able to name it. The English text lives in <c>App/Localization/Strings.resx</c> under
/// exactly these keys, and <c>EveryCoreMessageKey_HasAnEnglishEntry</c> binds the two — it arms itself off
/// these very fields, so a key added here without an entry fails the build.</para>
///
/// <para>⚠ <b>Why several keys where one interpolation used to do.</b> The analyzer chose between two
/// sentences with a <c>?:</c> (snapshot vs ordinary transaction). Keeping that as one key plus a data
/// argument would push a NOUN into the sentence — and a language that inflects would then need the noun in a
/// case the argument cannot know. So each sentence the analyzer can utter is its own key, and only genuine
/// data (counts, ids, ages) travels as an argument.</para>
///
/// <para>⚠ <b>HISTORICAL — this paragraph used to say the verdict headline was deliberately absent</b>, because
/// its forms differ by a COUNT and English's two-way singular/plural split does not carry to a language with
/// more plural categories. The reasoning stands; its stated precondition — <i>"until the plural mechanism is
/// decided"</i> — was met by etap C6, so the three <c>Verdict*</c> keys below now exist and the count travels
/// as argument <c>{0}</c> like every other counted sentence.</para>
/// </summary>
public static class SessionHealthMessages
{
    // ── Garbage-collection risk ──────────────────────────────────────────────────────────────────────

    public static readonly MessageKey GcBlockedTitle = new("SessionHealth.GcBlocked.Title");

    /// <summary>The holder is a snapshot transaction.</summary>
    public static readonly MessageKey GcBlockedExplanationSnapshot =
        new("SessionHealth.GcBlocked.Explanation.Snapshot");

    /// <summary>The holder is an ordinary transaction.</summary>
    public static readonly MessageKey GcBlockedExplanationTransaction =
        new("SessionHealth.GcBlocked.Explanation.Transaction");

    /// <summary>Takes the OAT lag as <c>{0}</c>.</summary>
    public static readonly MessageKey GcBlockedImpact = new("SessionHealth.GcBlocked.Impact");

    public static readonly MessageKey GcBlockedCheckReporting = new("SessionHealth.GcBlocked.Check.Reporting");

    public static readonly MessageKey GcBlockedCheckLeftOpen = new("SessionHealth.GcBlocked.Check.LeftOpen");

    // ── Long-running transaction ─────────────────────────────────────────────────────────────────────

    public static readonly MessageKey LongTransactionTitle = new("SessionHealth.LongTransaction.Title");

    public static readonly MessageKey LongTransactionExplanationSnapshot =
        new("SessionHealth.LongTransaction.Explanation.Snapshot");

    public static readonly MessageKey LongTransactionExplanationTransaction =
        new("SessionHealth.LongTransaction.Explanation.Transaction");

    public static readonly MessageKey LongTransactionImpact = new("SessionHealth.LongTransaction.Impact");

    public static readonly MessageKey LongTransactionCheckIdle = new("SessionHealth.LongTransaction.Check.Idle");

    public static readonly MessageKey LongTransactionCheckCommit =
        new("SessionHealth.LongTransaction.Check.Commit");

    // ── Evidence rows ────────────────────────────────────────────────────────────────────────────────
    //
    // ⚠ These carry Firebird's own vocabulary — "Tx", "OAT", "OST" are the engine's abbreviations and the
    // isolation label is either MON$ITSELF or a Firebird term. They travel as ARGUMENTS, never as keys: a
    // translator must be able to rearrange the row, and must not be invited to translate "OAT".

    /// <summary>Transaction id as <c>{0}</c>, isolation label as <c>{1}</c>.</summary>
    public static readonly MessageKey EvidenceTransaction = new("SessionHealth.Evidence.Transaction");

    /// <summary>Formatted age as <c>{0}</c>.</summary>
    public static readonly MessageKey EvidenceAge = new("SessionHealth.Evidence.Age");

    public static readonly MessageKey EvidenceAgeUnknown = new("SessionHealth.Evidence.AgeUnknown");

    /// <summary>OAT lag <c>{0}</c>, oldest snapshot <c>{1}</c>, next transaction <c>{2}</c>.</summary>
    public static readonly MessageKey EvidenceGap = new("SessionHealth.Evidence.Gap");

    // ── The verdict headline ─────────────────────────────────────────────────────────────────────────
    //
    // ⭐⭐ These are the keys the class comment above says are "deliberately NOT here", and the note was right
    // when it was written: the headline's wording is chosen by a COUNT, and in C1 there was no mechanism that
    // could let English keep a two-way split while Polish declares three. ⚠ The deferral had a stated
    // precondition — "until the plural mechanism is decided" — and C6 decided it. Declaring them now is
    // USING that mechanism, not designing one: the count travels as argument {0} (ratified R3), and each
    // language declares in its own catalog how many forms the sentence has.
    //
    // ⚠ Which is why the PL QA round found "All sessions healthy." still in English on a Polish screen: it was
    // never migrated, so there was no key to translate.

    /// <summary>Garbage collection is blocked. Takes the count as <c>{0}</c>. Plural family.</summary>
    public static readonly MessageKey VerdictGcBlocked = new("SessionHealth.Verdict.GcBlocked");

    /// <summary>Long-running transactions detected. Takes the count as <c>{0}</c>. Plural family.</summary>
    public static readonly MessageKey VerdictLongTransaction = new("SessionHealth.Verdict.LongTransaction");

    /// <summary>Nothing to report. ⚠ Flat — it carries no count, so it has no plural family and must not
    /// grow one.</summary>
    public static readonly MessageKey VerdictHealthy = new("SessionHealth.Verdict.Healthy");
}

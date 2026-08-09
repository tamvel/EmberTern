using System.Linq;

namespace EmberTern.Core.Metadata;

/// <summary>
/// Which Apply failures EmberTern is willing to explain in its own words.
///
/// <para>⭐ Exactly the <c>DebugErrorClassifier</c> contract, and for the same reason: <b>recognition is by
/// SQLSTATE / GDS code ONLY, never by message text.</b> Anything unrecognised is
/// <see cref="DatabaseApplyFailure.Unknown"/>, and the caller then shows the server's raw words.</para>
///
/// <para>⛔⛔ <b>The measured Legacy_Auth case is deliberately NOT here, and that is the point of the rule.</b>
/// The probe measured that a WRONG PASSWORD surfaces as <i>"Not supported plugin 'Legacy_Auth'"</i> — a
/// message that describes something other than the cause, and which therefore looks like the most valuable
/// thing to translate. But it arrives with <b>no SQLSTATE and no GDS codes at all</b>, so recognising it would
/// mean matching on the text. Ratified: we do not. A heuristic that reads "Legacy_Auth ⇒ wrong password" is
/// right until the day a server genuinely has a plugin problem, and then it confidently misdirects.</para>
/// </summary>
public enum DatabaseApplyFailure
{
    /// <summary>Not recognisable from codes — show the raw server message alone.</summary>
    Unknown,

    /// <summary>The account may connect but lacks the system privilege these operations require.</summary>
    MissingPrivilege,

    /// <summary>
    /// The database is in use and the operation needs exclusive access.
    ///
    /// <para>⚠ <b>Its measured justification was Read Only, which left V1</b> — and all three settings that
    /// remain were measured writable ONLINE with an attachment open, so this arm is <b>not provably
    /// reachable any more</b>. Kept because the alternative on a database in shutdown or single-user mode is
    /// a bare lock message with no lead, and because removing it is its own decision rather than a
    /// consequence of dropping Read Only. ⛔ Recorded rather than silently retained: if it is ever confirmed
    /// unreachable, delete it — an inert branch reads to the next author as a real safety net (§15.7).</para>
    /// </summary>
    DatabaseInUse,
}

/// <summary>Turns one <see cref="DatabaseSettingOutcome"/> into a category, from its codes alone.</summary>
public static class DatabaseConfigurationDiagnosis
{
    /// <summary>SQLSTATE for an insufficient-privilege refusal (measured: USE_GFIX_UTILITY missing).</summary>
    internal const string SqlStateInsufficientPrivilege = "28000";

    /// <summary>SQLSTATE reported when the operation needs exclusive access and the database is attached.</summary>
    internal const string SqlStateLockConflict = "40001";

    /// <summary>isc_no_priv — the leading GDS code of the privilege refusal.</summary>
    internal const int GdsNoPrivilege = 335544788;

    /// <summary>isc_obj_in_use — the database is attached by someone.</summary>
    internal const int GdsObjectInUse = 335544453;

    public static DatabaseApplyFailure Classify(DatabaseSettingOutcome outcome)
    {
        if (outcome.Succeeded)
        {
            return DatabaseApplyFailure.Unknown;
        }

        if (outcome.SqlState == SqlStateInsufficientPrivilege
            || (outcome.GdsCodes?.Contains(GdsNoPrivilege) ?? false))
        {
            return DatabaseApplyFailure.MissingPrivilege;
        }

        if (outcome.SqlState == SqlStateLockConflict
            || (outcome.GdsCodes?.Contains(GdsObjectInUse) ?? false))
        {
            return DatabaseApplyFailure.DatabaseInUse;
        }

        return DatabaseApplyFailure.Unknown;
    }
}

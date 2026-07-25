namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// A coarse, friendly category for a <see cref="DebugError"/> (Stage X / D15.4 Seam B — Friendly Error
/// Mapping). Deliberately small and honest: only cases the driver's <b>codes</b> distinguish
/// unambiguously get a category; everything else is <see cref="Unknown"/> ("prefer silence, don't guess").
/// </summary>
public enum FriendlyErrorCategory
{
    /// <summary>None of the below could be established from the codes — the caller shows the raw message.</summary>
    Unknown = 0,

    /// <summary>A user-defined <c>EXCEPTION</c> was raised (its name is on <see cref="DebugError.ExceptionName"/>).</summary>
    UserException,

    /// <summary>A database constraint was violated — NOT NULL / CHECK / PRIMARY-or-UNIQUE key.</summary>
    ConstraintViolation,

    /// <summary>A DSQL error preparing the statement/expression — a syntax error OR an unknown name
    /// (table/column). The server reports all of these with the same generic code, so the debugger cannot
    /// split them here; the precise reason (unclosed paren, unknown variable/function) is D15.4 Seam C's job
    /// (local pre-validation via the Language Service, before the statement is sent).</summary>
    SqlError,
}

/// <summary>
/// Classifies a <see cref="DebugError"/> into a <see cref="FriendlyErrorCategory"/> from its
/// <b>SQLSTATE / GDS codes only</b> — never by parsing the message text (the same rule as
/// <c>DebugErrorMapper</c>, spec §3.6 / §F). The friendly <i>text</i> for each category lives in the App
/// layer (UiStrings), not here — Core carries no user-facing UI strings (project rule #6).
/// <para>
/// The GDS codes below were <b>measured against the live FB5 lab</b> (D15.4 Seam B probe, 2026-07-23) — the
/// first at-or-above-ISC-base <c>Number</c> the managed driver surfaces, i.e. exactly the value
/// <c>DebugErrorMapper</c> stores on <see cref="DebugError.GdsCode"/>. The measurement's key finding is why
/// <see cref="FriendlyErrorCategory.SqlError"/> is one bucket: token-unknown (-104), table-unknown (-204)
/// and column-unknown (-206) all arrive as the same generic <c>335544569</c> (isc_dsql_error), because
/// <see cref="DebugError"/> carries only the leading GDS code (not the SQLCODE nor the specific sub-code).
/// </para>
/// </summary>
public static class DebugErrorClassifier
{
    // ── Measured GDS codes (D15.4 Seam B probe, live FB5) ────────────────────────────────────────────
    /// <summary>isc_dsql_error — the leading code for token-unknown (-104), table-unknown (-204) and
    /// column-unknown (-206). Cannot be split further from a <see cref="DebugError"/> (see the class note).</summary>
    internal const long GdsDsqlError = 335544569;

    /// <summary>Validation error for a NOT NULL variable/column (the NOT NULL-domain case D2 measured).</summary>
    internal const long GdsValidationNotNull = 335544879;

    /// <summary>isc_not_valid — a CHECK-constraint validation error.</summary>
    internal const long GdsCheckConstraint = 335544347;

    /// <summary>Violation of a PRIMARY or UNIQUE key constraint.</summary>
    internal const long GdsUniqueViolation = 335544665;

    /// <summary>Classifies the error from its codes alone. Null / no recognizable code ⇒
    /// <see cref="FriendlyErrorCategory.Unknown"/> (the caller then shows the raw message).</summary>
    public static FriendlyErrorCategory Classify(DebugError? error)
    {
        if (error is null)
        {
            return FriendlyErrorCategory.Unknown;
        }

        // A user EXCEPTION is the most reliable signal: DebugErrorMapper sets ExceptionName exactly when the
        // vector carries isc_except. Prefer it over the numeric code (a raise can carry other codes too).
        if (!string.IsNullOrEmpty(error.ExceptionName))
        {
            return FriendlyErrorCategory.UserException;
        }

        return error.GdsCode switch
        {
            GdsValidationNotNull or GdsCheckConstraint or GdsUniqueViolation
                => FriendlyErrorCategory.ConstraintViolation,
            GdsDsqlError => FriendlyErrorCategory.SqlError,
            _ => FriendlyErrorCategory.Unknown,
        };
    }
}

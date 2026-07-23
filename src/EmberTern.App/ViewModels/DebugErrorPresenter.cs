using System.Globalization;
using EmberTern.Core.Sql.Debugging;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Turns a Core <see cref="DebugError"/> into user-facing text for the debugger's error surfaces (Stage X /
/// D15.4 Seam B — Friendly Error Mapping). The single place that composes debug-error text, so the four
/// surfaces that used to each hand-roll <c>Message ?? ExceptionName ?? …</c> — the Immediate/Executed-SQL
/// result, the Watch value, the breakpoint-condition reason, and the Error Bar — cannot drift apart.
/// <para>
/// Two outputs: <see cref="Raw"/> picks the best raw field (never friendly — the Error Bar's job is the full
/// Firebird message), and <see cref="Describe"/> is the friendly, categorised one-liner for the three
/// expression surfaces (falling back to <see cref="Raw"/> when the category is
/// <see cref="FriendlyErrorCategory.Unknown"/>). The category comes from
/// <see cref="DebugErrorClassifier"/> (codes only); this class only maps a category to its
/// <see cref="UiStrings"/> text — it never parses the message (spec §F).
/// </para>
/// </summary>
internal static class DebugErrorPresenter
{
    /// <summary>The best raw field to show, in priority order (message → exception name → SQLSTATE → GDS →
    /// a generic fallback). Never friendly — used where the full server text is wanted (Error Bar) and as the
    /// <see cref="FriendlyErrorCategory.Unknown"/> fallback for <see cref="Describe"/>.</summary>
    public static string Raw(DebugError? e)
    {
        if (e is null)
        {
            return string.Empty;
        }
        if (!string.IsNullOrWhiteSpace(e.Message)) return e.Message!.Trim();
        if (!string.IsNullOrWhiteSpace(e.ExceptionName)) return e.ExceptionName!;
        if (!string.IsNullOrWhiteSpace(e.SqlState)) return $"SQLSTATE {e.SqlState}";
        if (e.GdsCode is { } g) return $"GDS {g}";
        return UiStrings.DebuggerErrorUnknown;
    }

    /// <summary>The friendly, categorised one-liner for the expression surfaces. Falls back to
    /// <see cref="Raw"/> for <see cref="FriendlyErrorCategory.Unknown"/> so nothing is ever lost.</summary>
    public static string Describe(DebugError? e)
    {
        if (e is null)
        {
            return UiStrings.DebuggerErrorUnknown;
        }
        return DebugErrorClassifier.Classify(e) switch
        {
            FriendlyErrorCategory.UserException => string.Format(
                CultureInfo.CurrentCulture, UiStrings.DebuggerFriendlyUserExceptionFormat,
                string.IsNullOrWhiteSpace(e.ExceptionName) ? Raw(e) : e.ExceptionName),
            FriendlyErrorCategory.ConstraintViolation => UiStrings.DebuggerFriendlyConstraint,
            FriendlyErrorCategory.SqlError => UiStrings.DebuggerFriendlySqlError,
            _ => Raw(e),
        };
    }
}

using System;
using System.Collections.Generic;
using FirebirdSql.Data.FirebirdClient;
using EmberTern.Core.Sql.Debugging;

namespace EmberTern.Firebird;

/// <summary>
/// Maps a driver <see cref="FbException"/> to the interpreter's <see cref="DebugError"/> (Stage X / D2 seam c,
/// spec §3.6: "Error → exception mapping comes from the driver's <c>FbException</c> (SQLSTATE / GDS codes),
/// not from parsing messages"). The <see cref="ExceptionRouter"/> then matches this identity against the
/// AST's <c>WHEN … DO</c> conditions.
/// <para>
/// Grounded against the live FB5 engine (§15, D2 seam c probe): a <b>user</b> <c>EXCEPTION</c> raises with
/// GDS <c>isc_except</c> (335544517) present in the error vector and its <b>name on the first line of the
/// message</b> (<c>E_CUSTOMER_NOT_FOUND\nCustomer not found.\nAt block line: …</c>); a domain <c>NOT NULL</c>
/// validation raises SQLSTATE 42000 / GDS 335544879 with no <c>isc_except</c>. The small vector entries
/// (0, 1, argument counts) are not GDS codes — only <see cref="Number"/> values at or above the ISC base are.
/// </para>
/// <para>
/// The decision is factored into the pure <see cref="Build"/> (unit-tested without a live server — an
/// <see cref="FbException"/> cannot be constructed in a test); <see cref="FromFirebird"/> is the thin shim
/// that reads the driver fields and calls it. Two <b>documented D2 boundaries</b>: the legacy <c>SQLCODE</c>
/// is not distinctly exposed by the driver (so <c>WHEN SQLCODE</c> matching is best-effort — left null), and
/// the symbolic GDS name is not resolved here (numeric <c>WHEN GDSCODE</c> matches; symbolic needs the GDS
/// name table — added later only if a feature needs it). <c>WHEN EXCEPTION &lt;name&gt;</c>, <c>WHEN ANY</c>,
/// numeric <c>WHEN GDSCODE</c> and <c>WHEN SQLSTATE</c> all match faithfully.
/// </para>
/// </summary>
internal static class DebugErrorMapper
{
    /// <summary>The ISC error-code base (0x14000000). A vector <see cref="FbError.Number"/> at or above this
    /// is a real GDS/ISC code; below it are argument/separator values.</summary>
    internal const long IscCodeBase = 335544320;

    /// <summary>GDS <c>isc_except</c> — the marker of a user-defined <c>EXCEPTION</c> raise.</summary>
    internal const long IscExcept = 335544517;

    /// <summary>Reads the driver fields off <paramref name="ex"/> and builds the <see cref="DebugError"/>.</summary>
    public static DebugError FromFirebird(FbException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var gdsNumbers = new List<long>();
        foreach (var e in ex.Errors)
        {
            gdsNumbers.Add(e.Number);
        }
        return Build(ex.SQLSTATE, ex.Message, gdsNumbers);
    }

    /// <summary>The pure mapping (unit-testable): the primary GDS code is the first vector number at or above
    /// the ISC base; a user exception is one whose vector contains <see cref="IscExcept"/>, and its name is the
    /// first line of the message. <paramref name="sqlState"/> and <paramref name="message"/> are carried
    /// through; <c>SqlCode</c> and the symbolic GDS name are left null (documented D2 boundaries).</summary>
    public static DebugError Build(string? sqlState, string? message, IReadOnlyList<long> gdsNumbers)
    {
        long? gdsCode = null;
        bool isUserException = false;
        foreach (var n in gdsNumbers)
        {
            if (n == IscExcept)
            {
                isUserException = true;
            }
            if (gdsCode is null && n >= IscCodeBase)
            {
                gdsCode = n;
            }
        }

        string? exceptionName = isUserException ? FirstLine(message) : null;

        return new DebugError(
            ExceptionName: exceptionName,
            GdsCode: gdsCode,
            GdsCodeSymbol: null,
            SqlCode: null,
            SqlState: string.IsNullOrEmpty(sqlState) ? null : sqlState,
            Message: message);
    }

    // The first non-empty line, trimmed — a user exception reports its NAME there (message text follows on
    // line 2, the "At block line" position on line 3).
    private static string? FirstLine(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return null;
        }
        int nl = message.IndexOfAny(new[] { '\r', '\n' });
        var line = (nl < 0 ? message : message.Substring(0, nl)).Trim();
        return line.Length == 0 ? null : line;
    }
}

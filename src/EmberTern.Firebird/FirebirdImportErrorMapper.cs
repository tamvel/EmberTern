using System;
using System.Collections.Generic;
using EmberTern.Core.Import;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Turns a Firebird refusal into a structured <see cref="ImportErrorKind"/> — <b>from the GDS vector, never
/// from the message text</b>.
/// <para>
/// ⭐ <b>Why the vector and not <c>ErrorCode</c>.</b> I0 measured three completely different failures that share
/// a leading GDS code and a SQLSTATE: a string that is too long, a number out of range, and a character that
/// cannot be transliterated are ALL <c>335544321</c> / <c>22000</c>. They separate only on a LATER element of
/// the vector (<c>335544914</c> / <c>335544916</c> / <c>335544565</c>). A mapper written the obvious way — the
/// way <see cref="DebugErrorMapper"/> legitimately can, because its domain has no such collision — would tell
/// a user with a six-character-too-long name that they had an "arithmetic error". That is precisely the
/// useless-message failure §8 point 10 criticises in the tool this module replaces.
/// </para>
/// <para>
/// ⭐ <b>The vector also carries FACTS.</b> A truncation vector holds the column's limit and the value's actual
/// length as plain numbers, so the report can say "26 characters, limit 20" <em>on the server's authority</em>
/// rather than by parsing English out of a message that changes between versions and locales.
/// </para>
/// <para>
/// <b>What it deliberately does NOT claim:</b> a primary-key violation and a unique-index violation are
/// <em>the same code at every depth</em> (both <c>335544665</c>), so both become
/// <see cref="ImportErrorKind.ServerUniqueViolation"/>. Reporting which one it was would be inventing
/// information (§0) — the honest answer is "uniqueness violated".
/// </para>
/// </summary>
public static class FirebirdImportErrorMapper
{
    // ── The GDS codes, measured against a live FB5 engine in etap I0 (findings §2.6) ────────────────────

    /// <summary>NOT NULL violation.</summary>
    public const int GdsNotNull = 335544347;

    /// <summary>Uniqueness violated through a PRIMARY KEY or UNIQUE <b>constraint</b>. The two are
    /// indistinguishable — both report exactly this vector.</summary>
    public const int GdsUniqueViolation = 335544665;

    /// <summary>
    /// ⭐ Uniqueness violated through a standalone <c>CREATE UNIQUE INDEX</c> — a <b>different leading code</b>
    /// from the constraint form above.
    /// <para>
    /// <b>Measured in I4, and it is a genuine addition to the I0 table.</b> I0 exercised a PK and a UNIQUE
    /// <em>constraint</em> and saw <c>335544665</c> for both, so it concluded the two were interchangeable.
    /// They are — but an index created on its own is a third thing, and it leads with <c>335544349</c>
    /// (<c>isc_no_dup</c>, <em>"attempt to store duplicate value … in unique index"</em>). Without this the
    /// import reported a duplicate key as a generic <see cref="ImportErrorKind.ServerError"/>.
    /// </para>
    /// <para>
    /// This is exactly why the etap's Definition of Done demands a live run rather than trusting the earlier
    /// measurement: the earlier measurement was correct about what it measured, and incomplete about the world.
    /// </para>
    /// </summary>
    public const int GdsDuplicateInUniqueIndex = 335544349;

    /// <summary>CHECK constraint (including a domain's).</summary>
    public const int GdsCheckConstraint = 335544558;

    /// <summary>Foreign key — the referenced row does not exist.</summary>
    public const int GdsForeignKey = 335544466;

    /// <summary>"arithmetic exception, numeric overflow, or string truncation" — <b>ambiguous by itself</b>.
    /// Which of the three it is lives further down the vector.</summary>
    public const int GdsArithmeticOrTruncation = 335544321;

    /// <summary>Discriminator: the value was too long for the column.</summary>
    public const int GdsStringTruncation = 335544914;

    /// <summary>Discriminator: the numeric value is out of range for the column.</summary>
    public const int GdsNumericOutOfRange = 335544916;

    /// <summary>Discriminator: the value could not be transliterated into the column's charset.</summary>
    public const int GdsTransliterationFailed = 335544565;

    /// <summary>
    /// Anything at or above this is a GDS code; anything below is a PARAMETER the server put in the vector.
    /// <para>
    /// Every Firebird GDS code sits in the 335 5xx xxx range, and the parameters that matter here are lengths
    /// — a column limit tops out at 32 765. Reading the numbers positionally instead would be brittle: the
    /// measured truncation vector carries a second GDS code between the discriminator and the numbers
    /// (<c>[335544321, 335544914, 335545033, 10, 16, 335544321]</c>), and nothing promises that stays put.
    /// </para>
    /// </summary>
    private const int LowestGdsCode = 335_000_000;

    /// <summary>
    /// Maps one refused row to a batch result carrying the structured cause, the server's own message, and any
    /// numbers the vector supplied.
    /// </summary>
    public static ImportBatchItemResult Map(Exception? exception)
    {
        if (exception is not FbException fb)
        {
            // Not a Firebird refusal at all (a broken connection, a driver fault). Reported honestly rather
            // than classified into a data error the user would then go looking for in their file.
            return ImportBatchItemResult.Failure(ImportErrorKind.ServerError, exception?.Message);
        }

        var kind = Classify(ReadGdsVector(fb), out var limit, out var actualLength);
        return ImportBatchItemResult.Failure(kind, FirstLine(fb.Message), limit, actualLength);
    }

    /// <summary>The GDS codes the exception carries, in order. Empty when the driver gave none.</summary>
    public static IReadOnlyList<int> ReadGdsVector(FbException exception)
    {
        if (exception?.Errors is null) return Array.Empty<int>();

        var codes = new List<int>(exception.Errors.Count);
        foreach (FbError error in exception.Errors) codes.Add(error.Number);
        return codes;
    }

    /// <summary>
    /// ⭐ The classification itself — <b>pure</b>, so it can be pinned against the exact vectors I0 measured
    /// without a server, and re-verified against a live one.
    /// </summary>
    /// <param name="gdsCodes">The vector, leading code first.</param>
    /// <param name="limit">The column's declared limit when the vector reported one.</param>
    /// <param name="actualLength">The value's actual length when the vector reported one.</param>
    public static ImportErrorKind Classify(
        IReadOnlyList<int> gdsCodes, out int? limit, out int? actualLength)
    {
        limit = null;
        actualLength = null;

        if (gdsCodes is null || gdsCodes.Count == 0) return ImportErrorKind.ServerError;

        // The unambiguous classes are decided by the LEADING code. Scanning the whole vector for them would
        // be wrong, not merely wasteful: the measured foreign-key and primary-key vectors share a later
        // element (335545072), so a whole-vector scan could report an FK failure as a uniqueness violation.
        switch (gdsCodes[0])
        {
            case GdsNotNull: return ImportErrorKind.ServerNullViolation;

            // Both forms mean the same thing to the user — "this value is already there" — so both become one
            // kind. Which mechanism enforced it (a constraint or a bare index) is not information the report
            // has any use for.
            case GdsUniqueViolation:
            case GdsDuplicateInUniqueIndex:
                return ImportErrorKind.ServerUniqueViolation;

            case GdsCheckConstraint: return ImportErrorKind.ServerCheckViolation;
            case GdsForeignKey: return ImportErrorKind.ServerForeignKeyViolation;
            case GdsArithmeticOrTruncation: return Discriminate(gdsCodes, out limit, out actualLength);
            default: return ImportErrorKind.ServerError;
        }
    }

    /// <summary>
    /// Splits the one ambiguous leading code into the three failures that hide behind it, by looking for the
    /// discriminator the server put further down the vector.
    /// </summary>
    private static ImportErrorKind Discriminate(
        IReadOnlyList<int> gdsCodes, out int? limit, out int? actualLength)
    {
        limit = null;
        actualLength = null;

        for (var i = 1; i < gdsCodes.Count; i++)
        {
            switch (gdsCodes[i])
            {
                case GdsStringTruncation:
                    ReadLengthParameters(gdsCodes, i + 1, out limit, out actualLength);
                    return ImportErrorKind.ServerStringTruncation;

                case GdsNumericOutOfRange:
                    return ImportErrorKind.ServerNumericOverflow;

                case GdsTransliterationFailed:
                    return ImportErrorKind.ServerTransliterationFailed;
            }
        }

        // A genuine arithmetic fault (a division by zero in a trigger, say) with no discriminator we know.
        // An honest bucket that still carries the server's message, rather than a guess dressed as precision.
        return ImportErrorKind.ServerError;
    }

    /// <summary>Reads the first two non-GDS numbers after the truncation discriminator: the declared limit,
    /// then the actual length. Missing numbers stay <c>null</c> — a report that says nothing is better than
    /// one that says something wrong.</summary>
    private static void ReadLengthParameters(
        IReadOnlyList<int> gdsCodes, int from, out int? limit, out int? actualLength)
    {
        limit = null;
        actualLength = null;

        for (var i = from; i < gdsCodes.Count; i++)
        {
            var value = gdsCodes[i];
            if (value >= LowestGdsCode || value < 0) continue;

            if (limit is null) limit = value;
            else
            {
                actualLength = value;
                return;
            }
        }
    }

    /// <summary>The server's message, first line only — the driver concatenates the whole error vector into
    /// one string, and the report shows the headline while the full text stays available on the row.</summary>
    private static string FirstLine(string? message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;

        var newline = message.IndexOf('\n');
        var line = newline < 0 ? message : message.Substring(0, newline);
        return line.TrimEnd('\r').Trim();
    }
}

using System.Globalization;

namespace EmberTern.Core.Sql;

/// <summary>Result of parsing a <c>CREATE [OR ALTER] TRIGGER</c> statement into its
/// editable parts. <see cref="Success"/> is false when the text doesn't match the
/// expected relation-trigger shape (e.g. a DB-level / DDL trigger, or a malformed
/// header) — callers keep their last-good model and surface a non-blocking notice
/// rather than discarding the user's edits, exactly like the procedure parser.</summary>
public sealed class TriggerSignature
{
    public bool Success { get; init; }
    public string? Name { get; init; }
    public string Table { get; init; } = string.Empty;
    public bool IsBefore { get; init; }
    public bool FiresInsert { get; init; }
    public bool FiresUpdate { get; init; }
    public bool FiresDelete { get; init; }
    public int Position { get; init; }
    public bool Active { get; init; } = true;
    /// <summary>Everything after the header <c>AS</c> — the DECLARE…BEGIN…END body.</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>
/// Bounded parser for a Firebird relation trigger header:
/// <c>{CREATE [OR ALTER] | ALTER | RECREATE} TRIGGER name [ACTIVE|INACTIVE] FOR table
/// [ACTIVE|INACTIVE] {BEFORE|AFTER} event[ OR event …] [POSITION n] AS body</c>.
/// Not a full PSQL grammar — it only splits the fixed header from the body (the body
/// is split further by <see cref="ProcedureBodySplitter"/>, which a trigger body shares
/// with a procedure body). Used for the Source→Easy round-trip; pure + testable without
/// a DB. ACTIVE/INACTIVE is accepted in either position (before or after FOR) so both
/// the canonical form and the form EmberTern's DDL reader emits parse.
/// </summary>
public static class TriggerSignatureParser
{
    public static TriggerSignature Parse(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Fail();
        var s = sql!;
        int i = 0;

        SqlScanHelpers.SkipTrivia(s, ref i);
        // CREATE [OR ALTER] | RECREATE | ALTER
        if (SqlScanHelpers.TryKeyword(s, ref i, "CREATE"))
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (SqlScanHelpers.TryKeyword(s, ref i, "OR"))
            {
                SqlScanHelpers.SkipTrivia(s, ref i);
                if (!SqlScanHelpers.TryKeyword(s, ref i, "ALTER")) return Fail();
                SqlScanHelpers.SkipTrivia(s, ref i);
            }
        }
        else if (!SqlScanHelpers.TryKeyword(s, ref i, "RECREATE") && !SqlScanHelpers.TryKeyword(s, ref i, "ALTER"))
        {
            return Fail();
        }

        SqlScanHelpers.SkipTrivia(s, ref i);
        if (!SqlScanHelpers.TryKeyword(s, ref i, "TRIGGER")) return Fail();
        SqlScanHelpers.SkipTrivia(s, ref i);

        var name = ReadFoldedIdentifier(s, ref i);
        if (string.IsNullOrEmpty(name)) return Fail();

        // name [ACTIVE|INACTIVE] FOR table [ACTIVE|INACTIVE] — consumed in any order
        // until the BEFORE/AFTER timing keyword.
        string table = string.Empty;
        bool active = true;
        bool isBefore = false;
        bool gotTiming = false;
        while (i < s.Length)
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (SqlScanHelpers.TryKeyword(s, ref i, "BEFORE")) { isBefore = true; gotTiming = true; break; }
            if (SqlScanHelpers.TryKeyword(s, ref i, "AFTER")) { isBefore = false; gotTiming = true; break; }
            if (SqlScanHelpers.TryKeyword(s, ref i, "ACTIVE")) { active = true; continue; }
            if (SqlScanHelpers.TryKeyword(s, ref i, "INACTIVE")) { active = false; continue; }
            if (SqlScanHelpers.TryKeyword(s, ref i, "FOR"))
            {
                SqlScanHelpers.SkipTrivia(s, ref i);
                table = ReadFoldedIdentifier(s, ref i) ?? string.Empty;
                if (table.Length == 0) return Fail();
                continue;
            }
            // Unexpected token (e.g. ON DATABASE — a DB-level trigger, out of scope).
            return Fail();
        }

        if (!gotTiming || table.Length == 0) return Fail();

        // event [ OR event … ]
        bool ins = false, upd = false, del = false;
        while (i < s.Length)
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (SqlScanHelpers.TryKeyword(s, ref i, "INSERT")) ins = true;
            else if (SqlScanHelpers.TryKeyword(s, ref i, "UPDATE")) upd = true;
            else if (SqlScanHelpers.TryKeyword(s, ref i, "DELETE")) del = true;
            else break;

            int save = i;
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (!SqlScanHelpers.TryKeyword(s, ref i, "OR")) { i = save; break; }
        }
        if (!(ins || upd || del)) return Fail();

        // optional POSITION n
        int position = 0;
        SqlScanHelpers.SkipTrivia(s, ref i);
        if (SqlScanHelpers.TryKeyword(s, ref i, "POSITION"))
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            position = ReadInt(s, ref i);
        }

        // AS body
        SqlScanHelpers.SkipTrivia(s, ref i);
        if (!SqlScanHelpers.TryKeyword(s, ref i, "AS")) return Fail();
        var body = s.Substring(i).Trim();

        return new TriggerSignature
        {
            Success = true,
            Name = name,
            Table = table,
            IsBefore = isBefore,
            FiresInsert = ins,
            FiresUpdate = upd,
            FiresDelete = del,
            Position = position,
            Active = active,
            Body = body,
        };
    }

    private static TriggerSignature Fail() => new() { Success = false };

    // Firebird folds unquoted identifiers to uppercase; quoted identifiers keep their
    // literal case. Match that so parsed names align with the catalog.
    private static string? ReadFoldedIdentifier(string s, ref int i)
    {
        bool quoted = i < s.Length && s[i] == '"';
        var name = SqlScanHelpers.ReadIdentifier(s, ref i);
        if (name is null) return null;
        return quoted ? name : name.ToUpperInvariant();
    }

    private static int ReadInt(string s, ref int i)
    {
        int start = i;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return i > start && int.TryParse(s.Substring(start, i - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : 0;
    }
}

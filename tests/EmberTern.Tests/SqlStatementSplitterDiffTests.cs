using System;
using System.Collections.Generic;
using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// §0 (Paramount Law) gate for the O5 migration: the new parser-backed
/// <see cref="SqlStatementSplitter.Split"/> must be <b>byte-for-byte identical</b> to the previous
/// char-based DDL splitter — its output is the exact DDL sent to the server, so any divergence
/// could corrupt user metadata. The legacy algorithm is inlined verbatim below as the permanent
/// "before" reference; every corpus case asserts new == old. This is the old-vs-new corpus diff
/// the audit required before trusting the switch.
/// </summary>
public class SqlStatementSplitterDiffTests
{
    // Representative of what FirebirdDdlExecutor actually receives — DDL/DML EmberTern generates
    // (no SET TERM; one-or-few statements; PSQL bodies with internal semicolons) — plus the
    // long-standing pinned edge cases and pathological inputs. The diff only asserts new == old,
    // so any string is a valid identity probe.
    public static IEnumerable<object[]> Corpus() => AsRows(new[]
    {
        // Trivial / empty / whitespace / lone terminators.
        "",
        "   \n\t  ",
        ";",
        ";;;",
        "A;;B;;;C",
        "  A  ;\n  B\n;C",
        // Plain single statements (with / without trailing ';').
        "SELECT 1 FROM RDB$DATABASE",
        "SELECT 1 FROM RDB$DATABASE;",
        "ALTER TABLE T ADD A INTEGER",
        "CREATE GENERATOR GEN_X",
        "COMMENT ON TABLE T IS 'hello'",
        "COMMENT ON COLUMN T.C IS 'a; b'",
        "GRANT SELECT ON T TO PUBLIC",
        "CREATE DOMAIN D AS VARCHAR(10) DEFAULT 'x' NOT NULL",
        "CREATE EXCEPTION E 'boom; bang'",
        "CREATE ROLE R",
        // Multi-statement plain batches.
        "ALTER TABLE T ADD A INTEGER; ALTER TABLE T ADD B INTEGER",
        "CREATE VIEW V AS SELECT 1 AS X FROM RDB$DATABASE; ALTER TABLE T ADD A INTEGER",
        "CREATE SEQUENCE S; ALTER SEQUENCE S RESTART WITH 100; DROP SEQUENCE S",
        // Strings / comments with semicolons + keywords (must not split / affect nesting).
        "EXECUTE PROCEDURE P('a;b');SELECT 1 FROM RDB$DATABASE",
        "INSERT INTO T VALUES ('begin case end'); -- end\nUPDATE T SET X = 1",
        "EXECUTE STATEMENT 'select ''begin'' from rdb$database';",
        "-- leading comment\nCREATE TABLE T (ID INTEGER)",
        "/* block */ ALTER TABLE T ADD C INTEGER; -- trailing",
        "ALTER TABLE T ADD \"BEGIN\" INTEGER; ALTER TABLE T ADD \"END\" INTEGER",
        // PSQL definitions — one statement each (DECLARE / CASE / subprogram in body).
        "CREATE OR ALTER PROCEDURE P (AID INTEGER) RETURNS (V INTEGER) AS\nbegin\n  for select sum(x) from t where (case when :aid > 0 then 1 else 0 end) = 1 into :v do\n    suspend;\nend",
        "CREATE OR ALTER PROCEDURE P AS\nbegin\n  if (1 = 1) then\n  begin\n    x = case when a then 1 else 2 end;\n    y = 3;\n  end\nend",
        "CREATE OR ALTER PROCEDURE P AS\nbegin\n  x = case\n        when a then case when b then 1 else 2 end\n        else 3\n      end;\n  y = 4;\nend",
        "CREATE OR ALTER TRIGGER XXX_NAGL_BIU_99 FOR NAGL\nACTIVE BEFORE INSERT OR UPDATE POSITION 99\nAS\n\nDECLARE VARIABLE ID_NAGL T_ID;\n\nbegin\n  id_nagl = new.id_nagl;\nend",
        "CREATE OR ALTER PROCEDURE P RETURNS (R INTEGER) AS\nDECLARE VARIABLE T INTEGER;\nDECLARE VARIABLE S VARCHAR(10);\nBEGIN\n  T = 1;\n  R = T;\n  SUSPEND;\nEND",
        "CREATE OR ALTER PROCEDURE P AS\nDECLARE PROCEDURE SUB (A INTEGER) AS BEGIN A = A + 1; END\nDECLARE VARIABLE V INTEGER;\nBEGIN\n  V = 1;\n  SUSPEND;\nEND",
        // PSQL definition amid plain statements (the whole trigger is one unit).
        "CREATE GENERATOR GEN_X;\nCREATE OR ALTER TRIGGER T FOR NAGL ACTIVE BEFORE INSERT POSITION 0\nAS\nDECLARE VARIABLE V INTEGER;\nBEGIN\n  V = GEN_ID(GEN_X, 1);\n  NEW.ID = V;\nEND;\nALTER TABLE NAGL ADD X INTEGER",
        // Function + package (PSQL-definition families).
        "CREATE FUNCTION F (A INTEGER) RETURNS INTEGER AS BEGIN RETURN A + 1; END",
        "RECREATE PACKAGE PKG AS BEGIN PROCEDURE P; FUNCTION F RETURNS INTEGER; END",
        // Header-with-no-body (UDR / EXTERNAL) — a ';' before AS terminates.
        "CREATE FUNCTION F EXTERNAL NAME 'x' ENGINE UDR;\nALTER TABLE T ADD A INTEGER",
        // ALTER TABLE / CREATE VIEW AS SELECT are NOT PSQL — split on ';'.
        "ALTER TABLE T ADD A INTEGER; ALTER TABLE T ADD B INTEGER; ALTER TABLE T ADD C INTEGER",
        // Unterminated / pathological (both impls must agree, whatever they do).
        "CREATE PROCEDURE P AS BEGIN",
        "CREATE PROCEDURE P (A INTEGER",
        "SELECT 'unterminated string",
        "SELECT \"unterminated ident FROM T",
        "SELECT 1 /* unterminated block comment",
        "END; SELECT 1 FROM T",
    });

    [Theory]
    [MemberData(nameof(Corpus))]
    public void NewSplitter_IsByteForByteIdenticalToLegacy(string sql)
        => Assert.Equal(LegacySplitStatements(sql), SqlStatementSplitter.Split(sql));

    private static IEnumerable<object[]> AsRows(string[] cases)
    {
        foreach (var c in cases) yield return new object[] { c };
    }

    // ───────────────────────────────────────────────────────────────────────────────────────
    // LEGACY REFERENCE — an exact copy of the previous FirebirdDdlExecutor char-based splitter,
    // kept here as the permanent §0 "before" the migration is diffed against. Do NOT "improve"
    // it; it is a frozen spec fixture.
    // ───────────────────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> LegacySplitStatements(string sql)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(sql)) return result;

        int i = 0, n = sql.Length;
        while (i < n)
        {
            i = SkipTriviaAndComments(sql, i);
            if (i >= n) break;
            int start = i;
            i = IsPsqlDefinitionStart(sql, i)
                ? ScanPsqlStatement(sql, i)
                : ScanPlainStatement(sql, i);
            AddStatement(sql.Substring(start, i - start), result);
        }
        return result;
    }

    private static int ScanPlainStatement(string sql, int start)
    {
        int i = start, n = sql.Length, depth = 0;
        while (i < n)
        {
            char c = sql[i];
            if (c == '\'') { i = SkipString(sql, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(sql, i); continue; }
            if (c == '-' && i + 1 < n && sql[i + 1] == '-') { i = SkipLineComment(sql, i); continue; }
            if (c == '/' && i + 1 < n && sql[i + 1] == '*') { i = SkipBlockComment(sql, i); continue; }
            if (c == ';' && depth == 0) return i + 1;
            if (IsWordBoundary(sql, i - 1))
            {
                if ((Matches(sql, i, "BEGIN") && IsWordEndAt(sql, i + 5)) || (Matches(sql, i, "CASE") && IsWordEndAt(sql, i + 4))) depth++;
                else if (Matches(sql, i, "END") && IsWordEndAt(sql, i + 3)) { if (depth > 0) depth--; }
            }
            i++;
        }
        return i;
    }

    private static int ScanPsqlStatement(string sql, int start)
    {
        int i = start, n = sql.Length;
        bool pastAs = false, bodyOpened = false;
        int depth = 0;
        while (i < n)
        {
            char c = sql[i];
            if (c == '\'') { i = SkipString(sql, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(sql, i); continue; }
            if (c == '-' && i + 1 < n && sql[i + 1] == '-') { i = SkipLineComment(sql, i); continue; }
            if (c == '/' && i + 1 < n && sql[i + 1] == '*') { i = SkipBlockComment(sql, i); continue; }

            if (!pastAs)
            {
                if (c == '(') { i = SkipParens(sql, i); continue; }
                if (KeywordAt(sql, i, "AS")) { pastAs = true; i += 2; continue; }
                if (c == ';') return i + 1;
                i++;
                continue;
            }

            if (IsWordBoundary(sql, i - 1))
            {
                if (Matches(sql, i, "BEGIN") && IsWordEndAt(sql, i + 5)) { depth++; bodyOpened = true; i += 5; continue; }
                if (Matches(sql, i, "CASE") && IsWordEndAt(sql, i + 4)) { if (depth > 0) depth++; i += 4; continue; }
                if (Matches(sql, i, "END") && IsWordEndAt(sql, i + 3))
                {
                    i += 3;
                    if (depth > 0)
                    {
                        depth--;
                        if (depth == 0 && bodyOpened)
                        {
                            int j = SkipTriviaAndComments(sql, i);
                            if (j < n && (KeywordAt(sql, j, "BEGIN") || KeywordAt(sql, j, "DECLARE"))) continue;
                            return j < n && sql[j] == ';' ? j + 1 : i;
                        }
                    }
                    continue;
                }
            }
            i++;
        }
        return i;
    }

    private static bool IsPsqlDefinitionStart(string sql, int i)
    {
        int j = i;
        if (KeywordAt(sql, j, "CREATE"))
        {
            j = SkipWordAndTrivia(sql, j, "CREATE");
            if (KeywordAt(sql, j, "OR"))
            {
                j = SkipWordAndTrivia(sql, j, "OR");
                if (!KeywordAt(sql, j, "ALTER")) return false;
                j = SkipWordAndTrivia(sql, j, "ALTER");
            }
        }
        else if (KeywordAt(sql, j, "RECREATE")) { j = SkipWordAndTrivia(sql, j, "RECREATE"); }
        else if (KeywordAt(sql, j, "ALTER")) { j = SkipWordAndTrivia(sql, j, "ALTER"); }
        else return false;

        return KeywordAt(sql, j, "PROCEDURE") || KeywordAt(sql, j, "TRIGGER")
            || KeywordAt(sql, j, "FUNCTION") || KeywordAt(sql, j, "PACKAGE");
    }

    private static int SkipWordAndTrivia(string s, int i, string word) => SkipTriviaAndComments(s, i + word.Length);

    private static int SkipString(string s, int i)
    {
        int n = s.Length; i++;
        while (i < n)
        {
            if (s[i] == '\'')
            {
                if (i + 1 < n && s[i + 1] == '\'') { i += 2; continue; }
                return i + 1;
            }
            i++;
        }
        return i;
    }

    private static int SkipQuotedIdent(string s, int i)
    {
        int n = s.Length; i++;
        while (i < n && s[i] != '"') i++;
        return i < n ? i + 1 : i;
    }

    private static int SkipLineComment(string s, int i)
    {
        while (i < s.Length && s[i] != '\n') i++;
        return i;
    }

    private static int SkipBlockComment(string s, int i)
    {
        int n = s.Length; i += 2;
        while (i + 1 < n && !(s[i] == '*' && s[i + 1] == '/')) i++;
        return i + 1 < n ? i + 2 : n;
    }

    private static int SkipParens(string s, int i)
    {
        int n = s.Length, depth = 0;
        while (i < n)
        {
            char c = s[i];
            if (c == '\'') { i = SkipString(s, i); continue; }
            if (c == '"') { i = SkipQuotedIdent(s, i); continue; }
            if (c == '-' && i + 1 < n && s[i + 1] == '-') { i = SkipLineComment(s, i); continue; }
            if (c == '/' && i + 1 < n && s[i + 1] == '*') { i = SkipBlockComment(s, i); continue; }
            if (c == '(') { depth++; i++; continue; }
            if (c == ')') { depth--; i++; if (depth == 0) return i; continue; }
            i++;
        }
        return i;
    }

    private static int SkipTriviaAndComments(string s, int i)
    {
        int n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '-' && i + 1 < n && s[i + 1] == '-') { i = SkipLineComment(s, i); continue; }
            if (c == '/' && i + 1 < n && s[i + 1] == '*') { i = SkipBlockComment(s, i); continue; }
            break;
        }
        return i;
    }

    private static bool KeywordAt(string s, int i, string keyword)
        => IsWordBoundary(s, i - 1) && Matches(s, i, keyword) && IsWordEndAt(s, i + keyword.Length);

    private static bool IsWordBoundary(string s, int index)
    {
        if (index < 0) return true;
        var c = s[index];
        return !(char.IsLetterOrDigit(c) || c == '_' || c == '$');
    }

    private static bool IsWordEndAt(string s, int index)
        => index >= s.Length || !(char.IsLetterOrDigit(s[index]) || s[index] == '_' || s[index] == '$');

    private static bool Matches(string s, int start, string token)
    {
        if (start + token.Length > s.Length) return false;
        for (int i = 0; i < token.Length; i++)
        {
            if (char.ToUpperInvariant(s[start + i]) != token[i]) return false;
        }
        return true;
    }

    private static void AddStatement(string raw, List<string> sink)
    {
        var trimmed = raw.Trim();
        if (trimmed.EndsWith(";", StringComparison.Ordinal)) trimmed = trimmed.Substring(0, trimmed.Length - 1).Trim();
        if (trimmed.Length > 0) sink.Add(trimmed);
    }
}

using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// The error-tolerant Firebird SQL/PSQL parser — Etap 2 of the editor rebuild. It turns the
/// lossless token stream from <see cref="SqlLexer"/> into a <see cref="SqlScript"/>: an ordered
/// list of top-level statements, each classified into its own <see cref="SqlStatement"/> node.
/// <para>
/// <b>Error tolerance (§4.2 #1):</b> the parser never throws and never returns null. Every byte of
/// the input lands in exactly one statement; a statement whose leading keyword it does not
/// recognise becomes a <see cref="RawStatement"/> (verbatim — the §0 safety valve), not an error.
/// </para>
/// <para>
/// <b>Depth (Etap 2, "statement skeleton"):</b> statements are classified but their interiors are
/// kept verbatim in <see cref="SqlStatement.Tokens"/>. Clause / expression / PSQL-body structure
/// is added in later etaps. This keeps the §0 round-trip guaranteed by the token stream, never by
/// grammar completeness.
/// </para>
/// <para>
/// <b>Single source of truth for statement boundaries.</b> The segmentation here is the one
/// authority for "what is a statement" — the DDL executor's splitter rides it (via
/// <see cref="SqlStatementSplitter"/>) rather than carrying its own scanner. The boundary rules
/// mirror the long-standing PSQL-aware splitter exactly (gotchas #55/#117/#128/#140/#152): a
/// plain statement ends at the next top-level <c>;</c> (BEGIN/CASE/END-depth and string/comment
/// aware — strings and comments are already opaque as tokens/trivia); a <c>CREATE/ALTER/RECREATE</c>
/// of a <c>PROCEDURE/TRIGGER/FUNCTION/PACKAGE</c> is kept whole from its header <c>AS</c> through
/// the <c>END</c> that closes the outermost <c>BEGIN</c>, so its DECLARE-section semicolons never
/// split it.
/// </para>
/// <para>Pure — no Avalonia, no Firebird driver — and offline unit-testable.</para>
/// </summary>
public static class SqlParser
{
    private static readonly IReadOnlyList<Diagnostic> NoDiagnostics = Array.Empty<Diagnostic>();

    /// <summary>Parses <paramref name="text"/> into a <see cref="SqlScript"/>. Never throws.</summary>
    public static ParseResult Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        var tokens = SqlLexer.Tokenize(text);

        // Significant tokens only (trivia is attached; the trailing EndOfFile token is the sentinel).
        var sig = new List<SqlToken>(tokens.Count);
        foreach (var t in tokens)
        {
            if (t.Kind != TokenKind.EndOfFile) sig.Add(t);
        }

        var statements = new List<SqlStatement>();
        int n = text.Length;
        int idx = 0;
        while (idx < sig.Count)
        {
            int startIdx = idx;
            int startChar = sig[startIdx].Start;

            (int endIdxExcl, int endChar) = IsPsqlDefinitionStart(sig, startIdx)
                ? ScanPsql(sig, startIdx, n)
                : ScanPlain(sig, startIdx, n);

            var slice = sig.GetRange(startIdx, endIdxExcl - startIdx);
            statements.Add(Classify(slice, startChar, endChar - startChar));
            idx = endIdxExcl;
        }

        return new ParseResult(new SqlScript(text, tokens, statements), NoDiagnostics);
    }

    // ── Statement segmentation (the O5 boundary authority) ────────────────────────────────────

    // Plain statement: ends at the next top-level ';' (BEGIN/CASE/END-depth aware). Returns the
    // exclusive token index and the char offset just past the ';' — or (all-consumed, text length)
    // when it runs to the end without a terminator, matching the legacy char scanner exactly.
    private static (int EndIdxExcl, int EndChar) ScanPlain(IReadOnlyList<SqlToken> sig, int start, int n)
    {
        int i = start, depth = 0;
        while (i < sig.Count)
        {
            var t = sig[i];
            if (t.Kind == TokenKind.Semicolon && depth == 0)
            {
                return (i + 1, t.End);
            }
            if (Kw(t, "BEGIN") || Kw(t, "CASE")) depth++;
            else if (Kw(t, "END")) { if (depth > 0) depth--; }
            i++;
        }
        return (sig.Count, n);
    }

    // PSQL definition: one statement, body semicolons included. Phase 1 (before AS): skip balanced
    // parens (so an AS inside CAST(x AS y) / a param list is not the body separator) and end at a
    // top-level ';' (a bodyless UDR/EXTERNAL header). Phase 2 (after AS): track BEGIN/CASE/END depth
    // and end at the END closing the outermost BEGIN — peeking past a FB3 subprogram's END.
    private static (int EndIdxExcl, int EndChar) ScanPsql(IReadOnlyList<SqlToken> sig, int start, int n)
    {
        int i = start, depth = 0;
        bool pastAs = false, bodyOpened = false;
        while (i < sig.Count)
        {
            var t = sig[i];

            if (!pastAs)
            {
                if (t.Kind == TokenKind.LParen) { i = SkipParens(sig, i); continue; }
                if (Kw(t, "AS")) { pastAs = true; i++; continue; }
                if (t.Kind == TokenKind.Semicolon) return (i + 1, t.End); // header, no PSQL body
                i++;
                continue;
            }

            if (Kw(t, "BEGIN")) { depth++; bodyOpened = true; i++; continue; }
            if (Kw(t, "CASE")) { if (depth > 0) depth++; i++; continue; }
            if (Kw(t, "END"))
            {
                i++; // past END
                if (depth > 0)
                {
                    depth--;
                    if (depth == 0 && bodyOpened)
                    {
                        // A subprogram's END (more DECLAREs / the main BEGIN follow) → keep scanning.
                        if (i < sig.Count && (Kw(sig[i], "BEGIN") || Kw(sig[i], "DECLARE"))) continue;
                        if (i < sig.Count && sig[i].Kind == TokenKind.Semicolon) return (i + 1, sig[i].End);
                        return (i, sig[i - 1].End); // main body closed, no ';': end right after END
                    }
                }
                continue;
            }
            i++;
        }
        return (sig.Count, n);
    }

    // With the cursor on '(', returns the token index just past the matching ')' (nesting-aware;
    // strings/comments are already opaque tokens/trivia).
    private static int SkipParens(IReadOnlyList<SqlToken> sig, int i)
    {
        int depth = 0;
        while (i < sig.Count)
        {
            var kind = sig[i].Kind;
            if (kind == TokenKind.LParen) { depth++; i++; continue; }
            if (kind == TokenKind.RParen) { depth--; i++; if (depth == 0) return i; continue; }
            i++;
        }
        return i;
    }

    // CREATE [OR ALTER] | ALTER | RECREATE  +  PROCEDURE | TRIGGER | FUNCTION | PACKAGE.
    // (ALTER TABLE / CREATE VIEW … AS SELECT / CREATE GENERATOR etc. are NOT PSQL definitions.)
    private static bool IsPsqlDefinitionStart(IReadOnlyList<SqlToken> sig, int start)
    {
        int j = start;
        if (Kw(At(sig, j), "CREATE"))
        {
            j++;
            if (Kw(At(sig, j), "OR"))
            {
                j++;
                if (!Kw(At(sig, j), "ALTER")) return false;
                j++;
            }
        }
        else if (Kw(At(sig, j), "RECREATE")) j++;
        else if (Kw(At(sig, j), "ALTER")) j++;
        else return false;

        var t = At(sig, j);
        return Kw(t, "PROCEDURE") || Kw(t, "TRIGGER") || Kw(t, "FUNCTION") || Kw(t, "PACKAGE");
    }

    // ── Classification into the typed statement nodes ─────────────────────────────────────────

    private static SqlStatement Classify(IReadOnlyList<SqlToken> slice, int start, int length)
    {
        if (slice.Count == 0)
        {
            return new RawStatement(start, length, slice); // defensive — a scan always consumes ≥1 token
        }

        var first = slice[0];
        if (first.Kind == TokenKind.Semicolon)
        {
            return new EmptyStatement(start, length, slice);
        }

        string? word = first.Kind is TokenKind.Keyword or TokenKind.Identifier ? first.Text : null;
        if (word is null)
        {
            return new RawStatement(start, length, slice);
        }

        switch (word.ToUpperInvariant())
        {
            case "SELECT":
            case "WITH":
                return new SelectStatement(start, length, slice);
            case "INSERT":
                return new InsertStatement(start, length, slice);
            case "UPDATE":
                return IsUpdateOrInsert(slice)
                    ? new UpdateOrInsertStatement(start, length, slice)
                    : new UpdateStatement(start, length, slice);
            case "DELETE":
                return new DeleteStatement(start, length, slice);
            case "MERGE":
                return new MergeStatement(start, length, slice);
            case "EXECUTE":
                return ClassifyExecute(slice, start, length);
            case "CREATE":
            case "ALTER":
            case "RECREATE":
            case "DROP":
                return BuildDdl(slice, start, length);
            case "COMMENT":
                return new CommentStatement(start, length, slice);
            case "SET":
                return new SetStatement(start, length, slice, WordValueAt(slice, 1));
            case "GRANT":
                return new GrantStatement(start, length, slice);
            case "REVOKE":
                return new RevokeStatement(start, length, slice);
            case "BEGIN":
                // A bare anonymous PSQL block (a formattable body, not unparseable input).
                return new AnonymousBlockStatement(start, length, slice);
            case "DECLARE":
                // A top-level DECLARE that runs into a BEGIN is a PSQL body fragment (a DECLARE
                // section + local subprograms + main block, e.g. the body editor's text), NOT a
                // top-level DECLARE EXTERNAL FUNCTION / DECLARE FILTER (which has no BEGIN).
                return ContainsBeginKeyword(slice)
                    ? new AnonymousBlockStatement(start, length, slice)
                    : new DeclareStatement(start, length, slice);
            default:
                return new RawStatement(start, length, slice);
        }
    }

    // True when the slice contains a BEGIN keyword token (a valid DECLARE EXTERNAL FUNCTION /
    // DECLARE FILTER never does). Used to tell a top-level declaration from a PSQL body fragment.
    private static bool ContainsBeginKeyword(IReadOnlyList<SqlToken> slice)
    {
        foreach (var t in slice)
        {
            if (Kw(t, "BEGIN")) return true;
        }
        return false;
    }

    private static bool IsUpdateOrInsert(IReadOnlyList<SqlToken> slice)
        => slice.Count >= 3 && Kw(slice[1], "OR") && Kw(slice[2], "INSERT");

    private static SqlStatement ClassifyExecute(IReadOnlyList<SqlToken> slice, int start, int length)
    {
        if (slice.Count >= 2)
        {
            if (Kw(slice[1], "BLOCK")) return new ExecuteBlockStatement(start, length, slice);
            if (Kw(slice[1], "PROCEDURE"))
                return new ExecuteProcedureStatement(start, length, slice, ReadProcedureName(slice));
        }
        return new ExecuteStatementStatement(start, length, slice);
    }

    // EXECUTE PROCEDURE <name> — unquoted name upper-cased (catalog convention), quoted name kept
    // in its literal case; null when there is no readable identifier.
    private static string? ReadProcedureName(IReadOnlyList<SqlToken> slice)
    {
        if (slice.Count < 3) return null;
        var t = slice[2];
        return t.Kind switch
        {
            TokenKind.QuotedIdentifier => t.Value,
            TokenKind.Keyword or TokenKind.Identifier => t.Text.ToUpperInvariant(),
            _ => null,
        };
    }

    private static DdlStatement BuildDdl(IReadOnlyList<SqlToken> slice, int start, int length)
    {
        // Verb + the index just past the verb phrase.
        DdlVerb verb;
        int afterVerb;
        if (Kw(slice[0], "CREATE"))
        {
            if (slice.Count >= 3 && Kw(slice[1], "OR") && Kw(slice[2], "ALTER"))
            {
                verb = DdlVerb.CreateOrAlter;
                afterVerb = 3;
            }
            else
            {
                verb = DdlVerb.Create;
                afterVerb = 1;
            }
        }
        else if (Kw(slice[0], "RECREATE")) { verb = DdlVerb.Recreate; afterVerb = 1; }
        else if (Kw(slice[0], "DROP")) { verb = DdlVerb.Drop; afterVerb = 1; }
        else { verb = DdlVerb.Alter; afterVerb = 1; }

        bool isPsql = IsPsqlDefinitionStart(slice, 0);

        // Best-effort object kind + name: skip modifier keywords, read the object keyword, then the
        // next identifier (skipping IF [NOT] EXISTS). Not consumed by any Etap-2 client; a miss just
        // leaves Unknown/null — the interior stays verbatim regardless.
        int j = afterVerb;
        while (j < slice.Count && IsDdlModifier(slice[j])) j++;

        var objectKind = DdlObjectKind.Unknown;
        if (j < slice.Count)
        {
            objectKind = MapObjectKind(slice[j]);
            if (objectKind != DdlObjectKind.Unknown) j++;
        }

        while (j < slice.Count && IsExistenceGuard(slice[j])) j++;
        string? objectName = j < slice.Count ? ReadIdentifierName(slice[j]) : null;

        return new DdlStatement(start, length, slice, verb, objectKind, objectName, isPsql);
    }

    private static bool IsDdlModifier(SqlToken t)
    {
        // Some spellings (e.g. DESCENDING) are not catalogued keywords and lex as identifiers, so
        // match by text over both word kinds — this is best-effort header sugar, never an object name.
        if (t.Kind != TokenKind.Keyword && t.Kind != TokenKind.Identifier) return false;
        return t.Text.ToUpperInvariant() switch
        {
            "UNIQUE" or "ASC" or "ASCENDING" or "DESC" or "DESCENDING"
                or "GLOBAL" or "TEMPORARY" or "EXTERNAL" => true,
            _ => false,
        };
    }

    private static bool IsExistenceGuard(SqlToken t)
    {
        if (t.Kind != TokenKind.Keyword) return false;
        var u = t.Text.ToUpperInvariant();
        return u is "IF" or "NOT" or "EXISTS";
    }

    private static DdlObjectKind MapObjectKind(SqlToken t)
    {
        if (t.Kind != TokenKind.Keyword && t.Kind != TokenKind.Identifier) return DdlObjectKind.Unknown;
        return t.Text.ToUpperInvariant() switch
        {
            "TABLE" => DdlObjectKind.Table,
            "VIEW" => DdlObjectKind.View,
            "INDEX" => DdlObjectKind.Index,
            "SEQUENCE" => DdlObjectKind.Sequence,
            "GENERATOR" => DdlObjectKind.Generator,
            "PROCEDURE" => DdlObjectKind.Procedure,
            "FUNCTION" => DdlObjectKind.Function,
            "TRIGGER" => DdlObjectKind.Trigger,
            "DOMAIN" => DdlObjectKind.Domain,
            "EXCEPTION" => DdlObjectKind.Exception,
            "ROLE" => DdlObjectKind.Role,
            "PACKAGE" => DdlObjectKind.Package,
            "COLLATION" => DdlObjectKind.Collation,
            "FILTER" => DdlObjectKind.Filter,
            _ => DdlObjectKind.Unknown,
        };
    }

    private static string? ReadIdentifierName(SqlToken t) => t.Kind switch
    {
        TokenKind.QuotedIdentifier => t.Value,
        TokenKind.Identifier => t.Text.ToUpperInvariant(),
        // A bare keyword as an object name is unusual; keep it verbatim rather than guess.
        _ => null,
    };

    // The word value of the token at <paramref name="index"/> (its text; a quoted identifier's
    // decoded name), or null when it is not a word token.
    private static string? WordValueAt(IReadOnlyList<SqlToken> slice, int index)
    {
        if (index < 0 || index >= slice.Count) return null;
        var t = slice[index];
        return t.Kind switch
        {
            TokenKind.Keyword or TokenKind.Identifier => t.Text,
            TokenKind.QuotedIdentifier => t.Value,
            _ => null,
        };
    }

    // ── Token predicates ──────────────────────────────────────────────────────────────────────

    private static readonly SqlToken NoToken =
        new(TokenKind.EndOfFile, 0, 0, string.Empty, Array.Empty<SqlTrivia>());

    private static SqlToken At(IReadOnlyList<SqlToken> sig, int index)
        => index >= 0 && index < sig.Count ? sig[index] : NoToken;

    // A keyword token whose text equals <paramref name="keyword"/> (case-insensitive). Only
    // unquoted, catalogued keywords lex as TokenKind.Keyword, so a quoted "BEGIN" (a
    // QuotedIdentifier) never matches — mirroring the legacy scanner, which skipped quoted runs
    // before its keyword check.
    private static bool Kw(SqlToken t, string keyword)
        => t.Kind == TokenKind.Keyword && string.Equals(t.Text, keyword, StringComparison.OrdinalIgnoreCase);
}

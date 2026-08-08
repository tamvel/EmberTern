using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// ⭐⭐ <b>Where Firebird's grammar — not an expression — decides what a bare word means.</b> The one owner
/// of that question, asked by both of the binder's expression walkers (PSQL and query).
/// <para>
/// <b>The problem it exists for.</b> Firebird reserves very few words, so most of its own vocabulary
/// (<c>MONTH</c>, <c>PLACING</c>, <c>UNBOUNDED</c>, <c>OFB</c>, …) lexes as an ordinary IDENTIFIER — that is
/// deliberate, because a user may legally name a column <c>MONTH</c>. An expression walker therefore cannot
/// tell <c>DATEADD(MONTH, 1, d)</c>'s unit from a column reference by looking at the token: it has to look at
/// the POSITION.
/// </para>
/// <para>
/// ⭐ <b>Two answers, not one, because the consequences differ.</b> A GENERATOR name must be
/// <em>resolved</em> (an unknown one is a provable ET0001, so dropping it would lose a finding);
/// a SYNTAX word must be claimed by <em>nobody</em> (it resolves to nothing at all, and both a variable
/// reference and a column reference would be wrong). Merging them into one predicate would either make the
/// catalog scan look up a sequence called <c>YEAR</c> or stop it looking up generators.
/// </para>
/// <para>
/// ⚠ <b>Why the syntax-word rule is POSITIONAL and not merely a word list.</b> A word list is the right
/// tool for suppressing a <em>diagnostic</em> (see <see cref="FirebirdSyntax.IsNonReservedWord"/>) because
/// its only effect is silence. It is the wrong tool for the QUERY walker, where the same word must still
/// bind as a column when it really is one: <c>SELECT MONTH FROM SALES</c> has to keep its colour, its Quick
/// Info and its find-references. So here the vocabulary is only a cheap pre-filter, and the decision is made
/// by the construct the word sits in.
/// </para>
/// </summary>
public static class FirebirdGrammar
{
    // How far back a bounded scan will look for an enclosing '('. An argument list longer than this is
    // pathological; stopping keeps the walk linear on a large script instead of quadratic.
    private const int EnclosingScanLimit = 512;

    // ── Built-ins whose ARGUMENT GRAMMAR contains syntactic words ─────────────────────────────
    //
    // Transcribed from the Firebird 5 Language Reference's built-in function chapter — every function
    // whose argument list can hold a bare word that is not an expression. ⛔ The list is deliberately
    // EXPLICIT rather than "any function": inside COALESCE(MONTH, 0) the word IS a column, and a blanket
    // rule would strip its binding. Membership here is a statement about that function's grammar.
    private static readonly HashSet<string> WordSlotFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Date/time: the unit operand — EXTRACT(<part> FROM …), DATEADD(<part>, …) and DATEADD(n <part> TO …),
        // DATEDIFF(<part> FROM … TO …), FIRST_DAY/LAST_DAY(OF <part> FROM …).
        "EXTRACT", "DATEADD", "DATEDIFF", "FIRST_DAY", "LAST_DAY",
        // String: OVERLAY(… PLACING … FROM …), and the FROM/FOR/SIMILAR forms whose markers are keywords.
        "OVERLAY", "SUBSTRING", "TRIM", "POSITION",
        // Cryptography: USING <algorithm> [MODE <mode>] KEY … IV … CTR_LENGTH … COUNTER …
        "HASH", "CRYPT_HASH", "ENCRYPT", "DECRYPT",
        "RSA_ENCRYPT", "RSA_DECRYPT", "RSA_SIGN_HASH", "RSA_VERIFY_HASH", "RSA_PRIVATE", "RSA_PUBLIC",
        // Window: NTH_VALUE(…, n) FROM FIRST|LAST.
        "NTH_VALUE",
    };

    // Functions whose FIRST argument is always a unit word, never an expression. Their opening slot is
    // pinned WITHOUT a vocabulary check — see IsSyntaxWordPosition for why that matters while typing.
    private static readonly HashSet<string> UnitFirstArgFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "EXTRACT", "DATEADD", "DATEDIFF",
    };

    // ── Multi-word phrases whose words are syntax wherever they appear ────────────────────────
    //
    // Only the phrases reachable from a QUERY clause need to be here: inside a PSQL body the vocabulary
    // rule already keeps the walker quiet, and it is only in a query that a like-named column could
    // otherwise claim the word. Each entry is the whole phrase; a token is syntax when it falls inside a
    // match. Words are compared by text so it does not matter whether one lexes as a keyword.
    private static readonly string[][] SyntaxPhrases =
    {
        new[] { "AT", "TIME", "ZONE" },
        new[] { "AT", "LOCAL" },
        new[] { "NULLS", "FIRST" },
        new[] { "NULLS", "LAST" },
        new[] { "WITH", "LOCK" },
        new[] { "TYPE", "OF", "COLUMN" },
        new[] { "TYPE", "OF" },
    };

    private static readonly int LongestPhrase = 3;

    /// <summary>
    /// Does Firebird's grammar say the name token at <paramref name="k"/> is a GENERATOR (sequence) name
    /// rather than an ordinary expression? True in exactly two positions: the operand of
    /// <c>NEXT VALUE FOR</c>, and the FIRST argument of <c>GEN_ID(…)</c>.
    /// <para>
    /// Those two are the whole list, measured on FB5 (2026-08-01) rather than assumed: <c>GEN_ID</c> takes a
    /// bare identifier (<c>GEN_ID(GEN_ORDER_ID, 0)</c> → 999), while <c>MAKE_DBKEY</c>'s first argument is an
    /// ordinary expression — a bare name there is rejected by the engine with "-206 Column unknown" — and
    /// <c>RDB$GET_CONTEXT</c> / <c>RDB$SET_CONTEXT</c> take string literals, which never lex as identifiers.
    /// </para>
    /// <para>
    /// ⚠ Asked by two binders with OPPOSITE jobs — the global catalog scan RESOLVES the name, while the PSQL
    /// expression walker must leave the occurrence unclaimed instead of treating it as a local variable. A
    /// partial second copy of it (a bare "is the previous token FOR" test, which covered
    /// <c>NEXT VALUE FOR</c> but not <c>GEN_ID</c>) is exactly how GEN_ID's argument came to be reported as an
    /// unresolved variable (gotcha #302).
    /// </para>
    /// </summary>
    public static bool IsGeneratorNamePosition(IReadOnlyList<SqlToken> t, int k)
    {
        // NEXT VALUE FOR <name> — each word may lex as a keyword or an identifier, so match by text.
        if (k >= 3 && IsWordText(t[k - 3], "NEXT") && IsWordText(t[k - 2], "VALUE") && IsWordText(t[k - 1], "FOR"))
        {
            return true;
        }

        // GEN_ID( <name> , … ) — the first argument only; every later one is an ordinary expression.
        return k >= 2 && t[k - 1].Kind == TokenKind.LParen && IsWordText(t[k - 2], "GEN_ID");
    }

    /// <summary>
    /// Is the name token at <paramref name="k"/> a <b>syntax word</b> — part of Firebird's own grammar rather
    /// than an operand — so that no binder may claim it as a variable or a column?
    /// <para>Four constructs, each a position the Language Reference fixes:</para>
    /// <list type="bullet">
    ///   <item>the first argument of <c>EXTRACT</c> / <c>DATEADD</c> / <c>DATEDIFF</c>, which is always a
    ///   date/time unit — pinned <b>without</b> a vocabulary check, deliberately: requiring one would make a
    ///   half-typed <c>EXTRACT(YEA</c> report an unresolved variable on every keystroke, and that is the state
    ///   the editor spends most of its time in;</item>
    ///   <item>anywhere inside the argument list of a <see cref="WordSlotFunctions">function whose grammar has
    ///   word slots</see>, for a word that is Firebird vocabulary (<c>PLACING</c>, <c>OFB</c>, <c>SHA256</c>, …);</item>
    ///   <item>anywhere inside a window specification — <c>OVER (…)</c> or <c>WINDOW w AS (…)</c> — for a word
    ///   that is Firebird vocabulary (<c>UNBOUNDED</c>, <c>PRECEDING</c>, <c>EXCLUDE</c>, <c>TIES</c>, …);</item>
    ///   <item>inside one of the <see cref="SyntaxPhrases">fixed phrases</see> (<c>AT TIME ZONE</c>,
    ///   <c>NULLS FIRST</c>, …).</item>
    /// </list>
    /// <para>
    /// ⭐ The vocabulary pre-filter is what makes this safe in a query: an identifier Firebird does not use
    /// is never touched, so an ordinary column inside <c>OVERLAY(…)</c> or an <c>OVER (…)</c> partition still
    /// resolves, colours and navigates. The cost is confined to a column literally named after a Firebird
    /// word AND written inside one of these constructs — where the grammar would read it as syntax anyway.
    /// </para>
    /// </summary>
    public static bool IsSyntaxWordPosition(IReadOnlyList<SqlToken> t, int k)
    {
        if (k < 0 || k >= t.Count) return false;

        // 1. The unit slot: the token immediately after `EXTRACT(` / `DATEADD(` / `DATEDIFF(`.
        if (k >= 2 && t[k - 1].Kind == TokenKind.LParen && IsWordIn(t[k - 2], UnitFirstArgFunctions))
        {
            return true;
        }

        // Everything below is vocabulary-gated. Checking it FIRST also keeps the enclosing-construct scans
        // off the hot path: an ordinary identifier costs one hash lookup and nothing else.
        if (!FirebirdSyntax.IsNonReservedWord(t[k].Text)) return false;

        if (IsInsideEnclosure(t, k, out var owner))
        {
            // 2. Inside a word-slot built-in's argument list.
            if (owner >= 0 && IsWordIn(t[owner], WordSlotFunctions)) return true;

            // 3. Inside a window specification: `OVER (` directly, or the `(` of `WINDOW name AS (`.
            if (owner >= 0 && IsWordText(t[owner], "OVER")) return true;
            if (owner >= 2 && IsWordText(t[owner], "AS") && IsWordText(t[owner - 2], "WINDOW")) return true;
        }

        // 4. A fixed multi-word phrase.
        return IsInsidePhrase(t, k);
    }

    /// <summary>
    /// Is the name token at <paramref name="k"/> in the TYPE position of a <c>CAST(<em>expr</em> AS
    /// <em>type</em>)</c> — i.e. after the cast's own top-level <c>AS</c> and before its closing paren?
    /// <para>
    /// ⚠ Reported as a region rather than a single token because a Firebird type is not one word:
    /// <c>CAST(x AS TYPE OF COLUMN ORDERS.AMOUNT)</c> and <c>CAST(x AS VARCHAR(10) CHARACTER SET WIN1250)</c>
    /// both put several bare words there. The caller may resolve the first of them as a domain (the
    /// <c>DECLARE VARIABLE</c> / parameter path already does), but none of them is an expression operand.
    /// </para>
    /// </summary>
    public static bool IsCastTypePosition(IReadOnlyList<SqlToken> t, int k)
    {
        if (!IsInsideEnclosure(t, k, out int owner) || owner < 0) return false;
        if (!IsWordText(t[owner], "CAST")) return false;

        // The cast's own top-level AS lies between its '(' (owner + 1) and k.
        int depth = 0;
        for (int i = owner + 2; i < k; i++)
        {
            var kind = t[i].Kind;
            if (kind == TokenKind.LParen) depth++;
            else if (kind == TokenKind.RParen) { if (depth > 0) depth--; }
            else if (depth == 0 && IsWordText(t[i], "AS")) return true;
        }
        return false;
    }

    /// <summary>
    /// Is the name token at <paramref name="k"/> a word of Firebird's vocabulary sitting <b>next to another
    /// word</b> — i.e. inside a multi-word phrase rather than standing alone as an operand?
    /// <para>
    /// ⭐⭐ This is the precision half of the vocabulary rule, and without it that rule is too blunt. Silencing
    /// every unresolved identifier that spells a Firebird word would also silence <c>v = year;</c>, which is a
    /// genuine unknown variable and was already pinned as one. Adjacency separates the two cases without
    /// enumerating constructs: <c>YEAR</c> alone between <c>=</c> and <c>;</c> is an operand and nothing else,
    /// while <c>… USING SHA256</c>, <c>… AT LOCAL</c>, <c>UNBOUNDED PRECEDING</c> and <c>OF MONTH</c> are all
    /// two words the grammar reads together.
    /// </para>
    /// <para>
    /// ⚠ Adjacency is about WORDS, not about punctuation: <c>DATEADD(MONTH, 1, d)</c> has a paren on one side
    /// and a comma on the other, so it is NOT covered here — it is covered by
    /// <see cref="IsSyntaxWordPosition"/>, which knows that slot. The two rules are complements, and neither
    /// is a superset of the other.
    /// </para>
    /// <para>
    /// ⚠ A QUOTED identifier never counts as the neighbouring word: quoting is how a user says "this is a
    /// name", so it is evidence against a phrase rather than for one.
    /// </para>
    /// </summary>
    public static bool IsVocabularyInsidePhrase(IReadOnlyList<SqlToken> t, int k)
    {
        if (k < 0 || k >= t.Count) return false;
        if (!FirebirdSyntax.IsNonReservedWord(t[k].Text)) return false;
        return IsPlainWord(t, k - 1) || IsPlainWord(t, k + 1);
    }

    private static bool IsPlainWord(IReadOnlyList<SqlToken> t, int i)
        => i >= 0 && i < t.Count && t[i].Kind is TokenKind.Identifier or TokenKind.Keyword;

    /// <summary>
    /// The length, in tokens, of a PSQL <b>statement prefix</b> starting at <paramref name="i"/> — text that
    /// decorates a following compound statement without changing what statement it is. <c>0</c> when there is
    /// none, which is the overwhelmingly common case.
    /// <list type="bullet">
    ///   <item><c>&lt;label&gt; :</c> — 2 tokens. A loop/block label, the target of <c>LEAVE &lt;label&gt;</c>.</item>
    ///   <item><c>IN AUTONOMOUS TRANSACTION DO</c> — 4 tokens.</item>
    /// </list>
    /// <para>
    /// ⭐ ONE owner, because two consumers need the identical answer for opposite reasons: the PARSER must
    /// step over the prefix to reach the statement it decorates (otherwise the statement collapses into a
    /// leaf that ends at the first nested semicolon), and the BINDER must step over it when walking that
    /// statement's header (otherwise a label reads as a variable reference and is reported as ET0003). The
    /// first half without the second fixes the structure and leaves the squiggle.
    /// </para>
    /// <para>
    /// ⚠ Both forms require a COMPOUND statement to follow. See the parser's <c>TryConsumeStatementPrefix</c>
    /// for why that narrowing is deliberate rather than an omission.
    /// </para>
    /// <para>
    /// ⚠ The label's colon must be a lone <c>:</c> operator — written tight (<c>retry:while</c>) the lexer
    /// produces a single Parameter token <c>:WHILE</c>, which no consumer can dispatch on.
    /// </para>
    /// </summary>
    public static int StatementPrefixLength(IReadOnlyList<SqlToken> t, int i)
    {
        if (i < 0 || i >= t.Count) return 0;

        if (t[i].Kind == TokenKind.Identifier
            && i + 2 < t.Count
            && t[i + 1].Kind == TokenKind.Operator && t[i + 1].Text == ":"
            && StartsCompoundStatement(t[i + 2]))
        {
            return 2;
        }

        if (IsWordText(t[i], "IN")
            && i + 4 < t.Count
            && IsWordText(t[i + 1], "AUTONOMOUS")
            && IsWordText(t[i + 2], "TRANSACTION")
            && IsWordText(t[i + 3], "DO")
            && StartsCompoundStatement(t[i + 4]))
        {
            return 4;
        }

        return 0;
    }

    /// <summary>The keywords that open a PSQL compound statement (one that can contain other statements).</summary>
    public static bool StartsCompoundStatement(SqlToken t)
        => IsWordText(t, "BEGIN") || IsWordText(t, "IF") || IsWordText(t, "WHILE") || IsWordText(t, "FOR");

    // ── Bounded backward scans ────────────────────────────────────────────────────────────────

    // Walks left from k to the '(' that encloses it, reporting the token just before that paren in
    // <paramref name="owner"/> (-1 when the paren opens the statement). False when k is not inside a paren
    // within the scan limit — which is also the answer for "the scan gave up", so a caller can never mistake
    // an abandoned scan for a negative result.
    private static bool IsInsideEnclosure(IReadOnlyList<SqlToken> t, int k, out int owner)
    {
        owner = -1;
        int depth = 0;
        int stop = Math.Max(0, k - EnclosingScanLimit);
        for (int i = k - 1; i >= stop; i--)
        {
            var kind = t[i].Kind;
            if (kind == TokenKind.RParen) depth++;
            else if (kind == TokenKind.LParen)
            {
                if (depth > 0) { depth--; continue; }
                owner = i - 1;
                return true;
            }
        }
        return false;
    }

    private static bool IsInsidePhrase(IReadOnlyList<SqlToken> t, int k)
    {
        foreach (var phrase in SyntaxPhrases)
        {
            // The token at k may be any word of the phrase, so try every alignment.
            for (int offset = 0; offset < phrase.Length; offset++)
            {
                int start = k - offset;
                if (start < 0 || start + phrase.Length > t.Count) continue;
                bool ok = true;
                for (int p = 0; p < phrase.Length; p++)
                {
                    if (!IsWordText(t[start + p], phrase[p])) { ok = false; break; }
                }
                if (ok) return true;
            }
        }
        return false;
    }

    private static bool IsWordText(SqlToken t, string text)
        => t.Kind is TokenKind.Identifier or TokenKind.Keyword
           && string.Equals(t.Text, text, StringComparison.OrdinalIgnoreCase);

    private static bool IsWordIn(SqlToken t, HashSet<string> set)
        => t.Kind is TokenKind.Identifier or TokenKind.Keyword && set.Contains(t.Text);

    /// <summary>Longest phrase length — exposed so a test can prove the scan window covers the table.</summary>
    internal static int LongestSyntaxPhrase => LongestPhrase;
}

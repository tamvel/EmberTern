using System.Text;
using EmberTern.Core.Sql;

namespace EmberTern.Core.Trace;

/// <summary>
/// Reduces a SQL statement to a stable <em>fingerprint</em> so identical queries
/// collapse into one group regardless of parameter values, literals, whitespace,
/// or formatting — the differentiator over IBExpert's flat grid ("this statement
/// ran 300× / 4.2 s total"). A pure function; the grouping/aggregation lives in
/// <see cref="TraceEventGrouper"/>.
/// <para>
/// Canonicalisation (token-based, reusing the shared <c>SqlScanHelpers</c> scanner —
/// no new tokenizer): string literals, numeric literals and parameters (<c>?</c>,
/// <c>:name</c>) become <c>?</c>; unquoted words (keywords + identifiers, which
/// Firebird folds to upper) become UPPER; quoted identifiers are kept verbatim
/// (case-sensitive object identity); comments are dropped; tokens are joined by a
/// single space so any spacing/operator-formatting difference (<c>a=b</c> vs
/// <c>a = b</c>) collapses. It is intentionally TEXTUAL, not semantic —
/// <c>SELECT WARTOSC FROM T</c> and <c>SELECT T.WARTOSC FROM T</c> are different
/// statements and stay different fingerprints.
/// </para>
/// </summary>
public static class TraceStatementFingerprinter
{
    /// <summary>Computes the fingerprint of <paramref name="sql"/>. Returns the empty string
    /// for null/blank input (so non-statement events simply have no fingerprint).</summary>
    public static string Fingerprint(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return string.Empty;

        var sb = new StringBuilder(sql!.Length);
        int i = 0;
        bool first = true;

        void Sep()
        {
            if (!first) sb.Append(' ');
            first = false;
        }

        while (i < sql.Length)
        {
            SqlScanHelpers.SkipTrivia(sql, ref i); // whitespace + line/block comments
            if (i >= sql.Length) break;

            char c = sql[i];

            if (c == '\'')
            {
                SqlScanHelpers.TrySkipQuoted(sql, ref i); // string literal → placeholder
                Sep(); sb.Append('?');
            }
            else if (c == '"')
            {
                var name = SqlScanHelpers.ReadIdentifier(sql, ref i); // quoted identifier — identity, kept verbatim
                Sep(); sb.Append('"').Append(name).Append('"');
            }
            else if (char.IsDigit(c))
            {
                SkipNumber(sql, ref i); // numeric literal → placeholder
                Sep(); sb.Append('?');
            }
            else if (c == ':' && i + 1 < sql.Length && SqlScanHelpers.IsIdentifierChar(sql[i + 1]) && !char.IsDigit(sql[i + 1]))
            {
                i++;                                   // named parameter :name → placeholder
                SqlScanHelpers.ReadWord(sql, ref i);
                Sep(); sb.Append('?');
            }
            else if (IsWordStart(c))
            {
                var w = SqlScanHelpers.ReadWord(sql, ref i);
                Sep(); sb.Append(w.ToUpperInvariant()); // keyword / identifier → upper
            }
            else
            {
                i++;                                    // punctuation / operator / '?' → its own single-char token
                Sep(); sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_' || c == '$';

    private static void SkipNumber(string s, ref int i)
    {
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int save = i;
            i++;
            if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
            if (i < s.Length && char.IsDigit(s[i])) { while (i < s.Length && char.IsDigit(s[i])) i++; }
            else i = save; // a bare 'e' wasn't an exponent
        }
    }
}

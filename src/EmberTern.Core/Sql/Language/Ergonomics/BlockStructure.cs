using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ergonomics;

/// <summary>
/// The PSQL block-nesting rule, in ONE place — shared by <see cref="KeywordPairing"/> (where does this
/// block's <c>end</c> go?) and <see cref="AutoIndent"/> (how deep is this line?). Both questions are the
/// same question, so they must not be answered by two counters that can disagree.
/// <para><b>CASE-aware</b> (gotchas #117/#128/#129): <c>CASE … END</c> contributes an <c>END</c> with no
/// <c>BEGIN</c>, so a bare begin/end counter silently mis-reads every body containing a CASE. CASE counts
/// as an opener because it consumes exactly one <c>END</c>. Token-level throughout, so a keyword inside a
/// string literal or a comment is never counted.</para>
/// </summary>
internal static class BlockStructure
{
    /// <summary>The document's significant tokens (no EOF).</summary>
    public static IReadOnlyList<SqlToken> Significant(string text)
    {
        var tokens = new List<SqlToken>();
        foreach (var t in SqlLexer.Tokenize(text))
        {
            if (t.IsEndOfFile) break;
            tokens.Add(t);
        }
        return tokens;
    }

    /// <summary>How many blocks are still open immediately before <paramref name="offset"/> — i.e. the
    /// nesting level of whatever sits there. Floored at zero so a stray surplus <c>end</c> cannot produce
    /// a negative indent.</summary>
    public static int DepthBefore(IReadOnlyList<SqlToken> tokens, int offset)
    {
        int depth = 0;
        foreach (var t in tokens)
        {
            if (t.Start >= offset) break;
            if (t.Kind != TokenKind.Keyword) continue;
            if (IsCloser(t.Text)) { if (depth > 0) depth--; }
            else if (IsOpener(t.Text)) depth++;
        }
        return depth;
    }

    /// <summary>Whether any opener in the whole token stream lacks its closer — "an <c>end</c> is
    /// genuinely missing". Deliberately NOT floored: a surplus <c>end</c> must be able to drive the
    /// balance negative so it reads as "closed", never as "open".</summary>
    public static bool HasUnclosedOpener(IReadOnlyList<SqlToken> tokens)
    {
        int depth = 0;
        foreach (var t in tokens)
        {
            if (t.Kind != TokenKind.Keyword) continue;
            if (IsCloser(t.Text)) depth--;
            else if (IsOpener(t.Text)) depth++;
        }
        return depth > 0;
    }

    /// <summary>The last significant token strictly before <paramref name="offset"/>, or null.</summary>
    public static SqlToken? PreviousSignificant(IReadOnlyList<SqlToken> tokens, int offset)
    {
        SqlToken? prev = null;
        foreach (var t in tokens)
        {
            if (t.End > offset) break;
            prev = t;
        }
        return prev;
    }

    /// <summary>A keyword that consumes one <c>end</c>: a <see cref="KeywordPairCatalog"/> opener, or
    /// <c>CASE</c> (which is not a pair — nothing auto-closes it — but does take an <c>END</c>).</summary>
    public static bool IsOpener(string word)
        => PairOpenedBy(word) is not null || word.Equals("case", StringComparison.OrdinalIgnoreCase);

    public static bool IsCloser(string word)
    {
        foreach (var p in KeywordPairCatalog.All)
        {
            if (word.Equals(p.Closer, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static KeywordPair? PairOpenedBy(string word)
    {
        foreach (var p in KeywordPairCatalog.All)
        {
            if (word.Equals(p.Opener, StringComparison.OrdinalIgnoreCase)) return p;
        }
        return null;
    }

    public static string Repeat(string unit, int times)
    {
        if (times <= 0) return string.Empty;
        var sb = new System.Text.StringBuilder(unit.Length * times);
        for (int i = 0; i < times; i++) sb.Append(unit);
        return sb.ToString();
    }

    /// <summary>The offset at which the line containing <paramref name="pos"/> begins.</summary>
    public static int LineStartOf(string text, int pos)
    {
        for (int i = Math.Min(pos, text.Length) - 1; i >= 0; i--)
        {
            if (text[i] == '\n') return i + 1;
        }
        return 0;
    }
}

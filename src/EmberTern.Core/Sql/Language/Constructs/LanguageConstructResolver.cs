using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Constructs;

/// <summary>
/// Resolves the text before the caret to the single language construct it is a natural prefix of — the
/// heart of <b>Language Completion</b> (design: <c>docs/design/editor-language-expansion.md</c>).
/// <para><b>Pure, synchronous, timing-independent.</b> It is a function of (text, caret) only — never of
/// the async <c>SemanticModel</c> — so arming can never depend on how far a background parse has caught
/// up (design §1/§5). It performs no grammar gating; that is layered on separately (a later milestone)
/// by intersecting the match with the set of construct-starts the grammar allows at the caret.</para>
/// <para>Rules: match the trailing typed word(s) against catalog spellings as a case-insensitive prefix;
/// arm only when exactly one construct matches (silent-until-unique, measured against the curated
/// catalog); multi-word constructs (<c>group by</c>) match across up to
/// <see cref="LanguageConstructCatalog.MaxWords"/> trailing words.</para>
/// </summary>
public static class LanguageConstructResolver
{
    /// <summary>The full resolve for the App: the construct the caret's text uniquely completes AND that
    /// the grammar allows to begin here. Null when nothing is armed. This is the one entry point the App
    /// calls per keystroke — pure, synchronous, timing-free (prefix match via <see cref="Match"/> gated by
    /// <see cref="ConstructContext"/>).</summary>
    public static ConstructMatch? Resolve(string text, int caret)
    {
        var match = Match(text, caret);
        if (match is null) return null;

        int prefixStart = caret - match.PrefixLength;
        var position = ConstructContext.Classify(text, prefixStart);
        return ConstructContext.Allows(match.Construct.Category, position) ? match : null;
    }

    /// <summary>Returns the construct the caret's preceding text uniquely completes, or null when there
    /// is no word before the caret, no catalog prefix matches, or the prefix is still ambiguous.
    /// Prefix matching only — no grammar gating (see <see cref="Resolve"/> for the gated result).</summary>
    public static ConstructMatch? Match(string text, int caret)
    {
        if (string.IsNullOrEmpty(text) || caret <= 0 || caret > text.Length) return null;

        // Walk left over the trailing run of identifier chars and single spaces (a keyword phrase may
        // contain interior spaces, e.g. "group by"); stop at any other char, tab, or newline.
        int runStart = caret;
        while (runStart > 0)
        {
            char c = text[runStart - 1];
            if (IsWordChar(c) || c == ' ') runStart--;
            else break;
        }

        // Absolute start offsets of the words inside that run (a "word" = maximal identifier-char span).
        var wordStarts = new List<int>();
        bool inWord = false;
        for (int i = runStart; i < caret; i++)
        {
            bool w = IsWordChar(text[i]);
            if (w && !inWord) wordStarts.Add(i);
            inWord = w;
        }
        if (wordStarts.Count == 0) return null; // caret sits after a space/boundary, no partial word

        // Try the longest trailing window first (last k words), shrinking until a window matches. A
        // window is the exact typed text from that word's start to the caret (interior spaces as typed).
        int maxK = Math.Min(wordStarts.Count, LanguageConstructCatalog.MaxWords);
        for (int k = maxK; k >= 1; k--)
        {
            int windowStart = wordStarts[wordStarts.Count - k];
            string window = text.Substring(windowStart, caret - windowStart);

            LanguageConstruct? only = null;
            int count = 0;
            foreach (var c in LanguageConstructCatalog.All)
            {
                if (!c.Spelling.StartsWith(window, StringComparison.OrdinalIgnoreCase)) continue;
                only = c;
                if (++count > 1) break;
            }

            if (count == 1) return new ConstructMatch(only!, caret - windowStart);
            if (count > 1) return null; // ambiguous at this window → silent until the user disambiguates
            // count == 0 → this window matches nothing; try a shorter one
        }
        return null;
    }

    // Firebird identifier characters. Kept broad (letters/digits/_/$) so a partial identifier is treated
    // as one unit and its tail never spuriously matches a keyword prefix; catalog spellings are letters
    // only, so a window containing a digit/underscore simply matches nothing.
    private static bool IsWordChar(char c)
        => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '$';
}

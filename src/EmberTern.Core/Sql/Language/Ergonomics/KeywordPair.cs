using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ergonomics;

/// <summary>
/// A structural delimiter pair spelled with keywords rather than punctuation — <c>begin … end</c>. An
/// opener that always needs its closer, exactly like <c>(</c>/<c>)</c> or <c>'</c>/<c>'</c>, which is why
/// it belongs to <b>Typing Ergonomics</b> and not to the Language Completion catalog (design
/// <c>editor-language-expansion.md</c> §3.1, §13 — a settled decision: Language Completion finishes
/// <i>constructs</i>, Typing Ergonomics maintains <i>pairs</i>).
/// </summary>
public sealed record KeywordPair(string Opener, string Closer);

/// <summary>
/// The keyword delimiter pairs Typing Ergonomics owns. <b>Declarative data only — no behaviour.</b> The
/// pairing itself (auto-close, caret placement, backspace-removes-the-pair) is the Typing Ergonomics
/// milestone; this catalog exists now because ownership is needed now: IntelliSense must not offer
/// <c>begin</c> as a keyword when another tool is responsible for it, and that fact has to be declared
/// somewhere that both tools can read.
/// <para>Same rule as <c>LanguageConstructCatalog.OwnedWords</c>: <b>one responsibility, one owner</b>,
/// and the owner declares its own vocabulary so no consumer keeps a hand-written copy that can drift.</para>
/// </summary>
public static class KeywordPairCatalog
{
    /// <summary>Every keyword pair, canonical (lowercase) spelling.</summary>
    public static IReadOnlyList<KeywordPair> All { get; } = new[]
    {
        new KeywordPair("begin", "end"),
    };

    /// <summary>
    /// The words Typing Ergonomics owns — both halves of every pair. Any other completion vocabulary must
    /// exclude these; derived from <see cref="All"/>, never hand-listed. Consumer:
    /// <c>CompletionEngine.AddKeywords</c>.
    /// </summary>
    public static IReadOnlySet<string> OwnedWords { get; } = ComputeOwnedWords(All);

    private static IReadOnlySet<string> ComputeOwnedWords(IReadOnlyList<KeywordPair> all)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in all)
        {
            owned.Add(p.Opener);
            owned.Add(p.Closer);
        }
        return owned;
    }
}

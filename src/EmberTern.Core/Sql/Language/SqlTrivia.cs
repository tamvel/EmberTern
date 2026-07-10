namespace EmberTern.Core.Sql.Language;

/// <summary>The kind of a piece of trivia (whitespace or comment).</summary>
public enum TriviaKind
{
    /// <summary>A run of whitespace.</summary>
    Whitespace,

    /// <summary>A line comment (<c>-- …</c> up to, but not including, the end of line).</summary>
    LineComment,

    /// <summary>A block comment (<c>/* … */</c>).</summary>
    BlockComment,
}

/// <summary>
/// One piece of insignificant text (whitespace or a comment) that precedes a token. Trivia is
/// preserved verbatim so the token stream round-trips byte-for-byte (the §0 Paramount Law
/// foundation: never lose information).
/// </summary>
/// <param name="Kind">Whitespace / line comment / block comment.</param>
/// <param name="Start">Absolute source offset where the trivia begins.</param>
/// <param name="Length">Length in characters.</param>
/// <param name="Text">The exact source text of the trivia.</param>
public readonly record struct SqlTrivia(TriviaKind Kind, int Start, int Length, string Text)
{
    /// <summary>Absolute source offset just past the trivia.</summary>
    public int End => Start + Length;
}

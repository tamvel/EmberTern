namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>
/// A half-open absolute source span <c>[Start, End)</c> — the location of a symbol declaration
/// or a symbol reference in the script text. Mirrors the offset/length convention the AST
/// (<see cref="Ast.SqlNode"/>) and the lexer (<see cref="SqlToken"/>) already use, so a semantic
/// span maps directly onto editor offsets. Pure value type.
/// </summary>
/// <param name="Start">Absolute source offset where the span begins.</param>
/// <param name="Length">Length of the span in characters (never negative).</param>
public readonly record struct TextSpan(int Start, int Length)
{
    /// <summary>Absolute source offset just past the span.</summary>
    public int End => Start + Length;

    /// <summary>True when <paramref name="offset"/> lies within the half-open span <c>[Start, End)</c>.</summary>
    public bool Contains(int offset) => offset >= Start && offset < End;

    /// <summary>Builds a span from inclusive start / exclusive end offsets.</summary>
    public static TextSpan FromBounds(int start, int end)
        => new(start, end > start ? end - start : 0);

    /// <summary>The span covering a lexer token.</summary>
    public static TextSpan Of(SqlToken token) => new(token.Start, token.Length);

    public override string ToString() => $"[{Start}..{End})";
}

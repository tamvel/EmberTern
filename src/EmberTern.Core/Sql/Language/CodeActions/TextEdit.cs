namespace EmberTern.Core.Sql.Language.CodeActions;

/// <summary>
/// One replacement in a document: put <paramref name="NewText"/> where <paramref name="Start"/> /
/// <paramref name="Length"/> currently are.
/// <para>
/// <b><paramref name="ExpectedOldText"/> is the safety mechanism, not a convenience.</b> It is what the
/// producer believed occupied that span when it built the edit. The applier re-reads the document and
/// refuses the WHOLE action unless every span still reads exactly that. The user can type between the
/// moment a fix is offered and the moment they pick it, and an offset that has drifted would put text in
/// the wrong place — the one outcome Architecture rule #11 exists to prevent. Comparing content is both
/// simpler and stronger than tracking document versions: it catches any drift, whatever caused it.
/// </para>
/// <para>Pure data — no Avalonia, no document type. See
/// <see href="../../docs/design/editor-quick-fixes.md">editor-quick-fixes.md</see> §5/§6.</para>
/// </summary>
/// <param name="Start">Absolute source offset where the replaced span begins.</param>
/// <param name="Length">Length of the replaced span, in characters (0 for a pure insertion).</param>
/// <param name="NewText">The text to put there.</param>
/// <param name="ExpectedOldText">The text the producer expects to find at that span (empty for an
/// insertion). Verified before anything is written.</param>
public readonly record struct TextEdit(int Start, int Length, string NewText, string ExpectedOldText)
{
    /// <summary>Absolute source offset just past the replaced span.</summary>
    public int End => Start + Length;
}

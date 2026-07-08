namespace EmberTern.Core.Query;

/// <summary>
/// How a statement is executed — how many rows, and for what purpose. The mode is
/// inferred from the user's action (F5 = Preview, Shift+F5 / "Load all rows" = Full),
/// never from a persistent toggle.
/// </summary>
public enum ExecutionIntent
{
    /// <summary>Stream until <see cref="ExecutionRequest.PreviewLimit"/>, then stop and
    /// flag the result <see cref="QueryResult.Truncated"/>. The default "let me look" mode.</summary>
    Preview,

    /// <summary>Stream <b>all</b> rows into memory, up to a hard
    /// <see cref="ExecutionRequest.FullSafetyCeiling"/> backstop (past which the result is
    /// flagged <see cref="QueryResult.CeilingHit"/>). Used by "Load all rows" and Shift+F5.</summary>
    Full,

    /// <summary>Run the whole query but <b>discard</b> rows — count + time only. Reserved for a
    /// later etap (feeds the Performance advisor with whole-set reads); not yet surfaced in the UI.</summary>
    Benchmark,
}

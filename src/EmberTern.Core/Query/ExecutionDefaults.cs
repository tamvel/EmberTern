namespace EmberTern.Core.Query;

/// <summary>
/// Single source of truth for execution row limits. Hardcoded for now — there is deliberately
/// no user configuration yet. They live here (never as scattered literals) so that moving them
/// into user settings later, when EmberTern's configuration system is designed, is a one-line
/// change at the call site that fills an <see cref="ExecutionRequest"/> — nothing deeper, because
/// the request already carries the limits as values rather than reading a global.
/// </summary>
public static class ExecutionDefaults
{
    /// <summary>Preview (F5) stops after this many rows and flags the result
    /// <see cref="QueryResult.Truncated"/>. Matches the historical cap (no behaviour regression).</summary>
    public const int PreviewLimit = 5000;

    /// <summary>Full stops after this many rows as a hard memory backstop and flags the result
    /// <see cref="QueryResult.CeilingHit"/>. The <i>smart soft threshold</i> (a mid-stream
    /// "keep loading?" prompt) lands in a later etap and sits below this hard ceiling.</summary>
    public const long FullSafetyCeiling = 1_000_000;
}

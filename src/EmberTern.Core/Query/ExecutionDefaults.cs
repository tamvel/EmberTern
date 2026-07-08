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
    /// <see cref="QueryResult.CeilingHit"/>.</summary>
    public const long FullSafetyCeiling = 1_000_000;

    /// <summary>Smart soft threshold (Etap 2): while a Full load streams, once this many rows are
    /// read AND more remain, the user is asked once whether to keep loading the whole result into
    /// memory (a mid-stream "keep loading? / stop here" prompt). Sits below <see cref="FullSafetyCeiling"/>,
    /// so a normal-sized result never prompts and only a genuinely large one does.</summary>
    public const long FullSoftThreshold = 250_000;
}

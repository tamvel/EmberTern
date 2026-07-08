using System.Collections.Generic;

namespace EmberTern.Core.Query;

/// <summary>
/// A single execution described end-to-end: the SQL, the <see cref="ExecutionIntent"/>, and the
/// row limits as <b>values</b> (not a global lookup) so the executor is fully parameterised and the
/// limits' eventual home (settings) is decided only at the VM call site. Optional bound parameters
/// (Smart SQL Parameters / Execute Procedure) are carried through unchanged.
/// </summary>
public sealed record ExecutionRequest
{
    public required string Sql { get; init; }
    public ExecutionIntent Intent { get; init; } = ExecutionIntent.Preview;
    public int PreviewLimit { get; init; } = ExecutionDefaults.PreviewLimit;
    public long FullSafetyCeiling { get; init; } = ExecutionDefaults.FullSafetyCeiling;
    public IReadOnlyList<QueryParameter>? Parameters { get; init; }
}

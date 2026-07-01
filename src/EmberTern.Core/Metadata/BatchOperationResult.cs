namespace EmberTern.Core.Metadata;

/// <summary>
/// One row in a bulk-operation report (recompile / recompute statistics /
/// activate-deactivate triggers / any future bulk metadata op). <see cref="Success"/>
/// drives the Result column; <see cref="Error"/> carries the server message on failure.
/// Streamed into the live batch-results dialog one at a time as each object completes.
/// </summary>
public sealed record BatchOperationResult(string Object, string Operation, bool Success, string? Error);

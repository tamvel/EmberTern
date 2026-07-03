using System;

namespace EmberTern.Core.Performance;

/// <summary>Self-measured execution timings. In Phase 1 <see cref="Prepare"/> is the
/// statement-prepare cost and <see cref="Execute"/> is the combined execute+fetch cost
/// (the executor reports one span); <see cref="Fetch"/> is reserved for a finer split in
/// a later phase and is null for now.</summary>
public sealed record ExecutionTimings
{
    public TimeSpan? Prepare { get; init; }

    public required TimeSpan Execute { get; init; }

    public TimeSpan? Fetch { get; init; }

    /// <summary>True when this was the first run against a cold cache (numbers are a
    /// one-off and should not be optimized against). Reserved; false in Phase 1.</summary>
    public bool ColdCache { get; init; }

    public TimeSpan Total => (Prepare ?? TimeSpan.Zero) + Execute + (Fetch ?? TimeSpan.Zero);
}

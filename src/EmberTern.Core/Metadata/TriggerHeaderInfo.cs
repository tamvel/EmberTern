namespace EmberTern.Core.Metadata;

/// <summary>
/// Structured header of a relation (table/view) trigger — the catalog-derived
/// metadata the Trigger Detail Easy mode edits, alongside the body. Decoded from
/// <c>RDB$TRIGGERS</c> (relation name + the bit-encoded <c>RDB$TRIGGER_TYPE</c> +
/// sequence + inactive flag). DB-level / DDL triggers (type ≥ 8192) are out of
/// scope for V1; the reader returns a best-effort header for those.
/// </summary>
public sealed class TriggerHeaderInfo
{
    /// <summary>The table (or view) the trigger fires for.</summary>
    public string Table { get; init; } = string.Empty;

    /// <summary>True = BEFORE timing, false = AFTER.</summary>
    public bool IsBefore { get; init; }

    public bool FiresInsert { get; init; }
    public bool FiresUpdate { get; init; }
    public bool FiresDelete { get; init; }

    /// <summary>Firing order (<c>RDB$TRIGGER_SEQUENCE</c>).</summary>
    public int Position { get; init; }

    /// <summary>True when the trigger is ACTIVE (RDB$TRIGGER_INACTIVE != 1).</summary>
    public bool Active { get; init; } = true;
}

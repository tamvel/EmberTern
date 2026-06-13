namespace EmberTern.Core.Connections;

/// <summary>
/// Firebird transaction profile, mirroring IBExpert's transaction presets so it
/// is intuitive for Firebird admins. Selected per connection; affects only
/// transactions started AFTER the change (an active transaction keeps its
/// parameters until Commit/Rollback). Maps to a TPB in
/// <c>TransactionService.BuildTransactionOptions</c>.
///
/// The numeric order matters for JSON forward-compat: <see cref="ReadCommitted"/>
/// is 0 so older connection profiles (without the field) deserialize to the safe
/// default.
/// </summary>
public enum TransactionProfile
{
    /// <summary>isc_tpb_write + read_committed + rec_version + nowait. The safe default.</summary>
    ReadCommitted = 0,

    /// <summary>isc_tpb_write + concurrency + nowait. Stable snapshot of the database.</summary>
    Snapshot,

    /// <summary>isc_tpb_read + consistency. Table stability — read-only; can block other users.</summary>
    ReadOnlyTableStability,

    /// <summary>isc_tpb_write + consistency. Table stability — read-write; can block other users.</summary>
    ReadWriteTableStability,
}

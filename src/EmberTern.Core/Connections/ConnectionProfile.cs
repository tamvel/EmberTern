using System.Text.Json.Serialization;

namespace EmberTern.Core.Connections;

public sealed class ConnectionProfile
{
    public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3050;
    public string DatabasePath { get; set; } = string.Empty;
    public string Username { get; set; } = "SYSDBA";
    public string Password { get; set; } = string.Empty;
    public string Charset { get; set; } = "WIN1250";
    public int Dialect { get; set; } = 3;
    public string ClientLibraryPath { get; set; } = string.Empty;

    // Developer Mode (single user-facing switch, replaces the old TPB profile pickers).
    // A WAIT POLICY, not a transaction or a lane. DDL always runs WAIT + a bounded lock timeout;
    // the modes differ only in how long: OFF (default) is short — long enough to absorb our own
    // other lane's transient metadata-cache release (~10 ms, gotcha #214) while still failing fast
    // against another SESSION; ON is long, i.e. actually wait for another session to release the
    // object. Built by FirebirdDdlExecutor.BuildDdlTransactionOptions(bool).
    //
    // Scope — the object editors' Compile/Recompile, and the Script Executor's transaction when a
    // script is ALL DDL under auto-commit. Data operations always stay NOWAIT, and the SQL Editor
    // never consults it (it is a console: one working transaction, always NOWAIT).
    public bool DeveloperMode { get; set; }

    // Transaction profile for the DATA working transaction — SQL Editor F5, data
    // preview, and inline data editing run on connection #1 under this profile
    // (see TransactionProfile). Defaults to ReadCommitted.
    public TransactionProfile DataTransactionProfile { get; set; } = TransactionProfile.ReadCommitted;

    // Transaction profile for the METADATA working transaction — DDL from the
    // structure editor, Shift+F5 ("Execute on Metadata"), and structure refresh
    // run on connection #2 under this profile. Defaults to ReadCommitted so a
    // metadata-only profile change never leaks into everyday data work.
    public TransactionProfile MetadataTransactionProfile { get; set; } = TransactionProfile.ReadCommitted;

    // Legacy single-profile field (pre-C2 Data/Metadata split). Read-only migration
    // shim: settings written before the split carry "TransactionProfile". On load it
    // is mapped into DataTransactionProfile (MetadataTransactionProfile stays
    // ReadCommitted — variant A) by ApplicationSettingsStore, then cleared so it is
    // never written again. Omitted from output when null.
    [JsonPropertyName("TransactionProfile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TransactionProfile? LegacyTransactionProfile { get; set; }
}

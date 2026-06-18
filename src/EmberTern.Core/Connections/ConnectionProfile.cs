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
    // OFF (default): DDL runs NOWAIT — fail-fast on an in-use object. ON: DDL runs WAIT
    // + a lock timeout, so a Compile of an object currently used by other sessions waits
    // briefly for it to be released instead of immediately returning "object is in use".
    // Affects ONLY DDL (CREATE/ALTER/DROP/Compile); data operations always stay NOWAIT.
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

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

    // Transaction profile for the working transaction (see TransactionProfile).
    // Defaults to ReadCommitted; older connections.json files without this field
    // deserialize to ReadCommitted (enum value 0).
    public TransactionProfile TransactionProfile { get; set; } = TransactionProfile.ReadCommitted;
}

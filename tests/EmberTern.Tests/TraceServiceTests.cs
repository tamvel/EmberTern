using EmberTern.Core.Connections;
using EmberTern.Core.Trace;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The unit-testable slice of <see cref="FirebirdTraceService"/> — the Core preset →
/// driver flag translation and the Services connection string. The live FbTrace
/// wiring (start/stream/stop session dance) is manual-smoke (needs a privileged live
/// server) and is not covered here.
/// </summary>
public class TraceServiceTests
{
    [Fact]
    public void BuildDatabaseEvents_DefaultPreset_HasStatementsRoutinesErrorsAndPerf()
    {
        var e = FirebirdTraceService.BuildDatabaseEvents(TraceSessionConfig.DefaultPreset);

        Assert.True(e.HasFlag(FbDatabaseTraceEvents.StatementFinish));
        Assert.True(e.HasFlag(FbDatabaseTraceEvents.ProcedureStart));   // START needed so pairs fold
        Assert.True(e.HasFlag(FbDatabaseTraceEvents.ProcedureFinish));
        Assert.True(e.HasFlag(FbDatabaseTraceEvents.FunctionStart));
        Assert.True(e.HasFlag(FbDatabaseTraceEvents.FunctionFinish));
        Assert.True(e.HasFlag(FbDatabaseTraceEvents.TriggerFinish));
        Assert.True(e.HasFlag(FbDatabaseTraceEvents.Errors));
        Assert.True(e.HasFlag(FbDatabaseTraceEvents.PrintPerf));        // always — duration + per-table reads

        Assert.False(e.HasFlag(FbDatabaseTraceEvents.Connections));    // off by default
        Assert.False(e.HasFlag(FbDatabaseTraceEvents.Transactions));
        Assert.False(e.HasFlag(FbDatabaseTraceEvents.StatementStart)); // we only want the FINISH (has the result)
    }

    [Fact]
    public void BuildDatabaseEvents_OptionalLanesTurnOnTheirFlags()
    {
        var e = FirebirdTraceService.BuildDatabaseEvents(new TraceSessionConfig
        {
            IncludeConnections = true,
            IncludeTransactions = true,
        });
        Assert.True(e.HasFlag(FbDatabaseTraceEvents.Connections));
        Assert.True(e.HasFlag(FbDatabaseTraceEvents.Transactions));
    }

    [Fact]
    public void BuildServiceConnectionString_UsesHostPortUser_AndNoDatabase()
    {
        var profile = new ConnectionProfile
        {
            Host = "db.example",
            Port = 3051,
            Username = "SYSDBA",
            Password = "secret",
            DatabasePath = @"C:\Prestiz\BAZA\SZKOLENIE.FB",
        };

        var cs = FirebirdTraceService.BuildServiceConnectionString(profile);
        var b = new FbConnectionStringBuilder(cs);

        Assert.Equal("db.example", b.DataSource);
        Assert.Equal(3051, b.Port);
        Assert.Equal("SYSDBA", b.UserID);
        Assert.Equal(FbServerType.Default, b.ServerType);
        Assert.True(string.IsNullOrEmpty(b.Database)); // service connection — no database
    }
}

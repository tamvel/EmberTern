using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the pure pre-run checks + outcome counters (no driver, no database).</summary>
public class ScriptValidationTests
{
    private static ScriptStatement Stmt(ScriptStatementKind kind, string text = "x")
        => new(text, kind, SourceOffset: 0, SourceLength: text.Length);

    [Fact]
    public void FindDisallowed_ReturnsTransactionAndSessionControl_InOrder()
    {
        var statements = new List<ScriptStatement>
        {
            Stmt(ScriptStatementKind.Ddl, "create table t"),
            Stmt(ScriptStatementKind.TransactionControl, "commit"),
            Stmt(ScriptStatementKind.Dml, "insert into t"),
            Stmt(ScriptStatementKind.SessionControl, "set names win1250"),
        };

        var disallowed = ScriptValidation.FindDisallowed(statements);

        Assert.Equal(2, disallowed.Count);
        Assert.Equal(ScriptStatementKind.TransactionControl, disallowed[0].Kind);
        Assert.Equal(ScriptStatementKind.SessionControl, disallowed[1].Kind);
    }

    [Fact]
    public void FindDisallowed_OnlyDdlDmlSelect_ReturnsEmpty()
    {
        var statements = new List<ScriptStatement>
        {
            Stmt(ScriptStatementKind.Ddl),
            Stmt(ScriptStatementKind.Dml),
            Stmt(ScriptStatementKind.Select),
            Stmt(ScriptStatementKind.ExecuteProcedure),
            Stmt(ScriptStatementKind.ExecuteBlock),
        };

        Assert.Empty(ScriptValidation.FindDisallowed(statements));
    }

    [Fact]
    public void ScriptRunOutcome_CountsSuccessesAndFailures()
    {
        ScriptStatementResult Res(int i, bool ok) =>
            new(i, "s", ScriptStatementKind.Dml, ok, RecordsAffected: 1, RowCount: null, TimeSpan.Zero, ok ? null : "boom");

        var outcome = new ScriptRunOutcome(
            new[] { Res(0, true), Res(1, false), Res(2, true) },
            TransactionLeftOpen: true, AnyFailed: true, Cancelled: false);

        Assert.Equal(2, outcome.SuccessCount);
        Assert.Equal(1, outcome.FailedCount);
    }
}

using System;
using System.Collections.Generic;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the Step 5 seam B pre-flight: a single-transaction mode (Manual / Auto-commit) must reject a
/// MIXED DDL+DML script BEFORE running (gotcha #213), pointing the user at Sequenced; Sequenced itself
/// is never blocked. Pure VM helper — no services / DB. Statements carry real SQL text because the
/// detector classifies via the AST-based SqlStatementClassifier.
/// </summary>
public class ScriptExecutorMixedScriptTests
{
    private static ScriptStatement St(string text)
        => new(text, ScriptStatementKind.Unknown, SourceOffset: 0, SourceLength: text.Length);

    private static IReadOnlyList<ScriptStatement> Script(params string[] texts)
    {
        var list = new List<ScriptStatement>();
        foreach (var t in texts) list.Add(St(t));
        return list;
    }

    [Theory]
    [InlineData(ScriptTransactionMode.Manual)]
    [InlineData(ScriptTransactionMode.AutoCommitOnSuccess)]
    public void MixedScript_InSingleTransactionMode_IsBlocked_AndNamesSequenced(ScriptTransactionMode mode)
    {
        var script = Script("create table t (id integer)", "insert into t values (1)");
        var block = ScriptExecutorTabViewModel.ResolveMixedScriptBlock(script, mode);

        Assert.Equal(UiStrings.ScriptStatusMixedNeedsSequenced, block);
        Assert.Contains("Sequenced", block!, StringComparison.Ordinal);
        Assert.Contains("single transaction", block, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MixedScript_InSequenced_IsAllowed()
    {
        var script = Script("create table t (id integer)", "insert into t values (1)");
        Assert.Null(ScriptExecutorTabViewModel.ResolveMixedScriptBlock(script, ScriptTransactionMode.Sequenced));
    }

    [Fact]
    public void MixedIsOrderIndependent_DataThenSchema_IsBlocked()
    {
        var script = Script("insert into t values (1)", "create table u (id integer)");
        Assert.NotNull(ScriptExecutorTabViewModel.ResolveMixedScriptBlock(script, ScriptTransactionMode.Manual));
    }

    [Theory]
    [InlineData(ScriptTransactionMode.Manual)]
    [InlineData(ScriptTransactionMode.AutoCommitOnSuccess)]
    public void AllDdlScript_IsNotBlocked(ScriptTransactionMode mode)
    {
        var script = Script("create table a (id integer)", "alter table a add note varchar(10)");
        Assert.Null(ScriptExecutorTabViewModel.ResolveMixedScriptBlock(script, mode));
    }

    [Theory]
    [InlineData(ScriptTransactionMode.Manual)]
    [InlineData(ScriptTransactionMode.AutoCommitOnSuccess)]
    public void AllDmlScript_IsNotBlocked(ScriptTransactionMode mode)
    {
        var script = Script("insert into t values (1)", "update t set x = 2", "select * from t");
        Assert.Null(ScriptExecutorTabViewModel.ResolveMixedScriptBlock(script, mode));
    }

    [Fact]
    public void SingleStatement_IsNeverMixed()
    {
        Assert.Null(ScriptExecutorTabViewModel.ResolveMixedScriptBlock(Script("create table t (id integer)"), ScriptTransactionMode.Manual));
        Assert.Null(ScriptExecutorTabViewModel.ResolveMixedScriptBlock(Script("insert into t values (1)"), ScriptTransactionMode.Manual));
    }

    [Fact]
    public void EmptyScript_IsNotBlocked()
        => Assert.Null(ScriptExecutorTabViewModel.ResolveMixedScriptBlock(Array.Empty<ScriptStatement>(), ScriptTransactionMode.Manual));

    [Fact]
    public void DclMixedWithData_IsBlocked()
    {
        // GRANT is schema (DCL); with a data statement it is a mixed migration.
        var script = Script("grant select on t to someuser", "insert into t values (1)");
        Assert.NotNull(ScriptExecutorTabViewModel.ResolveMixedScriptBlock(script, ScriptTransactionMode.Manual));
    }
}

using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Scripting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the Etap 3 pre-run gate helpers (pure — no VM services / DB).</summary>
public class ScriptExecutorGateTests
{
    [Fact]
    public void ResolveRunBlock_NoTransaction_AllowsRun()
    {
        Assert.Null(ScriptExecutorTabViewModel.ResolveRunBlock(transactionActive: false, ownLeftover: false));
        Assert.Null(ScriptExecutorTabViewModel.ResolveRunBlock(transactionActive: false, ownLeftover: true));
    }

    [Fact]
    public void ResolveRunBlock_OwnLeftover_PointsToCommitRollbackHere()
        => Assert.Equal(UiStrings.ScriptBlockOwnTxOpen,
            ScriptExecutorTabViewModel.ResolveRunBlock(transactionActive: true, ownLeftover: true));

    [Fact]
    public void ResolveRunBlock_ExternalTransaction_PointsToTheOtherTransaction()
        => Assert.Equal(UiStrings.ScriptBlockExternalTxOpen,
            ScriptExecutorTabViewModel.ResolveRunBlock(transactionActive: true, ownLeftover: false));

    [Fact]
    public void BuildDisallowedMessage_ListsTheOffendingStatements()
    {
        var disallowed = new[]
        {
            new ScriptStatement("COMMIT", ScriptStatementKind.TransactionControl, 0, 6),
            new ScriptStatement("SET NAMES WIN1250", ScriptStatementKind.SessionControl, 10, 17),
        };

        var message = ScriptExecutorTabViewModel.BuildDisallowedMessage(disallowed);

        Assert.Contains("COMMIT", message);
        Assert.Contains("SET NAMES WIN1250", message);
    }

    [Fact]
    public void BuildDisallowedMessage_ElidesLongStatements()
    {
        var longText = new string('A', 60);
        var message = ScriptExecutorTabViewModel.BuildDisallowedMessage(
            new[] { new ScriptStatement(longText, ScriptStatementKind.TransactionControl, 0, 60) });

        Assert.Contains("…", message);
        Assert.DoesNotContain(new string('A', 60), message);
    }
}

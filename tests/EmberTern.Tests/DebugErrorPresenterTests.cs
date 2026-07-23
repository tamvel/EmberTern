using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql.Debugging;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X / D15.4 Seam B — the App-side friendly presentation over a <see cref="DebugError"/>, and its
/// wiring into the two expression row VMs. <c>Raw</c> = the full server text (Error Bar / tooltip); <c>Describe</c>
/// = the friendly, categorised one-liner (falling back to <c>Raw</c> for Unknown). The row VMs show friendly
/// text and keep the raw message reachable via a tooltip ("friendly + raw available").
/// </summary>
public sealed class DebugErrorPresenterTests
{
    [Fact]
    public void Raw_PrefersMessage_ThenName_ThenSqlState_ThenGds()
    {
        Assert.Equal("boom", DebugErrorPresenter.Raw(new DebugError(Message: "  boom  ")));
        Assert.Equal("E_X", DebugErrorPresenter.Raw(new DebugError(ExceptionName: "E_X")));
        Assert.Equal("SQLSTATE 42000", DebugErrorPresenter.Raw(new DebugError(SqlState: "42000")));
        Assert.Equal("GDS 335544569", DebugErrorPresenter.Raw(new DebugError(GdsCode: 335544569)));
        Assert.Equal(string.Empty, DebugErrorPresenter.Raw(null));
    }

    [Fact]
    public void Describe_UserException_IncludesTheName()
    {
        var e = new DebugError(ExceptionName: "E_CUSTOMER_NOT_FOUND", GdsCode: 335544517,
            Message: "E_CUSTOMER_NOT_FOUND\nCustomer not found.");
        var text = DebugErrorPresenter.Describe(e);
        Assert.Contains("E_CUSTOMER_NOT_FOUND", text);
        Assert.NotEqual(DebugErrorPresenter.Raw(e), text); // friendly, not the raw multi-line message
    }

    [Fact]
    public void Describe_Constraint_IsTheFriendlyConstant()
        => Assert.Equal(UiStrings.DebuggerFriendlyConstraint,
            DebugErrorPresenter.Describe(new DebugError(GdsCode: 335544879)));

    [Fact]
    public void Describe_SqlError_IsTheFriendlyConstant()
        => Assert.Equal(UiStrings.DebuggerFriendlySqlError,
            DebugErrorPresenter.Describe(new DebugError(GdsCode: 335544569, SqlState: "42000",
                Message: "Dynamic SQL Error\nToken unknown")));

    [Fact]
    public void Describe_Unknown_FallsBackToRaw()
    {
        var e = new DebugError(GdsCode: 335544345, Message: "lock conflict");
        Assert.Equal(DebugErrorPresenter.Raw(e), DebugErrorPresenter.Describe(e));
    }

    [Fact]
    public void ExecutedSqlRow_FriendlyResultWithRawTooltip_OnDsqlError()
    {
        var err = new DebugError(GdsCode: 335544569, SqlState: "42000",
            Message: "Dynamic SQL Error\nSQL error code = -104\nToken unknown");
        var row = DebugExecutedSqlRowViewModel.ForResult(
            "1 +", EvaluationKind.Expression, EvaluationResult.Failed("<harness>", err));

        Assert.True(row.IsError);
        Assert.Equal(UiStrings.DebuggerFriendlySqlError, row.ResultText);
        Assert.True(row.HasRawError);
        Assert.Contains("Token unknown", row.RawError!);
    }

    [Fact]
    public void ExecutedSqlRow_NoRawTooltip_WhenFriendlyEqualsRaw()
    {
        // Unknown category ⇒ Describe == Raw ⇒ no redundant tooltip.
        var err = new DebugError(GdsCode: 335544345, Message: "lock conflict");
        var row = DebugExecutedSqlRowViewModel.ForResult(
            "x", EvaluationKind.Expression, EvaluationResult.Failed("<harness>", err));

        Assert.True(row.IsError);
        Assert.Equal("lock conflict", row.ResultText);
        Assert.False(row.HasRawError);
    }

    [Fact]
    public void WatchRow_FriendlyValueWithRawTooltip_OnConstraint()
    {
        var err = new DebugError(GdsCode: 335544879, SqlState: "42000",
            Message: "validation error for variable V, value \"*** null ***\"");
        var row = new WatchRowViewModel("v", hasSideEffect: false);
        row.Apply(EvaluationResult.Failed("<harness>", err));

        Assert.True(row.IsError);
        Assert.Equal(UiStrings.DebuggerFriendlyConstraint, row.ValueText);
        Assert.Contains("validation error", row.RawError!);
    }

    [Fact]
    public void WatchRow_Reset_ClearsRawError()
    {
        var err = new DebugError(GdsCode: 335544569, Message: "Dynamic SQL Error");
        var row = new WatchRowViewModel("v", hasSideEffect: false);
        row.Apply(EvaluationResult.Failed("<harness>", err));
        row.Reset();

        Assert.False(row.IsError);
        Assert.Null(row.RawError);
    }
}

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Export;
using EmberTern.Core.Export.Sql;

namespace EmberTern.App.Export;

/// <summary>The outcome of building one row's SQL: either the runnable statement (already formatted
/// through <c>SqlFormatter</c>, ready for the clipboard) or the sentence explaining the refusal.</summary>
public readonly record struct SqlCopyText(bool IsBuilt, string Text)
{
    public static SqlCopyText Ok(string sql) => new(true, sql);

    public static SqlCopyText Refused(string message) => new(false, message);
}

/// <summary>
/// The shared view-model glue for the "Copy as INSERT / UPDATE" grid actions — one per result set.
/// It wraps a <see cref="SqlCopyCoordinator"/> (the actual capture → resolve → build mechanism) and
/// exposes the bindable availability + tooltips and the two operations, so <b>every grid host</b>
/// (SQL Editor, Table Data) gets identical behaviour from one implementation rather than a re-derived
/// copy. The host owns only what is genuinely host-specific: the clipboard write and where a message
/// goes.
/// <para>
/// <b>Disabled-with-a-reason</b> is the whole point (design §5/§6): a greyed item carries a tooltip that
/// names the actual obstacle. The tooltips are empty when the action is available, so an enabled item
/// says nothing and a disabled one always says why.
/// </para>
/// </summary>
public sealed partial class SqlCopyController : ObservableObject
{
    private readonly SqlCopyCoordinator _coordinator;

    public SqlCopyController(SqlCopyCoordinator coordinator) => _coordinator = coordinator;

    /// <summary>The coordinator, so a host can set its <see cref="SqlCopyCoordinator.WarmColumns"/> or
    /// call <see cref="SqlCopyCoordinator.Reset"/> when its result set changes.</summary>
    public SqlCopyCoordinator Coordinator => _coordinator;

    [ObservableProperty]
    private bool _canCopyAsInsert;

    [ObservableProperty]
    private bool _canCopyAsUpdate;

    /// <summary>Empty when the action is available — a tooltip that only appears to explain a refusal,
    /// because a greyed item that says nothing teaches nothing.</summary>
    [ObservableProperty]
    private string _copyAsInsertTooltip = string.Empty;

    [ObservableProperty]
    private string _copyAsUpdateTooltip = string.Empty;

    /// <summary>Re-evaluates whether the SQL copy actions are available — call when the grid's context
    /// menu opens. That gesture is what makes the ~7 ms provenance capture "on demand" without ever
    /// touching an execution path, where the same cost would be an across-the-board regression.</summary>
    /// <param name="hasResult">False (no result set) collapses everything to unavailable with no capture.</param>
    public async Task RefreshAvailabilityAsync(bool hasResult, CancellationToken cancellationToken = default)
    {
        if (!hasResult)
        {
            Reset();
            return;
        }

        var insert = await _coordinator.GetAvailabilityAsync(ExportFormat.InsertScript, cancellationToken).ConfigureAwait(true);
        var update = await _coordinator.GetAvailabilityAsync(ExportFormat.UpdateScript, cancellationToken).ConfigureAwait(true);

        CanCopyAsInsert = insert.IsAvailable;
        CanCopyAsUpdate = update.IsAvailable;
        CopyAsInsertTooltip = TooltipFor(insert, UiStrings.GridCopyAsInsert);
        CopyAsUpdateTooltip = TooltipFor(update, UiStrings.GridCopyAsUpdate);
    }

    /// <summary>Builds the formatted statement for one row, or the sentence explaining why it cannot be
    /// built. Re-checks through the coordinator rather than trusting the menu's enabled state: that flag
    /// is a hint computed a moment ago, this is the authority, and a wrong statement must never reach the
    /// clipboard.</summary>
    public async Task<SqlCopyText> BuildFormattedAsync(
        ExportFormat format,
        IReadOnlyList<object?> row,
        CancellationToken cancellationToken = default)
    {
        var built = await _coordinator.BuildAsync(format, row, cancellationToken).ConfigureAwait(true);
        if (!built.IsBuilt)
            return SqlCopyText.Refused(SqlCopyReasonText.DescribeForMenu(HeaderFor(format), built.Reason!));

        return SqlCopyText.Ok(FormatGeneratedSql(built.Sql!));
    }

    /// <summary>Drops the cached provenance/verdict and clears the availability. Call when the result set
    /// is replaced (the SQL Editor on each run); a fixed-table grid never needs it.</summary>
    public void Reset()
    {
        _coordinator.Reset();
        CanCopyAsInsert = false;
        CanCopyAsUpdate = false;
        CopyAsInsertTooltip = string.Empty;
        CopyAsUpdateTooltip = string.Empty;
    }

    private static string TooltipFor(FormatAvailability availability, string header)
        => availability.IsAvailable || availability.Reason is null
            ? string.Empty
            : SqlCopyReasonText.DescribeForMenu(header, availability.Reason);

    private static string HeaderFor(ExportFormat format)
        => format == ExportFormat.UpdateScript ? UiStrings.GridCopyAsUpdate : UiStrings.GridCopyAsInsert;

    // Through the SHARED formatter — one formatting language for everything EmberTern emits, so generated
    // DML is lowercase like the rest. §0's checked invariant means the formatter either preserves every
    // lexeme or returns our input unchanged, so this cannot corrupt the statement.
    private static string FormatGeneratedSql(string sql)
    {
        var formatted = EmberTern.Core.Sql.SqlFormatter.Format(sql);
        return string.IsNullOrWhiteSpace(formatted) ? sql : formatted.TrimEnd('\r', '\n');
    }
}

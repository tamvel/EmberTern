using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Settings;
using EmberTern.Core.Sql.Debugging;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The launch-panel editor for debugging a <b>trigger</b> (Stage X / D10, spec §8.1). A <b>dumb VM</b>: it holds
/// no availability rules of its own — it builds a Core <see cref="TriggerContext"/> for the picked action and
/// <i>reads</i> <see cref="TriggerContext.NewAvailable"/>/<see cref="TriggerContext.OldAvailable"/> from it, so
/// the BEFORE/AFTER × INSERT/UPDATE/DELETE → NEW/OLD truth lives in exactly one place (Core), never duplicated
/// in the UI.
/// <para>
/// The <c>NEW</c>/<c>OLD</c> value grids show <b>only the columns the trigger body references</b> (from
/// <see cref="ContextSubstitution.BuildColumns"/>), typed from the target table's catalog — not every column of
/// the table. They reuse the Smart-Parameters editor (<see cref="ExecuteProcedureDialogViewModel"/>): typed rows
/// + history + validation, no second parameter UI. <see cref="CollectRootValues"/> maps each entered value onto
/// its stable synthetic frame-variable name (<c>ET_CTX_i</c>), which is exactly the launch's root-frame seed.
/// </para>
/// </summary>
public sealed partial class TriggerContextEditorViewModel : ObservableObject
{
    private readonly TriggerHeader _header;
    private readonly IReadOnlyList<ContextColumn> _columns;

    public TriggerContextEditorViewModel(
        TriggerHeader header,
        IReadOnlyList<ContextColumn> columns,
        IReadOnlyDictionary<string, string> columnTypes,
        string? connectionId = null,
        ParameterHistoryStore? historyStore = null)
    {
        _header = header ?? throw new ArgumentNullException(nameof(header));
        _columns = columns ?? throw new ArgumentNullException(nameof(columns));
        ArgumentNullException.ThrowIfNull(columnTypes);

        Actions = header.Events.Select(ActionLabel).ToList();

        // The NEW / OLD grids: only the referenced columns of each record, typed from the target-table catalog
        // (a builtin fallback keeps a missing type a plain text box rather than crashing the launch). Built once;
        // visibility is what changes with the action, not their contents.
        NewParameters = BuildRecordEditor(TriggerRecord.New, columnTypes, connectionId, historyStore);
        OldParameters = BuildRecordEditor(TriggerRecord.Old, columnTypes, connectionId, historyStore);

        RecomputeAvailability();
    }

    /// <summary>The DML actions the trigger declares (display labels) — the action selector's items. A
    /// single-action trigger has one; a multi-action trigger (<c>BEFORE INSERT OR UPDATE</c>) lets the user pick
    /// which one to simulate (spec §8.1), which drives NEW/OLD availability and the predicate values.</summary>
    public IReadOnlyList<string> Actions { get; }

    /// <summary>True when the trigger declares more than one action — the selector is shown only then.</summary>
    public bool HasMultipleActions => Actions.Count > 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedEvent))]
    private int _selectedActionIndex;

    partial void OnSelectedActionIndexChanged(int value) => RecomputeAvailability();

    /// <summary>The typed NEW-record value rows (only the referenced <c>NEW.col</c> columns).</summary>
    public ExecuteProcedureDialogViewModel NewParameters { get; }

    /// <summary>The typed OLD-record value rows (only the referenced <c>OLD.col</c> columns).</summary>
    public ExecuteProcedureDialogViewModel OldParameters { get; }

    /// <summary><c>NEW</c> is available for the picked action (INSERT/UPDATE) — read from Core, not decided here.</summary>
    [ObservableProperty]
    private bool _newAvailable;

    /// <summary><c>OLD</c> is available for the picked action (UPDATE/DELETE) — read from Core, not decided here.</summary>
    [ObservableProperty]
    private bool _oldAvailable;

    /// <summary>The single DML event currently selected for simulation.</summary>
    public TriggerEvent SelectedEvent =>
        _header.Events[Math.Clamp(SelectedActionIndex, 0, _header.Events.Count - 1)];

    /// <summary>The table this trigger fires on. Read by the launch rebuild: NEW/OLD values are only carried
    /// into a rebuilt editor while the target table is the same one, because a column's identity is its name
    /// <em>in that table</em> — the same name on a different table is a different column.</summary>
    public string TargetTable => _header.TargetTable;

    /// <summary>Re-selects a previously chosen action after a rebuild, if this trigger still declares it.
    /// Matched by the event itself rather than by its index: the declared list may have gained or lost an
    /// action, which makes the old index meaningless while the choice is still perfectly valid. Returns
    /// whether the action survived.</summary>
    internal bool TrySelectEvent(TriggerEvent action)
    {
        for (int i = 0; i < _header.Events.Count; i++)
        {
            if (_header.Events[i] != action) continue;
            SelectedActionIndex = i;
            return true;
        }
        return false;
    }

    /// <summary>Builds the Core <see cref="TriggerContext"/> for the picked action — the value the launch mounts
    /// on the trigger root frame (§8.1). All availability + predicate semantics live on it, not here.</summary>
    public TriggerContext BuildTriggerContext()
        => new(_header.TargetTable, SelectedEvent, _header.Timing, _columns);

    /// <summary>Validates + resolves the available NEW/OLD grids (reusing the Smart-Parameters accept path). An
    /// unavailable record is skipped (its grid is hidden). Returns false when a shown grid fails validation.</summary>
    public bool Accept()
    {
        if (NewAvailable)
        {
            NewParameters.AcceptCommand.Execute(null);
            if (NewParameters.Result is null) return false;
        }
        if (OldAvailable)
        {
            OldParameters.AcceptCommand.Execute(null);
            if (OldParameters.Result is null) return false;
        }
        return true;
    }

    /// <summary>Maps the entered NEW/OLD values onto their synthetic frame-variable names (<c>ET_CTX_i</c>) — the
    /// root-frame seed the launch passes as <c>rootValues</c> (spec §8.1). Only available records contribute;
    /// call <see cref="Accept"/> first (it fills each grid's <c>Result</c>).</summary>
    public IReadOnlyDictionary<string, object?> CollectRootValues(TriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (context.NewAvailable) CollectRecord(values, context, TriggerRecord.New, NewParameters);
        if (context.OldAvailable) CollectRecord(values, context, TriggerRecord.Old, OldParameters);
        return values;
    }

    // Maps each parameter row's resolved value onto the matching context column's synthetic name. Uses the
    // grid's Result (set by Accept) so time fields are committed; a row with no matching column is ignored.
    private void CollectRecord(
        Dictionary<string, object?> values, TriggerContext context, TriggerRecord record,
        ExecuteProcedureDialogViewModel editor)
    {
        var result = editor.Result;
        if (result is null) return;
        for (int i = 0; i < editor.Params.Count && i < result.Count; i++)
        {
            string column = editor.Params[i].Name;
            var match = context.Columns.FirstOrDefault(
                c => c.Record == record && string.Equals(c.Column, column, StringComparison.OrdinalIgnoreCase));
            if (match is not null) values[match.Synthetic] = result[i];
        }
    }

    private ExecuteProcedureDialogViewModel BuildRecordEditor(
        TriggerRecord record, IReadOnlyDictionary<string, string> columnTypes,
        string? connectionId, ParameterHistoryStore? historyStore)
    {
        var rows = _columns
            .Where(c => c.Record == record)
            .Select(c => (c.Column, TypeText: columnTypes.TryGetValue(c.Column, out var t) ? t : "VARCHAR"))
            .ToList();
        // Scope history per (table, record) so NEW and OLD sets are recalled independently.
        string historyName = $"{_header.TargetTable}.{(record == TriggerRecord.New ? "NEW" : "OLD")}";
        return new ExecuteProcedureDialogViewModel(
            rows, historyName, connectionId, objectKind: "Trigger", historyStore: historyStore);
    }

    private void RecomputeAvailability()
    {
        var context = BuildTriggerContext();
        NewAvailable = context.NewAvailable;
        OldAvailable = context.OldAvailable;
    }

    private static string ActionLabel(TriggerEvent e) => e switch
    {
        TriggerEvent.Insert => UiStrings.DebuggerTriggerActionInsert,
        TriggerEvent.Update => UiStrings.DebuggerTriggerActionUpdate,
        TriggerEvent.Delete => UiStrings.DebuggerTriggerActionDelete,
        _ => e.ToString(),
    };
}

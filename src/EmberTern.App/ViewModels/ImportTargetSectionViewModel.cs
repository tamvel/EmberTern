using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The <b>Target</b> tile (§3.4): which table the rows are going into.
/// <para>
/// ⭐ <b>It does not own the configuration.</b> <see cref="DataImportTabViewModel"/> holds the one
/// <see cref="ImportConfiguration"/>; this reads its slice (<see cref="Apply"/>) and produces a new one on
/// demand (<see cref="BuildTarget"/> / <see cref="BuildBehavior"/>). §4.8.6 — the same rule the Source tile
/// follows, and the reason named profiles can arrive in I11 as pure UI.
/// </para>
/// <para>
/// <b>Etap I6 covers the EXISTING-table variant only.</b> "New table" is I8, and it is deliberately not shown
/// as a disabled radio: an option that looks like a choice but leads nowhere is the lie the readiness strip
/// could not correct, which is the same reason I5 shipped no command bar.
/// </para>
/// <para>
/// The facts line answers the questions that decide whether an import will work at all — how many columns,
/// whether there is a primary key, and which <c>BEFORE INSERT</c> triggers will rewrite the values on the way
/// in (R6). ⚠ The record count from the §3.4 sketch is deliberately NOT read here: it costs a
/// <c>SELECT COUNT(*)</c> on every target change, and the decision it serves — confirming that "empty the
/// table first" is about to delete N rows — happens at run time, so it belongs to I7 where it is needed once
/// rather than on every keystroke.
/// </para>
/// </summary>
public sealed partial class ImportTargetSectionViewModel : ViewModelBase
{
    private bool _suspendChangeNotification;

    public ImportTargetSectionViewModel()
    {
        Tables = new ObservableCollection<string>();
    }

    /// <summary>Raised whenever a user decision here changes, so the coordinator re-runs the chain (§4.7).</summary>
    public event EventHandler? Changed;

    /// <summary>Table names from the METADATA lane (read-only, implicit per-command transactions).</summary>
    public ObservableCollection<string> Tables { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    private string? _selectedTable;

    partial void OnSelectedTableChanged(string? value) => RaiseChanged();

    public bool HasTarget => !string.IsNullOrWhiteSpace(SelectedTable);

    /// <summary>Columns · primary key · BEFORE INSERT triggers — read from the target, never guessed.</summary>
    [ObservableProperty] private string _factsText = string.Empty;

    /// <summary>True while the target is being read.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>
    /// <c>DELETE FROM</c> before the rows go in, inside the SAME transaction (decision D5), so a rollback
    /// takes the deletion with it. Default off: it destroys data, and §0 makes the conservative answer the
    /// default for every option that does.
    /// </summary>
    [ObservableProperty] private bool _emptyBeforeImport;

    partial void OnEmptyBeforeImportChanged(bool value) => RaiseChanged();

    // ── The section's slice of the ONE record (§4.8.6) ──────────────────────────────────────────────────

    /// <summary>
    /// Produces the target slice. A configuration whose target is a NEW table is passed through untouched —
    /// this build cannot edit that decision (I8), and silently degrading it to an existing-table target would
    /// be exactly the "an older build quietly robbed the profile" defect §4.8.6 exists to prevent.
    /// </summary>
    public TargetDescriptor BuildTarget(TargetDescriptor current)
    {
        if (current is { Kind: ImportTargetKind.NewTable }) return current;

        return TargetDescriptor.Existing(SelectedTable ?? string.Empty);
    }

    public ImportBehaviorOptions BuildBehavior(ImportBehaviorOptions current)
        => current with { EmptyTargetBeforeImport = EmptyBeforeImport };

    public void Apply(ImportConfiguration configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        using (SuspendChangeNotifications())
        {
            SelectedTable = configuration.Target.Kind == ImportTargetKind.ExistingTable
                                && configuration.Target.TableName.Length > 0
                ? configuration.Target.TableName
                : null;
            EmptyBeforeImport = configuration.Behavior.EmptyTargetBeforeImport;
        }
    }

    // ── Facts ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restates the target as the three facts that decide whether the import behaves as expected. Pure
    /// projection — every value comes from the <see cref="ImportTarget"/> the reader produced.
    /// </summary>
    public void ShowFacts(ImportTarget? target)
    {
        if (target is null || target.Columns.Count == 0)
        {
            FactsText = string.Empty;
            return;
        }

        var primaryKey = target.Columns.Where(c => c.IsPrimaryKey).Select(c => c.Name).ToList();

        var parts = new List<string>(3)
        {
            string.Format(CultureInfo.CurrentCulture, UiStrings.ImportTargetColumnsFormat, target.Columns.Count),
            primaryKey.Count == 0
                ? UiStrings.ImportTargetNoPrimaryKey
                : string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.ImportTargetPrimaryKeyFormat,
                    string.Join(", ", primaryKey)),
        };

        // Triggers are named, not counted: "2 triggers" tells the user something is there, the names tell
        // them WHAT will rewrite their values (R6).
        parts.Add(target.BeforeInsertTriggers.Count == 0
            ? UiStrings.ImportTargetNoBeforeInsertTriggers
            : string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportTargetBeforeInsertTriggersFormat,
                string.Join(", ", target.BeforeInsertTriggers)));

        FactsText = string.Join(" · ", parts);
    }

    /// <summary>Replaces the table list, keeping the current selection when it survives the refresh.</summary>
    public void ShowTables(IReadOnlyList<string> tables)
    {
        var previous = SelectedTable;

        using (SuspendChangeNotifications())
        {
            Tables.Clear();
            foreach (var table in tables) Tables.Add(table);
            SelectedTable = previous is not null && tables.Contains(previous, StringComparer.OrdinalIgnoreCase)
                ? previous
                : null;
        }
    }

    /// <summary>Suppresses <see cref="Changed"/> while this VM writes to itself — without it, publishing a
    /// freshly read table list would restart the very chain that produced it.</summary>
    public IDisposable SuspendChangeNotifications() => new ChangeSuspension(this);

    private void RaiseChanged()
    {
        if (_suspendChangeNotification) return;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class ChangeSuspension : IDisposable
    {
        private readonly ImportTargetSectionViewModel _owner;
        private readonly bool _previous;

        public ChangeSuspension(ImportTargetSectionViewModel owner)
        {
            _owner = owner;
            _previous = owner._suspendChangeNotification;
            owner._suspendChangeNotification = true;
        }

        public void Dispose() => _owner._suspendChangeNotification = _previous;
    }
}

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
/// ⭐ <b>Etap I8 added the second variant: a table that does not exist yet.</b> Until it worked, it was
/// deliberately not shown even as a disabled radio — an option that looks like a choice but leads nowhere is
/// the lie the readiness strip could not correct. It is a real choice now, and it carries the module's most
/// important honest warning: the <c>CREATE</c> runs on the Ddl lane and is COMMITTED before the first row
/// (gotcha #213), so <b>Rollback will not remove that table</b> (§0.5). That sentence lives beside the
/// checkbox that causes it, not in the report afterwards.
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
        NewColumns = new ObservableCollection<ImportNewTableColumnRowViewModel>();
    }

    /// <summary>Raised whenever a user decision here changes, so the coordinator re-runs the chain (§4.7).</summary>
    public event EventHandler? Changed;

    /// <summary>Table names from the METADATA lane (read-only, implicit per-command transactions).</summary>
    public ObservableCollection<string> Tables { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    private string? _selectedTable;

    partial void OnSelectedTableChanged(string? value) => RaiseChanged();

    /// <summary>True when a target has been chosen — a picked table, or a named new one.</summary>
    public bool HasTarget => IsNewTable
        ? !string.IsNullOrWhiteSpace(NewTableName)
        : !string.IsNullOrWhiteSpace(SelectedTable);

    // ── The two variants (§3.4) ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Which variant the user is on. A radio pair, not a mode: the two carry genuinely different decisions —
    /// an existing table has a shape to obey, a new one has a shape to choose.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    [NotifyPropertyChangedFor(nameof(IsExistingTable))]
    private bool _isNewTable;

    partial void OnIsNewTableChanged(bool value) => RaiseChanged();

    /// <summary>The inverse, for the radio the view binds and for everything gated on the existing-table
    /// variant. One owner, so the two can never both be true.</summary>
    public bool IsExistingTable
    {
        get => !IsNewTable;
        set => IsNewTable = !value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    private string _newTableName = string.Empty;

    private bool _settingNameUpper;

    partial void OnNewTableNameChanged(string value)
    {
        // Catalog UPPERCASE, like every other name-entry field in the application (gotcha #141): the DDL quotes
        // the identifier, so a lower-case name here would create a table the metadata tree then shows under a
        // name the user did not type.
        if (_settingNameUpper) return;

        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, StringComparison.Ordinal))
        {
            _settingNameUpper = true;
            try { NewTableName = upper; } finally { _settingNameUpper = false; }
            return;
        }

        RaiseChanged();
    }

    /// <summary>
    /// The columns about to be created — inferred, then <b>editable</b> (§0.3). The grid is the last moment a
    /// wrong type costs nothing: after the <c>CREATE</c> it is committed and beyond a Rollback's reach.
    /// </summary>
    public ObservableCollection<ImportNewTableColumnRowViewModel> NewColumns { get; }

    /// <summary>How many rows the inference was based on, plus whether the safety limit stopped it — ⭐ always
    /// visible (§3.4), because a type is worth exactly as much as the evidence behind it.</summary>
    [ObservableProperty] private string _inferenceBasisText = string.Empty;

    /// <summary>True while the source is being scanned for types. The scan reads the WHOLE source (R19), so it
    /// is the one part of this section the user can be left waiting on.</summary>
    [ObservableProperty] private bool _isInferring;

    /// <summary>
    /// ⚠ §0.5 — <c>DROP TABLE</c> on the Ddl lane if the import then fails, because Rollback cannot remove a
    /// table whose <c>CREATE</c> had to be committed first (gotcha #213). Off by default: it destroys an
    /// object, and every option that destroys something defaults to the conservative answer.
    /// </summary>
    [ObservableProperty] private bool _dropTableOnFailure;

    partial void OnDropTableOnFailureChanged(bool value) => RaiseChanged();

    /// <summary>True once the grid has something in it — the gate between the "name it and choose a source"
    /// empty state and the type grid itself.</summary>
    public bool HasNewColumns => NewColumns.Count > 0;

    /// <summary>Whether the generated DDL is on screen. View state, so it deliberately does NOT enter
    /// <see cref="ImportConfiguration"/> — a disclosure is not a decision about an import (§4.8.2), and the
    /// reflection round-trip guard would otherwise ship it inside a saved profile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreateTableSql))]
    [NotifyPropertyChangedFor(nameof(DdlToggleText))]
    private bool _isDdlVisible;

    /// <summary>The disclosure's own label — it says what the click will DO, which is the only reading that
    /// stays true in both states.</summary>
    public string DdlToggleText => IsDdlVisible ? UiStrings.ImportNewTableHideDdl : UiStrings.ImportNewTableShowDdl;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void ToggleDdl() => IsDdlVisible = !IsDdlVisible;

    /// <summary>
    /// The exact statement that will run. ⭐ From <see cref="ImportNewTable.BuildCreateSql"/>, which is the same
    /// call the run itself makes — so "Show DDL" is a preview of the real thing rather than an illustration of
    /// it (§3.4).
    /// </summary>
    public string CreateTableSql
    {
        get
        {
            var columns = BuildNewColumns();
            if (columns.Count == 0 || string.IsNullOrWhiteSpace(NewTableName)) return string.Empty;

            try
            {
                return ImportNewTable.BuildCreateSql(NewTableName, columns);
            }
            catch (ArgumentException)
            {
                // A half-typed name is a state, not a fault. The readiness strip is what says the target is
                // not usable yet; the preview just has nothing to show.
                return string.Empty;
            }
        }
    }

    /// <summary>Replaces the grid with a fresh inference. Called by the coordinator, which owns when the source
    /// has changed enough for the old types to be describing a different file.</summary>
    public void ShowInferredColumns(ColumnTypeInference inference)
    {
        if (inference is null) throw new ArgumentNullException(nameof(inference));

        using (SuspendChangeNotifications())
        {
            NewColumns.Clear();
            foreach (var column in inference.Columns)
            {
                NewColumns.Add(new ImportNewTableColumnRowViewModel(column, OnColumnEdited));
            }
        }

        OnPropertyChanged(nameof(HasNewColumns));

        InferenceBasisText = inference.Columns.Count == 0
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                inference.ScanTruncated
                    ? UiStrings.ImportNewTableInferenceTruncatedFormat
                    : UiStrings.ImportNewTableInferenceFormat,
                inference.RowsAnalysed);

        OnPropertyChanged(nameof(CreateTableSql));
        RaiseChanged();
    }

    /// <summary>An edit to any cell is a decision: it reaches the record and re-runs the chain, exactly like
    /// choosing a different table would.</summary>
    private void OnColumnEdited()
    {
        OnPropertyChanged(nameof(CreateTableSql));
        RaiseChanged();
    }

    private IReadOnlyList<ImportColumnDefinition> BuildNewColumns()
    {
        var columns = new List<ImportColumnDefinition>(NewColumns.Count);
        foreach (var row in NewColumns)
        {
            var definition = row.Build();
            if (definition.Name.Length == 0) continue;
            columns.Add(definition);
        }
        return columns;
    }

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
    /// Produces the target slice — for whichever variant the user is on. The grid's rows become
    /// <see cref="ImportColumnDefinition"/>s here and nowhere else, which is what keeps a new table's design
    /// inside the ONE record and therefore inside a saved profile (§4.8.6).
    /// </summary>
    /// <remarks>
    /// The <paramref name="current"/> descriptor is no longer passed through: since I8 this section can edit
    /// both variants, so there is nothing left it would be preserving. (It was passed through in I6 precisely
    /// because it could not — an older build must never quietly degrade a decision a newer one made.)
    /// </remarks>
    public TargetDescriptor BuildTarget(TargetDescriptor current)
    {
        if (!IsNewTable) return TargetDescriptor.Existing(SelectedTable ?? string.Empty);

        return TargetDescriptor.New(NewTableName.Trim(), BuildNewColumns());
    }

    /// <summary>
    /// Produces the behaviour slice.
    /// <para>
    /// ⚠ <b>"Empty the table first" is emitted only for an EXISTING table, and that is a correctness rule, not
    /// tidiness.</b> A table this import is about to create cannot have rows in it, so the option has no meaning
    /// there — and the surface hides its checkbox in that variant. But <b>hiding a control does not retract the
    /// decision it carries</b>: a user who ticked the box on the existing-table variant and then switched to
    /// "new table" left <c>true</c> sitting in the record, invisible, and the run then tried to
    /// <c>SELECT COUNT(*)</c> from a table that did not exist yet. The two facts live in this VM, so the
    /// reconciliation belongs here, in the ONE place that turns them into the record (§4.8.6).
    /// </para>
    /// <para>
    /// <see cref="EmptyBeforeImport"/> itself is deliberately NOT cleared: switching back to an existing table
    /// should find the tick where the user left it. What the record must not carry is a decision that does not
    /// apply.
    /// </para>
    /// </summary>
    public ImportBehaviorOptions BuildBehavior(ImportBehaviorOptions current)
        => current with
        {
            EmptyTargetBeforeImport = !IsNewTable && EmptyBeforeImport,
            DropTableOnFailure = DropTableOnFailure,
        };

    public void Apply(ImportConfiguration configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var target = configuration.Target;

        using (SuspendChangeNotifications())
        {
            IsNewTable = target.Kind == ImportTargetKind.NewTable;

            SelectedTable = target.Kind == ImportTargetKind.ExistingTable && target.TableName.Length > 0
                ? target.TableName
                : null;

            NewTableName = target.Kind == ImportTargetKind.NewTable ? target.TableName : string.Empty;

            NewColumns.Clear();
            if (target.Kind == ImportTargetKind.NewTable)
            {
                // ⭐ A restored column carries no basis, and it must not borrow one: the evidence behind a type
                // is a fact about a file that was read at some other time, and reprinting it here would claim
                // the current source said something it may never have said (§4.8.2 keeps evidence out of the
                // profile for exactly this reason). The coordinator re-infers when the source stops matching.
                foreach (var column in target.NewTableColumns)
                {
                    NewColumns.Add(new ImportNewTableColumnRowViewModel(
                        column, UiStrings.ImportNewTableBasisRestored, OnColumnEdited));
                }
                InferenceBasisText = string.Empty;
            }

            OnPropertyChanged(nameof(HasNewColumns));

            EmptyBeforeImport = configuration.Behavior.EmptyTargetBeforeImport;
            DropTableOnFailure = configuration.Behavior.DropTableOnFailure;
        }

        OnPropertyChanged(nameof(CreateTableSql));
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

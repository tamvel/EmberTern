using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Detail surface for a Firebird relation TRIGGER. Tabs: Editor (Source ⇄ Easy modes)
/// · Description · Dependencies · DDL — consistent with the Procedure / Function editors.
/// Shares the routine-editor skeleton (dirty tracking, mode toggle, Format/Comment,
/// Variables grid, Dependencies, Description, Compile, Revert, load lifecycle, field-row
/// owner) with <see cref="SourceObjectDetailTabViewModel"/>; adds the trigger-specific
/// header metadata (Table / Timing / Events / Position / Active) + auto-naming.
/// <list type="bullet">
/// <item>Source mode = the full editable CREATE OR ALTER TRIGGER text.</item>
/// <item>Easy mode = the trigger metadata + an editable Variables grid ABOVE a body-only
/// editor (the IBExpert improvement — variable declarations get a structured grid).</item>
/// </list>
/// Name auto-derives from {table, timing, events, position} until the user edits it.
/// </summary>
public partial class TriggerDetailTabViewModel : SourceObjectDetailTabViewModel
{
    // Top-tab indices — must match the TabItem order in the view.
    public const int EditorSubTabIndex = 0;

    // Cursors / subprograms found in the body are preserved verbatim through the
    // round-trip (Easy mode surfaces only the Variables grid per spec, but a trigger
    // body MAY contain a cursor / local routine — keep them so Compile doesn't drop
    // them). Re-emitted by BuildBodyModel.
    private readonly List<ProcedureCursor> _preservedCursors = new();
    private readonly List<ProcedureSubprogram> _preservedSubprograms = new();

    public TriggerDetailTabViewModel(string triggerName)
        : this(triggerName, null, null, null)
    {
    }

    public TriggerDetailTabViewModel(
        string triggerName,
        FirebirdTableDetailReader? reader,
        FirebirdDdlReader? ddlReader,
        FirebirdDdlExecutor? ddlExecutor)
        : base(reader, ddlReader, ddlExecutor)
    {
        TriggerName = triggerName;
        EditableTriggerName = triggerName;

        // The table list loads async; re-evaluate the picker's selection once it arrives
        // so a header-loaded TableName resolves to the matching item.
        AvailableTables.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectedTable));
        // Release the ctor-time suppression now that all fields are assigned.
        _suppressDirty = false;
    }

    public string TriggerName { get; }

    // ─── Trigger metadata (Easy mode header) ──────────────────────────────

    [ObservableProperty]
    private string _tableName = string.Empty;

    partial void OnTableNameChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedTable));
        MarkDirty();
        MaybeAutoName();
    }

    /// <summary>Wrapper for the Table ComboBox's SelectedItem (TwoWay). Avalonia's ComboBox
    /// nulls SelectedItem and writes null back when the bound value isn't in ItemsSource
    /// (the table list loads async after the header) — that would wipe a loaded TableName.
    /// Ignoring a null write keeps the value (gotcha #71).</summary>
    public string? SelectedTable
    {
        get => string.IsNullOrEmpty(TableName) ? null : TableName;
        set { if (value is not null) TableName = value; }
    }

    public IReadOnlyList<string> TimingOptions { get; } = new[] { "BEFORE", "AFTER" };

    [ObservableProperty]
    private string _selectedTiming = "BEFORE";

    public bool IsBefore => string.Equals(SelectedTiming, "BEFORE", StringComparison.OrdinalIgnoreCase);

    partial void OnSelectedTimingChanged(string value) { MarkDirty(); MaybeAutoName(); }

    [ObservableProperty] private bool _firesInsert;
    [ObservableProperty] private bool _firesUpdate;
    [ObservableProperty] private bool _firesDelete;

    partial void OnFiresInsertChanged(bool value) { MarkDirty(); MaybeAutoName(); }
    partial void OnFiresUpdateChanged(bool value) { MarkDirty(); MaybeAutoName(); }
    partial void OnFiresDeleteChanged(bool value) { MarkDirty(); MaybeAutoName(); }

    /// <summary>Easy-mode body editor: the body text is only the BEGIN…END block — it has no
    /// <c>CREATE TRIGGER … FOR &lt;table&gt;</c> header — so the semantic model can't establish the
    /// trigger scope on its own. Seed the trigger context as ambient symbols (the same seam the
    /// routine editors use for their params/variables): the NEW/OLD record aliases bound to the
    /// target table, and the INSERTING/UPDATING/DELETING predicates. This makes them resolve and
    /// get the context-variable highlight in the body editor exactly as in the full CREATE TRIGGER
    /// text (the SQL Editor path, bound by <c>SemanticBinder.BindTriggerDefinition</c>). Plus the
    /// Variables grid, as every routine editor.</summary>
    public override IReadOnlyList<Symbol> BuildAmbientSymbols()
    {
        var table = string.IsNullOrWhiteSpace(TableName) ? null : TableName.Trim();
        var symbols = new List<Symbol>
        {
            new RecordAliasSymbol("NEW") { TargetTable = table },
            new RecordAliasSymbol("OLD") { TargetTable = table },
            new TriggerPredicateSymbol("INSERTING"),
            new TriggerPredicateSymbol("UPDATING"),
            new TriggerPredicateSymbol("DELETING"),
        };
        AddVariableSymbols(symbols);
        return symbols;
    }

    [ObservableProperty] private int _position;

    partial void OnPositionChanged(int value)
    {
        OnPropertyChanged(nameof(PositionValue));
        MarkDirty();
        MaybeAutoName();
    }

    /// <summary>decimal? bridge for the NumericUpDown (Avalonia 12 NumericUpDown.Value is
    /// decimal?; the model keeps Position as int). See gotcha #57.</summary>
    public decimal? PositionValue
    {
        get => Position;
        set => Position = value is { } v ? (int)v : 0;
    }

    [ObservableProperty] private bool _active = true;

    partial void OnActiveChanged(bool value) => MarkDirty();

    /// <summary>True for a database-level (ON CONNECT / ON TRANSACTION …) or DDL trigger.
    /// Such triggers have no table / BEFORE-AFTER event, so the relation-trigger Easy model
    /// can't represent them — Easy mode is disabled and they stay in Source mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUseEasyMode))]
    private bool _isDatabaseTrigger;

    /// <summary>Easy mode is only meaningful for relation triggers; a DB-level / DDL
    /// trigger is Source-only (see <see cref="IsDatabaseTrigger"/>).</summary>
    public override bool CanUseEasyMode => !IsDatabaseTrigger;

    // ─── Trigger name (auto-derived until overridden) ─────────────────────

    /// <summary>Trigger name shown in Easy mode. Editable in the New Trigger flow
    /// (auto-derives from the metadata until the user types one); read-only for an
    /// existing trigger (Firebird can't rename via CREATE OR ALTER).</summary>
    [ObservableProperty]
    private string _editableTriggerName = string.Empty;

    private bool _autoWritingName;
    private bool _settingNameUpper;
    private string _lastAutoName = string.Empty;
    private bool _userOverrodeName;

    partial void OnEditableTriggerNameChanged(string value)
    {
        // UPPERCASE user-entered names (Firebird folds unquoted identifiers; EmberTern
        // keeps object names uppercase consistently — gotcha #141). Programmatic sets
        // (ctor / load, under _suppressDirty) and the already-uppercase auto-name don't
        // need coercing, so this only fires on a genuine user edit.
        if (!_settingNameUpper && !_suppressDirty)
        {
            var upper = (value ?? string.Empty).ToUpperInvariant();
            if (!string.Equals(value, upper, StringComparison.Ordinal))
            {
                _settingNameUpper = true;
                try { EditableTriggerName = upper; } finally { _settingNameUpper = false; }
                return; // re-runs this handler with the uppercased value
            }
        }
        MarkDirty();
        // During ctor / programmatic load (_suppressDirty) and during our own
        // auto-write, a value change is not a user override.
        if (_suppressDirty || _autoWritingName) return;
        if (!string.Equals(value, _lastAutoName, StringComparison.Ordinal)) _userOverrodeName = true;
    }

    // Auto-name: {TABLE}_{B|A}{I?}{U?}{D?}_{position}. Fires only for a NEW trigger the
    // user hasn't manually named yet, and only once a table is chosen. Mirrors the FK
    // wizard's "user override sticks" pattern (gotcha #66).
    private void MaybeAutoName()
    {
        if (!IsNew || _userOverrodeName) return;
        if (string.IsNullOrWhiteSpace(TableName)) return;
        var name = DdlGenerator.BuildTriggerName(TableName, IsBefore, FiresInsert, FiresUpdate, FiresDelete, Position);
        _autoWritingName = true;
        try
        {
            EditableTriggerName = name;
            _lastAutoName = name;
        }
        finally { _autoWritingName = false; }
    }

    private void ApplyHeader(string table, bool isBefore, bool ins, bool upd, bool del, int position, bool active)
    {
        TableName = table;
        SelectedTiming = isBefore ? "BEFORE" : "AFTER";
        FiresInsert = ins;
        FiresUpdate = upd;
        FiresDelete = del;
        Position = position;
        Active = active;
    }

    // ─── Easy-mode model (header + DECLARE section + body) ────────────────

    /// <summary>Reassembles the full CREATE OR ALTER TRIGGER text from the Easy-mode model.
    /// Defensive — placeholders keep it from throwing while a new trigger is still being
    /// filled in (a real Compile validates the metadata first).</summary>
    internal override string BuildFullSource()
    {
        // A DB-level / DDL trigger can't be represented by the relation-trigger Easy model
        // (no table, no BEFORE/AFTER). Keep the loaded Source text as the single source of
        // truth so a stray Easy⇄Source toggle can't fabricate "FOR TABLE_NAME BEFORE INSERT".
        if (IsDatabaseTrigger) return SourceText;

        var name = string.IsNullOrWhiteSpace(EditableTriggerName) ? TriggerName : EditableTriggerName.Trim();
        var table = string.IsNullOrWhiteSpace(TableName) ? "TABLE_NAME" : TableName.Trim();
        bool ins = FiresInsert, upd = FiresUpdate, del = FiresDelete;
        if (!(ins || upd || del)) ins = true; // never emit invalid DDL
        return DdlGenerator.BuildCreateOrAlterTrigger(
            name, table, IsBefore, ins, upd, del, Position, Active,
            DdlGenerator.BuildProcedureBody(BuildBodyModel()));
    }

    internal ProcedureBodyModel BuildBodyModel()
    {
        var model = new ProcedureBodyModel { ExecutableBody = ExecutableBody };
        foreach (var v in Variables) model.Variables.Add(v.ToVariable());
        foreach (var c in _preservedCursors) model.Cursors.Add(c);
        foreach (var sp in _preservedSubprograms) model.Subprograms.Add(sp);
        return model;
    }

    /// <summary>Splits a body (text after AS) into the editable Variables collection + the
    /// executable body editor content. Cursors / subprograms (rare in triggers) are
    /// preserved verbatim for the round-trip but not surfaced as editable grids.</summary>
    internal void SyncEasyModelFromBody(string? body)
    {
        var model = ProcedureBodySplitter.Split(body);
        Variables.Clear();
        foreach (var v in model.Variables) Variables.Add(ProcedureVariableRowViewModel.From(v, this));
        _preservedCursors.Clear();
        _preservedCursors.AddRange(model.Cursors);
        _preservedSubprograms.Clear();
        _preservedSubprograms.AddRange(model.Subprograms);
        ExecutableBody = model.ExecutableBody;
    }

    // ─── Object-specific hooks (SourceObjectDetailTabViewModel) ───────────

    protected override string ObjectDisplayName =>
        string.IsNullOrWhiteSpace(EditableTriggerName) ? TriggerName : EditableTriggerName.Trim();

    protected override string ParseFailedNotice => UiStrings.TriggerParseFailedNotice;
    protected override string CompileFailedFormat => UiStrings.TriggerCompileFailedFormat;
    protected override string UnsavedNewFormat => UiStrings.UnsavedNewTriggerFormat;
    protected override string UnsavedModifiedFormat => UiStrings.UnsavedModifiedTriggerFormat;

    protected override string CommentSql(string? comment)
        => DdlGenerator.BuildCommentTrigger(TriggerName, comment);

    protected override bool TryApplySource(string source)
    {
        var sig = TriggerSignatureParser.Parse(source);
        if (!sig.Success) return false;
        if (!string.IsNullOrWhiteSpace(sig.Name)) EditableTriggerName = sig.Name!;
        ApplyHeader(sig.Table, sig.IsBefore, sig.FiresInsert, sig.FiresUpdate, sig.FiresDelete, sig.Position, sig.Active);
        SyncEasyModelFromBody(sig.Body);
        return true;
    }

    protected override string? TryParseName(string? sql) => TryParseTriggerName(sql);

    internal static string? TryParseTriggerName(string? sql)
    {
        var sig = TriggerSignatureParser.Parse(sql);
        return sig.Success ? sig.Name : null;
    }

    // In Easy mode a table and at least one event are mandatory — block Compile with a
    // clear message instead of letting the server reject the generated DDL.
    protected override string? ValidateBeforeCompile()
    {
        if (!EasyMode) return null;
        if (string.IsNullOrWhiteSpace(TableName)) return UiStrings.TriggerTableRequiredNotice;
        if (!(FiresInsert || FiresUpdate || FiresDelete)) return UiStrings.TriggerEventRequiredNotice;
        return null;
    }

    protected override async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        await SafeLoadAsync(async () =>
        {
            SourceText = await DdlReader!.FetchTriggerSourceAsync(
                new MetadataObject(TriggerName, MetadataObjectKind.Trigger), cancellationToken).ConfigureAwait(true);
        });

        await SafeLoadAsync(async () =>
        {
            var header = await Reader!.GetTriggerHeaderAsync(TriggerName, cancellationToken).ConfigureAwait(true);
            // Set the DB-trigger flag first so it gates BuildFullSource before we force
            // Source mode — a DB-level trigger has no relation-trigger Easy representation.
            IsDatabaseTrigger = header.IsDatabaseTrigger;
            if (IsDatabaseTrigger) EasyMode = false;
            ApplyHeader(header.Table, header.IsBefore, header.FiresInsert, header.FiresUpdate, header.FiresDelete, header.Position, header.Active);
        });

        await SafeLoadAsync(async () =>
        {
            var body = await DdlReader!.FetchTriggerBodyAsync(
                new MetadataObject(TriggerName, MetadataObjectKind.Trigger), cancellationToken).ConfigureAwait(true);
            SyncEasyModelFromBody(body);
        });

        await SafeLoadAsync(async () =>
        {
            var (dependsOn, dependedOnBy) = await Reader!.GetTriggerDependenciesAsync(TriggerName, cancellationToken).ConfigureAwait(true);
            DependsOnTree.Clear();
            foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependsOn)) DependsOnTree.Add(g);
            DependedOnByTree.Clear();
            foreach (var g in TableDetailTabViewModel.BuildDependencyTree(dependedOnBy)) DependedOnByTree.Add(g);
        });

        await SafeLoadAsync(async () =>
        {
            // DDL tab == Export (structure + COMMENT ON via MetadataExportService); the
            // editable Source (Editor tab) is untouched.
            DdlText = await new MetadataExportService(DdlReader!, Reader!).BuildObjectScriptAsync(
                new MetadataObject(TriggerName, MetadataObjectKind.Trigger), cancellationToken).ConfigureAwait(true);
        });

        await SafeLoadAsync(async () =>
        {
            Description = await Reader!.GetTriggerDescriptionAsync(TriggerName, cancellationToken).ConfigureAwait(true);
            DescriptionLoaded = true;
        });
    }
}

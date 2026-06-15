using System;
using System.IO;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Tests for the stabilization sprint:
///   - Tab activation history (close → return to the previously-active tab).
///   - Inline Size/Scale editing on the Pola grid (parity with the Edit-Field
///     dialog; ALTER COLUMN TYPE generated through the shared DdlGenerator).
///   - FieldRowViewModel event-subscription Detach (the refresh-storm leak fix).
/// </summary>
public class SprintStabilizationTests
{
    // ─── Task 3: tab activation history ───────────────────────────────────

    [Fact]
    public void CloseActiveTab_ReturnsToPreviouslyActiveTab_NotIndexNeighbour()
    {
        using var h = new Harness();
        var main = h.Main;
        main.ApplyActiveConnectionChange("A"); // fresh Query tab at index 0

        var a = WorkspaceTabViewModel.CreateDdl(main, new MetadataObject("A", MetadataObjectKind.Procedure), "", "A");
        var b = WorkspaceTabViewModel.CreateDdl(main, new MetadataObject("B", MetadataObjectKind.Procedure), "", "A");
        var c = WorkspaceTabViewModel.CreateDdl(main, new MetadataObject("C", MetadataObjectKind.Procedure), "", "A");
        main.WorkspaceTabs.Add(a);
        main.WorkspaceTabs.Add(b);
        main.WorkspaceTabs.Add(c);

        main.SelectTab(a);   // visit A
        main.SelectTab(c);   // jump to C (B is the index-neighbour of C, A is the history-previous)
        main.CloseTab(c);

        // History returns to A; the old index-neighbour logic would have landed on B.
        Assert.Equal(a, main.SelectedWorkspaceTab);
        Assert.NotEqual(b, main.SelectedWorkspaceTab);
    }

    [Fact]
    public void CloseProcedureOpenedFromTable_ReturnsToTable()
    {
        using var h = new Harness();
        var main = h.Main;
        main.ApplyActiveConnectionChange("A");

        var tableDetail = new TableDetailTabViewModel("MYTABLE");
        var table = WorkspaceTabViewModel.CreateTableDetail(
            main, new MetadataObject("MYTABLE", MetadataObjectKind.Table), tableDetail, "A");
        main.WorkspaceTabs.Add(table);
        main.SelectTab(table);

        var proc = WorkspaceTabViewModel.CreateDdl(main, new MetadataObject("MYPROC", MetadataObjectKind.Procedure), "", "A");
        main.WorkspaceTabs.Add(proc);
        main.SelectTab(proc);

        main.CloseTab(proc);

        Assert.Equal(table, main.SelectedWorkspaceTab);
    }

    [Fact]
    public void CloseNonActiveTab_DoesNotChangeSelection()
    {
        using var h = new Harness();
        var main = h.Main;
        main.ApplyActiveConnectionChange("A");

        var a = WorkspaceTabViewModel.CreateDdl(main, new MetadataObject("A", MetadataObjectKind.Procedure), "", "A");
        var b = WorkspaceTabViewModel.CreateDdl(main, new MetadataObject("B", MetadataObjectKind.Procedure), "", "A");
        main.WorkspaceTabs.Add(a);
        main.WorkspaceTabs.Add(b);
        main.SelectTab(b);

        main.CloseTab(a); // close the non-active one

        Assert.Equal(b, main.SelectedWorkspaceTab);
    }

    // ─── Task 4 (secondary): inline Size / Scale editing ──────────────────

    [Fact]
    public void VarcharSizeParsedFromType_AndEffectiveTypeMatchesOriginal()
    {
        var row = new FieldRowViewModel(new FieldInfo { Name = "C", Type = "VARCHAR(50)" });
        Assert.Equal(50, row.Size);
        Assert.Null(row.Scale);
        Assert.Equal("VARCHAR(50)", row.EffectiveTypeText);
        Assert.False(row.IsModified);
    }

    [Fact]
    public void EditingVarcharSize_MarksModified_AndUpdatesEffectiveType()
    {
        var row = new FieldRowViewModel(new FieldInfo { Name = "C", Type = "VARCHAR(50)" });
        row.Size = 100;
        Assert.True(row.IsModified);
        Assert.Equal("VARCHAR(100)", row.EffectiveTypeText);
    }

    [Fact]
    public void NumericPrecisionScaleParsed_AndEditingPrecisionUpdatesType()
    {
        var row = new FieldRowViewModel(new FieldInfo { Name = "N", Type = "NUMERIC(15,2)" });
        Assert.Equal(15, row.Size);   // precision
        Assert.Equal(2, row.Scale);
        Assert.Equal("NUMERIC(15,2)", row.EffectiveTypeText);
        Assert.False(row.IsModified);

        row.Size = 18;
        Assert.True(row.IsModified);
        Assert.Equal("NUMERIC(18,2)", row.EffectiveTypeText);
    }

    [Fact]
    public void EditingSizeOnNonSizedType_DoesNotMarkModified()
    {
        var row = new FieldRowViewModel(new FieldInfo { Name = "I", Type = "INTEGER" });
        Assert.Null(row.Size);
        row.Size = 9; // meaningless for INTEGER — type ignores it
        Assert.Equal("INTEGER", row.EffectiveTypeText);
        Assert.False(row.IsModified);
    }

    [Fact]
    public void RevertTypeToOriginal_RestoresSizeAndClearsModified()
    {
        var row = new FieldRowViewModel(new FieldInfo { Name = "C", Type = "VARCHAR(50)" });
        row.Size = 100;
        Assert.True(row.IsModified);
        row.RevertTypeToOriginal();
        Assert.Equal(50, row.Size);
        Assert.False(row.IsModified);
    }

    [Fact]
    public void EnqueueRowEdits_SizeChange_QueuesAlterColumnType()
    {
        var td = new TableDetailTabViewModel("MYTABLE");
        td.Fields.Add(new FieldInfo { Name = "C", Type = "VARCHAR(50)" });
        var row = td.EditableFields[0];

        row.Size = 100;
        td.EnqueueRowEdits(row);

        Assert.Contains(td.PendingChanges, c =>
            c.Sql.Contains("VARCHAR(100)", StringComparison.OrdinalIgnoreCase)
            && c.Sql.Contains("TYPE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnqueueRowEdits_NoChange_QueuesNothing()
    {
        var td = new TableDetailTabViewModel("MYTABLE");
        td.Fields.Add(new FieldInfo { Name = "C", Type = "VARCHAR(50)" });
        var row = td.EditableFields[0];

        td.EnqueueRowEdits(row);

        Assert.Empty(td.PendingChanges);
    }

    // ─── Task 4: FieldRowViewModel event-subscription Detach (leak fix) ────

    [Fact]
    public void Detach_StopsOwnerEventPropagation_AndIsIdempotent()
    {
        var td = new TableDetailTabViewModel("T");
        var row = new FieldRowViewModel(new FieldInfo { Name = "C", Type = "INTEGER" }, td);

        var notifications = 0;
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FieldRowViewModel.IsCellEditable)) notifications++;
        };

        td.IsFieldEditMode = true;          // before detach → row sees it
        Assert.Equal(1, notifications);

        row.Detach();
        td.IsFieldEditMode = false;         // after detach → no propagation
        Assert.Equal(1, notifications);

        row.Detach();                       // idempotent — no throw
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Store = new ConnectionProfileStore(TempDir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(Store, Service);
        }

        public string TempDir { get; }
        public ConnectionProfileStore Store { get; }
        public FirebirdConnectionService Service { get; }
        public MainWindowViewModel Main { get; }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}

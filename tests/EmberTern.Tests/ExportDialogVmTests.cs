using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Export;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Export;
using EmberTern.Core.Query;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Etap 3 — the shared Export dialog VM (format-driven disclosure, scope options, the export flow via
// injected file/clipboard delegates) + the MainWindowViewModel entry points that raise it.
public class ExportDialogVmTests
{
    private static readonly QueryColumn[] Columns =
    {
        new("Name", typeof(string)),
        new("Value", typeof(int)),
    };

    private static QueryResultExportSource Source(
        IReadOnlyList<object?[]>? view = null,
        IReadOnlyList<object?[]>? all = null,
        bool truncated = false)
    {
        view ??= new object?[][] { new object?[] { "a", 1 } };
        all ??= view;
        return new QueryResultExportSource(Columns, view, all, truncated, streamAll: null, "query_result");
    }

    // ── format-driven disclosure + defaults ─────────────────────────────────
    [Fact]
    public void Ctor_DefaultsToCsv_WithExcelFriendlyOptions()
    {
        var vm = new ExportDialogViewModel(Source(), ExportScope.AllRows);
        Assert.Equal(ExportFormat.Csv, vm.SelectedFormat);
        Assert.True(vm.IsFormatCsv);
        Assert.True(vm.ShowDelimitedOptions);
        Assert.True(vm.ShowEncodingOption);
        Assert.Equal(';', vm.SelectedDelimiterOption.Value);
        Assert.True(vm.UseBom);
        Assert.True(vm.IncludeHeader);
    }

    [Fact]
    public void SwitchToText_UsesTabAndNoBom()
    {
        var vm = new ExportDialogViewModel(Source(), ExportScope.AllRows) { SelectedFormat = ExportFormat.Text };
        Assert.True(vm.IsFormatText);
        Assert.True(vm.ShowDelimitedOptions);
        Assert.Equal('\t', vm.SelectedDelimiterOption.Value);
        Assert.False(vm.UseBom);
    }

    [Fact]
    public void SwitchToClipboard_HidesDelimitedAndEncodingOptions()
    {
        var vm = new ExportDialogViewModel(Source(), ExportScope.AllRows) { SelectedFormat = ExportFormat.Clipboard };
        Assert.True(vm.IsFormatClipboard);
        Assert.False(vm.ShowDelimitedOptions);
        Assert.False(vm.ShowEncodingOption);
    }

    [Fact]
    public void IsFormatSetter_SwitchesSelectedFormat()
    {
        var vm = new ExportDialogViewModel(Source(), ExportScope.AllRows);
        vm.IsFormatClipboard = true;
        Assert.Equal(ExportFormat.Clipboard, vm.SelectedFormat);
        Assert.False(vm.IsFormatCsv);
    }

    // ── scope options ────────────────────────────────────────────────────────
    [Fact]
    public void ScopeOptions_ComeFromCapabilities_WithCountLabels()
    {
        var view = new object?[][] { new object?[] { "a", 1 } };
        var all = new object?[][] { new object?[] { "a", 1 }, new object?[] { "b", 2 } };
        var vm = new ExportDialogViewModel(Source(view, all), ExportScope.AllRows);

        Assert.Equal(2, vm.ScopeOptions.Count); // CurrentView + AllRows (no SelectedRows for the SQL grid)
        var currentView = vm.ScopeOptions.Single(o => o.Scope == ExportScope.CurrentView);
        var allRows = vm.ScopeOptions.Single(o => o.Scope == ExportScope.AllRows);
        Assert.Contains("1", currentView.Label);   // exact count of the view
        Assert.Contains("2", allRows.Label);        // exact count of the complete set
    }

    [Fact]
    public void DefaultScope_HonouredWhenSupported()
    {
        var vm = new ExportDialogViewModel(Source(), ExportScope.CurrentView);
        Assert.Equal(ExportScope.CurrentView, vm.SelectedScope);
    }

    [Fact]
    public void ScopeOption_IsSelected_TwoWaySetsParentScope()
    {
        var vm = new ExportDialogViewModel(Source(), ExportScope.AllRows);
        var currentViewOption = vm.ScopeOptions.Single(o => o.Scope == ExportScope.CurrentView);

        currentViewOption.IsSelected = true;
        Assert.Equal(ExportScope.CurrentView, vm.SelectedScope);
        // The previously-selected AllRows option reports unselected.
        Assert.False(vm.ScopeOptions.Single(o => o.Scope == ExportScope.AllRows).IsSelected);
    }

    // ── export flow ────────────────────────────────────────────────────────
    [Fact]
    public async Task ExportToClipboard_CopiesTsv_ReturnsOutcome_AndCloses()
    {
        var vm = new ExportDialogViewModel(Source(), ExportScope.AllRows) { SelectedFormat = ExportFormat.Clipboard };
        string? copied = null;
        bool closed = false;
        vm.WriteClipboard = t => { copied = t; return Task.CompletedTask; };
        vm.RequestClose += () => closed = true;

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal("Name\tValue\r\na\t1\r\n", copied);
        Assert.NotNull(vm.Result);
        Assert.Equal(ExportFormat.Clipboard, vm.Result!.Format);
        Assert.Equal(1, vm.Result.RowCount);
        Assert.Null(vm.Result.FilePath);
        Assert.True(closed);
    }

    [Fact]
    public async Task ExportToFile_WritesFile_ReturnsOutcomeWithPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "embertern-export-vm-" + Guid.NewGuid().ToString("N") + ".csv");
        var vm = new ExportDialogViewModel(Source(), ExportScope.AllRows); // Csv default
        vm.RequestSavePath = _ => Task.FromResult<string?>(path);
        bool closed = false;
        vm.RequestClose += () => closed = true;

        try
        {
            await vm.ExportCommand.ExecuteAsync(null);

            Assert.NotNull(vm.Result);
            Assert.Equal(ExportFormat.Csv, vm.Result!.Format);
            Assert.Equal(path, vm.Result.FilePath);
            Assert.True(closed);
            Assert.True(File.Exists(path));
            Assert.Contains("Name;Value", await File.ReadAllTextAsync(path));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task ExportToFile_PickerCancelled_DoesNotExportOrClose()
    {
        var vm = new ExportDialogViewModel(Source(), ExportScope.AllRows);
        vm.RequestSavePath = _ => Task.FromResult<string?>(null); // user cancelled the save picker
        bool closed = false;
        vm.RequestClose += () => closed = true;

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Null(vm.Result);
        Assert.False(closed);
        Assert.False(vm.IsExporting); // returned to the options view
    }

    [Fact]
    public async Task ExportCurrentViewScope_ExportsTheViewRows_NotAllRows()
    {
        var view = new object?[][] { new object?[] { "v", 9 } };
        var all = new object?[][] { new object?[] { "a", 1 }, new object?[] { "b", 2 } };
        var vm = new ExportDialogViewModel(Source(view, all), ExportScope.CurrentView) { SelectedFormat = ExportFormat.Clipboard };
        string? copied = null;
        vm.WriteClipboard = t => { copied = t; return Task.CompletedTask; };

        await vm.ExportCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Result!.RowCount);            // the 1-row view, not the 2-row full set
        Assert.Contains("v\t9", copied);
    }

    [Fact]
    public void Cancel_WhileConfiguring_ClosesWithNullResult()
    {
        var vm = new ExportDialogViewModel(Source(), ExportScope.AllRows);
        bool closed = false;
        vm.RequestClose += () => closed = true;

        vm.CancelCommand.Execute(null);

        Assert.Null(vm.Result);
        Assert.True(closed);
    }

    // ── MainWindowViewModel entry points ─────────────────────────────────────
    [Fact]
    public void CanExportResults_TracksCurrentResult()
    {
        using var h = new Harness();
        Assert.False(h.Main.CanExportResults);
        h.Main.CurrentResult = MakeResult(3);
        Assert.True(h.Main.CanExportResults);
    }

    [Fact]
    public void BuildResultsExportSource_ForCompleteResult_ExposesBothScopesExact()
    {
        using var h = new Harness();
        h.Main.CurrentResult = MakeResult(3);
        var source = h.Main.BuildResultsExportSource();

        Assert.NotNull(source);
        Assert.Equal(RowEstimate.Exact(3), source!.Capabilities.EstimateFor(ExportScope.CurrentView));
        Assert.Equal(RowEstimate.Exact(3), source.Capabilities.EstimateFor(ExportScope.AllRows));
    }

    [Fact]
    public async Task ExportResults_RaisesRequest_WithAllRowsDefault_AndReportsOutcome()
    {
        using var h = new Harness();
        h.Main.CurrentResult = MakeResult(3);

        ExportDialogRequest? captured = null;
        h.Main.ExportRequested += req =>
        {
            captured = req;
            return Task.FromResult<ExportOutcome?>(new ExportOutcome(ExportFormat.Csv, req.DefaultScope, 3, @"C:\x.csv"));
        };

        await h.Main.ExportResultsCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.Equal(ExportScope.AllRows, captured!.DefaultScope);
        Assert.Contains(h.Main.Messages, m => m.Text.Contains("Exported 3 rows"));
    }

    [Fact]
    public async Task ExportResults_CancelledDialog_PostsNoMessage()
    {
        using var h = new Harness();
        h.Main.CurrentResult = MakeResult(3);
        var before = h.Main.Messages.Count;
        h.Main.ExportRequested += _ => Task.FromResult<ExportOutcome?>(null); // dialog cancelled

        await h.Main.ExportResultsCommand.ExecuteAsync(null);

        Assert.Equal(before, h.Main.Messages.Count);
    }

    private static QueryResult MakeResult(int count)
    {
        var rows = new object?[count][];
        for (int i = 0; i < count; i++) rows[i] = new object?[] { "r" + i, i };
        return new QueryResult { Columns = Columns, Rows = rows };
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
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

using System;
using System.IO;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Query;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// Etap 1 — Execution Modes: Preview / Full model + the loud truncated-Preview notice bar.
// The streaming executor's row-cap-by-intent behaviour needs a live Firebird, so it is
// smoke-verified (DB-path convention); these pin the pure Core surface + the VM state the
// notice bar / record indicator / Load-all binds to, and the preserve-view-state seam.
public class ExecutionModesTests
{
    [Fact]
    public void ExecutionDefaults_MatchFrozenDesign()
    {
        Assert.Equal(5000, ExecutionDefaults.PreviewLimit);
        Assert.Equal(1_000_000L, ExecutionDefaults.FullSafetyCeiling);
    }

    [Fact]
    public void ExecutionRequest_DefaultsToPreviewWithDefaultLimits()
    {
        var req = new ExecutionRequest { Sql = "select 1 from rdb$database" };
        Assert.Equal(ExecutionIntent.Preview, req.Intent);
        Assert.Equal(ExecutionDefaults.PreviewLimit, req.PreviewLimit);
        Assert.Equal(ExecutionDefaults.FullSafetyCeiling, req.FullSafetyCeiling);
        Assert.Null(req.Parameters);
    }

    [Fact]
    public void QueryResult_TruncatedAndCeiling_AreIndependentFlags()
    {
        var r = new QueryResult { Truncated = true };
        Assert.True(r.Truncated);
        Assert.False(r.CeilingHit);
    }

    [Fact]
    public void TruncatedPreview_ShowsNotice_WithLoadAll()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Result(10, truncated: true);

        Assert.True(h.Main.ShowResultsNotice);
        Assert.True(h.Main.ShowLoadAllButton);
        Assert.Contains("Showing the first", h.Main.ResultsNoticeText);
    }

    [Fact]
    public void CeilingHit_ShowsNotice_ButNoLoadAll()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Result(10, ceilingHit: true);

        Assert.True(h.Main.ShowResultsNotice);
        Assert.False(h.Main.ShowLoadAllButton);          // nothing more to safely load
        Assert.Contains("safety limit", h.Main.ResultsNoticeText);
    }

    [Fact]
    public void NormalResult_ShowsNoNotice()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Result(10);

        Assert.False(h.Main.ShowResultsNotice);
        Assert.False(h.Main.ShowLoadAllButton);
        Assert.Equal(string.Empty, h.Main.ResultsNoticeText);
    }

    [Fact]
    public void TruncatedPreview_RecordInfo_HasPreviewSuffix()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Result(5000, truncated: true);

        Assert.Equal("5000+ rows (preview)", h.Main.ResultRecordInfo);

        h.Main.SetResultSelectedRow(2); // 3rd record on page 1
        Assert.Equal("Record 3 of 5000+ (preview)", h.Main.ResultRecordInfo);
    }

    [Fact]
    public void NormalResult_RecordInfo_HasNoPreviewSuffix()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Result(10);
        Assert.Equal("10 rows", h.Main.ResultRecordInfo);
    }

    [Fact]
    public void CanLoadAllRows_FalseWhenNotConnected_EvenIfTruncated()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Result(10, truncated: true);
        // The button is visible (ShowLoadAllButton), but the command can't run without a
        // connection — the gate documents that Load-all always re-reads from the DB.
        Assert.False(h.Main.CanLoadAllRows);
    }

    [Fact]
    public void ApplyFullResult_PreservesClientSideSort_AndClearsPreviewMarker()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Result(10, truncated: true);
        h.Main.CycleResultSort(0);                 // sort ascending on column 0
        Assert.Equal(0, h.Main.ResultSortColumnIndex);

        h.Main.ApplyFullResult(Result(25));        // full set of the same query (not truncated)

        Assert.Equal(0, h.Main.ResultSortColumnIndex);        // sort preserved across load-all
        Assert.False(h.Main.ShowResultsNotice);               // no longer truncated
        Assert.Equal("25 rows", h.Main.ResultRecordInfo);     // full count, no preview suffix
    }

    [Fact]
    public void NormalResultAssignment_ResetsSort()
    {
        using var h = new Harness();
        h.Main.CurrentResult = Result(10, truncated: true);
        h.Main.CycleResultSort(0);
        Assert.Equal(0, h.Main.ResultSortColumnIndex);

        h.Main.CurrentResult = Result(25);         // a fresh, unrelated result → reset the view
        Assert.Equal(-1, h.Main.ResultSortColumnIndex);
    }

    private static QueryResult Result(int count, bool truncated = false, bool ceilingHit = false)
    {
        var rows = new object?[count][];
        for (int i = 0; i < count; i++) rows[i] = new object?[] { i };
        return new QueryResult
        {
            Columns = new[] { new QueryColumn("N", typeof(int)) },
            Rows = rows,
            Truncated = truncated,
            CeilingHit = ceilingHit,
        };
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

using System;
using System.IO;
using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

// Parameter dialog improvements: TIMESTAMP default 00:00:00, free-text time parsing,
// and persistent per-object parameter history.
public class ExecuteProcedureDialogTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));

    private static ProcedureParamRowViewModel Param(string name, string type)
        => new() { Name = name, TypeText = type };

    // ─── TIMESTAMP default = today 00:00:00 ───────────────────────────────────

    [Fact]
    public void Timestamp_DefaultsToMidnight()
    {
        var row = new ExecuteProcedureParamRowViewModel("T", "TIMESTAMP");
        Assert.Equal(DateTime.Now.Date, row.DateValue);
        Assert.Equal(TimeSpan.Zero, row.TimeValue);
        Assert.Equal("00:00:00", row.TimeText);
    }

    [Fact]
    public void Time_DefaultsToCurrentTimeOfDay()
    {
        var row = new ExecuteProcedureParamRowViewModel("T", "TIME");
        Assert.NotNull(row.TimeValue);
        // TimeText normalized to HH:mm:ss.
        Assert.Matches(@"^\d\d:\d\d:\d\d$", row.TimeText);
    }

    [Fact]
    public void Date_DefaultUnchanged()
        => Assert.Equal(DateTime.Now.Date, new ExecuteProcedureParamRowViewModel("D", "DATE").DateValue);

    // ─── Time text parsing ────────────────────────────────────────────────────

    [Theory]
    [InlineData("8", 8, 0, 0)]
    [InlineData("8:30", 8, 30, 0)]
    [InlineData("08:30", 8, 30, 0)]
    [InlineData("8:30:15", 8, 30, 15)]
    [InlineData("14:45:10", 14, 45, 10)]
    [InlineData("23:59:59", 23, 59, 59)]
    [InlineData("00:00:00", 0, 0, 0)]
    [InlineData("  9:5:3  ", 9, 5, 3)]
    public void TryParseTime_Accepts(string input, int h, int m, int s)
    {
        Assert.True(ExecuteProcedureParamRowViewModel.TryParseTime(input, out var v));
        Assert.Equal(new TimeSpan(h, m, s), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseTime_EmptyIsMidnight(string input)
    {
        Assert.True(ExecuteProcedureParamRowViewModel.TryParseTime(input, out var v));
        Assert.Equal(TimeSpan.Zero, v);
    }

    [Theory]
    [InlineData("24:00:00")]
    [InlineData("8:60")]
    [InlineData("8:30:60")]
    [InlineData("abc")]
    [InlineData("8:xx")]
    [InlineData("8:30:15:20")]
    [InlineData("-1:00")]
    public void TryParseTime_Rejects(string input)
        => Assert.False(ExecuteProcedureParamRowViewModel.TryParseTime(input, out _));

    [Fact]
    public void CommitTime_NormalizesToHhMmSs()
    {
        var row = new ExecuteProcedureParamRowViewModel("T", "TIME") { IsNull = false, TimeText = "8:5" };
        Assert.True(row.CommitTime());
        Assert.Equal("08:05:00", row.TimeText);
        Assert.Equal(new TimeSpan(8, 5, 0), row.TimeValue);
        Assert.False(row.HasTimeError);
    }

    [Fact]
    public void CommitTime_FlagsError_OnGarbage()
    {
        var row = new ExecuteProcedureParamRowViewModel("T", "TIMESTAMP") { IsNull = false, TimeText = "99:99" };
        Assert.False(row.CommitTime());
        Assert.True(row.HasTimeError);
    }

    [Fact]
    public void CommitTime_NoErrorWhenNull()
    {
        var row = new ExecuteProcedureParamRowViewModel("T", "TIMESTAMP") { IsNull = true, TimeText = "garbage" };
        Assert.True(row.CommitTime());
        Assert.False(row.HasTimeError);
    }

    // ─── History value round-trip (invariant strings) ────────────────────────

    [Fact]
    public void HistoryValue_RoundTrips_PerKind()
    {
        AssertRoundTrip("INTEGER", r => r.NumericValue = 42m, r => Assert.Equal(42m, r.NumericValue));
        AssertRoundTrip("VARCHAR(10)", r => r.TextValue = "hi", r => Assert.Equal("hi", r.TextValue));
        AssertRoundTrip("BOOLEAN", r => r.BoolValue = true, r => Assert.True(r.BoolValue));
        AssertRoundTrip("DATE", r => r.DateValue = new DateTime(2024, 3, 4), r => Assert.Equal(new DateTime(2024, 3, 4), r.DateValue));
        AssertRoundTrip("TIME",
            r => { r.TimeValue = new TimeSpan(13, 30, 15); r.TimeText = "13:30:15"; },
            r => Assert.Equal(new TimeSpan(13, 30, 15), r.TimeValue));
    }

    [Fact]
    public void HistoryValue_Timestamp_PreservesSubSecond()
    {
        var source = new ExecuteProcedureParamRowViewModel("T", "TIMESTAMP")
        {
            IsNull = false,
            DateValue = new DateTime(2024, 6, 1),
            TimeValue = new TimeSpan(0, 14, 35, 12, 340),
        };
        var pv = source.ToHistoryValue();

        var target = new ExecuteProcedureParamRowViewModel("T", "TIMESTAMP");
        target.ApplyHistoryValue(pv);

        Assert.Equal(new DateTime(2024, 6, 1, 14, 35, 12, 340), (DateTime)target.Resolve()!);
    }

    [Fact]
    public void HistoryValue_NullRoundTrips()
    {
        var source = new ExecuteProcedureParamRowViewModel("N", "INTEGER") { IsNull = true };
        var pv = source.ToHistoryValue();
        Assert.True(pv.IsNull);

        var target = new ExecuteProcedureParamRowViewModel("N", "INTEGER") { IsNull = false, NumericValue = 9m };
        target.ApplyHistoryValue(pv);
        Assert.True(target.IsNull);
    }

    private static void AssertRoundTrip(string type, Action<ExecuteProcedureParamRowViewModel> set,
        Action<ExecuteProcedureParamRowViewModel> assert)
    {
        var source = new ExecuteProcedureParamRowViewModel("P", type) { IsNull = false };
        set(source);
        var pv = source.ToHistoryValue();

        var target = new ExecuteProcedureParamRowViewModel("P", type);
        target.ApplyHistoryValue(pv);
        Assert.False(target.IsNull);
        assert(target);
    }

    // ─── Dialog VM history behavior ───────────────────────────────────────────

    [Fact]
    public void Dialog_NoStore_HasNoHistory_AndDefaultsHold()
    {
        var dlg = new ExecuteProcedureDialogViewModel(new[] { Param("N", "INTEGER") });
        Assert.False(dlg.HasHistory);
        Assert.True(dlg.Params[0].IsNull);
    }

    [Fact]
    public void Dialog_AutoLoadsMostRecent()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            var inputs = new[] { Param("DATAOD", "DATE"), Param("DATADO", "DATE") };

            var first = new ExecuteProcedureDialogViewModel(inputs, "RPT", "c1", "Procedure", store);
            first.Params[0].IsNull = false; first.Params[0].DateValue = new DateTime(2024, 1, 1);
            first.Params[1].IsNull = false; first.Params[1].DateValue = new DateTime(2024, 12, 31);
            first.AcceptCommand.Execute(null);

            var second = new ExecuteProcedureDialogViewModel(inputs, "RPT", "c1", "Procedure", new ParameterHistoryStore(dir));
            second.Params[0].IsNull = false; second.Params[0].DateValue = new DateTime(2025, 1, 1);
            second.Params[1].IsNull = false; second.Params[1].DateValue = new DateTime(2025, 12, 31);
            second.AcceptCommand.Execute(null);

            // A fresh dialog auto-applies the newest set.
            var reopened = new ExecuteProcedureDialogViewModel(inputs, "RPT", "c1", "Procedure", new ParameterHistoryStore(dir));
            Assert.Equal(2, reopened.History.Count);
            Assert.Same(reopened.History[0], reopened.SelectedHistory);
            Assert.Equal(new DateTime(2025, 1, 1), reopened.Params[0].DateValue);
            Assert.Equal(new DateTime(2025, 12, 31), reopened.Params[1].DateValue);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Dialog_SelectingHistory_AppliesValues()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            var inputs = new[] { Param("N", "INTEGER") };

            var d1 = new ExecuteProcedureDialogViewModel(inputs, "SP", "c1", "Procedure", store);
            d1.Params[0].IsNull = false; d1.Params[0].NumericValue = 10m; d1.AcceptCommand.Execute(null);
            var d2 = new ExecuteProcedureDialogViewModel(inputs, "SP", "c1", "Procedure", new ParameterHistoryStore(dir));
            d2.Params[0].IsNull = false; d2.Params[0].NumericValue = 20m; d2.AcceptCommand.Execute(null);

            var dlg = new ExecuteProcedureDialogViewModel(inputs, "SP", "c1", "Procedure", new ParameterHistoryStore(dir));
            Assert.Equal(20m, dlg.Params[0].NumericValue);   // newest auto-applied

            dlg.SelectedHistory = dlg.History.Last();          // pick the older set
            Assert.Equal(10m, dlg.Params[0].NumericValue);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void Dialog_HistorySnapshot_PreviewShowsValues()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            var inputs = new[] { Param("DATAOD", "DATE"), Param("DATADO", "DATE") };
            var d1 = new ExecuteProcedureDialogViewModel(inputs, "RPT", "c1", "Procedure", store);
            d1.Params[0].IsNull = false; d1.Params[0].DateValue = new DateTime(2024, 1, 1);
            d1.Params[1].IsNull = false; d1.Params[1].DateValue = new DateTime(2024, 12, 31);
            d1.AcceptCommand.Execute(null);

            var dlg = new ExecuteProcedureDialogViewModel(inputs, "RPT", "c1", "Procedure", new ParameterHistoryStore(dir));
            var preview = dlg.History[0].PreviewText;
            Assert.Contains("DATAOD=2024-01-01", preview);
            Assert.Contains("DATADO=2024-12-31", preview);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    // ─── Invalid time blocks execution ────────────────────────────────────────

    [Fact]
    public void Dialog_InvalidTime_BlocksAccept()
    {
        var dlg = new ExecuteProcedureDialogViewModel(new[] { Param("T", "TIMESTAMP") });
        dlg.Params[0].IsNull = false;
        dlg.Params[0].TimeText = "99:99";

        bool closed = false;
        dlg.RequestClose += () => closed = true;

        dlg.AcceptCommand.Execute(null);

        Assert.Null(dlg.Result);            // no values returned
        Assert.False(closed);               // dialog stays open
        Assert.True(dlg.HasValidationError);
        Assert.True(dlg.Params[0].HasTimeError);
    }

    [Fact]
    public void Dialog_ValidTime_AllowsAccept()
    {
        var dlg = new ExecuteProcedureDialogViewModel(new[] { Param("T", "TIMESTAMP") });
        dlg.Params[0].IsNull = false;
        dlg.Params[0].DateValue = new DateTime(2024, 1, 2);
        dlg.Params[0].TimeText = "8:30";

        bool closed = false;
        dlg.RequestClose += () => closed = true;

        dlg.AcceptCommand.Execute(null);

        Assert.True(closed);
        Assert.False(dlg.HasValidationError);
        Assert.NotNull(dlg.Result);
        Assert.Equal(new DateTime(2024, 1, 2, 8, 30, 0), (DateTime)dlg.Result![0]!);
    }
}

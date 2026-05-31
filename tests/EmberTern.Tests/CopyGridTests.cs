using System;
using System.IO;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Query;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class CopyGridTests
{
    [Fact]
    public void BuildCopyText_Cell_ReturnsSingleValue()
    {
        using var h = new Harness();
        h.Main.CurrentResult = SampleResult();

        var text = h.Main.BuildCopyText(CopyGridMode.Cell, rowIndex: 1, columnIndex: 1);

        Assert.Equal("Bob", text);
    }

    [Fact]
    public void BuildCopyText_Cell_NullCell_ReturnsEmptyString()
    {
        using var h = new Harness();
        h.Main.CurrentResult = SampleResult();

        var text = h.Main.BuildCopyText(CopyGridMode.Cell, rowIndex: 2, columnIndex: 1);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void BuildCopyText_Row_TabSeparatedNoHeader()
    {
        using var h = new Harness();
        h.Main.CurrentResult = SampleResult();

        var text = h.Main.BuildCopyText(CopyGridMode.Row, rowIndex: 0, columnIndex: 0);

        Assert.Equal("1\tAlice\talice@example.com", text);
    }

    [Fact]
    public void BuildCopyText_RowWithHeaders_PrependsHeaderLine()
    {
        using var h = new Harness();
        h.Main.CurrentResult = SampleResult();

        var text = h.Main.BuildCopyText(CopyGridMode.RowWithHeaders, rowIndex: 0, columnIndex: 0);

        var expected = "ID\tNAME\tEMAIL" + Environment.NewLine + "1\tAlice\talice@example.com";
        Assert.Equal(expected, text);
    }

    [Fact]
    public void BuildCopyText_AllWithHeaders_EmitsHeaderThenAllRows()
    {
        using var h = new Harness();
        h.Main.CurrentResult = SampleResult();

        var text = h.Main.BuildCopyText(CopyGridMode.AllWithHeaders, rowIndex: -1, columnIndex: -1);

        var nl = Environment.NewLine;
        var expected =
            "ID\tNAME\tEMAIL" + nl +
            "1\tAlice\talice@example.com" + nl +
            "2\tBob\tbob@example.com" + nl +
            "3\t\tcharlie@example.com";
        Assert.Equal(expected, text);
    }

    [Fact]
    public void BuildCopyText_EscapesTabsAndNewlinesInsideCells()
    {
        using var h = new Harness();
        h.Main.CurrentResult = new QueryResult
        {
            Columns = new[] { new QueryColumn("C1", typeof(string)), new QueryColumn("C2", typeof(string)) },
            Rows = new[]
            {
                new object?[] { "has\ttab", "has\nnewline" },
            },
        };

        var text = h.Main.BuildCopyText(CopyGridMode.Row, rowIndex: 0, columnIndex: 0);

        Assert.Equal("has tab\thas newline", text);
    }

    [Fact]
    public void BuildCopyText_NoResultSet_ReturnsNull()
    {
        using var h = new Harness();
        h.Main.CurrentResult = null;

        var text = h.Main.BuildCopyText(CopyGridMode.AllWithHeaders, 0, 0);

        Assert.Null(text);
    }

    [Fact]
    public void BuildCopyText_OutOfRangeIndex_ReturnsNull()
    {
        using var h = new Harness();
        h.Main.CurrentResult = SampleResult();

        Assert.Null(h.Main.BuildCopyText(CopyGridMode.Cell, rowIndex: 99, columnIndex: 0));
        Assert.Null(h.Main.BuildCopyText(CopyGridMode.Cell, rowIndex: 0, columnIndex: 99));
        Assert.Null(h.Main.BuildCopyText(CopyGridMode.Row, rowIndex: -1, columnIndex: 0));
    }

    [Fact]
    public async Task CopyGridAsync_InvokesClipboardWriteRequested()
    {
        using var h = new Harness();
        h.Main.CurrentResult = SampleResult();
        string? captured = null;
        h.Main.ClipboardWriteRequested += text =>
        {
            captured = text;
            return Task.CompletedTask;
        };

        var ok = await h.Main.CopyGridAsync(CopyGridMode.Cell, rowIndex: 0, columnIndex: 0);

        Assert.True(ok);
        Assert.Equal("1", captured);
    }

    [Fact]
    public async Task CopyGridAsync_NoResult_ReturnsFalseAndDoesNotInvokeClipboard()
    {
        using var h = new Harness();
        h.Main.CurrentResult = null;
        var invoked = false;
        h.Main.ClipboardWriteRequested += _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        };

        var ok = await h.Main.CopyGridAsync(CopyGridMode.Row, 0, 0);

        Assert.False(ok);
        Assert.False(invoked);
    }

    [Fact]
    public async Task CopyGridAsync_LogsConfirmationMessage()
    {
        using var h = new Harness();
        h.Main.CurrentResult = SampleResult();

        await h.Main.CopyGridAsync(CopyGridMode.AllWithHeaders, -1, -1);

        Assert.Contains(h.Main.Messages, m => m.Text.Contains("Copied", StringComparison.Ordinal));
    }

    private static QueryResult SampleResult() => new QueryResult
    {
        Columns = new[]
        {
            new QueryColumn("ID", typeof(int)),
            new QueryColumn("NAME", typeof(string)),
            new QueryColumn("EMAIL", typeof(string)),
        },
        Rows = new[]
        {
            new object?[] { 1, "Alice", "alice@example.com" },
            new object?[] { 2, "Bob", "bob@example.com" },
            new object?[] { 3, null, "charlie@example.com" },
        },
    };

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

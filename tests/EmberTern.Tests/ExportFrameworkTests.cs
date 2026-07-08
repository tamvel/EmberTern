using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Export;
using EmberTern.Core.Export;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

// Etap 3 — Export Framework core: the pure exporters (CSV/TXT RFC-4180, Clipboard TSV), the value
// formatter, ExportService orchestration, and the QueryResultExportSource adapter (smart
// cached-vs-re-run). All exercisable without a live DB — the exporters stream to an in-memory sink.
public class ExportFrameworkTests
{
    private static readonly ExportColumn[] TwoColumns =
    {
        new("Name", typeof(string)),
        new("Value", typeof(int)),
    };

    // ── ExportValueFormatter ─────────────────────────────────────────────────
    [Fact]
    public void ValueFormatter_NullAndDbNull_AreEmpty()
    {
        Assert.Equal(string.Empty, ExportValueFormatter.Format(null, CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, ExportValueFormatter.Format(DBNull.Value, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ValueFormatter_Blob_IsPlaceholder()
        => Assert.Equal(ExportValueFormatter.BlobPlaceholder,
            ExportValueFormatter.Format(new byte[] { 1, 2, 3 }, CultureInfo.InvariantCulture));

    [Fact]
    public void ValueFormatter_Decimal_HonoursCulture()
    {
        Assert.Equal("1.5", ExportValueFormatter.Format(1.5m, CultureInfo.InvariantCulture));
        var pl = CultureInfo.GetCultureInfo("pl-PL");
        Assert.Equal("1,5", ExportValueFormatter.Format(1.5m, pl));
    }

    [Fact]
    public void ValueFormatter_BoolAndString_UseToString()
    {
        Assert.Equal("True", ExportValueFormatter.Format(true, CultureInfo.InvariantCulture));
        Assert.Equal("hello", ExportValueFormatter.Format("hello", CultureInfo.InvariantCulture));
    }

    // ── DelimitedTextExporter (CSV / TXT) ────────────────────────────────────
    [Fact]
    public async Task Csv_WritesHeaderAndRows_CrlfTerminated()
    {
        var rows = new object?[][] { new object?[] { "a", 1 }, new object?[] { "b", 2 } };
        var text = await RunToString(new DelimitedTextExporter(new DelimitedTextOptions(';', true, true)), TwoColumns, rows);
        Assert.Equal("Name;Value\r\na;1\r\nb;2\r\n", text);
    }

    [Fact]
    public async Task Csv_NoHeader_OmitsHeaderLine()
    {
        var rows = new object?[][] { new object?[] { "a", 1 } };
        var text = await RunToString(new DelimitedTextExporter(new DelimitedTextOptions(';', false, true)), TwoColumns, rows);
        Assert.Equal("a;1\r\n", text);
    }

    [Theory]
    [InlineData(';')]
    [InlineData(',')]
    [InlineData('|')]
    [InlineData('\t')]
    public async Task Csv_UsesChosenDelimiter(char delimiter)
    {
        var rows = new object?[][] { new object?[] { "a", 1 } };
        var text = await RunToString(new DelimitedTextExporter(new DelimitedTextOptions(delimiter, false, true)), TwoColumns, rows);
        Assert.Equal($"a{delimiter}1\r\n", text);
    }

    [Fact]
    public async Task Csv_QuotesFieldsContainingDelimiterQuoteOrNewline()
    {
        var cols = new[] { new ExportColumn("C", typeof(string)) };
        var rows = new object?[][]
        {
            new object?[] { "has;delimiter" },          // contains the delimiter → quoted
            new object?[] { "has\"quote" },             // contains a quote → quoted + doubled
            new object?[] { "line1\r\nline2" },         // contains CRLF → quoted
            new object?[] { "plain" },                  // nothing special → not quoted
        };
        var text = await RunToString(new DelimitedTextExporter(new DelimitedTextOptions(';', false, true)), cols, rows);
        Assert.Equal("\"has;delimiter\"\r\n\"has\"\"quote\"\r\n\"line1\r\nline2\"\r\nplain\r\n", text);
    }

    [Fact]
    public async Task Csv_NullCell_IsEmptyField()
    {
        var rows = new object?[][] { new object?[] { null, 1 } };
        var text = await RunToString(new DelimitedTextExporter(new DelimitedTextOptions(';', false, true)), TwoColumns, rows);
        Assert.Equal(";1\r\n", text);
    }

    [Fact]
    public async Task Csv_ReturnsRowCount()
    {
        var rows = new object?[][] { new object?[] { "a", 1 }, new object?[] { "b", 2 }, new object?[] { "c", 3 } };
        var sink = new StringExportSink();
        var n = await new DelimitedTextExporter(new DelimitedTextOptions(';', true, true))
            .ExportAsync(TwoColumns, ToAsync(rows), sink, null, CancellationToken.None);
        Assert.Equal(3, n);
    }

    // ── ClipboardTextExporter (TSV, space-replacement) ───────────────────────
    [Fact]
    public async Task Clipboard_TabSeparated_WithHeader()
    {
        var rows = new object?[][] { new object?[] { "a", 1 } };
        var text = await RunToString(new ClipboardTextExporter(includeHeader: true), TwoColumns, rows);
        Assert.Equal("Name\tValue\r\na\t1\r\n", text);
    }

    [Fact]
    public async Task Clipboard_ReplacesTabAndNewlineWithSpace_NotQuotes()
    {
        var cols = new[] { new ExportColumn("C", typeof(string)) };
        var rows = new object?[][] { new object?[] { "a\tb\r\nc" } };
        var text = await RunToString(new ClipboardTextExporter(includeHeader: false), cols, rows);
        // Internal tab/CR/LF → spaces; no quoting (the Excel-paste convention).
        Assert.Equal("a b  c\r\n", text);
    }

    // ── ExportService ────────────────────────────────────────────────────────
    [Fact]
    public async Task Service_ClipboardCsvVsTsv_UsesTheRequestedExporter()
    {
        var source = MakeSource(new object?[][] { new object?[] { "a", 1 } });

        var clip = await new ExportService().ExportToClipboardTextAsync(
            source,
            new ExportRequest { Format = ExportFormat.Clipboard, Scope = ExportScope.AllRows, IncludeHeader = true },
            null, CancellationToken.None);

        Assert.Equal(1, clip.RowCount);
        Assert.Equal("Name\tValue\r\na\t1\r\n", clip.Text);
    }

    [Fact]
    public async Task Service_ExportToFile_Csv_WritesUtf8Bom()
    {
        var source = MakeSource(new object?[][] { new object?[] { "ą", 1 } });
        var path = Path.Combine(Path.GetTempPath(), "embertern-export-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var n = await new ExportService().ExportToFileAsync(
                source,
                new ExportRequest { Format = ExportFormat.Csv, Scope = ExportScope.AllRows, Delimited = new DelimitedTextOptions(';', true, true) },
                path, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true), null, CancellationToken.None);

            Assert.Equal(1, n);
            var bytes = await File.ReadAllBytesAsync(path);
            Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "CSV for Excel must start with a UTF-8 BOM.");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public async Task Service_ExportToFile_Text_NoBom()
    {
        var source = MakeSource(new object?[][] { new object?[] { "ą", 1 } });
        var path = Path.Combine(Path.GetTempPath(), "embertern-export-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            await new ExportService().ExportToFileAsync(
                source,
                new ExportRequest { Format = ExportFormat.Text, Scope = ExportScope.AllRows, Delimited = new DelimitedTextOptions('\t', true, false) },
                path, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), null, CancellationToken.None);

            var bytes = await File.ReadAllBytesAsync(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "Text export must NOT start with a BOM.");
        }
        finally { TryDelete(path); }
    }

    // ── QueryResultExportSource ──────────────────────────────────────────────
    [Fact]
    public void Source_Capabilities_ExposesCurrentViewAndAllRows_NotSelected()
    {
        var source = MakeSource(new object?[][] { new object?[] { "a", 1 } });
        Assert.True(source.Capabilities.Supports(ExportScope.CurrentView));
        Assert.True(source.Capabilities.Supports(ExportScope.AllRows));
        Assert.False(source.Capabilities.Supports(ExportScope.SelectedRows));
        Assert.Equal("test", source.Capabilities.DefaultBaseFileName);
    }

    [Fact]
    public void Source_Estimates_ExactForCurrentView_AndForAllRowsWhenComplete()
    {
        var view = new object?[][] { new object?[] { "a", 1 } };
        var all = new object?[][] { new object?[] { "a", 1 }, new object?[] { "b", 2 } };
        var source = new QueryResultExportSource(TwoQueryColumns, view, all, isPartial: false, streamAll: null, "test");

        Assert.Equal(RowEstimate.Exact(1), source.Capabilities.EstimateFor(ExportScope.CurrentView));
        Assert.Equal(RowEstimate.Exact(2), source.Capabilities.EstimateFor(ExportScope.AllRows));
    }

    [Fact]
    public void Source_AllRowsEstimate_UnknownWhenTruncated()
    {
        var rows = new object?[][] { new object?[] { "a", 1 } };
        var source = new QueryResultExportSource(TwoQueryColumns, rows, rows, isPartial: true, streamAll: null, "test");
        Assert.Equal(RowEstimate.Unknown, source.Capabilities.EstimateFor(ExportScope.AllRows));
    }

    [Fact]
    public async Task Source_CurrentView_YieldsTheViewRows()
    {
        var view = new object?[][] { new object?[] { "v", 1 } };
        var all = new object?[][] { new object?[] { "a", 1 }, new object?[] { "b", 2 } };
        var source = new QueryResultExportSource(TwoQueryColumns, view, all, isPartial: false, streamAll: null, "test");

        var got = await Collect(source.GetRowsAsync(ExportScope.CurrentView, CancellationToken.None));
        Assert.Single(got);
        Assert.Equal("v", got[0][0]);
    }

    [Fact]
    public async Task Source_AllRows_Untruncated_YieldsMaterialized()
    {
        var all = new object?[][] { new object?[] { "a", 1 }, new object?[] { "b", 2 } };
        var source = new QueryResultExportSource(TwoQueryColumns, all, all, isPartial: false, streamAll: null, "test");

        var got = await Collect(source.GetRowsAsync(ExportScope.AllRows, CancellationToken.None));
        Assert.Equal(2, got.Count);
    }

    [Fact]
    public async Task Source_AllRows_Truncated_StreamsViaDelegate()
    {
        var preview = new object?[][] { new object?[] { "a", 1 } };
        var full = new object?[][] { new object?[] { "a", 1 }, new object?[] { "b", 2 }, new object?[] { "c", 3 } };
        var source = new QueryResultExportSource(
            TwoQueryColumns, preview, preview, isPartial: true, streamAll: _ => ToAsync(full), "test");

        var got = await Collect(source.GetRowsAsync(ExportScope.AllRows, CancellationToken.None));
        Assert.Equal(3, got.Count); // re-fetched the full set, not the 1-row preview
    }

    // ── helpers ───────────────────────────────────────────────────────────────
    private static readonly QueryColumn[] TwoQueryColumns =
    {
        new("Name", typeof(string)),
        new("Value", typeof(int)),
    };

    private static QueryResultExportSource MakeSource(IReadOnlyList<object?[]> rows)
        => new(TwoQueryColumns, rows, rows, isPartial: false, streamAll: null, "test");

    private static async Task<string> RunToString(IExporter exporter, IReadOnlyList<ExportColumn> cols, IReadOnlyList<object?[]> rows)
    {
        await using var sink = new StringExportSink();
        await exporter.ExportAsync(cols, ToAsync(rows), sink, null, CancellationToken.None);
        return sink.Text;
    }

    private static async IAsyncEnumerable<object?[]> ToAsync(IReadOnlyList<object?[]> rows)
    {
        foreach (var r in rows) yield return r;
        await Task.CompletedTask;
    }

    private static async Task<List<object?[]>> Collect(IAsyncEnumerable<object?[]> source)
    {
        var list = new List<object?[]>();
        await foreach (var r in source) list.Add(r);
        return list;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}

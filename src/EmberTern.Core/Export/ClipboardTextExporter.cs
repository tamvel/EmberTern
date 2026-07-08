using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Export;

/// <summary>
/// Clipboard exporter — TAB-separated values using the Excel-paste convention: internal TAB / CR / LF
/// are replaced with spaces rather than RFC-4180-quoted, because Excel's clipboard paste does not
/// honour quotes well (so the two destinations legitimately differ — file CSV quotes, clipboard TSV
/// space-replaces). Values are formatted with the current culture (matches the existing Copy path).
/// Pure + streaming into the sink's writer (a string sink for the clipboard).
/// </summary>
public sealed class ClipboardTextExporter : IExporter
{
    private const int ReportEvery = 1000;
    private const string LineTerminator = "\r\n";

    private readonly bool _includeHeader;

    public ClipboardTextExporter(bool includeHeader)
    {
        _includeHeader = includeHeader;
    }

    public async Task<long> ExportAsync(
        IReadOnlyList<ExportColumn> columns,
        IAsyncEnumerable<object?[]> rows,
        IExportSink sink,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var writer = sink.Writer;

        if (_includeHeader)
        {
            var header = new List<string>(columns.Count);
            foreach (var c in columns) header.Add(c.Name);
            await writer.WriteAsync(BuildLine(header)).ConfigureAwait(false);
        }

        long written = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var fields = new List<string>(row.Length);
            foreach (var cell in row) fields.Add(ExportValueFormatter.Format(cell, CultureInfo.CurrentCulture));
            await writer.WriteAsync(BuildLine(fields)).ConfigureAwait(false);

            written++;
            if (written % ReportEvery == 0) progress?.Report(written);
        }

        progress?.Report(written);
        return written;
    }

    private static string BuildLine(IReadOnlyList<string> fields)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0) sb.Append('\t');
            sb.Append(EscapeField(fields[i]));
        }
        sb.Append(LineTerminator);
        return sb.ToString();
    }

    private static string EscapeField(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.IndexOfAny(new[] { '\t', '\r', '\n' }) < 0) return value;
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }
}

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Export;
using EmberTern.Export.Office;

namespace EmberTern.App.Export;

/// <summary>
/// Orchestrates one export: resolve the exporter for the requested format, open the destination sink,
/// and stream the source's rows into it. Stateless — a fresh instance per export is fine. The sink
/// choice (file vs in-memory-for-clipboard) is the destination; the exporter is the serialization.
/// </summary>
public sealed class ExportService
{
    /// <summary>Streams the export to a file. <paramref name="encoding"/> carries the per-format BOM
    /// choice (CSV → UTF-8 with BOM; Text → UTF-8 no BOM). Returns the number of rows written.</summary>
    public async Task<long> ExportToFileAsync(
        IExportDataSource source,
        ExportRequest request,
        string path,
        Encoding encoding,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        await using var sink = new FileExportSink(path, encoding);
        return await RunAsync(source, request, sink, progress, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Streams the export into an in-memory string (for the Clipboard format). Returns the
    /// row count and the text; the caller writes the text to the clipboard.</summary>
    public async Task<(long RowCount, string Text)> ExportToClipboardTextAsync(
        IExportDataSource source,
        ExportRequest request,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        await using var sink = new StringExportSink();
        var rows = await RunAsync(source, request, sink, progress, cancellationToken).ConfigureAwait(true);
        return (rows, sink.Text);
    }

    private static Task<long> RunAsync(
        IExportDataSource source,
        ExportRequest request,
        IExportSink sink,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        var exporter = ResolveExporter(request);
        return exporter.ExportAsync(source.Columns, source.GetRowsAsync(request.Scope, cancellationToken), sink, progress, cancellationToken);
    }

    private static IExporter ResolveExporter(ExportRequest request) => request.Format switch
    {
        ExportFormat.Xlsx => new XlsxExporter(request.IncludeHeader),
        ExportFormat.Csv or ExportFormat.Text =>
            new DelimitedTextExporter(request.Delimited
                ?? throw new InvalidOperationException("Delimited options are required for CSV/Text export.")),
        ExportFormat.Clipboard => new ClipboardTextExporter(request.IncludeHeader),
        _ => throw new NotSupportedException($"Export format '{request.Format}' is not supported."),
    };
}

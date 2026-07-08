using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Export;

/// <summary>
/// What a module supplies to the shared export framework: its columns, its capabilities
/// (scopes + estimates + name hint), and an async row stream per scope. A module implements
/// <b>only</b> this (a ~30-line adapter) — all file / clipboard / format logic is central.
/// The stream is async throughout because <see cref="ExportScope.AllRows"/> may be a large DB
/// round-trip; the exporter streams it straight to the sink without a second buffer.
/// </summary>
public interface IExportDataSource
{
    IReadOnlyList<ExportColumn> Columns { get; }
    ExportCapabilities Capabilities { get; }
    IAsyncEnumerable<object?[]> GetRowsAsync(ExportScope scope, CancellationToken cancellationToken);
}

/// <summary>One serializer per format — pure and streaming: it pulls rows from the source stream and
/// writes them to <paramref name="sink"/>'s <see cref="IExportSink.Writer"/> one at a time, reporting
/// rows written via <paramref name="progress"/>. Returns the total rows written.</summary>
public interface IExporter
{
    Task<long> ExportAsync(
        IReadOnlyList<ExportColumn> columns,
        IAsyncEnumerable<object?[]> rows,
        IExportSink sink,
        IProgress<long>? progress,
        CancellationToken cancellationToken);
}

/// <summary>The export destination, offering both a text and a binary surface (per the design's
/// "TextWriter / Stream" sink). Text exporters (CSV/TXT/Clipboard) write to <see cref="Writer"/>;
/// binary exporters (XLSX) write to <see cref="Stream"/>. A file sink supports both; a string/clipboard
/// sink is text-only and throws on <see cref="Stream"/> (XLSX is file-only, never clipboard). A given
/// export uses exactly one surface. Disposing flushes/closes.</summary>
public interface IExportSink : IAsyncDisposable
{
    TextWriter Writer { get; }
    Stream Stream { get; }
}

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

    /// <summary>
    /// Where these rows came from — the facts the SQL formats need to prove which table (if any) they
    /// belong to. <b>Facts only, never a verdict</b>: a source says what it knows and
    /// <c>ResultOriginResolver</c> decides.
    /// <para>
    /// <b>Required on purpose.</b> A source that cannot supply provenance must say so explicitly
    /// (<c>ResultOrigin.None(reason)</c> — an honest, permanent veto for procedure results), rather than
    /// omit it. Making this a compile error is the point: a new grid cannot silently ship without
    /// answering, and "silently missed a seam" is a mistake this codebase has made before.
    /// </para>
    /// </summary>
    Sql.ResultOrigin Origin { get; }

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

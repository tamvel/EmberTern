using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Import;

/// <summary>
/// Where the bytes or text physically come from — the mirror image of the export framework's
/// <c>IExportSink</c>, and for the same reason: it separates the ORIGIN of the data from the code that parses
/// it, so the clipboard is not a second parser but a different origin for the same one (design §1.5).
/// <para>
/// Two surfaces, exactly one of which a given provider uses: a text provider opens
/// <see cref="OpenTextAsync"/>, a spreadsheet provider opens <see cref="OpenStreamAsync"/>. A file source
/// supports both; a clipboard/string source is text-only and throws on the byte surface — the same honest
/// split <c>StringExportSink</c> already uses.
/// </para>
/// </summary>
public interface IImportSource
{
    /// <summary>What to call this source in the UI (a file name, or a clipboard label). Never a path the user
    /// did not supply.</summary>
    string DisplayName { get; }

    /// <summary>Size in bytes when known — used to drive progress as bytes-read/total, because a streaming
    /// import cannot know its row count in advance without reading the file twice (design R8). <c>null</c> when
    /// unknown (clipboard), and progress then shows a count without a percentage.</summary>
    long? SizeBytes { get; }

    /// <summary>
    /// Whether the source can still be reached, WITHOUT opening it.
    /// <para>
    /// Exists for the profile flow: a stored configuration names a path, and reloading it must be able to say
    /// "that file is gone" as a readiness item rather than by throwing somewhere inside a read (§4.8.5).
    /// </para>
    /// </summary>
    bool StillExists();

    /// <summary>Opens the text surface using <paramref name="encoding"/>. The caller disposes the reader.</summary>
    Task<TextReader> OpenTextAsync(Encoding encoding, CancellationToken cancellationToken);

    /// <summary>Opens the byte surface. The caller disposes the stream. Throws
    /// <see cref="NotSupportedException"/> on a text-only source.</summary>
    Task<Stream> OpenStreamAsync(CancellationToken cancellationToken);
}

/// <summary>
/// What a provider can be asked about — the import counterpart of <c>ExportCapabilities</c>. The Format section
/// renders whichever controls these flags declare, instead of switching on <see cref="ImportSourceKind"/> in
/// the view: a new provider brings its own capabilities and the UI follows with no XAML change (§3.3).
/// </summary>
public sealed class ImportProviderCapabilities
{
    public ImportProviderCapabilities(
        bool supportsDelimiters,
        bool supportsEncoding,
        bool supportsSheets,
        bool supportsRowRange)
    {
        SupportsDelimiters = supportsDelimiters;
        SupportsEncoding = supportsEncoding;
        SupportsSheets = supportsSheets;
        SupportsRowRange = supportsRowRange;
    }

    /// <summary>Column/text separators are meaningful (delimited text).</summary>
    public bool SupportsDelimiters { get; }

    /// <summary>The source's character encoding is the caller's choice (a text file). A workbook carries its
    /// own encoding, so a spreadsheet provider says false and the control is not shown at all — rather than
    /// shown and ignored.</summary>
    public bool SupportsEncoding { get; }

    /// <summary>The source has selectable sheets.</summary>
    public bool SupportsSheets { get; }

    /// <summary>First/last row can be restricted.</summary>
    public bool SupportsRowRange { get; }

    /// <summary>Delimited text: separators + encoding + row range, no sheets.</summary>
    public static ImportProviderCapabilities DelimitedText { get; } = new(true, true, false, true);

    /// <summary>Spreadsheet: sheets + row range, no separators and no encoding choice.</summary>
    public static ImportProviderCapabilities Spreadsheet { get; } = new(false, false, true, true);
}

/// <summary>
/// Turns a source into a schema and a STREAM of raw records. The one place that knows a file format.
/// <para>
/// <b>Streaming is part of the contract, not an optimization.</b> A provider must never materialize the whole
/// source: I0 measured a DOM-style workbook read at 300 MB of heap for 100 000 rows against 3.9 MB for the
/// streaming read (design R8).
/// </para>
/// <para>
/// The provider receives the whole <see cref="ImportConfiguration"/> rather than a hand-picked options object,
/// because the configuration is the single representation of what the user asked for (§4.8.1) — the same value
/// the pipeline and a saved profile carry. A provider reads only the block that belongs to it
/// (<c>Delimited</c> or <c>Spreadsheet</c>) and ignores the rest.
/// </para>
/// </summary>
public interface IImportProvider
{
    ImportProviderCapabilities Capabilities { get; }

    /// <summary>Reads just enough to describe the source's fields (and a row-count hint when it is cheap).</summary>
    Task<SourceSchema> ReadSchemaAsync(
        IImportSource source, ImportConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>Streams the data records, honouring the configured first/last row window. Values stay RAW —
    /// conversion belongs to one place, and that place is not here.</summary>
    IAsyncEnumerable<RawRecord> ReadRecordsAsync(
        IImportSource source, ImportConfiguration configuration, CancellationToken cancellationToken);
}

/// <summary>
/// Where converted rows go. Two production implementations from day one (rule #2):
/// <c>FirebirdImportWriter</c> and <c>DryRunImportWriter</c> — and the dry run is a FEATURE ("Validate"), not a
/// test double, which is what makes the whole pipeline exercisable without a database.
/// <para>
/// ⭐ <b>Why <see cref="WriteAsync"/> returns nothing</b> (decision D9, forced by measurement): rows are written
/// in batches, and <em>at the moment a row is queued its error does not exist yet</em> — it appears when the
/// batch is sent. A per-row result here would therefore be a lie. So queuing and reporting are separated:
/// <see cref="FlushBatchAsync"/> performs the write and returns one result per queued row, in queue order, and
/// <c>ImportPipeline</c> owns the „batch index → source row number" window that turns those positions into the
/// row numbers the report shows. I0 measured that the driver keeps that 1:1 alignment (findings §2.3).
/// </para>
/// </summary>
public interface IImportWriter
{
    /// <summary>Prepares the write for one target and one mapping. Called once, before any row.</summary>
    Task BeginAsync(
        ImportTarget target, IReadOnlyList<ColumnMapping> mapping, CancellationToken cancellationToken);

    /// <summary>Queues one row into the current batch. Does NOT write and does NOT report — see the type
    /// remarks.</summary>
    Task WriteAsync(ImportRow row, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the queued rows and returns one result per queued row, <b>in queue order</b> (index <c>i</c> is
    /// the <c>i</c>-th row passed to <see cref="WriteAsync"/> since the previous flush). An empty batch returns
    /// an empty list. After this call the batch is empty again.
    /// </summary>
    Task<IReadOnlyList<ImportBatchItemResult>> FlushBatchAsync(CancellationToken cancellationToken);

    /// <summary>Flushes anything left, releases resources, and reports the totals. Never commits by itself —
    /// finalizing the transaction is the caller's decision (hard rule #3).</summary>
    Task<ImportWriteSummary> CompleteAsync(CancellationToken cancellationToken);
}

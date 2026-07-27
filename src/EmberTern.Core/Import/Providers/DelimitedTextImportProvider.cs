using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Connections;

namespace EmberTern.Core.Import.Providers;

/// <summary>
/// Reads delimited text — CSV, TXT <b>and the clipboard</b> — into the pipeline's one currency: a
/// <see cref="SourceSchema"/> plus a stream of <see cref="RawRecord"/>.
/// <para>
/// ⭐ <b>One provider, three "sources".</b> The clipboard is not a second parser; it is a different origin for
/// the same text, which is exactly what <see cref="IImportSource"/> abstracts (design §1.5). App reads the
/// clipboard, hands Core a <see cref="string"/> wrapped in a <see cref="TextImportSource"/>, and this class
/// cannot tell the difference from a file.
/// </para>
/// <para>
/// <b>What it does NOT do</b>, so that "text becomes a value" happens in exactly one place (§0.1): it does not
/// convert, infer types, or interpret. Every field leaves here as the <see cref="string"/> that was in the
/// source — with one exception, and it is a deliberate one: the configured NULL token is resolved here, because
/// "this field is empty / says NULL" is a property of READING a text source, and resolving it later would mean
/// the converter had to know about delimited options it otherwise never sees.
/// </para>
/// <para>
/// <b>Streaming is contractual</b> (design R8): records are pulled one at a time from the underlying
/// <see cref="DelimitedTextReader"/>, and the whole source is never materialized.
/// </para>
/// </summary>
public sealed class DelimitedTextImportProvider : IImportProvider
{
    /// <summary>How many records the schema pass reads. Enough to see a ragged file's widest record without
    /// paying for the whole source — the schema is about SHAPE, and the exact row count is a different
    /// question (see <see cref="ReadSchemaAsync"/>).</summary>
    public const int SchemaSampleRecords = 200;

    public ImportProviderCapabilities Capabilities => ImportProviderCapabilities.DelimitedText;

    /// <summary>
    /// Describes the source's fields.
    /// <para>
    /// The field count is the WIDEST record in the sample, not the header's width: a ragged file must still
    /// show every column it actually contains, or a column would be unmappable because the header forgot to
    /// name it.
    /// </para>
    /// <para>
    /// <see cref="SourceSchema.EstimatedRows"/> is left <c>null</c> on purpose. A count derived from file size
    /// divided by an average record length is a fabrication, and §9.1's "numbers, not adjectives" means REAL
    /// numbers; progress for a file is driven by bytes-read/size instead (design R8). Etap I8 gets an exact
    /// count for free, because the type inferencer already scans the whole source.
    /// </para>
    /// </summary>
    public async Task<SourceSchema> ReadSchemaAsync(
        IImportSource source, ImportConfiguration configuration, CancellationToken cancellationToken)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var options = configuration.Delimited ?? new DelimitedOptions();

        using var reader = await source
            .OpenTextAsync(CharsetCatalog.Resolve(options.EncodingName), cancellationToken)
            .ConfigureAwait(false);

        var sample = new DelimitedTextReader(options).ReadSample(reader, SchemaSampleRecords);
        if (sample.Count == 0) return SourceSchema.Empty;

        var width = 0;
        foreach (var record in sample)
        {
            if (record.Fields.Length > width) width = record.Fields.Length;
        }

        var header = options.HasHeader ? sample[0].Fields : Array.Empty<string>();
        var fields = new List<SourceField>(width);
        for (var i = 0; i < width; i++)
        {
            var name = i < header.Length ? header[i].Trim() : string.Empty;
            var hasRealName = name.Length > 0;

            // An unnamed column still needs a usable key, so it falls back to its spreadsheet-style position
            // label — but HasRealName stays false, which is what tells the mapping planner that this key is a
            // position and not an identity that survives reordering.
            fields.Add(new SourceField(i, hasRealName ? name : SourceField.PositionalName(i), hasRealName));
        }

        return new SourceSchema(fields, options.HasHeader, EstimatedRows: null);
    }

    /// <summary>
    /// Streams the data records inside the configured window.
    /// <para>
    /// The header is not special-cased: it is record 1, and the default
    /// <see cref="DelimitedOptions.FirstDataRow"/> of 2 skips it. That is why the two are separate settings —
    /// a file can carry banner lines above its header, and the user says where the data starts (§3.3).
    /// </para>
    /// </summary>
    public async IAsyncEnumerable<RawRecord> ReadRecordsAsync(
        IImportSource source,
        ImportConfiguration configuration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        var options = configuration.Delimited ?? new DelimitedOptions();
        var first = Math.Max(1, options.FirstDataRow);
        var last = options.LastRow;

        using var reader = await source
            .OpenTextAsync(CharsetCatalog.Resolve(options.EncodingName), cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in new DelimitedTextReader(options).ReadAll(reader))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.RecordNumber < first) continue;
            if (last is not null && record.RecordNumber > last.Value) yield break;

            yield return new RawRecord(record.RecordNumber, ToValues(record.Fields, options));
        }
    }

    /// <summary>Delimited text has no sheets. Empty, never an exception: the caller is the Format section, which
    /// asks every provider the same question and shows a picker only when the capability says so.</summary>
    public Task<IReadOnlyList<SourceSheet>> ListSheetsAsync(
        IImportSource source, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SourceSheet>>(Array.Empty<SourceSheet>());

    /// <summary>
    /// Turns the record's text fields into raw values, resolving the declared NULL token.
    /// <para>
    /// With the default token (<c>""</c>) an empty field is SQL NULL. The comparison is case-insensitive
    /// because a token like <c>NULL</c> is a marker the user declared, not a value to be matched byte for byte
    /// — and recognising <c>null</c> as the same marker is honouring the declaration, not guessing at it.
    /// </para>
    /// <para>
    /// Note the asymmetry with a spreadsheet, and that it is intended: a blank CELL carries no literal at all,
    /// so that question is answered by <c>ImportBehaviorOptions.TreatEmptyAsNull</c> instead. One question, one
    /// owner, per source shape.
    /// </para>
    /// </summary>
    private static object?[] ToValues(string[] fields, DelimitedOptions options)
    {
        var values = new object?[fields.Length];
        for (var i = 0; i < fields.Length; i++)
        {
            values[i] = string.Equals(fields[i], options.NullToken, StringComparison.OrdinalIgnoreCase)
                ? null
                : fields[i];
        }
        return values;
    }
}

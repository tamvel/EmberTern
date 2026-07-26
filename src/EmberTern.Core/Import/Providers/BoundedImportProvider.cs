using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Import.Providers;

/// <summary>
/// Wraps a provider and stops after the first <see cref="MaxRecords"/> records.
/// <para>
/// ⭐ <b>This is what lets the converted preview be the ONE import.</b> §3.6 needs the values exactly as they
/// will reach the database — which is precisely what <see cref="ImportPipeline"/> already produces — but a
/// preview must not read a million-row file to show a hundred rows. Bounding the SOURCE rather than teaching
/// the pipeline about previews keeps the promise that the pipeline "does not know what it is reading": the
/// preview is a different provider and a different writer, never a second converter.
/// </para>
/// <para>
/// The schema is passed straight through — a bound is about how much data is read, not about what the source
/// looks like.
/// </para>
/// </summary>
public sealed class BoundedImportProvider : IImportProvider
{
    private readonly IImportProvider _inner;

    public BoundedImportProvider(IImportProvider inner, int maxRecords)
    {
        _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
        MaxRecords = maxRecords < 0 ? 0 : maxRecords;
    }

    /// <summary>How many records this provider will yield at most.</summary>
    public int MaxRecords { get; }

    public ImportProviderCapabilities Capabilities => _inner.Capabilities;

    public Task<SourceSchema> ReadSchemaAsync(
        IImportSource source, ImportConfiguration configuration, CancellationToken cancellationToken)
        => _inner.ReadSchemaAsync(source, configuration, cancellationToken);

    public async IAsyncEnumerable<RawRecord> ReadRecordsAsync(
        IImportSource source,
        ImportConfiguration configuration,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (MaxRecords == 0) yield break;

        var taken = 0;
        await foreach (var record in _inner
            .ReadRecordsAsync(source, configuration, cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return record;
            if (++taken >= MaxRecords) yield break;
        }
    }
}

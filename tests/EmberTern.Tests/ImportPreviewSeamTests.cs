using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I7, the two Core pieces that let the converted preview BE the real import instead of
/// imitating it: a provider decorator that bounds the read, and a writer that keeps rows instead of sending
/// them.
/// <para>
/// Both exist so that §3.6's promise ("this is what reaches the database") rests on the pipeline itself. The
/// alternative — a private "convert for display" routine — is a second path, and a second path drifts.
/// </para>
/// </summary>
public class ImportPreviewSeamTests
{
    private static ImportTarget Target() => new(
        "IMP_LAB",
        new[]
        {
            new ColumnSpec("KOD", "VARCHAR(20)", NotNull: true),
            new ColumnSpec("ILOSC", "INTEGER"),
        },
        Array.Empty<string>());

    private static ImportConfiguration Configuration() => ImportConfiguration.Empty with
    {
        Delimited = new DelimitedOptions { Delimiter = ';', AutoDetectDelimiter = false, HasHeader = true },
        Target = TargetDescriptor.Existing("IMP_LAB"),
        Mapping = new[]
        {
            new ColumnMapping { TargetColumnName = "KOD", SourceFieldName = "KOD", SourceFieldIndex = 0 },
            new ColumnMapping { TargetColumnName = "ILOSC", SourceFieldName = "ILOSC", SourceFieldIndex = 1 },
        },
    };

    private static string Csv(int rows)
    {
        var sb = new System.Text.StringBuilder("KOD;ILOSC\n");
        for (var i = 1; i <= rows; i++) sb.Append("K").Append(i).Append(';').Append(i).Append('\n');
        return sb.ToString();
    }

    [Fact]
    public async Task BoundedProvider_StopsAfterTheGivenNumberOfRecords()
    {
        var provider = new BoundedImportProvider(new DelimitedTextImportProvider(), 3);
        var source = new TextImportSource(Csv(50));

        var records = new List<RawRecord>();
        await foreach (var record in provider.ReadRecordsAsync(source, Configuration(), CancellationToken.None))
        {
            records.Add(record);
        }

        Assert.Equal(3, records.Count);
    }

    /// <summary>The bound is about how much data is read, never about what the source looks like — so the
    /// schema passes straight through.</summary>
    [Fact]
    public async Task BoundedProvider_LeavesTheSchemaAlone()
    {
        var inner = new DelimitedTextImportProvider();
        var source = new TextImportSource(Csv(10));
        var configuration = Configuration();

        var bounded = await new BoundedImportProvider(inner, 1)
            .ReadSchemaAsync(source, configuration, CancellationToken.None);
        var direct = await inner.ReadSchemaAsync(source, configuration, CancellationToken.None);

        Assert.Equal(direct.Fields.Select(f => f.Name), bounded.Fields.Select(f => f.Name));
    }

    /// <summary>
    /// ⭐ The preview writer keeps the rows the pipeline built — converted, validated, in mapped-column order.
    /// This is what the grid renders, which is why it can claim to show what reaches the database.
    /// </summary>
    [Fact]
    public async Task PreviewWriter_KeepsTheConvertedRows()
    {
        var writer = new PreviewImportWriter(100);
        var provider = new BoundedImportProvider(new DelimitedTextImportProvider(), 100);

        var outcome = await ImportPipeline.RunAsync(
            Configuration(), Target(), provider, new TextImportSource(Csv(3)), writer);

        Assert.Equal(3, writer.Rows.Count);
        Assert.Equal("K1", writer.Rows[0].Values[0]);
        Assert.Equal(1, writer.Rows[0].Values[1]);
        // A preview leaves nothing to commit, and the report must be able to say so plainly (§0.6).
        Assert.False(outcome.TransactionLeftOpen);
    }

    /// <summary>
    /// ⚠ The retained-row cap must never become a cap on what the run CLAIMS to have done: a flush that
    /// returns fewer results than rows were queued is how a real batch says "I stopped here", and the pipeline
    /// would then honestly report the remainder as never attempted. Queued and retained are counted apart, and
    /// this is the test that says so.
    /// </summary>
    [Fact]
    public async Task PreviewWriter_CapsWhatItKeeps_ButNotWhatItReports()
    {
        var writer = new PreviewImportWriter(2);

        var outcome = await ImportPipeline.RunAsync(
            Configuration(), Target(), new DelimitedTextImportProvider(), new TextImportSource(Csv(5)), writer);

        Assert.Equal(2, writer.Rows.Count);
        Assert.Equal(5, outcome.RowsRead);
        Assert.Equal(5, outcome.RowsWritten);
        Assert.Equal(0, outcome.RowsFailed);
    }
}

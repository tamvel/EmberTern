using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I3: the delimited provider, i.e. the bridge from I1's RFC 4180 reader to the pipeline's
/// one currency (<see cref="SourceSchema"/> + <see cref="RawRecord"/>).
/// <para>
/// Everything here runs on <see cref="TextImportSource"/>, which is the point: the clipboard is not a second
/// parser, so the tests that prove the clipboard works are the same ones that prove a CSV works (design §1.5).
/// </para>
/// </summary>
public class ImportDelimitedProviderTests
{
    private static readonly DelimitedTextImportProvider Provider = new();

    private static ImportConfiguration Config(DelimitedOptions options)
        => new() { Source = SourceDescriptor.Clipboard(), Delimited = options };

    private static Task<SourceSchema> SchemaOf(string text, DelimitedOptions options)
        => Provider.ReadSchemaAsync(new TextImportSource(text), Config(options), CancellationToken.None);

    private static async Task<List<RawRecord>> RecordsOf(string text, DelimitedOptions options)
    {
        var records = new List<RawRecord>();
        await foreach (var record in Provider.ReadRecordsAsync(
                           new TextImportSource(text), Config(options), CancellationToken.None))
        {
            records.Add(record);
        }
        return records;
    }

    // ── Capabilities ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The Format section renders whatever these declare, instead of switching on the source kind in
    /// the view — so a control that cannot mean anything is not shown at all rather than shown and ignored.</summary>
    [Fact]
    public void Capabilities_DescribeADelimitedSource()
    {
        var capabilities = Provider.Capabilities;

        Assert.True(capabilities.SupportsDelimiters);
        Assert.True(capabilities.SupportsEncoding);
        Assert.True(capabilities.SupportsRowRange);
        Assert.False(capabilities.SupportsSheets);
    }

    // ── Schema ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Schema_TakesItsFieldNamesFromTheHeader()
    {
        var schema = await SchemaOf("Indeks;Nazwa\n1;abc\n", new DelimitedOptions());

        Assert.True(schema.HasHeader);
        Assert.Equal(new[] { "Indeks", "Nazwa" }, schema.Fields.Select(f => f.Name));
        Assert.All(schema.Fields, f => Assert.True(f.HasRealName));
    }

    /// <summary>Without a header there is no identity, only position — and <c>HasRealName=false</c> is how the
    /// mapping planner learns that this key must not be treated as one (it is the flag that stops a positional
    /// mapping from being carried into a named source).</summary>
    [Fact]
    public async Task Schema_WithoutAHeader_UsesSpreadsheetStylePositionLabels()
    {
        var schema = await SchemaOf("1;abc\n2;def\n", new DelimitedOptions { HasHeader = false });

        Assert.False(schema.HasHeader);
        Assert.Equal(new[] { "A", "B" }, schema.Fields.Select(f => f.Name));
        Assert.All(schema.Fields, f => Assert.False(f.HasRealName));
    }

    /// <summary>⭐ The width is the WIDEST record, not the header's. A column the header forgot to name would
    /// otherwise be invisible — and therefore unmappable — even though the file plainly contains it.</summary>
    [Fact]
    public async Task Schema_WidthComesFromTheWidestRecord_NotTheHeader()
    {
        var schema = await SchemaOf("A;B\n1;2;3\n", new DelimitedOptions());

        Assert.Equal(3, schema.Fields.Count);
        Assert.Equal("C", schema.Fields[2].Name);       // positional fallback
        Assert.False(schema.Fields[2].HasRealName);     // …but not an identity
    }

    [Fact]
    public async Task Schema_AnEmptyHeaderCellFallsBackToItsPosition()
    {
        var schema = await SchemaOf("A;;C\n1;2;3\n", new DelimitedOptions());

        Assert.Equal(new[] { "A", "B", "C" }, schema.Fields.Select(f => f.Name));
        Assert.Equal(new[] { true, false, true }, schema.Fields.Select(f => f.HasRealName));
    }

    [Fact]
    public async Task Schema_OfAnEmptySource_IsEmpty()
        => Assert.Empty((await SchemaOf("", new DelimitedOptions())).Fields);

    /// <summary>A fabricated row count would be worse than none: §9.1 asks for real numbers, and progress for a
    /// file comes from bytes read rather than a guessed total (design R8).</summary>
    [Fact]
    public async Task Schema_DoesNotInventARowCount()
        => Assert.Null((await SchemaOf("A;B\n1;2\n", new DelimitedOptions())).EstimatedRows);

    // ── Records and the row window ──────────────────────────────────────────────────────────────────────

    /// <summary>The header is not special-cased — it is record 1, and the default first-data-row of 2 skips it.
    /// That is why they are two settings: a file can carry banner lines above its header (§3.3).</summary>
    [Fact]
    public async Task Records_SkipTheHeaderThroughTheRowWindow_NotThroughASpecialCase()
    {
        var records = await RecordsOf("A;B\n1;x\n2;y\n", new DelimitedOptions());

        Assert.Equal(2, records.Count);
        Assert.Equal(2, records[0].SourceRowNumber);      // the number the user sees in their file
        Assert.Equal(new object?[] { "1", "x" }, records[0].Values);
        Assert.Equal(3, records[1].SourceRowNumber);
    }

    [Fact]
    public async Task Records_HonourAnExplicitWindow()
    {
        var options = new DelimitedOptions { HasHeader = false, FirstDataRow = 2, LastRow = 3 };
        var records = await RecordsOf("a\nb\nc\nd\n", options);

        Assert.Equal(new[] { 2, 3 }, records.Select(r => r.SourceRowNumber));
        Assert.Equal(new object?[] { "b" }, records[0].Values);
    }

    [Fact]
    public async Task Records_WithBannerLinesAboveTheHeader()
    {
        // A real-world shape: two banner lines, then the header, then data.
        var options = new DelimitedOptions { FirstDataRow = 4 };
        var records = await RecordsOf("report\ngenerated\nA;B\n1;x\n", options);

        Assert.Single(records);
        Assert.Equal(4, records[0].SourceRowNumber);
    }

    /// <summary>A quoted field spanning physical lines is ONE record, so the numbering the report shows stays
    /// the numbering the user can act on.</summary>
    [Fact]
    public async Task Records_CountAMultilineQuotedFieldAsOneRecord()
    {
        var records = await RecordsOf("A;B\n1;\"two\nlines\"\n2;x\n", new DelimitedOptions());

        Assert.Equal(2, records.Count);
        Assert.Equal("two\nlines", records[0].Values[1]);
        Assert.Equal(3, records[1].SourceRowNumber);
    }

    // ── The NULL token ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>With the default token an empty field is SQL NULL. Resolving it HERE is deliberate: it is a
    /// property of reading a text source, and doing it later would force the converter to know about delimited
    /// options it otherwise never sees.</summary>
    [Fact]
    public async Task Records_ResolveTheDefaultNullToken()
    {
        var records = await RecordsOf("A;B\n;x\n", new DelimitedOptions());

        Assert.Null(records[0].Values[0]);
        Assert.Equal("x", records[0].Values[1]);
    }

    [Fact]
    public async Task Records_ResolveADeclaredNullToken_CaseInsensitively()
    {
        var options = new DelimitedOptions { NullToken = "NULL" };
        var records = await RecordsOf("A;B;C\nNULL;null;\n", options);

        Assert.Null(records[0].Values[0]);
        Assert.Null(records[0].Values[1]);
        // …and with a token declared, an EMPTY field is no longer NULL — it is an empty string.
        Assert.Equal("", records[0].Values[2]);
    }

    // ── Streaming and cancellation ──────────────────────────────────────────────────────────────────────

    /// <summary>Streaming is contractual, not an optimization (design R8) — the enumerable must be consumable
    /// lazily rather than handing back a materialized list.</summary>
    [Fact]
    public async Task Records_AreStreamed_NotMaterialized()
    {
        var text = "A;B\n" + string.Join("\n", Enumerable.Range(1, 10_000).Select(i => $"{i};x"));
        var taken = 0;

        await foreach (var _ in Provider.ReadRecordsAsync(
                           new TextImportSource(text), Config(new DelimitedOptions()), CancellationToken.None))
        {
            if (++taken == 3) break;
        }

        Assert.Equal(3, taken);
    }

    [Fact]
    public async Task Records_ObserveCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in Provider.ReadRecordsAsync(
                               new TextImportSource("A;B\n1;x\n"), Config(new DelimitedOptions()), cts.Token))
            {
            }
        });
    }
}

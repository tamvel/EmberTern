using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Import;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I8: type inference for a table that does not exist yet.
/// <para>
/// The tests are organised around the one property that matters (§0.3): <b>a candidate type has to fit every
/// single value, and anything unclear becomes <c>VARCHAR</c></b>. The most important case in the file is
/// <see cref="MixedColumn_FallsBackToText_AndNamesTheValueThatDecidedIt"/> — it reproduces R19, the measured
/// real-world file where one column held 8 723 integers and a single piece of text. Typing that column
/// <c>INTEGER</c> would fail the import on row 8 724, <em>after</em> the table had been created and committed.
/// </para>
/// </summary>
public class ColumnTypeInferencerTests
{
    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────────────

    private static SourceSchema Schema(params string[] names)
        => new(names.Select((n, i) => new SourceField(i, n, true)).ToArray(), true, null);

    private static async IAsyncEnumerable<RawRecord> Records(params object?[][] rows)
    {
        var number = 1;
        foreach (var row in rows)
        {
            number++;
            yield return new RawRecord(number, row);
        }
        await Task.CompletedTask;
    }

    /// <summary>One column of text values, inferred under the default (Polish) culture.</summary>
    private static async Task<InferredColumn> InferOne(
        params string?[] values)
    {
        var result = await ColumnTypeInferencer.InferAsync(
            Schema("VALUE"),
            Records(values.Select(v => new object?[] { v }).ToArray()),
            new ImportCultureOptions());

        return result.Columns.Single();
    }

    private static async Task<InferredColumn> InferOneWith(ImportCultureOptions culture, params string?[] values)
    {
        var result = await ColumnTypeInferencer.InferAsync(
            Schema("VALUE"),
            Records(values.Select(v => new object?[] { v }).ToArray()),
            culture);

        return result.Columns.Single();
    }

    // ── §0.3 — the conservative rule ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ R19, measured on a real file. A column of numbers with ONE piece of text in it is normal, and the
    /// only safe answer is text — but the user is entitled to know which value decided it, or the fallback
    /// looks arbitrary.
    /// </summary>
    [Fact]
    public async Task MixedColumn_FallsBackToText_AndNamesTheValueThatDecidedIt()
    {
        var column = await InferOne("11881", "11881", "11 88x", "11881");

        Assert.Equal("VARCHAR", column.Definition.BasicType);
        Assert.True(column.Evidence.IsMixed);
        Assert.Equal(SqlValueKind.Integer, column.Evidence.RejectedKind);
        Assert.Equal("11 88x", column.Evidence.RejectedByValue);

        // The row NUMBER, not the position in the batch or in the scan — the number the user can find in
        // their file (§0.6). The fixture's third data row is source row 4.
        Assert.Equal(4, column.Evidence.RejectedAtRow);
    }

    /// <summary>
    /// ⭐ <c>007</c> parses as 7 without complaint, and storing it as 7 gives back different data than went in —
    /// a postal code, an index, an account number. Rule #11: never modify what cannot be reproduced identically.
    /// </summary>
    [Fact]
    public async Task LeadingZeros_AreNotANumber_BecauseTheZerosAreData()
    {
        var column = await InferOne("007", "042", "100");

        Assert.Equal("VARCHAR", column.Definition.BasicType);
        Assert.Equal(3, column.Definition.Size);
    }

    /// <summary>The rule above must not swallow ordinary numbers: a lone zero and a value below one are not
    /// codes.</summary>
    [Fact]
    public async Task ASingleLeadingZero_IsStillANumber()
    {
        Assert.Equal("INTEGER", (await InferOne("0", "5", "12")).Definition.BasicType);
        Assert.Equal("NUMERIC", (await InferOne("0,5", "1,25", "0")).Definition.BasicType);
    }

    [Fact]
    public async Task EmptyColumn_IsTextAtItsNarrowest_BecauseNoValueIsNoEvidence()
    {
        var column = await InferOne(null, "", null);

        Assert.Equal("VARCHAR", column.Definition.BasicType);
        Assert.Equal(ColumnTypeInferencer.EmptyColumnTextLength, column.Definition.Size);
        Assert.Equal(0, column.Evidence.ValuesSeen);
        Assert.Equal(3, column.Evidence.ValuesEmpty);

        // Nothing was rejected — the column never looked like anything, which is a different statement from
        // "it looked like a number until row 8 724".
        Assert.False(column.Evidence.IsMixed);
    }

    // ── The types themselves ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Integers_BecomeInteger_AndWidenToBigintOnlyWhenTheyMustl()
    {
        Assert.Equal("INTEGER", (await InferOne("1", "-5", "2147483647")).Definition.BasicType);
        Assert.Equal("BIGINT", (await InferOne("1", "2147483648")).Definition.BasicType);
    }

    /// <summary>SMALLINT is right for the file in hand and wrong for the next one; the difference from INTEGER
    /// buys nothing worth a rejected row later.</summary>
    [Fact]
    public async Task SmallValues_StillBecomeInteger_NeverSmallint()
    {
        Assert.Equal("INTEGER", (await InferOne("1", "2", "3")).Definition.BasicType);
    }

    /// <summary>Precision and scale come from what was actually seen — the widest whole part plus the deepest
    /// fraction.</summary>
    [Fact]
    public async Task Decimals_TakeTheWidestPrecisionAndDeepestScaleSeen()
    {
        var column = await InferOne("1,5", "1234,255", "7");

        Assert.Equal("NUMERIC", column.Definition.BasicType);
        Assert.Equal(3, column.Definition.Scale);
        Assert.Equal(7, column.Definition.Size);   // 4 whole digits + 3 decimals
    }

    /// <summary>A number needing more than 18 digits cannot be stored exactly, and an approximate type would
    /// lose digits silently (§0.1) — so it withdraws and the column is text.</summary>
    [Fact]
    public async Task ANumberTooWideForExactStorage_BecomesText()
    {
        var column = await InferOne("123456789012345678901234", "1");

        Assert.Equal("VARCHAR", column.Definition.BasicType);
    }

    [Fact]
    public async Task Dates_BecomeDate_AndOnlyWidenToTimestampWhenATimeAppears()
    {
        Assert.Equal("DATE", (await InferOne("03.04.2026", "01.01.2020")).Definition.BasicType);
        Assert.Equal("TIMESTAMP", (await InferOne("03.04.2026 14:02", "01.01.2020")).Definition.BasicType);
    }

    /// <summary>The declared field order decides, and nothing else — trying both would be the guess §0.4
    /// forbids. Under MDY the same text is a date; the point is that ONE order is consulted.</summary>
    [Fact]
    public async Task DateInference_ObeysTheDeclaredFieldOrder()
    {
        var mdy = new ImportCultureOptions { DateOrder = DateFieldOrder.Mdy };

        // 31 cannot be a month, so this is not a date under MDY at all — and is therefore text, not a date
        // quietly re-read the other way round.
        Assert.Equal("VARCHAR", (await InferOneWith(mdy, "31.12.2026")).Definition.BasicType);
        Assert.Equal("DATE", (await InferOneWith(mdy, "12.31.2026")).Definition.BasicType);
    }

    [Fact]
    public async Task Times_BecomeTime()
    {
        Assert.Equal("TIME", (await InferOne("14:02:00", "09:30:00")).Definition.BasicType);
    }

    /// <summary>
    /// ⭐ The default boolean tokens include <c>1</c> and <c>0</c>, so a column of ones and zeros satisfies
    /// BOOLEAN as well as INTEGER. Integer is consulted first on purpose: a flag stored as a number is far more
    /// common, and it is the reading that keeps the value the file actually holds.
    /// </summary>
    [Fact]
    public async Task ZeroAndOne_ReadAsInteger_NotAsBoolean()
    {
        Assert.Equal("INTEGER", (await InferOne("1", "0", "1")).Definition.BasicType);
        Assert.Equal("BOOLEAN", (await InferOne("TAK", "NIE")).Definition.BasicType);
    }

    /// <summary>Text longer than a VARCHAR can hold becomes a text BLOB — which has no limit and which the
    /// import already supports end to end — rather than a length that would reject rows.</summary>
    [Fact]
    public async Task VeryLongText_BecomesATextBlob()
    {
        var column = await InferOne(new string('x', ColumnTypeInferencer.MaxVarcharLength + 1));

        Assert.Equal("BLOB", column.Definition.BasicType);
        Assert.Equal(1, column.Definition.BlobSubType);
        Assert.Equal(SqlValueKind.TextBlob, column.Evidence.ChosenKind);
    }

    /// <summary>⭐ The user's explicit requirement: the length is the LONGEST value actually met — which is the
    /// second reason the scan covers the whole source rather than a sample.</summary>
    [Fact]
    public async Task VarcharLength_IsTheLongestValueSeen()
    {
        var column = await InferOne("ab", "abcdefghij", "abcd");

        Assert.Equal(10, column.Definition.Size);
        Assert.Equal(10, column.Evidence.MaxTextLength);
    }

    /// <summary>Nothing is ever inferred NOT NULL. The file in hand having no gaps says nothing about the next
    /// one, and the constraint outlives the import — so it stays the user's decision, on the grid.</summary>
    [Fact]
    public async Task NothingIsInferredNotNull()
    {
        var column = await InferOne("a", "b", "c");
        Assert.False(column.Definition.NotNull);
    }

    // ── Scanning ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ REK-7. A sample would type this column INTEGER — the disqualifying value sits past row 3. Scanning
    /// the whole source is the difference between a correct type and a failed import against a table that has
    /// already been committed.
    /// </summary>
    [Fact]
    public async Task TheScanCoversTheWholeSource_NotAHead()
    {
        var rows = Enumerable.Range(0, 500)
            .Select(i => new object?[] { i == 499 ? "not a number" : i.ToString() })
            .ToArray();

        var result = await ColumnTypeInferencer.InferAsync(
            Schema("VALUE"), Records(rows), new ImportCultureOptions());

        Assert.Equal(500, result.RowsAnalysed);
        Assert.False(result.ScanTruncated);
        Assert.Equal("VARCHAR", result.Columns.Single().Definition.BasicType);
    }

    /// <summary>The limit is a circuit breaker, not a sample size — and when it bites, it says so, so the
    /// surface never implies evidence it does not have.</summary>
    [Fact]
    public async Task TheSafetyLimitStopsTheScan_AndSaysSo()
    {
        var rows = Enumerable.Range(0, 20).Select(i => new object?[] { i.ToString() }).ToArray();

        var result = await ColumnTypeInferencer.InferAsync(
            Schema("VALUE"), Records(rows), new ImportCultureOptions(), scanLimit: 5);

        Assert.Equal(5, result.RowsAnalysed);
        Assert.True(result.ScanTruncated);
    }

    [Fact]
    public async Task ScanningIsCancellable()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var rows = Enumerable.Range(0, 100).Select(i => new object?[] { i.ToString() }).ToArray();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ColumnTypeInferencer.InferAsync(
            Schema("VALUE"), Records(rows), new ImportCultureOptions(),
            ColumnTypeInferencer.DefaultScanLimit, cts.Token));
    }

    /// <summary>A ragged record simply has no value for the missing field — it is not evidence, and it must
    /// not be read as an empty string that would drag the column to text.</summary>
    [Fact]
    public async Task AMissingFieldIsAbsence_NotAValue()
    {
        var result = await ColumnTypeInferencer.InferAsync(
            Schema("A", "B"),
            Records(
                new object?[] { "1", "2" },
                new object?[] { "3" }),
            new ImportCultureOptions());

        Assert.Equal("INTEGER", result.Columns[1].Definition.BasicType);
        Assert.Equal(1, result.Columns[1].Evidence.ValuesSeen);
        Assert.Equal(1, result.Columns[1].Evidence.ValuesEmpty);
    }

    // ── Naming ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The generated name goes through the mapping planner's own <c>NormalizeName</c>, which is what makes a
    /// new table need no special case in the mapping panel: the column is named after the field, so the planner
    /// pairs them back up by name like any other target.
    /// </summary>
    [Theory]
    [InlineData("Nr technologii", "NR_TECHNOLOGII")]
    [InlineData("indeks-kartoteki", "INDEKS_KARTOTEKI")]
    [InlineData("Netto (PLN)", "NETTO_PLN")]
    [InlineData("Nazwa żółwia", "NAZWA_ŻÓŁWIA")]
    [InlineData("2026", "C2026")]
    [InlineData("   ", "COLUMN_1")]
    [InlineData("###", "COLUMN_1")]
    public void ColumnNames_AreDerivedFromTheSourceField(string field, string expected)
    {
        Assert.Equal(expected, ColumnTypeInferencer.ToColumnName(field, 0));
    }

    /// <summary>Two fields that normalize to the same name would produce a CREATE TABLE Firebird refuses — so
    /// the second one is disambiguated rather than colliding.</summary>
    [Fact]
    public async Task DuplicateFieldNames_AreDisambiguated()
    {
        var result = await ColumnTypeInferencer.InferAsync(
            Schema("Kod", "kod"),
            Records(new object?[] { "a", "b" }),
            new ImportCultureOptions());

        Assert.Equal("KOD", result.Columns[0].Definition.Name);
        Assert.Equal("KOD_2", result.Columns[1].Definition.Name);
    }

    // ── The round trip that keeps inference and conversion honest ───────────────────────────────────────

    /// <summary>
    /// ⭐⭐ The whole architecture of this etap in one assertion: every type the inferencer proposes must be a
    /// type <see cref="ImportTargetType"/> can read back off the DDL — because the converted preview, the
    /// validator and the real run all resolve the column that way. A type this class could emit but that class
    /// could not read would mean an import refusing every row of a table the module itself designed.
    /// </summary>
    [Theory]
    [InlineData("1", SqlValueKind.Integer)]
    [InlineData("2147483648", SqlValueKind.Integer)]
    [InlineData("1,5", SqlValueKind.Decimal)]
    [InlineData("03.04.2026", SqlValueKind.Date)]
    [InlineData("03.04.2026 14:02", SqlValueKind.Timestamp)]
    [InlineData("14:02:00", SqlValueKind.Time)]
    [InlineData("TAK", SqlValueKind.Boolean)]
    [InlineData("cokolwiek", SqlValueKind.Text)]
    public async Task EveryProposedType_ResolvesBackToTheKindItWasChosenFor(string value, SqlValueKind expected)
    {
        var column = await InferOne(value);

        var resolved = ImportTargetType.Resolve(ImportNewTable.TypeText(column.Definition));

        Assert.True(resolved.IsSupported, $"{ImportNewTable.TypeText(column.Definition)} is not a type import can write.");
        Assert.Equal(expected, resolved.Kind);
        Assert.Equal(expected, column.Evidence.ChosenKind);
    }

    /// <summary>
    /// ⭐ And the consequence that actually protects the user: every value the inferencer saw must survive the
    /// converter against the type it proposed. If this ever fails, the module has designed a table its own
    /// import cannot fill — R19's timebomb, reproduced.
    /// </summary>
    [Fact]
    public async Task EveryValueSeen_ConvertsIntoTheTypeThatWasProposedForIt()
    {
        var culture = new ImportCultureOptions();
        string[][] columns =
        {
            new[] { "1", "-5", "2147483647" },
            new[] { "1,5", "1234,255", "7" },
            new[] { "03.04.2026", "01.01.2020" },
            new[] { "TAK", "NIE" },
            new[] { "007", "042" },
            new[] { "11881", "11 88x" },
            new[] { "abc", "abcdefghij" },
        };

        foreach (var values in columns)
        {
            var column = await InferOne(values);
            var type = ImportTargetType.Resolve(ImportNewTable.TypeText(column.Definition));

            foreach (var value in values)
            {
                var converted = ImportValueConverter.Convert(value, type, culture);
                Assert.True(
                    converted.IsSuccess,
                    $"\"{value}\" was refused by {type.BaseTypeName} — the type inference proposed for it.");
            }
        }
    }
}

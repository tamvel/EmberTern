using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I2: the readiness strip's engine (§3.2).
/// <para>
/// Two properties are worth stating, because they are what makes the strip better than a wizard's "Next"
/// button and both are pinned below: it reports <b>every</b> gap at once rather than the first, and every
/// blocking item names the section that caused it — a disabled button with no reason is a UX defect (§9.1).
/// A third is pinned in <see cref="MappingFindings_ComeFromThePlanner_NotFromASecondAnalysis"/>: the strip
/// does not re-derive mapping findings, so it cannot disagree with the mapping grid.
/// </para>
/// </summary>
public class ImportReadinessTests
{
    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────────────

    private static ColumnSpec Col(string name, string type = "VARCHAR(50)", bool notNull = false)
        => new(name, type, null, notNull);

    private static ImportTarget Target(params string[] triggers)
        => new("T", new[] { Col("A"), Col("B") }, triggers);

    private static SourceSchema Schema()
        => new(new[] { new SourceField(0, "A", true), new SourceField(1, "B", true) }, true, null);

    private static IReadOnlyList<ColumnMapping> FullMapping() => new[]
    {
        new ColumnMapping { TargetColumnName = "A", SourceFieldName = "A", SourceFieldIndex = 0 },
        new ColumnMapping { TargetColumnName = "B", SourceFieldName = "B", SourceFieldIndex = 1 },
    };

    /// <summary>A configuration that is ready to run — every negative test below changes exactly one thing.</summary>
    private static ImportConfiguration Ready() => new()
    {
        Source = SourceDescriptor.File(ImportSourceKind.Csv, @"C:\data\in.csv"),
        Target = TargetDescriptor.Existing("T"),
        Mapping = FullMapping(),
    };

    private static ImportReadinessInput Input(
        ImportConfiguration? configuration = null,
        ImportTarget? target = null,
        SourceSchema? schema = null)
        => new()
        {
            Configuration = configuration ?? Ready(),
            Schema = schema ?? Schema(),
            Target = target ?? Target(),
        };

    private static bool Has(ImportReadinessReport report, ImportDiagnosticCode code)
        => report.Items.Any(i => i.Code == code);

    private static ReadinessItem Item(ImportReadinessReport report, ImportDiagnosticCode code)
        => report.Items.Single(i => i.Code == code);

    // ── The happy path ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AFullyConfiguredImport_IsReady_AndSaysNothing()
    {
        var report = ImportReadiness.Evaluate(Input());

        Assert.True(report.CanRun);
        Assert.Empty(report.Items);
        Assert.Null(report.SeverityFor(ImportSection.Mapping));
    }

    // ── Environment ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoConnection_Blocks()
    {
        var report = ImportReadiness.Evaluate(Input() with { IsConnected = false });

        Assert.False(report.CanRun);
        Assert.True(Item(report, ImportDiagnosticCode.NotConnected).IsBlocking);
    }

    /// <summary>
    /// ⭐ I7.5: the console's transaction is not reported at all — not as a block, not as a warning. The import
    /// owns its own, so the SQL Editor's state cannot make it unready, and mentioning it would be noise the
    /// user cannot act on. This also dissolved the contradiction the design carried since I2: §3.2 called an
    /// open working transaction BLOCKING while §4.5 had the writer join one. Both could not be true.
    /// </summary>
    [Fact]
    public void TheConsolesTransaction_IsNotReported()
    {
        var report = ImportReadiness.Evaluate(Input());

        // IMP0021 was UserTransactionOpen; the code is retired and never reused.
        Assert.All(report.Items, i => Assert.NotEqual("IMP0021", i.CodeText));
        Assert.DoesNotContain(report.Blocking, i => i.Section == ImportSection.Transaction);
    }

    // ── Source ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoSourcePath_Blocks_AndPointsAtTheSourceSection()
    {
        var configuration = Ready() with { Source = SourceDescriptor.File(ImportSourceKind.Csv, "") };
        var report = ImportReadiness.Evaluate(Input(configuration));

        var item = Item(report, ImportDiagnosticCode.NoSource);
        Assert.True(item.IsBlocking);
        Assert.Equal(ImportSection.Source, item.Section);
    }

    /// <summary>Answered without opening anything — which is exactly why a stored configuration keeps a path
    /// rather than a handle (§4.8.5).</summary>
    [Fact]
    public void AMissingFile_Blocks_AndNamesIt()
    {
        var report = ImportReadiness.Evaluate(Input() with { SourceExists = false });

        var item = Item(report, ImportDiagnosticCode.SourceMissing);
        Assert.True(item.IsBlocking);
        Assert.Equal(@"C:\data\in.csv", item.Subject);
    }

    [Fact]
    public void AnUnreadableSource_Blocks()
        => Assert.True(Has(ImportReadiness.Evaluate(Input() with { SourceReadable = false }),
            ImportDiagnosticCode.SourceUnreadable));

    [Fact]
    public void ASourceThatHasNotBeenReadYet_Blocks()
        => Assert.True(Has(ImportReadiness.Evaluate(Input() with { Schema = null }),
            ImportDiagnosticCode.SourceHasNoFields));

    [Fact]
    public void ASourceWithNoFields_Blocks()
        => Assert.True(Has(ImportReadiness.Evaluate(Input() with { Schema = SourceSchema.Empty }),
            ImportDiagnosticCode.SourceHasNoFields));

    /// <summary>A record cannot express "exactly one of these two", so the mismatch is caught here rather than
    /// met as a null by the reader.</summary>
    [Fact]
    public void TheWrongOptionsBlockForTheSourceKind_Blocks()
    {
        var configuration = Ready() with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Xlsx, @"C:\data\in.xlsx"),
            Spreadsheet = null,
        };
        var report = ImportReadiness.Evaluate(Input(configuration));

        var item = Item(report, ImportDiagnosticCode.SourceOptionsMismatch);
        Assert.True(item.IsBlocking);
        Assert.Equal(ImportSection.Format, item.Section);
    }

    /// <summary>⭐ Design R1: those values would be written as '?' with no error at all. A warning rather than
    /// a block — reconnecting in UTF8 is the user's call, and the validator refuses each affected value anyway.</summary>
    [Fact]
    public void UnrepresentableSampleValues_Warn_ButDoNotBlock()
    {
        var report = ImportReadiness.Evaluate(Input() with { ValuesNotRepresentableInCharset = 3 });

        var item = Item(report, ImportDiagnosticCode.NotRepresentableInConnectionCharset);
        Assert.False(item.IsBlocking);
        Assert.Equal(ImportSeverity.Warning, item.Severity);
        Assert.Equal(3, item.Count);
        Assert.True(report.CanRun);
    }

    // ── Target ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NoTarget_Blocks()
    {
        var configuration = Ready() with { Target = TargetDescriptor.Existing("") };
        Assert.True(Has(ImportReadiness.Evaluate(Input(configuration)), ImportDiagnosticCode.NoTarget));
    }

    [Fact]
    public void ATableThatIsNotInTheCatalog_Blocks()
    {
        var report = ImportReadiness.Evaluate(Input() with { Target = null });

        var item = Item(report, ImportDiagnosticCode.TargetNotFound);
        Assert.True(item.IsBlocking);
        Assert.Equal("T", item.Subject);
    }

    [Fact]
    public void ANewTableWithNoColumns_Blocks()
    {
        var configuration = Ready() with
        {
            Target = TargetDescriptor.New("NEW_T", Array.Empty<ImportColumnDefinition>()),
        };
        Assert.True(Has(
            ImportReadiness.Evaluate(Input(configuration, target: null)),
            ImportDiagnosticCode.NewTableHasNoColumns));
    }

    /// <summary>
    /// ⭐ §0.5 / gotcha #213, the module's most important honest warning. The CREATE runs on the Ddl lane and
    /// is COMMITTED before the first row, because a Firebird transaction cannot use an object whose DDL it has
    /// not committed — so Rollback will NOT remove the table, and the strip says so before the run rather than
    /// the user discovering it afterwards.
    /// </summary>
    [Fact]
    public void ANewTable_WarnsThatRollbackWillNotRemoveIt_ButDoesNotBlock()
    {
        var configuration = Ready() with
        {
            Target = TargetDescriptor.New("NEW_T", new[] { new ImportColumnDefinition { Name = "A" } }),
        };
        var report = ImportReadiness.Evaluate(Input(configuration, target: null));

        var item = Item(report, ImportDiagnosticCode.NewTableWillBeCommitted);
        Assert.False(item.IsBlocking);
        Assert.Equal("NEW_T", item.Subject);
        Assert.True(report.CanRun);
    }

    /// <summary>A BEFORE INSERT trigger can overwrite an imported value; a user who does not know that cannot
    /// understand the result (design R6). It never changes what the import does.</summary>
    [Fact]
    public void BeforeInsertTriggers_Warn()
    {
        var report = ImportReadiness.Evaluate(Input(target: Target("TR_A", "TR_B")));

        var item = Item(report, ImportDiagnosticCode.TargetHasBeforeInsertTriggers);
        Assert.False(item.IsBlocking);
        Assert.Equal(2, item.Count);
    }

    [Fact]
    public void EmptyingTheTargetFirst_Warns()
    {
        var configuration = Ready() with
        {
            Behavior = new ImportBehaviorOptions { EmptyTargetBeforeImport = true },
        };
        Assert.True(Has(ImportReadiness.Evaluate(Input(configuration)), ImportDiagnosticCode.TargetWillBeEmptied));
    }

    // ── Mapping ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NothingMapped_Blocks()
    {
        var configuration = Ready() with
        {
            Mapping = new[] { ColumnMapping.Unmapped("A"), ColumnMapping.Unmapped("B") },
        };
        var report = ImportReadiness.Evaluate(Input(configuration));

        Assert.False(report.CanRun);
        Assert.True(Item(report, ImportDiagnosticCode.NothingMapped).IsBlocking);
    }

    /// <summary>⭐ One owner. The strip does not analyse the mapping itself — it reads
    /// <c>ImportMappingPlanner.Diagnose</c>, the same call the mapping panel makes, so a red strip and a clean
    /// grid are impossible by construction rather than by discipline.</summary>
    [Fact]
    public void MappingFindings_ComeFromThePlanner_NotFromASecondAnalysis()
    {
        var target = new ImportTarget(
            "T", new[] { Col("A"), Col("REQ", notNull: true) }, Array.Empty<string>());
        var configuration = Ready() with
        {
            Mapping = new[]
            {
                new ColumnMapping { TargetColumnName = "A", SourceFieldName = "A", SourceFieldIndex = 0 },
                ColumnMapping.Unmapped("REQ"),
            },
        };

        var report = ImportReadiness.Evaluate(Input(configuration, target));
        var fromPlanner = ImportMappingPlanner.Diagnose(target, Schema(), configuration.Mapping);

        var required = Item(report, ImportDiagnosticCode.RequiredColumnNotMapped);
        Assert.True(required.IsBlocking);
        Assert.Equal(ImportSection.Mapping, required.Section);
        Assert.Contains(fromPlanner, d => d.Code == ImportDiagnosticCode.RequiredColumnNotMapped);
    }

    /// <summary>Leaving a column out is legal and often deliberate — a warning, never a block.</summary>
    [Fact]
    public void APartiallyMappedTarget_Warns_ButStillRuns()
    {
        var configuration = Ready() with
        {
            Mapping = new[]
            {
                new ColumnMapping { TargetColumnName = "A", SourceFieldName = "A", SourceFieldIndex = 0 },
                ColumnMapping.Unmapped("B"),
            },
        };
        var report = ImportReadiness.Evaluate(Input(configuration));

        Assert.True(report.CanRun);
        Assert.Equal(1, Item(report, ImportDiagnosticCode.TargetColumnNotMapped).Count);
    }

    // ── Transaction and behaviour ───────────────────────────────────────────────────────────────────────

    /// <summary>I0 measured commit frequency as nearly free, so this mode's only price is atomicity — exactly
    /// the thing §0.5 says must be stated where the choice is made.</summary>
    [Fact]
    public void BatchedMode_WarnsThatItIsNotAtomic()
    {
        var configuration = Ready() with { Transaction = ImportTransactionMode.Batched };
        var report = ImportReadiness.Evaluate(Input(configuration));

        var item = Item(report, ImportDiagnosticCode.BatchedIsNotAtomic);
        Assert.False(item.IsBlocking);
        Assert.Equal(ImportConfiguration.DefaultCommitEveryRows, item.Count);
    }

    /// <summary>About the transaction's LIFETIME, not the import's speed (design R4).</summary>
    [Fact]
    public void AVeryLargeSingleTransactionImport_Warns()
    {
        var report = ImportReadiness.Evaluate(Input() with { EstimatedRows = 500_000 });
        Assert.True(Has(report, ImportDiagnosticCode.LongTransactionRisk));

        // …and Batched is the answer to it, so it does not also nag there.
        var batched = Ready() with { Transaction = ImportTransactionMode.Batched };
        Assert.False(Has(
            ImportReadiness.Evaluate(Input(batched) with { EstimatedRows = 500_000 }),
            ImportDiagnosticCode.LongTransactionRisk));
    }

    [Fact]
    public void AnUnknownRowCount_RaisesNoWarningAtAll()
        => Assert.False(Has(ImportReadiness.Evaluate(Input()), ImportDiagnosticCode.LongTransactionRisk));

    /// <summary>§0.2: trimming loses data, so it is stated up front rather than discovered in the report.</summary>
    [Fact]
    public void TrimmingEnabled_Warns()
    {
        var configuration = Ready() with
        {
            Behavior = new ImportBehaviorOptions { TrimTooLongValues = true },
        };
        Assert.True(Has(ImportReadiness.Evaluate(Input(configuration)), ImportDiagnosticCode.TrimmingEnabled));
    }

    // ── The strip's own projection ──────────────────────────────────────────────────────────────────────

    /// <summary>⭐ The property that beats a "Next" button: every gap is reported at once, not just the first
    /// one the evaluation happened to meet.</summary>
    [Fact]
    public void EveryGapIsReportedTogether()
    {
        var configuration = Ready() with
        {
            Target = TargetDescriptor.Existing(""),
            Mapping = Array.Empty<ColumnMapping>(),
        };
        var report = ImportReadiness.Evaluate(Input(configuration) with { IsConnected = false });

        Assert.True(Has(report, ImportDiagnosticCode.NotConnected));
        Assert.True(Has(report, ImportDiagnosticCode.NoTarget));
        Assert.True(report.Blocking.Count() >= 2);
    }

    [Fact]
    public void SectionProjection_TellsTheStripWhatToPaint()
    {
        var report = ImportReadiness.Evaluate(Input(target: Target("TR_A")) with { IsConnected = false });

        Assert.Equal(ImportSeverity.Warning, report.SeverityFor(ImportSection.Target));
        Assert.Equal(ImportSeverity.Error, report.SeverityFor(ImportSection.Transaction));
        Assert.Null(report.SeverityFor(ImportSection.Source));

        Assert.True(report.IsSectionRunnable(ImportSection.Target));      // a warning does not stop the run
        Assert.False(report.IsSectionRunnable(ImportSection.Transaction));
    }

    [Fact]
    public void EveryBlockingItemNamesTheSectionThatCausedIt()
    {
        var configuration = Ready() with
        {
            Source = SourceDescriptor.File(ImportSourceKind.Csv, ""),
            Target = TargetDescriptor.Existing(""),
        };
        var report = ImportReadiness.Evaluate(Input(configuration) with { IsConnected = false });

        // A disabled Import button with no reason is a UX defect; every blocker must lead somewhere.
        Assert.All(report.Blocking, item => Assert.True(Enum.IsDefined(item.Section)));
        Assert.All(report.Blocking, item => Assert.NotEqual(string.Empty, item.CodeText));
    }
}

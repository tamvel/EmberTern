using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I2: the mapping planner.
/// <para>
/// The governing rule (§4.7, carried over from the debugger's launch configuration) is <i>keep what can be
/// PROVEN still correct, hand back what cannot, never guess</i>. The tests that matter most are the ones
/// asserting that nothing happens: an ambiguous name pairs NOTHING, and the sole-remaining-pair rule with two
/// candidates on each side pairs NOTHING. A planner that resolved those by picking the first would write one
/// column's data into another — the worst defect class this project recognises (§0.1).
/// </para>
/// </summary>
public class ImportMappingPlannerTests
{
    // ── Fixtures ────────────────────────────────────────────────────────────────────────────────────────

    private static ColumnSpec Col(
        string name, string type = "VARCHAR(50)", bool notNull = false,
        string? defaultValue = null, bool computed = false, IdentityKind identity = IdentityKind.None)
        => new(name, type, null, notNull)
        {
            DefaultValue = defaultValue,
            IsComputed = computed,
            Identity = identity,
        };

    private static ImportTarget Target(params ColumnSpec[] columns)
        => new("T", columns, Array.Empty<string>());

    private static SourceSchema Schema(params string[] names)
    {
        var fields = names.Select((n, i) => new SourceField(i, n, HasRealName: true)).ToList();
        return new SourceSchema(fields, HasHeader: true, EstimatedRows: null);
    }

    private static SourceSchema Headerless(int count)
    {
        var fields = Enumerable.Range(0, count)
            .Select(i => new SourceField(i, SourceField.PositionalName(i), HasRealName: false)).ToList();
        return new SourceSchema(fields, HasHeader: false, EstimatedRows: null);
    }

    private static ColumnMapping For(ImportMappingPlan plan, string column)
        => plan.Mapping.Single(m => m.TargetColumnName == column);

    private static bool Has(ImportMappingPlan plan, ImportDiagnosticCode code)
        => plan.Diagnostics.Any(d => d.Code == code);

    // ── Name normalization is the "proof" ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Indeks kartoteki", "INDEKS_KARTOTEKI")]
    [InlineData("nr technologii", "NR_TECHNOLOGII")]
    [InlineData("Kod-Fantomu", "KOD_FANTOMU")]
    [InlineData("  spaced  out  ", "SPACED_OUT")]
    [InlineData("ALREADY_FINE", "ALREADY_FINE")]
    public void NormalizeName_FoldsCaseAndWordBreaks(string input, string expected)
        => Assert.Equal(expected, ImportMappingPlanner.NormalizeName(input));

    /// <summary>Diacritics are NOT stripped: <c>ZAZOLC</c> and <c>ZAŻÓŁĆ</c> are different names, and
    /// conflating them would map the wrong column's data.</summary>
    [Fact]
    public void NormalizeName_DoesNotStripDiacritics()
        => Assert.NotEqual(
            ImportMappingPlanner.NormalizeName("ZAZOLC"), ImportMappingPlanner.NormalizeName("ZAŻÓŁĆ"));

    // ── Fresh automatic mapping ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_MatchesByName_AcrossSpaceAndUnderscore()
    {
        var plan = ImportMappingPlanner.Plan(
            Target(Col("INDEKS_KARTOTEKI"), Col("NR_TECHNOLOGII")),
            Schema("Indeks kartoteki", "Nr technologii"));

        Assert.Equal(0, For(plan, "INDEKS_KARTOTEKI").SourceFieldIndex);
        Assert.Equal(1, For(plan, "NR_TECHNOLOGII").SourceFieldIndex);
        Assert.All(plan.Mapping, m => Assert.Equal(MappingOrigin.Restored, m.Origin));
    }

    /// <summary>
    /// Two unmatched columns and two unused fields, deliberately: with exactly one on each side the
    /// sole-remaining-pair rule would (correctly) fire and there would be nothing left unmapped to observe.
    /// That rule has its own tests below.
    /// </summary>
    [Fact]
    public void Plan_LeavesAColumnWithNoMatchUnmapped_AndListsTheUnusedFields()
    {
        var plan = ImportMappingPlanner.Plan(
            Target(Col("A"), Col("B"), Col("C")), Schema("A", "Y", "Z"));

        Assert.False(For(plan, "B").IsMapped);
        Assert.False(For(plan, "C").IsMapped);
        Assert.Equal(new[] { "Y", "Z" }, plan.UnusedSourceFields.Select(f => f.Name));
        Assert.Equal(2, plan.Diagnostics.Single(d => d.Code == ImportDiagnosticCode.SourceFieldUnused).Count);
        Assert.Equal(2, plan.Diagnostics.Single(d => d.Code == ImportDiagnosticCode.TargetColumnNotMapped).Count);
    }

    /// <summary>⭐ Two source fields answering to one name pair NOTHING. Picking the first would be a coin
    /// flip on which column's data lands in the table.</summary>
    [Fact]
    public void Plan_RefusesToResolveAnAmbiguousName()
    {
        var plan = ImportMappingPlanner.Plan(Target(Col("KOD")), Schema("Kod", "KOD"));

        Assert.False(For(plan, "KOD").IsMapped);
        Assert.True(Has(plan, ImportDiagnosticCode.AmbiguousNameMatch));
    }

    // ── Columns that cannot be written ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_NeverMapsAComputedColumn_ButExplainsWhy()
    {
        var plan = ImportMappingPlanner.Plan(
            Target(Col("WARTOSC", computed: true)), Schema("WARTOSC"));

        Assert.False(For(plan, "WARTOSC").IsMapped);
        Assert.True(Has(plan, ImportDiagnosticCode.ColumnNotWritable));
        // The row is still present, so the grid shows a locked column with a reason rather than a gap.
        Assert.Single(plan.Mapping);
    }

    [Fact]
    public void Plan_NeverMapsAColumnWhoseTypeCannotBeImported()
    {
        var plan = ImportMappingPlanner.Plan(Target(Col("BIG", "INT128")), Schema("BIG"));

        Assert.False(For(plan, "BIG").IsMapped);
        Assert.True(Has(plan, ImportDiagnosticCode.UnsupportedColumnType));
    }

    [Fact]
    public void Plan_FlagsAMappedGeneratedAlwaysIdentity()
    {
        var plan = ImportMappingPlanner.Plan(
            Target(Col("ID", "INTEGER", identity: IdentityKind.Always)), Schema("ID"));

        Assert.True(For(plan, "ID").IsMapped);
        Assert.True(Has(plan, ImportDiagnosticCode.IdentityOverrideRequired));
    }

    // ── Required columns ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Diagnose_BlocksOnANotNullColumnWithNoDefaultAndNoMapping()
    {
        var plan = ImportMappingPlanner.Plan(
            Target(Col("A"), Col("REQ", notNull: true)), Schema("A"));

        var required = plan.Diagnostics.Single(d => d.Code == ImportDiagnosticCode.RequiredColumnNotMapped);
        Assert.Equal("REQ", required.Subject);
        Assert.Equal(ImportSeverity.Error, required.Severity);
    }

    /// <summary>⭐ A NOT NULL column WITH a default is fine unmapped — it is simply left out of the INSERT and
    /// the default fills it. Flagging it would block a perfectly good import.</summary>
    [Fact]
    public void Diagnose_DoesNotBlockOnANotNullColumnThatHasADefault()
    {
        var plan = ImportMappingPlanner.Plan(
            Target(Col("A"), Col("REQ", notNull: true, defaultValue: "0")), Schema("A"));

        Assert.False(Has(plan, ImportDiagnosticCode.RequiredColumnNotMapped));
    }

    // ── Re-planning: the provable-preservation rule ─────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Design risk R16, pinned. The file's columns are REORDERED; because identity is the NAME, each
    /// mapping follows its own data instead of staying on a position that now holds something else. A purely
    /// positional model would silently import the wrong column here.
    /// </summary>
    [Fact]
    public void Replan_FollowsAFieldThatMoved()
    {
        var target = Target(Col("A"), Col("B"));
        var first = ImportMappingPlanner.Plan(target, Schema("A", "B"));

        var second = ImportMappingPlanner.Plan(target, Schema("B", "A"), first.Mapping);

        Assert.Equal(1, For(second, "A").SourceFieldIndex);
        Assert.Equal(0, For(second, "B").SourceFieldIndex);
    }

    [Fact]
    public void Replan_DropsAMappingWhoseFieldIsGone_AndSaysSo()
    {
        var target = Target(Col("A"), Col("B"));
        var first = ImportMappingPlanner.Plan(target, Schema("A", "B"));

        var second = ImportMappingPlanner.Plan(target, Schema("A"), first.Mapping);

        Assert.False(For(second, "B").IsMapped);
        Assert.True(Has(second, ImportDiagnosticCode.MappingDropped));
    }

    /// <summary>A skip is a DECISION, not an absence — it must survive a re-read, or the planner would quietly
    /// re-add a column the user deliberately excluded.</summary>
    [Fact]
    public void Replan_PreservesADeliberateSkip()
    {
        var target = Target(Col("A"), Col("B"));
        var previous = new[] { ColumnMapping.Unmapped("A"), ColumnMapping.Skipped("B") };

        var plan = ImportMappingPlanner.Plan(target, Schema("A", "B"), previous);

        Assert.True(For(plan, "B").IsSkipped);
        Assert.False(For(plan, "B").IsMapped);
    }

    [Fact]
    public void Replan_KeepsAManualChoiceMarkedManual()
    {
        var target = Target(Col("A"), Col("B"));
        var previous = new[]
        {
            new ColumnMapping
            {
                TargetColumnName = "A", SourceFieldName = "Z", SourceFieldIndex = 1,
                Origin = MappingOrigin.Manual,
            },
            ColumnMapping.Unmapped("B"),
        };

        var plan = ImportMappingPlanner.Plan(target, Schema("B", "Z"), previous);

        var a = For(plan, "A");
        Assert.Equal(MappingOrigin.Manual, a.Origin);
        Assert.Equal(1, a.SourceFieldIndex);
    }

    // ── The sole-remaining-pair rule ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SoleRemainingPair_FiresWithExactlyOneCandidateOnEachSide()
    {
        var plan = ImportMappingPlanner.Plan(
            Target(Col("A"), Col("RENAMED")), Schema("A", "COMPLETELY_DIFFERENT"));

        var paired = For(plan, "RENAMED");
        Assert.True(paired.IsMapped);
        Assert.Equal(MappingOrigin.Assumed, paired.Origin);
        Assert.True(Has(plan, ImportDiagnosticCode.PairingAssumed));
    }

    /// <summary>⭐ Two unmatched on each side ⇒ NOTHING is paired. This is the rule's whole safety property:
    /// with two candidates there is no proof, and a 50/50 guess writes data into the wrong column.</summary>
    [Fact]
    public void SoleRemainingPair_DoesNotFireWithTwoCandidates()
    {
        var plan = ImportMappingPlanner.Plan(
            Target(Col("A"), Col("X1"), Col("X2")), Schema("A", "Y1", "Y2"));

        Assert.False(For(plan, "X1").IsMapped);
        Assert.False(For(plan, "X2").IsMapped);
        Assert.False(Has(plan, ImportDiagnosticCode.PairingAssumed));
    }

    /// <summary>A column that can never be written is not a "remaining candidate" — otherwise a computed
    /// column would consume the pair and block a real one.</summary>
    [Fact]
    public void SoleRemainingPair_IgnoresUnwritableColumns()
    {
        var plan = ImportMappingPlanner.Plan(
            Target(Col("A"), Col("RENAMED"), Col("CALC", computed: true)),
            Schema("A", "SOMETHING"));

        Assert.True(For(plan, "RENAMED").IsMapped);
        Assert.False(For(plan, "CALC").IsMapped);
    }

    // ── Headerless sources ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Headerless_CarriesAPositionalMappingForward()
    {
        var target = Target(Col("A"), Col("B"));
        var previous = new[]
        {
            new ColumnMapping { TargetColumnName = "A", SourceFieldIndex = 1, Origin = MappingOrigin.Manual },
            ColumnMapping.Unmapped("B"),
        };

        var plan = ImportMappingPlanner.Plan(target, Headerless(2), previous);

        Assert.Equal(1, For(plan, "A").SourceFieldIndex);
        Assert.Equal(MappingOrigin.Manual, For(plan, "A").Origin);
    }

    /// <summary>Once the source HAS names, they are the better identity — a positional mapping made against a
    /// headerless file is not carried blindly into a named one.</summary>
    [Fact]
    public void Headerless_MappingIsNotCarriedIntoANamedSource()
    {
        var target = Target(Col("A"), Col("B"));
        var previous = new[]
        {
            new ColumnMapping { TargetColumnName = "A", SourceFieldIndex = 1, Origin = MappingOrigin.Manual },
            ColumnMapping.Unmapped("B"),
        };

        var plan = ImportMappingPlanner.Plan(target, Schema("A", "B"), previous);

        Assert.Equal(0, For(plan, "A").SourceFieldIndex);   // matched by NAME instead
        Assert.Equal(MappingOrigin.Restored, For(plan, "A").Origin);
    }

    // ── The explicit gestures ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MatchByPosition_PairsInOrder_AndSkipsUnwritableColumns()
    {
        var plan = ImportMappingPlanner.MatchByPosition(
            Target(Col("A"), Col("CALC", computed: true), Col("C")), Schema("F0", "F1"));

        Assert.Equal(0, For(plan, "A").SourceFieldIndex);
        Assert.False(For(plan, "CALC").IsMapped);
        Assert.Equal(1, For(plan, "C").SourceFieldIndex);
        Assert.All(plan.Mapping.Where(m => m.IsMapped), m => Assert.Equal(MappingOrigin.Manual, m.Origin));
    }

    [Fact]
    public void Clear_UnmapsEverything()
    {
        var plan = ImportMappingPlanner.Clear(Target(Col("A"), Col("B")));

        Assert.Equal(2, plan.Mapping.Count);
        Assert.All(plan.Mapping, m => Assert.False(m.IsMapped));
    }

    // ── Project (pipeline step 2) ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Project_PullsValuesIntoTargetOrder()
    {
        var mapped = new List<ColumnMapping>
        {
            new() { TargetColumnName = "A", SourceFieldIndex = 2 },
            new() { TargetColumnName = "B", SourceFieldIndex = 0 },
        };
        var record = new RawRecord(7, new object?[] { "zero", "one", "two" });

        Assert.Equal(new object?[] { "two", "zero" }, ImportMappingPlanner.Project(record, mapped));
    }

    /// <summary>A ragged record is legal: the absent field is simply absent (null) and does NOT shift its
    /// neighbours, which is the shift that would silently move a whole row's data one column over.</summary>
    [Fact]
    public void Project_YieldsNullForAFieldTheRecordDoesNotReach()
    {
        var mapped = new List<ColumnMapping>
        {
            new() { TargetColumnName = "A", SourceFieldIndex = 0 },
            new() { TargetColumnName = "B", SourceFieldIndex = 5 },
        };
        var record = new RawRecord(1, new object?[] { "only" });

        Assert.Equal(new object?[] { "only", null }, ImportMappingPlanner.Project(record, mapped));
    }

    // ── Diagnostic codes ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DiagnosticCodes_RenderAsStableImpNumbers()
    {
        Assert.Equal("IMP0002", ImportDiagnosticCode.RequiredColumnNotMapped.ToCode());
        Assert.Equal("IMP0027", ImportDiagnosticCode.NotRepresentableInConnectionCharset.ToCode());
        Assert.Equal(string.Empty, ImportDiagnosticCode.None.ToCode());
    }
}

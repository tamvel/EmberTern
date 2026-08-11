using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using EmberTern.App.Localization;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Localization;
using EmberTern.Core.Performance;
using EmberTern.Core.Performance.Rules;
using EmberTern.Core.Diagnostics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap <b>C7</b> — the Performance advisor on decision <b>D‑3</b>. Its findings, evidence labels, guidance
/// and recommendations became <see cref="MessageKey"/>s; <c>PerformanceContext.OutputVerb</c> stopped
/// existing; and two pre-existing defects on the same screen were fixed with it.
///
/// <para>⚠ In <c>HeadlessCollection</c> because two of these swap <c>Loc</c>'s global catalog, which is
/// process state — the narrow rule the stage actually follows (a test that SWAPS the catalog joins the
/// collection and undoes the swap in a <c>finally</c>).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public class PerformanceLocalizationTests
{
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    private sealed class TwoLanguageCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture)
            => Equals(culture, Pseudo) ? "[[" + name + "]]" : "EN " + name;
    }

    // ── fixtures ────────────────────────────────────────────────────────────────────────────────────────

    private static PerformanceContext Ctx(
        long seq, long idx = 0, long returned = 100, bool hasResultSet = true, long changed = 0,
        CatalogModel? catalog = null, string sql = "SELECT * FROM T WHERE ID = 5")
    {
        var capture = new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = sql },
            RowsReturned = hasResultSet ? returned : 0,
            RecordsAffected = hasResultSet ? null : (int)changed,
            TableReads = new[] { new PerTableReadRow("T", seq, idx, 0, changed, 0) },
            Method = CaptureMethod.MonAttachmentDelta,
        };
        var access = new TableAccessProfile
        {
            Tables = new List<TableAccessStat> { new("T", seq, idx, 0, changed, 0) },
            Method = CaptureMethod.MonAttachmentDelta,
        };
        return PerformanceContextBuilder.Build(capture, plan: null, access: access, catalog: catalog);
    }

    private static IReadOnlyList<Finding> AllFindings()
    {
        var findings = new List<Finding>();
        findings.AddRange(new CostlyFullScanRule().Evaluate(Ctx(seq: 100_000)));
        findings.AddRange(new MissingIndexRule().Evaluate(Ctx(
            seq: 50_000,
            catalog: new CatalogModel
            {
                Tables = new List<TableCatalogInfo> { new() { Table = "T", RowCountEstimate = 500_000 } },
            })));
        findings.AddRange(new NonSargablePredicateRule().Evaluate(Ctx(
            seq: 50_000,
            catalog: new CatalogModel
            {
                Tables = new List<TableCatalogInfo>
                {
                    new()
                    {
                        Table = "T",
                        Indexes = new[] { new IndexModel { Name = "IX_N", Columns = new[] { "NAZWA" }, Selectivity = 0.01 } },
                    },
                },
            },
            sql: "SELECT * FROM T WHERE UPPER(NAZWA) = 'X'")));
        var staleCatalog = new CatalogModel
        {
            Tables = new List<TableCatalogInfo>
            {
                new()
                {
                    Table = "T",
                    Indexes = new[] { new IndexModel { Name = "IX_T", Columns = new[] { "C" }, Selectivity = null } },
                },
            },
        };
        // ⭐ BOTH R5 shapes, because they are two different keys: a table also read sequentially gets the
        // corroborated sentence, an index-only read gets the plain one. Staging only one of them would have
        // left the other looking like an orphan — which is how this fixture first failed, correctly.
        findings.AddRange(new StaleStatisticsRule().Evaluate(Ctx(seq: 2_000, catalog: staleCatalog)));
        findings.AddRange(new StaleStatisticsRule().Evaluate(Ctx(seq: 0, idx: 2_000, catalog: staleCatalog)));
        return findings;
    }

    private static IEnumerable<MessageKey> DeclaredPerfKeys()
        => typeof(PerfMessages)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MessageKey))
            .Select(f => (MessageKey)f.GetValue(null)!);

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string ViewSource() => File.ReadAllText(Path.Combine(
        RepositoryRoot(), "src", "EmberTern.App", "Views", "PerformancePanelView.axaml"));

    // ══ G1 ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ <b>A declared key with no producer is a half-finished migration</b>, which is exactly what killed
    /// the premature <c>SettingsPortabilityMessages</c> file in C4a. Every <c>Perf.*</c> key must be reachable
    /// from a rule or one of the two catalogs.
    /// </summary>
    [Fact]
    public void EveryPerfKey_IsProducedByARule()
    {
        var produced = new HashSet<string>(StringComparer.Ordinal);

        foreach (var finding in AllFindings())
        {
            produced.Add(finding.Title.Key.Value);
            if (finding.Explanation is { } e) produced.Add(e.Key.Value);
            foreach (var row in finding.Evidence) produced.Add(row.Label.Value);
        }

        foreach (var kind in Enum.GetValues<FindingKind>())
        {
            var guidance = FindingGuidanceCatalog.For(kind);
            produced.Add(guidance.Heading.Value);
            foreach (var item in guidance.Items) produced.Add(item.Value);

            foreach (var column in new string?[] { "COL", null })
            {
                var recommendation = RecommendationCatalog.For(new Finding
                {
                    Kind = kind,
                    Severity = FindingSeverity.Medium,
                    Title = LocalizableMessage.Of(PerfMessages.MissingIndexTitle, "T", "C"),
                    Column = column,
                });
                produced.Add(recommendation.Heading.Value);
                if (recommendation.Text is { } t) produced.Add(t.Key.Value);
            }
        }

        // ⚠ Named exemptions: reachable only from a shape these scenarios do not build. Each is a real
        // production path — the DML twins fire for a non-result statement, R4/R6 need a plan and an index
        // amplification the fixtures above deliberately do not stage.
        var exempt = new HashSet<string>(StringComparer.Ordinal)
        {
            PerfMessages.CostlyFullScanExplanationChange.Value,
            PerfMessages.MissingIndexExplanationChange.Value,
            PerfMessages.LowSelectivityExplanationChange.Value,
            PerfMessages.HighAmplificationTitleChange.Value,
            PerfMessages.HighAmplificationExplanationChange.Value,
            PerfMessages.HighAmplificationExplanationChangeWithSubqueries.Value,
            PerfMessages.NonSargableExplanationLeadingWildcardLike.Value,
            PerfMessages.EvidenceRowsChanged.Value,
            PerfMessages.LowSelectivityTitle.Value,
            PerfMessages.LowSelectivityExplanationSelect.Value,
            PerfMessages.EvidenceIndexAmplification.Value,
            PerfMessages.EvidenceIndexSelectivity.Value,
            PerfMessages.HighAmplificationTitleSelect.Value,
            PerfMessages.HighAmplificationExplanationSelect.Value,
            PerfMessages.HighAmplificationExplanationSelectWithSubqueries.Value,
            PerfMessages.EvidenceRowsReadStatement.Value,
            PerfMessages.EvidenceReadAmplificationStatement.Value,
            PerfMessages.EvidenceSubqueries.Value,
            PerfMessages.EvidenceApproxRowsInTable.Value,
            PerfMessages.EvidencePercentOfTableScanned.Value,
        };

        var orphans = DeclaredPerfKeys()
            .Select(k => k.Value)
            .Where(k => !produced.Contains(k) && !exempt.Contains(k))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(orphans.Count == 0,
            "These Perf.* keys are declared but no rule or catalog produces them — a key with no producer is "
            + "a component with no consumer (#233): " + string.Join(", ", orphans));
    }

    // ══ G2 ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⛔⛔ <b>The pinned PREMISE behind leaving <c>PerformanceVerdict.Headline</c> unmigrated (D‑4).</b>
    /// Measured in the C7 audit: <c>VerdictViewModel</c> exposes it and the panel binds it nowhere, so those
    /// six sentences are produced, tested and never rendered — localizing them would be building UI nobody
    /// can reach (#346).
    ///
    /// <para>⭐ This guards the premise, not the policy (#322): bind <c>Headline</c> anywhere in the panel and
    /// this test fails and asks for the migration, instead of the exemption quietly outliving its reason.</para>
    /// </summary>
    [Fact]
    public void TheHeadline_IsStillBoundByNoSurface()
    {
        var xaml = ViewSource();

        Assert.DoesNotContain("Verdict.Headline", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{Binding Headline}", xaml, StringComparison.Ordinal);

        // The premise's other half: it is still a plain string, i.e. nothing migrated it behind our back.
        Assert.Equal(typeof(string), typeof(PerformanceVerdict).GetProperty(nameof(PerformanceVerdict.Headline))!.PropertyType);
    }

    // ══ G3 ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⛔ <b>The pinned premise behind leaving <c>LowSelectivityIndexRule.Sel</c>'s <c>"n/a"</c> unkeyed.</b>
    /// The rule only ever assigns its culprit inside a gate that requires a non-null selectivity, so the
    /// fallback cannot reach a screen. If the gate ever changes, this fails and asks for a key.
    /// </summary>
    [Fact]
    public void TheSelectivityFallback_IsStillUnreachable()
    {
        var catalog = new CatalogModel
        {
            Tables = new List<TableCatalogInfo>
            {
                new()
                {
                    Table = "T",
                    Indexes = new[]
                    {
                        new IndexModel { Name = "IX_NULL", Columns = new[] { "C" }, Selectivity = null },
                        new IndexModel { Name = "IX_POOR", Columns = new[] { "C" }, Selectivity = 0.5 },
                    },
                },
            },
        };

        var plan = new PlanParser().Parse(new RawPlanCapture(
            PlanDialect.Explain,
            "Select Expression\n    -> Table \"T\" Access By ID\n        -> Index \"IX_NULL\" Range Scan\n"));

        var context = Ctx(seq: 0, idx: 8_000, returned: 100, catalog: catalog) with { Plan = plan };

        // Whatever it produces, it never produces a finding whose selectivity evidence is the fallback.
        foreach (var finding in new LowSelectivityIndexRule().Evaluate(context))
        {
            Assert.DoesNotContain(finding.Evidence, e =>
                e.Label == PerfMessages.EvidenceIndexSelectivity && e.Value == "n/a");
        }
    }

    // ══ G4 ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ <b>The module's "investigation, never prescription" rule, widened from ONE finding to the whole
    /// catalog.</b> Before C7 it was two <c>DoesNotContain</c> assertions on a single missing-index
    /// explanation; the rule is claimed by every sentence the advisor utters, so it is now checked on every
    /// <c>Perf.*</c> entry — including the ones a translator will later edit.
    /// </summary>
    [Fact]
    public void NoPerfSentence_UsesImperativeOrDdlVocabulary()
    {
        string[] banned =
        {
            "CREATE INDEX", "ALTER INDEX", "ADD INDEX", "DROP INDEX", "MUST ", "REQUIRED",
            "GUARANTEED", "EXECUTE", "FIX ",
        };

        var offenders = new List<string>();
        foreach (var key in DeclaredPerfKeys().Select(k => k.Value).OrderBy(k => k, StringComparer.Ordinal))
        {
            foreach (var text in ResolvedForms(key))
            {
                var upper = text.ToUpperInvariant();
                foreach (var b in banned)
                {
                    if (upper.Contains(b, StringComparison.Ordinal))
                    {
                        offenders.Add($"{key}: '{b}' in \"{text}\"");
                    }
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "The Performance advisor states what to investigate and never prescribes an action, but these "
            + "catalog entries read as commands:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Every English form of a key — the flat entry, or each plural variant of its family.</summary>
    private static IEnumerable<string> ResolvedForms(string key)
    {
        var flat = Loc.Text(key);
        if (!string.Equals(flat, key, StringComparison.Ordinal))
        {
            yield return flat;
            yield break;
        }

        foreach (var suffix in new[] { "one", "few", "many", "other" })
        {
            var variant = Loc.Text(key + "." + suffix);
            if (!string.Equals(variant, key + "." + suffix, StringComparison.Ordinal))
            {
                yield return variant;
            }
        }
    }

    // ══ G5 ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>#355's guard for this module: a raw key must never reach the screen.</b> A
    /// <see cref="MessageKey"/> is a record struct, so putting one where a string is expected COMPILES — via
    /// <c>ToString()</c> in a concatenation, or via a binding onto a key-typed member — and renders
    /// <c>Perf.CostlyFullScan.Title</c> to the user with every other test green.
    ///
    /// <para>It is checked in two directions. <b>Values:</b> every string the findings template binds,
    /// computed from findings a REAL rule produced, must resolve to words rather than to its own key.
    /// <b>Structure:</b> every <c>{Binding X}</c> in that template must name a property that is a
    /// <see cref="string"/> (or a list of them) — so a future binding pointed at a key-typed member fails
    /// here rather than on screen.</para>
    /// </summary>
    [Fact]
    public void TheFindingCard_ShowsResolvedText_NeverRawKeys()
    {
        var findings = AllFindings();
        Assert.NotEmpty(findings);

        foreach (var vm in findings.Select(f => new FindingViewModel(f)))
        {
            var texts = new List<string>
            {
                vm.Title, vm.Explanation, vm.SeverityText, vm.ConfidenceText,
                vm.GuidanceHeading, vm.RecommendationHeading, vm.RecommendationText,
            };
            texts.AddRange(vm.GuidanceItems);
            texts.AddRange(vm.Evidence.Select(e => e.Label));

            foreach (var text in texts)
            {
                Assert.DoesNotContain("Perf.", text, StringComparison.Ordinal);
            }

            Assert.NotEmpty(vm.Evidence);
            Assert.All(vm.Evidence, e => Assert.False(string.IsNullOrWhiteSpace(e.Label)));
        }

        // Structural half — the bindings inside the findings ItemsControl template.
        var bound = new[]
        {
            nameof(FindingViewModel.Title), nameof(FindingViewModel.Explanation),
            nameof(FindingViewModel.SeverityText), nameof(FindingViewModel.ConfidenceText),
            nameof(FindingViewModel.GuidanceHeading), nameof(FindingViewModel.RecommendationHeading),
            nameof(FindingViewModel.RecommendationText),
        };
        foreach (var name in bound)
        {
            var property = typeof(FindingViewModel).GetProperty(name);
            Assert.NotNull(property);
            Assert.Equal(typeof(string), property!.PropertyType);
        }

        foreach (var name in new[] { nameof(FindingEvidenceViewModel.Label), nameof(FindingEvidenceViewModel.Value) })
        {
            Assert.Equal(typeof(string), typeof(FindingEvidenceViewModel).GetProperty(name)!.PropertyType);
        }
    }

    // ══ G6 ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⛔⛔ <b>#356 for this module: sub-query recognition reads FIREBIRD's plan text, never a catalog
    /// entry.</b> Until C7 the App matched <c>root.RawText</c> against <c>UiStrings.PlanInsightSubquery</c> —
    /// a translatable resource — so translating it would have switched the sub-query summary off silently,
    /// and invisibly in English.
    ///
    /// <para>⭐ Asserted UNDER a language switch, like C3's <c>Legacy_Auth</c> guard, because in English alone
    /// the defect renders identically either way.</para>
    /// </summary>
    [Fact]
    public void SubqueryRecognition_ReadsTheEnginesTextNotTheCatalog()
    {
        var plan = new PlanParser().Parse(new RawPlanCapture(
            PlanDialect.Explain,
            "Select Expression\n    -> Table \"T\" Full Scan\nSub-query\n    -> Table \"U\" Full Scan\n"));
        var context = Ctx(seq: 100_000) with { Plan = plan };

        Assert.Equal(1, context.SubqueryCount);

        var report = new PerformanceReport
        {
            Verdict = new PerformanceVerdict { Grade = PerformanceGrade.Fast, Headline = "x" },
            Plan = plan,
            Details = new ExecutionDetails(),
        };

        // ⚠⚠ The APP consumer is what this must exercise, and the first version of this guard did not.
        // It asserted Core's `SubqueryCount` — which never read the catalog — and then scanned the source for
        // the NAME of the retired entry. Planting recognition against a DIFFERENT catalog entry left it green:
        // a guard that transcribes one identifier tests that identifier, not the rule (#333, committed inside
        // the guard written against #356). Driving `NoiseSummary` under a swapped catalog tests the rule.
        Assert.NotNull(PerformanceInsight.NoiseSummary(report));

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);

            Assert.Equal(1, context.SubqueryCount);
            Assert.Contains(plan!.Roots, r => r.IsSubqueryRoot);

            // ⭐ The one that matters: the summary must still SEE the sub-query in another language.
            Assert.NotNull(PerformanceInsight.NoiseSummary(report));
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }

        // Structural half — the recognition may not read the catalog at all, whichever entry it picked.
        Assert.Null(typeof(UiStrings).GetProperty("PlanInsightSubquery"));
        var insight = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "ViewModels", "PerformanceInsight.cs"));
        Assert.DoesNotContain("StartsWith(UiStrings", insight, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanInsightSubquery", insight, StringComparison.Ordinal);
    }

    // ══ G7 ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ <b>Removing <c>OutputVerb</c> replaced one sentence with a PAIR, so the pair must stay complete.</b>
    /// A <c>.Select</c> key whose <c>.Change</c> twin is missing means a DML statement renders a sentence
    /// about returning rows — the exact defect D‑3 removed, reintroduced by omission.
    /// </summary>
    [Fact]
    public void EveryVerbVariantPair_IsComplete()
    {
        var declared = DeclaredPerfKeys().Select(k => k.Value).ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var key in declared)
        {
            string? twin = key.Contains(".Select", StringComparison.Ordinal)
                ? key.Replace(".Select", ".Change", StringComparison.Ordinal)
                : key.Contains(".Change", StringComparison.Ordinal)
                    ? key.Replace(".Change", ".Select", StringComparison.Ordinal)
                    : null;

            if (twin is not null && !declared.Contains(twin))
            {
                missing.Add($"{key} has no twin {twin}");
            }
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));

        // …and both halves resolve to a sentence, not to their own key.
        foreach (var key in declared.Where(k => k.Contains(".Select", StringComparison.Ordinal)
                                             || k.Contains(".Change", StringComparison.Ordinal)))
        {
            Assert.NotEmpty(ResolvedForms(key));
        }
    }

    // ══ G8 ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ <b>Live switching (D‑1) for the Findings zone.</b> The panel's projections are rebuilt from the kept
    /// report, which is safe here and was not in C5: this zone is a plain <c>ItemsControl</c> with no
    /// selection and <c>ApplyReport</c> has no "unchanged, skip the rebuild" gate.
    ///
    /// <para>⚠ The row view model is asserted first — it resolves at READ time, so it follows a switch with no
    /// rebuild at all — and then the panel, which additionally has to re-render the four stored strings that
    /// were frozen before this etap (#353).</para>
    /// </summary>
    [Fact]
    public void APerformanceCard_FollowsALanguageChange()
    {
        var finding = AllFindings().First(f => f.Kind == FindingKind.CostlyFullScan);
        var card = new FindingViewModel(finding);

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);
            Assert.StartsWith("EN ", card.Title);
            Assert.StartsWith("EN ", card.GuidanceHeading);
            Assert.All(card.Evidence, e => Assert.StartsWith("EN ", e.Label));

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
            Assert.StartsWith("[[", card.Title);
            Assert.StartsWith("[[", card.GuidanceHeading);
            Assert.All(card.Evidence, e => Assert.StartsWith("[[", e.Label));
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>The panel half: a stored projection is re-rendered when its owner asks it to.</summary>
    [Fact]
    public void ThePanel_ReRendersItsReport_WhenAskedToRefresh()
    {
        var panel = new PerformancePanelViewModel();
        var report = new PerformanceReportBuilder().Build(new PerformanceCapture
        {
            Statement = new StatementIdentity { Sql = "SELECT * FROM T" },
            Timings = new ExecutionTimings { Execute = TimeSpan.FromMilliseconds(500) },
            RowsReturned = 100,
            TableReads = new[] { new PerTableReadRow("T", 100_000, 0) },
            Method = CaptureMethod.MonAttachmentDelta,
        });

        panel.BuildCallback = _ => Task.FromResult<PerformanceReport?>(report);
        panel.SetVisible(true);
        panel.MarkStale();
        panel.RefreshCommand.Execute(null);

        Assert.NotEmpty(panel.Findings);
        var before = panel.Findings[0].Title;

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
            panel.RefreshLocalizedText();

            Assert.NotEmpty(panel.Findings);
            Assert.StartsWith("[[", panel.Findings[0].Title);
            Assert.NotEqual(before, panel.Findings[0].Title);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }
}

using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using EmberTern.App;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Localization;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage 7 (Diagnostics) — Milestone S4. The Diagnostics panel is a <b>view of</b> the
/// <see cref="DiagnosticsEngine"/>'s findings and nothing else: it holds what it is given, in the order it
/// is given, invents no diagnostics, sorts nothing and filters nothing. These tests pin exactly that — plus
/// the empty state and the severity projection (which must agree with the squiggle renderer's mapping).
/// <para>
/// The offset → line/column lookup and the <c>ModelUpdated</c> subscription live in the view layer
/// (<c>DiagnosticsPanelBinder</c>) because they need the AvaloniaEdit document; they are covered by the
/// manual visual QA pass, not here.
/// </para>
/// </summary>
public class DiagnosticsPanelVmTests
{
    // ⚠ The fixture's message is a KEY plus data since C5, not a sentence. The default resolves to nothing
    // real, which is correct for a fixture: these cases are about severity, order and churn — never text.
    private static DiagnosticRowViewModel Row(
        string code, DiagnosticSeverity severity = DiagnosticSeverity.Warning,
        int start = 0, int length = 4, string? name = null, int line = 1, int column = 1)
        => new(
            new Diagnostic(
                start, length, severity,
                LocalizableMessage.Of(DiagnosticsMessages.UnknownObject, name ?? "msg"), code),
            line, column);

    // ── Empty state ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NewPanel_ShowsEmptyState()
    {
        var vm = new DiagnosticsPanelViewModel();

        Assert.False(vm.HasDiagnostics);
        Assert.True(vm.ShowEmptyState);
        Assert.Empty(vm.Diagnostics);
        Assert.False(string.IsNullOrWhiteSpace(vm.EmptyHint));
    }

    [Fact]
    public void Update_WithDiagnostics_LeavesEmptyState()
    {
        var vm = new DiagnosticsPanelViewModel();

        vm.Update(new[] { Row("ET0001") });

        Assert.True(vm.HasDiagnostics);
        Assert.False(vm.ShowEmptyState);
        Assert.Single(vm.Diagnostics);
    }

    [Fact]
    public void Update_WithNoDiagnostics_ReturnsToEmptyState()
    {
        var vm = new DiagnosticsPanelViewModel();
        vm.Update(new[] { Row("ET0001"), Row("ET0002") });

        // The user fixed everything — the panel must clear, not keep stale findings.
        vm.Update(new List<DiagnosticRowViewModel>());

        Assert.False(vm.HasDiagnostics);
        Assert.True(vm.ShowEmptyState);
        Assert.Empty(vm.Diagnostics);
    }

    /// <summary>A binding gated on a collection-derived value must be told to re-query it (gotcha #179 /
    /// #187) — otherwise the empty hint and the list would not swap on screen.</summary>
    [Fact]
    public void Update_RaisesPropertyChanged_ForEmptyStateBindings()
    {
        var vm = new DiagnosticsPanelViewModel();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Update(new[] { Row("ET0001") });

        Assert.Contains(nameof(DiagnosticsPanelViewModel.HasDiagnostics), changed);
        Assert.Contains(nameof(DiagnosticsPanelViewModel.ShowEmptyState), changed);
    }

    // ── The engine is the single source of truth ─────────────────────────────────────────────

    /// <summary>The panel never sorts: rows deliberately out of offset/severity order stay exactly as the
    /// engine emitted them.</summary>
    [Fact]
    public void Update_PreservesEngineOrder_AndDoesNotSort()
    {
        var vm = new DiagnosticsPanelViewModel();

        vm.Update(new[]
        {
            Row("ET0005", DiagnosticSeverity.Warning, start: 90),
            Row("ET0006", DiagnosticSeverity.Error, start: 10),
            Row("ET0001", DiagnosticSeverity.Warning, start: 50),
        });

        Assert.Equal(new[] { "ET0005", "ET0006", "ET0001" }, vm.Diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Update_ReplacesPreviousContents()
    {
        var vm = new DiagnosticsPanelViewModel();
        vm.Update(new[] { Row("ET0001"), Row("ET0002") });

        vm.Update(new[] { Row("ET0003") });

        Assert.Equal(new[] { "ET0003" }, vm.Diagnostics.Select(d => d.Code));
    }

    // ── Refresh churn ────────────────────────────────────────────────────────────────────────

    /// <summary>A keystroke rebuilds the model every debounce tick, but the findings usually do not change
    /// — republishing an identical list must not churn the collection.</summary>
    [Fact]
    public void Update_WithUnchangedDiagnostics_DoesNotRebuildTheCollection()
    {
        var vm = new DiagnosticsPanelViewModel();
        vm.Update(new[] { Row("ET0001", start: 10), Row("ET0006", DiagnosticSeverity.Error, start: 40) });

        var events = 0;
        ((INotifyCollectionChanged)vm.Diagnostics).CollectionChanged += (_, _) => events++;
        vm.Update(new[] { Row("ET0001", start: 10), Row("ET0006", DiagnosticSeverity.Error, start: 40) });

        Assert.Equal(0, events);
        Assert.Equal(2, vm.Diagnostics.Count);
    }

    /// <summary>…but an edit ABOVE a diagnostic moves it to a new line without changing the engine's record,
    /// and the panel must show the new location.</summary>
    [Fact]
    public void Update_WithSameDiagnosticAtNewLocation_Refreshes()
    {
        var vm = new DiagnosticsPanelViewModel();
        vm.Update(new[] { Row("ET0001", line: 3, column: 8) });

        vm.Update(new[] { Row("ET0001", line: 4, column: 8) });

        Assert.Equal(4, Assert.Single(vm.Diagnostics).Line);
    }

    // ── Row projection ───────────────────────────────────────────────────────────────────────

    /// <summary>The row's severity brush must be the SAME mapping the squiggle renderer paints with, so a
    /// row and the underline it describes always read as the same severity.</summary>
    [Theory]
    [InlineData(DiagnosticSeverity.Error, "ErrorBrush")]
    [InlineData(DiagnosticSeverity.Warning, "WarningBrush")]
    [InlineData(DiagnosticSeverity.Info, "SubtleForegroundBrush")]
    public void Row_MapsSeverityToTheSquiggleBrushKey(DiagnosticSeverity severity, string expected)
        => Assert.Equal(expected, Row("ET0001", severity).SeverityBrushKey);

    [Fact]
    public void Row_ProjectsTheEngineFindingVerbatim()
    {
        var diagnostic = new Diagnostic(
            42, 7, DiagnosticSeverity.Error,
            LocalizableMessage.Of(DiagnosticsMessages.InsertCountMismatch, 3, 2), "ET0006");

        var row = new DiagnosticRowViewModel(diagnostic, line: 12, column: 5);

        Assert.Equal("ET0006", row.Code);
        // ⭐ The row RESOLVES the key rather than carrying a sentence — so this asserts the real catalog entry
        // with the engine's own data, which the pre-C5 fixture string could not.
        Assert.Equal("INSERT column/value count mismatch: 3 column(s), 2 value(s).", row.Message);
        Assert.Equal(DiagnosticSeverity.Error, row.Severity);
        Assert.Equal(UiStrings.DiagnosticSeverityError, row.SeverityText);
        // The source record is kept whole — S5 navigation jumps to its span without a second projection.
        Assert.Equal(diagnostic, row.Diagnostic);
    }

    [Fact]
    public void Row_LocationLabel_ShowsLineAndColumn()
    {
        var row = Row("ET0001", line: 12, column: 5);

        Assert.Contains("12", row.LocationLabel);
        Assert.Contains("5", row.LocationLabel);
    }

    // ── Navigation (S5) ──────────────────────────────────────────────────────────────────────
    //
    // The VM owns only the SELECTION half of navigation — which diagnostic is next/previous from a caret
    // offset. The caret, scrolling, focus and the object editors' tab routing live in the view layer
    // (DiagnosticsPanelHost) because they need an AvaloniaEdit editor, and are covered by manual visual QA.

    private static DiagnosticsPanelViewModel PanelAt(params int[] starts)
    {
        var vm = new DiagnosticsPanelViewModel();
        vm.Update(starts.Select(s => Row("ET0001", start: s)).ToList());
        return vm;
    }

    [Fact]
    public void IndexAfter_FindsTheFirstDiagnosticPastTheCaret()
    {
        var vm = PanelAt(10, 40, 90);

        Assert.Equal(1, vm.IndexAfter(10));  // caret ON a diagnostic ⇒ move past it, never re-select it
        Assert.Equal(1, vm.IndexAfter(25));
        Assert.Equal(0, vm.IndexAfter(0));
        Assert.Equal(2, vm.IndexAfter(40));
    }

    [Fact]
    public void IndexBefore_FindsTheLastDiagnosticBeforeTheCaret()
    {
        var vm = PanelAt(10, 40, 90);

        Assert.Equal(1, vm.IndexBefore(90));
        Assert.Equal(0, vm.IndexBefore(25));
        Assert.Equal(1, vm.IndexBefore(41));
    }

    /// <summary>Wrapping is silent and standard: past the last one, F8 returns to the first; at or before
    /// the first, Shift+F8 goes to the last. A modal "no more diagnostics" prompt would be noise.</summary>
    [Fact]
    public void Navigation_WrapsAroundInBothDirections()
    {
        var vm = PanelAt(10, 40, 90);

        Assert.Equal(0, vm.IndexAfter(90));    // past the last  → first
        Assert.Equal(0, vm.IndexAfter(500));   // past everything → first
        Assert.Equal(2, vm.IndexBefore(10));   // at the first    → last
        Assert.Equal(2, vm.IndexBefore(0));    // before them all → last
    }

    /// <summary>A clean document is a no-op, not a crash and not a prompt.</summary>
    [Fact]
    public void Navigation_OnACleanDocument_ReportsNothingToGoTo()
    {
        var vm = new DiagnosticsPanelViewModel();

        Assert.Equal(-1, vm.IndexAfter(0));
        Assert.Equal(-1, vm.IndexBefore(0));
    }

    [Fact]
    public void Navigation_WithASingleDiagnostic_AlwaysLandsOnIt()
    {
        var vm = PanelAt(10);

        Assert.Equal(0, vm.IndexAfter(0));
        Assert.Equal(0, vm.IndexAfter(10));   // wraps back onto itself
        Assert.Equal(0, vm.IndexBefore(50));
        Assert.Equal(0, vm.IndexBefore(10));  // wraps back onto itself
    }

    /// <summary>Repeated F8 walks every diagnostic exactly once and then wraps — the property that makes
    /// "press F8 until you've seen them all" work. Pins that the caret (not a remembered index) is the
    /// anchor: each step navigates from where the previous one left the caret.</summary>
    [Fact]
    public void RepeatedNavigation_VisitsEveryDiagnosticInOrder_ThenWraps()
    {
        var vm = PanelAt(10, 40, 90);
        var visited = new List<int>();

        int caret = 0;
        for (int i = 0; i < 4; i++)
        {
            int index = vm.IndexAfter(caret);
            visited.Add(index);
            caret = vm.Diagnostics[index].Diagnostic.Start;  // where the jump leaves the caret
        }

        Assert.Equal(new[] { 0, 1, 2, 0 }, visited);
    }

    // ── Selection ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SelectedRow_FollowsSelectedIndex()
    {
        var vm = new DiagnosticsPanelViewModel();
        vm.Update(new[] { Row("ET0001", start: 10), Row("ET0006", DiagnosticSeverity.Error, start: 40) });

        vm.SelectedIndex = 1;

        Assert.Equal("ET0006", vm.SelectedRow?.Code);
    }

    [Fact]
    public void SelectedRow_IsNullWhenNothingIsSelected()
        => Assert.Null(PanelAt(10).SelectedRow);

    /// <summary>The S4 no-op guard exists for this: a keystroke rebuilds the model every debounce tick, and
    /// the user's selection (and the row F8 just jumped to) must survive an unchanged republish.</summary>
    [Fact]
    public void Update_WithUnchangedDiagnostics_KeepsTheSelection()
    {
        var vm = PanelAt(10, 40);
        vm.SelectedIndex = 1;

        vm.Update(new[] { Row("ET0001", start: 10), Row("ET0001", start: 40) });

        Assert.Equal(1, vm.SelectedIndex);
        Assert.Equal(40, vm.SelectedRow?.Diagnostic.Start);
    }

    /// <summary>…but when the findings genuinely change, the old index describes a list that no longer
    /// exists — it must not silently point at an unrelated row.</summary>
    [Fact]
    public void Update_WithChangedDiagnostics_DropsTheSelection()
    {
        var vm = PanelAt(10, 40);
        vm.SelectedIndex = 1;

        vm.Update(new[] { Row("ET0001", start: 10) });

        Assert.Equal(-1, vm.SelectedIndex);
        Assert.Null(vm.SelectedRow);
    }

    // ── End-to-end against the real engine ───────────────────────────────────────────────────

    /// <summary>The whole point of S4: what the panel shows IS what the pure-Core engine produced — same
    /// findings, same count, same order. Runs the real <see cref="DiagnosticsEngine"/> over a real
    /// <see cref="SemanticModel"/> and projects its output the way the view layer does (the line/column
    /// lookup is the view's job and is stubbed here).</summary>
    [Fact]
    public void Panel_ShowsExactlyWhatTheEngineProduced()
    {
        // Two undeclared locals in a routine body — local scope, so no metadata/connection needed.
        const string sql = "create procedure loc returns (a integer, b integer) as begin a = :undeclared_one; b = :undeclared_two; end";
        var diagnostics = DiagnosticsEngine.Analyze(SemanticModel.Build(sql));
        Assert.Equal(2, diagnostics.Count); // guards the fixture, not the panel

        var vm = new DiagnosticsPanelViewModel();
        vm.Update(diagnostics.Select(d => new DiagnosticRowViewModel(d, line: 1, column: d.Start + 1)).ToList());

        // ⭐ The panel's text must be the engine's finding RESOLVED — comparing the row's rendered string with
        // Loc.Format of the engine's own message keeps this a projection check rather than a text pin.
        Assert.Equal(
            diagnostics.Select(d => (d.Code, Loc.Format(d.Message), d.Severity)),
            vm.Diagnostics.Select(r => (r.Code, r.Message, r.Severity)));
    }

    /// <summary>
    /// S5 navigation scans the panel's OWN order instead of sorting again — that is what makes the list and
    /// the caret agree by construction rather than by two implementations happening to match. It is only
    /// correct because <see cref="DiagnosticsEngine"/> emits findings ascending by <c>Start</c>, so pin that
    /// contract against the real engine: if it ever changed, next/previous would silently misbehave.
    /// </summary>
    [Fact]
    public void EngineOrder_IsAscendingByStart_TheContractNavigationRelies_On()
    {
        const string sql = "create procedure loc returns (a integer, b integer) as begin a = :undeclared_one; b = :undeclared_two; end";

        var diagnostics = DiagnosticsEngine.Analyze(SemanticModel.Build(sql));

        Assert.True(diagnostics.Count >= 2);
        for (int i = 1; i < diagnostics.Count; i++)
        {
            Assert.True(
                diagnostics[i - 1].Start <= diagnostics[i].Start,
                $"engine order is not ascending by Start at index {i}");
        }
    }

    /// <summary>End-to-end: walking the REAL engine's findings with F8 visits them left-to-right through
    /// the document, which is what a user pressing it repeatedly expects.</summary>
    [Fact]
    public void Navigation_OverRealEngineOutput_WalksTheDocumentInOrder()
    {
        const string sql = "create procedure loc returns (a integer, b integer) as begin a = :undeclared_one; b = :undeclared_two; end";
        var diagnostics = DiagnosticsEngine.Analyze(SemanticModel.Build(sql));
        var vm = new DiagnosticsPanelViewModel();
        vm.Update(diagnostics.Select(d => new DiagnosticRowViewModel(d, line: 1, column: d.Start + 1)).ToList());

        int first = vm.IndexAfter(0);
        int second = vm.IndexAfter(vm.Diagnostics[first].Diagnostic.Start);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
        Assert.True(vm.Diagnostics[first].Diagnostic.Start < vm.Diagnostics[second].Diagnostic.Start);
    }
}

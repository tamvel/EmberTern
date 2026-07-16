using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using EmberTern.App;
using EmberTern.App.ViewModels;
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
    private static DiagnosticRowViewModel Row(
        string code, DiagnosticSeverity severity = DiagnosticSeverity.Warning,
        int start = 0, int length = 4, string message = "msg", int line = 1, int column = 1)
        => new(new Diagnostic(start, length, severity, message, code), line, column);

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
        var diagnostic = new Diagnostic(42, 7, DiagnosticSeverity.Error, "count mismatch", "ET0006");

        var row = new DiagnosticRowViewModel(diagnostic, line: 12, column: 5);

        Assert.Equal("ET0006", row.Code);
        Assert.Equal("count mismatch", row.Message);
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

    // ── End-to-end against the real engine ───────────────────────────────────────────────────

    /// <summary>The whole point of S4: what the panel shows IS what the pure-Core engine produced — same
    /// findings, same count, same order. Runs the real <see cref="DiagnosticsEngine"/> over a real
    /// <see cref="SemanticModel"/> and projects its output the way the view layer does (the line/column
    /// lookup is the view's job and is stubbed here).</summary>
    [Fact]
    public void Panel_ShowsExactlyWhatTheEngineProduced()
    {
        // Two undeclared locals in a routine body — local scope, so no metadata/connection needed.
        const string sql = "execute block as begin a = :undeclared_one; b = :undeclared_two; end";
        var diagnostics = DiagnosticsEngine.Analyze(SemanticModel.Build(sql));
        Assert.Equal(2, diagnostics.Count); // guards the fixture, not the panel

        var vm = new DiagnosticsPanelViewModel();
        vm.Update(diagnostics.Select(d => new DiagnosticRowViewModel(d, line: 1, column: d.Start + 1)).ToList());

        Assert.Equal(
            diagnostics.Select(d => (d.Code, d.Message, d.Severity)),
            vm.Diagnostics.Select(r => (r.Code, r.Message, r.Severity)));
    }
}

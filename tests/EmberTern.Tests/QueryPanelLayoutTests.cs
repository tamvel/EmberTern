using System;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The saved-queries panel's column — specifically, <b>that hiding the panel actually gives its width back</b>.
///
/// <para>⚠⚠ This file exists for a defect the PL QA round produced and the codebase had already solved once.
/// The panel was made RESIZABLE, which means moving it from an <c>Auto</c> column (collapses for free when its
/// only child hides) into a bounded PIXEL column driven by a <c>GridSplitter</c>. The collapse was then written
/// as <c>Width = 0</c> — and a <c>ColumnDefinition</c> clamps its width to <c>[MinWidth, MaxWidth]</c>, so
/// <c>MinWidth="160"</c> kept 160 px reserved on every tab that hides the panel. The Border was correctly
/// invisible, so what the user saw was the editor grid's own background: an empty dark strip.</para>
///
/// <para>⭐ <c>MainWindow.CollapseSidebar</c> has carried the answer since V1, in a comment that describes this
/// exact symptom ("leaving an empty ~280px gap"): force <b>Min and Max to 0</b> so 0 becomes the only legal
/// width. Copying the sidebar's LAYOUT without its COLLAPSE reproduced a solved defect one panel over.</para>
///
/// <para>⭐ The first test measures the <b>mechanism</b> with the engine the product lays out with, rather than
/// transcribing the rule (#333/#336): it asks a real <see cref="Grid"/> what a bounded column does. If Avalonia
/// ever stopped clamping, the guard would go red and say the fix is no longer needed — which is the honest
/// outcome, not a silent pass.</para>
///
/// <para>⚠ No <c>MainWindow</c> is constructed anywhere here: that is the documented suite-hanging shape, and a
/// bare grid can carry the whole claim.</para>
/// </summary>
public sealed class QueryPanelLayoutTests
{
    private const double Available = 1000;

    /// <summary>Lays out three columns shaped like the editor area — content (*), splitter (Auto), panel
    /// (pixel, bounded) — and reports the panel column's realized width.</summary>
    private static (ColumnDefinition Panel, Func<double> Width) BuildEditorAreaGrid(
        double declaredWidth, double minWidth, double maxWidth)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var panel = new ColumnDefinition(new GridLength(declaredWidth, GridUnitType.Pixel))
        {
            MinWidth = minWidth,
            MaxWidth = maxWidth,
        };
        grid.ColumnDefinitions.Add(panel);

        double Measure()
        {
            grid.Measure(new Size(Available, 400));
            grid.Arrange(new Rect(0, 0, Available, 400));
            return panel.ActualWidth;
        }

        return (panel, Measure);
    }

    /// <summary>
    /// ⭐⭐ <b>The premise the fix rests on, measured rather than asserted:</b> a bounded column does not honour
    /// <c>Width = 0</c>. This is the defect, reproduced in isolation.
    /// </summary>
    [Fact]
    public void ABoundedColumn_IgnoresWidthZero_AndKeepsItsMinimum()
    {
        var (panel, width) = BuildEditorAreaGrid(declaredWidth: 200, minWidth: 160, maxWidth: 600);
        Assert.Equal(200, width(), 3);

        panel.Width = new GridLength(0, GridUnitType.Pixel);

        Assert.Equal(160, width(), 3);
    }

    /// <summary>The fix: zeroing the bounds first makes 0 the only legal width, so the column really goes away
    /// — and lifting them again restores the user's width, not the default.</summary>
    [Fact]
    public void ZeroingTheBoundsFirst_CollapsesTheColumn_AndLiftingThemRestoresIt()
    {
        var (panel, width) = BuildEditorAreaGrid(declaredWidth: 200, minWidth: 160, maxWidth: 600);

        // The user drags the splitter wider, then switches to a tab that hides the panel.
        panel.Width = new GridLength(320, GridUnitType.Pixel);
        Assert.Equal(320, width(), 3);
        var remembered = panel.Width.Value;

        panel.MinWidth = 0;
        panel.MaxWidth = 0;
        panel.Width = new GridLength(0, GridUnitType.Pixel);
        Assert.Equal(0, width(), 3);

        // Back to a Query tab.
        panel.MaxWidth = 600;
        panel.MinWidth = 160;
        panel.Width = new GridLength(remembered, GridUnitType.Pixel);
        Assert.Equal(320, width(), 3);
    }

    /// <summary>
    /// ⚠ The behavioural tests above build their own grid, so they would stay green if the product stopped
    /// using the mechanism. This is the half that reads the shipped code — and it checks BOTH sides, because
    /// either alone is satisfiable by a product that does not work: the markup must still declare the bound
    /// that makes the clamp real, and the collapse must still lift it.
    /// </summary>
    [Fact]
    public void TheProduct_DeclaresABoundedQueryPanelColumn_AndZeroesTheBoundsToCollapseIt()
    {
        var root = RepositoryRoot();
        var markup = File.ReadAllText(Path.Combine(root, "src", "EmberTern.App", "Views", "MainWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "EmberTern.App", "Views", "MainWindow.axaml.cs"));

        // If this ever stops matching, the column is no longer bounded and the tests above describe nothing
        // the product does — say so here rather than letting them pass vacuously.
        Assert.Matches(new Regex(@"<ColumnDefinition\s+Width=""200""\s+MinWidth=""\d+""\s+MaxWidth=""\d+""\s*/>"), markup);

        var body = code[code.IndexOf("private void ApplyQueryPanelColumn", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n    private ", StringComparison.Ordinal)];

        Assert.Contains("MinWidth = 0", body, StringComparison.Ordinal);
        Assert.Contains("MaxWidth = 0", body, StringComparison.Ordinal);
        Assert.Contains("_queryPanelColumn.MaxWidth = _queryPanelMaxWidth", body, StringComparison.Ordinal);
        Assert.Contains("_queryPanelColumn.MinWidth = _queryPanelMinWidth", body, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

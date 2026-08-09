using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ THE ACCEPTANCE CRITERION OF THE 2026-08-07 REQUEST, IN ONE PLACE: <b>a user has the same copying
/// options in every data grid, and only the module-specific operations differ.</b>
///
/// <para>Before that request four of the five data grids had none of them — the operations lived on the SQL
/// Editor grid alone, because that is where they were first needed. Nothing failed when Table Data, View Data
/// and the two result grids shipped without them; each grid simply had a slightly different menu, which is how
/// this kind of divergence arrives (R7: a capability added to one screen is a capability the other screens are
/// now silently missing).</para>
///
/// <para>⚠ It reads the markup rather than constructing anything, so it belongs to the MAIN test partition. It
/// pins the menu's COMPOSITION only — that the items are offered. Whether the right text reaches the clipboard
/// is <see cref="GridCopyTextTests"/>'s question, and the <c>Click</c> handlers are checked by the XAML
/// compiler, which fails the build on a name that does not exist.</para>
/// </summary>
public class DataGridCopyMenuTests
{
    /// <summary>Every grid whose rows are DATA, and the element that carries its context menu.</summary>
    private static readonly (string View, string Grid)[] DataGrids =
    {
        ("MainWindow", "ResultGrid"),                       // SQL Editor results — where the four originated
        ("TableDetailTabView", "DataPreviewGrid"),
        ("ViewDetailTabView", "DataPreviewGrid"),
        ("ProcedureDetailTabView", "ProcResultGrid"),
        ("FunctionDetailTabView", "FuncExecResultGrid"),
    };

    private static readonly string[] CopyItems =
    {
        "{app:Loc GridCopyCell}",
        "{app:Loc GridCopyRow}",            // ⚠ braced, or it also matches GridCopyRowWithHeaders
        "{app:Loc GridCopyRowWithHeaders}",
        "{app:Loc GridCopyAllWithHeaders}",
    };

    [Fact]
    public void EveryDataGrid_OffersTheSameFourCopyOperations()
    {
        var missing = new List<string>();
        foreach (var (view, grid) in DataGrids)
        {
            var menu = ContextMenuOf(view, grid);
            foreach (var item in CopyItems)
            {
                if (!menu.Contains(item, StringComparison.Ordinal))
                {
                    missing.Add($"{view}.{grid} → {item.TrimEnd('}')}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "These data grids do not offer the shared copy operations, so copying works differently depending "
            + "on which grid the user is looking at — the divergence the 2026-08-07 request closed: "
            + string.Join(", ", missing));
    }

    /// <summary>
    /// ⚠ The two filter verbs and Export are the rest of the shared set — pinned in the same sweep, because a
    /// new data grid is exactly as likely to be built without them, and "the same options everywhere" is not a
    /// statement about copying alone.
    /// </summary>
    [Fact]
    public void EveryDataGrid_AlsoOffersTheSharedFilterAndExportVerbs()
    {
        var missing = new List<string>();
        foreach (var (view, grid) in DataGrids)
        {
            var menu = ContextMenuOf(view, grid);
            foreach (var item in new[]
                     {
                         "{app:Loc FilterByValue}", "{app:Loc FilterExcludeValue}",
                         "{app:Loc FilterContainsValue}", "{app:Loc ExportResultsMenuItem}",
                     })
            {
                if (!menu.Contains(item, StringComparison.Ordinal)) missing.Add($"{view}.{grid} → {item}");
            }
        }

        Assert.True(missing.Count == 0, "Missing shared grid verbs: " + string.Join(", ", missing));
    }

    /// <summary>
    /// ⭐ …and the module-specific half of the rule, stated positively so it cannot be read as an oversight:
    /// <b>Copy as INSERT / UPDATE belongs only where a single table backs the rows.</b> A procedure's or
    /// function's result set and a view's rows have no such provenance, so those grids do not offer it — the
    /// same reason the shared <c>SqlCopy</c> path reports <c>NotATable</c> for them.
    /// </summary>
    [Fact]
    public void CopyAsSql_IsOfferedOnlyWhereATableBacksTheRows()
    {
        var expected = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["MainWindow.ResultGrid"] = true,               // provenance resolved from the executed statement
            ["TableDetailTabView.DataPreviewGrid"] = true,  // the table IS the tab
            ["ViewDetailTabView.DataPreviewGrid"] = false,
            ["ProcedureDetailTabView.ProcResultGrid"] = false,
            ["FunctionDetailTabView.FuncExecResultGrid"] = false,
        };

        foreach (var (view, grid) in DataGrids)
        {
            var menu = ContextMenuOf(view, grid);
            var offers = menu.Contains("{app:Loc GridCopyAsInsert}", StringComparison.Ordinal);
            Assert.Equal(expected[$"{view}.{grid}"], offers);
        }
    }

    /// <summary>The markup of the named grid's own context menu.</summary>
    private static string ContextMenuOf(string view, string grid)
    {
        var markup = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Views", view + ".axaml"));

        var named = markup.IndexOf($"x:Name=\"{grid}\"", StringComparison.Ordinal);
        Assert.True(named >= 0, $"{view} has no DataGrid named {grid} — the list in this test has gone stale.");

        // A DataGrid's own ContextMenu is a direct child, so the first one after its x:Name is that grid's.
        var open = markup.IndexOf("<DataGrid.ContextMenu>", named, StringComparison.Ordinal);
        var close = open >= 0 ? markup.IndexOf("</DataGrid.ContextMenu>", open, StringComparison.Ordinal) : -1;
        Assert.True(open >= 0 && close > open, $"{view}.{grid} declares no context menu.");

        return markup[open..close];
    }

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
}

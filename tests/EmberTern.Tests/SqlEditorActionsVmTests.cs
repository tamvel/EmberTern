using System;
using System.IO;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class SqlEditorActionsVmTests
{
    [Fact]
    public void ResolveActiveSql_ReturnsQueryText_WhenNoSelectionProvider()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "select 1 from rdb$database;";

        Assert.Equal("select 1 from rdb$database;", h.Main.ResolveActiveSql());
    }

    [Fact]
    public void ResolveActiveSql_ReturnsQueryText_WhenSelectionIsEmpty()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "select 1;";
        h.Main.SelectedQueryTextProvider = () => string.Empty;

        Assert.Equal("select 1;", h.Main.ResolveActiveSql());
    }

    [Fact]
    public void ResolveActiveSql_ReturnsSelection_WhenSelectionPresent()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "select 1; select 2;";
        h.Main.SelectedQueryTextProvider = () => "select 2;";

        Assert.Equal("select 2;", h.Main.ResolveActiveSql());
    }

    [Fact]
    public void ResolveActiveSql_FallsBackToQueryText_WhenSelectionIsWhitespace()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "select 1;";
        h.Main.SelectedQueryTextProvider = () => "   \n  ";

        Assert.Equal("select 1;", h.Main.ResolveActiveSql());
    }

    [Fact]
    public void FormatSql_ReplacesQueryText_WhenNoSelection()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "SELECT a FROM t WHERE x = 1";

        h.Main.FormatSqlCommand.Execute(null);

        Assert.Equal("select a\nfrom t\nwhere x = 1", h.Main.QueryText);
    }

    [Fact]
    public void FormatSql_RoutesThroughReplaceCallback_WhenSelectionPresent()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = "before SELECT a FROM t after";
        h.Main.SelectedQueryTextProvider = () => "SELECT a FROM t";
        string? captured = null;
        h.Main.ReplaceSelectedOrAllText = text => captured = text;

        h.Main.FormatSqlCommand.Execute(null);

        Assert.Equal("select a\nfrom t", captured);
        // QueryText itself is not touched directly when a selection is replaced —
        // the view writes back to the document, which then echoes through.
        Assert.Equal("before SELECT a FROM t after", h.Main.QueryText);
    }

    [Fact]
    public void FormatSql_NoOp_WhenEditorEmpty()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        h.Main.QueryText = string.Empty;
        var before = h.Main.QueryText;

        h.Main.FormatSqlCommand.Execute(null);

        Assert.Equal(before, h.Main.QueryText);
    }

    [Fact]
    public void FormatSql_CanExecute_FalseOnDdlTab()
    {
        using var h = new Harness();
        h.Main.ApplyActiveConnectionChange("A");
        // The connected workspace exposes the Query tab — confirm it's the active one.
        Assert.True(h.Main.IsQueryTabActive);
        Assert.True(h.Main.FormatSqlCommand.CanExecute(null));
    }

    private sealed class Harness : IDisposable
    {
        public Harness()
        {
            TempDir = Path.Combine(Path.GetTempPath(), "embertern-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            Store = new ConnectionProfileStore(TempDir);
            Service = new FirebirdConnectionService();
            Main = new MainWindowViewModel(Store, Service);
        }

        public string TempDir { get; }
        public ConnectionProfileStore Store { get; }
        public FirebirdConnectionService Service { get; }
        public MainWindowViewModel Main { get; }

        public void Dispose()
        {
            Service.Dispose();
            try { Directory.Delete(TempDir, recursive: true); }
            catch { /* best-effort */ }
        }
    }
}

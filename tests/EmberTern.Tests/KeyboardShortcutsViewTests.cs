using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmberTern.App.Commands;
using EmberTern.App.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace EmberTern.Tests;

/// <summary>
/// The Keyboard Shortcuts window is a <b>projection</b> of <see cref="CommandCatalog"/>, and these tests are
/// what make that a guarantee: they check the registry carries the one thing the window needs (a canonical
/// name), that the name comes from <c>UiStrings</c> rather than from the table, and that the order the user was
/// promised is the order the view model produces.
/// </summary>
public sealed class KeyboardShortcutsViewTests
{
    private readonly ITestOutputHelper _out;

    public KeyboardShortcutsViewTests(ITestOutputHelper output) => _out = output;

    // ⭐ Without this, a command added to the catalog later appears in the window as a blank row — the exact
    // silent-omission shape the icon-consistency test was written for in the previous sprint.
    [Fact]
    public void EveryCommandHasACanonicalTitle()
    {
        var untitled = CommandCatalog.All
            .Where(d => string.IsNullOrWhiteSpace(d.Title))
            .Select(d => d.Id.ToString())
            .ToArray();

        Assert.True(untitled.Length == 0, "these commands have no Title: " + string.Join(", ", untitled));

        // Titles are names, not sentences, and two commands sharing one would be indistinguishable in a list.
        var duplicates = CommandCatalog.All
            .GroupBy(d => d.Title, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} → {string.Join(" / ", g.Select(d => d.Id))}")
            .ToArray();

        Assert.True(duplicates.Length == 0, "these titles are not unique: " + string.Join(", ", duplicates));
    }

    // ⛔ The user's condition for accepting Title: the words stay in UiStrings, and no string is typed into
    // CommandCatalog. Scoped to the descriptor TABLE — the file's other methods legitimately hold format
    // strings for the collision report.
    [Fact]
    public void TheDescriptorTableContainsNoStringLiterals()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Commands", "CommandCatalog.cs"));

        var table = Regex.Match(source, @"AllDescriptors\s*=\s*\[(.*?)\n    \];", RegexOptions.Singleline);
        Assert.True(table.Success, "could not locate the AllDescriptors table");

        // Comment lines are excluded: the table is heavily annotated, and prose quoting a gesture or a name is
        // documentation, not a value the app reads.
        var offenders = table.Groups[1].Value
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal) && l.Contains('"', StringComparison.Ordinal))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "CommandCatalog's table must name no strings of its own — the words live in UiStrings. Found: "
            + string.Join(" | ", offenders));
    }

    // ⚠ The display order is NOT CommandScope's numeric order (that encodes the router's resolution
    // precedence). A scope added later must be given a rank deliberately, so this fails rather than letting it
    // sort to the bottom unnoticed.
    [Fact]
    public void EveryScopeHasADeclaredDisplayRank()
    {
        var ranked = KeyboardShortcutsViewModel.DisplayScopeOrder;

        Assert.Equal(Enum.GetValues<CommandScope>().Length, ranked.Count);
        Assert.All(Enum.GetValues<CommandScope>(), scope => Assert.Contains(scope, ranked));

        // The order the user ratified, spelled out so a well-meant "sort by the enum" reads as a change.
        Assert.Equal(
            new[] { CommandScope.Global, CommandScope.Tab, CommandScope.Tree, CommandScope.Grid, CommandScope.Editor },
            ranked);
    }

    [Fact]
    public void RowsAreOrderedByScopeThenName()
    {
        var vm = new KeyboardShortcutsViewModel();
        var ranks = KeyboardShortcutsViewModel.DisplayScopeOrder.Select(s => s.ToString()).ToList();

        _out.WriteLine($"{vm.Rows.Count} rows; first five: "
                       + string.Join(" | ", vm.Rows.Take(5).Select(r => $"{r.Scope}/{r.Command}/{r.Shortcut}")));

        // Scopes appear in the canonical order, and never interleave.
        var scopeSequence = vm.Rows.Select(r => ranks.IndexOf(r.Scope)).ToArray();
        Assert.Equal(scopeSequence.OrderBy(x => x), scopeSequence);

        // Within one scope, alphabetical by command name.
        foreach (var group in vm.Rows.GroupBy(r => r.Scope))
        {
            var names = group.Select(r => r.Command).ToArray();
            Assert.Equal(names.OrderBy(n => n, StringComparer.CurrentCulture), names);
        }
    }

    [Fact]
    public void OnlyCommandsWithAGestureAreListed()
    {
        var vm = new KeyboardShortcutsViewModel();

        Assert.Equal(CommandCatalog.All.Count(d => d.HasGesture), vm.Rows.Count);
        Assert.All(vm.Rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Shortcut)));

        // Reserved commands ARE listed: "dispatched by the control that owns it" is not "internal", and these
        // are exactly the keys a user comes here to look up.
        Assert.Contains(vm.Rows, r => r.Command == EmberTern.App.UiStrings.CommandTitleDebuggerStepOver);
        Assert.Contains(vm.Rows, r => r.Command == EmberTern.App.UiStrings.CommandTitleEditorQuickFix);

        // One row per command, not per gesture: an alternate shares its row, so the footer count means what
        // its label says.
        var add = Assert.Single(vm.Rows, r => r.Command == EmberTern.App.UiStrings.CommandTitleCollectionAdd);
        Assert.Contains("F3", add.Shortcut, StringComparison.Ordinal);
        Assert.Contains("Insert", add.Shortcut, StringComparison.Ordinal);

        // ⚠ Rendered by CommandTip, never KeyGesture.ToString() — which would print "Ctrl+OemPeriod".
        var quickFix = Assert.Single(vm.Rows, r => r.Command == EmberTern.App.UiStrings.CommandTitleEditorQuickFix);
        Assert.Equal("Ctrl+.", quickFix.Shortcut);
    }

    [Fact]
    public void SearchMatchesNameShortcutAndScope()
    {
        var vm = new KeyboardShortcutsViewModel();
        int all = vm.Rows.Count;

        // By shortcut text — this is what makes the Shortcut column searchable the way people search it.
        vm.SearchText = "ctrl";
        Assert.NotEmpty(vm.Rows);
        Assert.All(vm.Rows, r => Assert.Contains("Ctrl", r.Shortcut, StringComparison.OrdinalIgnoreCase));

        // By scope.
        vm.SearchText = "editor";
        Assert.NotEmpty(vm.Rows);
        Assert.All(vm.Rows, r => Assert.Equal(CommandScope.Editor.ToString(), r.Scope));

        // By command name, case-insensitively.
        vm.SearchText = "COMPILE";
        Assert.Contains(vm.Rows, r => r.Command == EmberTern.App.UiStrings.CommandTitleCompile);

        // ⚠ Substring matching means one gesture can be a prefix of another: searching "Ctrl+Shift+F" finds
        // Global Search AND Restart debugging (Ctrl+Shift+F5). That is correct for a search box — this test
        // originally asserted a single hit and was wrong, not the code — so the singular-count case uses a
        // gesture that is nobody's prefix.
        vm.SearchText = "Ctrl+Shift+F";
        Assert.Equal(2, vm.Rows.Count);

        // The count follows the filter and is singular when it should be.
        vm.SearchText = "Shift+F6";
        Assert.Single(vm.Rows);
        Assert.Equal(EmberTern.App.UiStrings.CommandTitleRollback, vm.Rows[0].Command);
        Assert.Equal(EmberTern.App.UiStrings.KeyboardShortcutsCountOne, vm.CountText);

        // Nothing matches ⇒ an explained empty state, not an empty grid.
        vm.SearchText = "zzzz-no-such-command";
        Assert.Empty(vm.Rows);
        Assert.False(vm.HasRows);

        // Clearing restores everything, in the canonical order.
        vm.SearchText = string.Empty;
        Assert.Equal(all, vm.Rows.Count);
        Assert.True(vm.HasRows);
        Assert.Contains(all.ToString(System.Globalization.CultureInfo.CurrentCulture), vm.CountText,
            StringComparison.Ordinal);
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

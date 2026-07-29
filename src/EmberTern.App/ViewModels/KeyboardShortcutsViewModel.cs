using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.App.Commands;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One row of the Keyboard Shortcuts window.
///
/// <para>⭐ It is a real typed row rather than a tuple or a pre-formatted string, and that is the whole of the
/// window's readiness for the details pane the design leaves for later (§8.5.5): a pane is a projection of the
/// selected row, so with a typed row it becomes a second grid column bound to the grid's own selection — a
/// column, not a rewrite. No property is added here ahead of a consumer (gotcha #233).</para>
/// </summary>
public sealed class KeyboardShortcutRowViewModel
{
    public KeyboardShortcutRowViewModel(string command, string shortcut, string scope, string context)
    {
        Command = command;
        Shortcut = shortcut;
        Scope = scope;
        Context = context;
    }

    /// <summary>The canonical command name — <c>CommandDescriptor.Title</c>.</summary>
    public string Command { get; }

    /// <summary>The gesture(s), rendered by the app's ONE gesture-to-text composer.</summary>
    public string Shortcut { get; }

    /// <summary>The scope's name, which is also what the default order groups by.</summary>
    public string Scope { get; }

    /// <summary>
    /// Where a Tab-scoped command is live, for the row's tooltip — the tab kinds the catalog declares. Empty
    /// for every other scope, which is why it is a tooltip rather than a column.
    /// </summary>
    public string Context { get; }
}

/// <summary>
/// The Keyboard Shortcuts window's content: a <b>read-only projection of <see cref="CommandCatalog"/></b>.
/// No command name, gesture or scope is written here — names come from <c>Title</c>, gestures from
/// <see cref="CommandTip.Format"/>, scopes from the enum. Browsing only; editing a binding may come one day and
/// would start by making the catalog writable, not by changing this class.
///
/// <para>Only commands that HAVE a gesture appear: it is a shortcuts window, and a command reachable solely
/// from a menu (the Application Menu's own rows) has nothing to show. One row per command, not per gesture, so
/// the footer count means what its label says.</para>
/// </summary>
public sealed partial class KeyboardShortcutsViewModel : ObservableObject
{
    /// <summary>
    /// ⚠ The user-facing order, which is NOT <see cref="CommandScope"/>'s numeric order. Those values encode
    /// the router's resolution precedence (Editor first, Global last) and are load-bearing there; reading them
    /// here would present the list upside down — and ascending order would still swap Tree and Grid. So the
    /// display order is declared, once, right here.
    /// <para>Pinned by a test that every <see cref="CommandScope"/> member has a rank, so adding a scope fails
    /// the suite instead of silently sorting to the bottom.</para>
    /// </summary>
    private static readonly CommandScope[] ScopeOrder =
    [
        CommandScope.Global,
        CommandScope.Tab,
        CommandScope.Tree,
        CommandScope.Grid,
        CommandScope.Editor,
    ];

    private readonly IReadOnlyList<(KeyboardShortcutRowViewModel Row, string Haystack)> _all;

    public KeyboardShortcutsViewModel()
    {
        _all = BuildRows();
        Rows = new ObservableCollection<KeyboardShortcutRowViewModel>(_all.Select(x => x.Row));
    }

    public ObservableCollection<KeyboardShortcutRowViewModel> Rows { get; }

    /// <summary>Live filter over the three displayed fields. Empty shows everything.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    public string CountText => Rows.Count == 1
        ? UiStrings.KeyboardShortcutsCountOne
        : string.Format(CultureInfo.CurrentCulture, UiStrings.KeyboardShortcutsCountFormat, Rows.Count);

    public bool HasRows => Rows.Count > 0;

    partial void OnSearchTextChanged(string value) => ApplyFilter(value);

    private void ApplyFilter(string search)
    {
        var matches = string.IsNullOrWhiteSpace(search)
            ? _all
            : _all.Where(x => x.Haystack.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();

        Rows.Clear();
        foreach (var (row, _) in matches) Rows.Add(row);

        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasRows));
    }

    /// <summary>
    /// The canonical order: scope rank, then command name. Not expressible as a single-column sort, which is
    /// exactly why the grid's own sorting is an overlay on top of this rather than the source of it — and why
    /// clearing that sort returns here for free.
    /// </summary>
    private static IReadOnlyList<(KeyboardShortcutRowViewModel, string)> BuildRows()
        => CommandCatalog.All
            .Where(d => d.HasGesture)
            .OrderBy(d => Array.IndexOf(ScopeOrder, d.Scope))
            .ThenBy(d => d.Title, StringComparer.CurrentCulture)
            .Select(d =>
            {
                var row = new KeyboardShortcutRowViewModel(
                    d.Title, DescribeGestures(d), d.Scope.ToString(), DescribeContext(d));

                // Searching matches what is DISPLAYED, so typing "ctrl" finds every Ctrl binding and "f5"
                // finds F5 — and what you see is what you searched.
                return (row, $"{row.Command}\n{row.Shortcut}\n{row.Scope}");
            })
            .ToArray();

    /// <summary>
    /// Both gestures in one cell, primary first. ⚠ Always through <see cref="CommandTip.Format"/>, never
    /// <c>KeyGesture.ToString()</c> — that spells the raw enum name, so <c>Ctrl+.</c> would read as
    /// "Ctrl+OemPeriod".
    /// </summary>
    private static string DescribeGestures(CommandDescriptor descriptor)
    {
        var gestures = new[] { descriptor.Gesture, descriptor.AlternateGesture }
            .Where(g => g is not null)
            .Select(g => CommandTip.Format(g!));

        return string.Join(", ", gestures);
    }

    private static string DescribeContext(CommandDescriptor descriptor)
        => descriptor.TabKinds is { Count: > 0 } kinds
            ? string.Join(", ", kinds.Select(k => k.ToString()))
            : string.Empty;

    /// <summary>The declared display order, for the test that pins it.</summary>
    internal static IReadOnlyList<CommandScope> DisplayScopeOrder => ScopeOrder;
}

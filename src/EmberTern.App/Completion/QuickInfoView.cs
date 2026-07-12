using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using EmberTern.Core.Sql.Language.QuickInfo;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Completion;

/// <summary>
/// Renders a Core <see cref="QuickInfo"/> into a themed Avalonia control (Etap 6). Shared by the
/// Ctrl-hover tooltip (M4) and — later — the completion detail pane (M5), so the two surfaces read
/// identically. Pure presentation: every colour is a theme token (no hardcoded colours, per the UI
/// rules); the object header reuses the same per-kind palette as the metadata tree + semantic
/// highlighter (<see cref="EditorSemanticColors"/>).
/// <para>
/// Brushes are resolved against the supplied <see cref="ThemeVariant"/> at build time — correct for
/// the transient hover tooltip (rebuilt each hover, so it always matches the current theme). A
/// persistent surface (M5) rebuilds on theme change.
/// </para>
/// </summary>
internal static class QuickInfoView
{
    private const double MaxWidth = 460;

    /// <summary>Builds the quick-info card. <paramref name="maxMembers"/> caps the member list (a
    /// table's columns / a routine's parameters) so the hover tooltip stays compact; the overflow is
    /// summarised. Returns a self-contained, themed <see cref="Border"/>.</summary>
    public static Control Build(QuickInfo info, ThemeVariant theme, int maxMembers = 12)
    {
        var panel = new StackPanel { Spacing = 2 };

        // Header — the identifier + its headline fact, coloured by resolved role.
        panel.Children.Add(new TextBlock
        {
            Text = info.Header,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = MaxWidth,
            Foreground = HeaderBrush(info.Kind, theme) ?? Brush("ForegroundBrush", theme),
        });

        // A subtle kind tag under the header ("Column", "Table", "Input parameter", …).
        var kindLabel = KindLabel(info.Kind);
        if (!string.IsNullOrEmpty(kindLabel))
        {
            panel.Children.Add(Subtle(kindLabel, theme, size: 11));
        }

        if (!string.IsNullOrEmpty(info.Description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = info.Description,
                FontStyle = FontStyle.Italic,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = MaxWidth,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = Brush("SubtleForegroundBrush", theme),
            });
        }

        AddFacts(panel, info.Facts, theme);
        AddMembers(panel, info.Members, theme, maxMembers);

        return new Border
        {
            Child = panel,
            Background = Brush("ElevatedPanelBrush", theme) ?? Brush("BackgroundBrush", theme),
            BorderBrush = Brush("BorderBrush", theme),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 7),
            MaxWidth = MaxWidth + 24,
        };
    }

    // ── Facts (owner, domain, nullability, default, keys, generated, direction, …) ──────────────

    private static void AddFacts(StackPanel panel, IReadOnlyList<QuickInfoFact> facts, ThemeVariant theme)
    {
        if (facts.Count == 0) return;
        var subtle = Brush("SubtleForegroundBrush", theme);
        var fg = Brush("ForegroundBrush", theme);
        var box = new StackPanel { Spacing = 1, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var fact in facts)
        {
            var line = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = MaxWidth };
            line.Inlines!.Add(new Run(fact.Label + "  ") { Foreground = subtle });
            line.Inlines!.Add(new Run(fact.Value) { Foreground = fg });
            box.Children.Add(line);
        }
        panel.Children.Add(box);
    }

    // ── Members (a table/view's columns, a routine's parameters/returns) ────────────────────────

    private static void AddMembers(
        StackPanel panel, IReadOnlyList<QuickInfoMember> members, ThemeVariant theme, int maxMembers)
    {
        if (members.Count == 0) return;

        var box = new StackPanel { Spacing = 1, Margin = new Thickness(0, 5, 0, 0) };
        int shown = 0;
        foreach (var group in new[] { QuickInfoMemberGroup.Column, QuickInfoMemberGroup.Parameter, QuickInfoMemberGroup.Returns })
        {
            bool headerAdded = false;
            foreach (var m in members)
            {
                if (m.Group != group) continue;
                if (shown >= maxMembers) break;
                if (!headerAdded)
                {
                    box.Children.Add(Subtle(GroupLabel(group), theme, size: 11, top: box.Children.Count == 0 ? 0 : 4));
                    headerAdded = true;
                }
                box.Children.Add(new TextBlock
                {
                    Text = m.Text,
                    FontSize = 12,
                    Margin = new Thickness(8, 0, 0, 0),
                    TextWrapping = TextWrapping.NoWrap,
                    Foreground = Brush("ForegroundBrush", theme),
                });
                shown++;
            }
        }

        int remaining = members.Count - shown;
        if (remaining > 0)
        {
            box.Children.Add(Subtle($"… and {remaining} more", theme, size: 11, top: 2));
        }

        panel.Children.Add(box);
    }

    // ── Brush + label helpers ────────────────────────────────────────────────────────────────────

    private static TextBlock Subtle(string text, ThemeVariant theme, double size, double top = 0) => new()
    {
        Text = text,
        FontSize = size,
        Margin = new Thickness(0, top, 0, 0),
        Foreground = Brush("SubtleForegroundBrush", theme),
    };

    // The header colour ties the card to the semantic highlighting: objects reuse the tree palette,
    // columns the calm column brush, locals the low-chroma local brush; anything else the default FG.
    private static IBrush? HeaderBrush(SymbolKind kind, ThemeVariant theme)
    {
        var objectKey = EditorSemanticColors.ObjectBrushKey(kind);
        if (objectKey is not null) return Brush(objectKey, theme);
        return kind switch
        {
            SymbolKind.Column => Brush("EditorColumnBrush", theme),
            SymbolKind.TableReference or SymbolKind.Variable or SymbolKind.Parameter
                or SymbolKind.Cte or SymbolKind.Cursor or SymbolKind.RecordAlias => Brush("EditorLocalBrush", theme),
            _ => null,
        };
    }

    private static IBrush? Brush(string key, ThemeVariant theme)
    {
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b)
        {
            return b;
        }
        return null;
    }

    private static string GroupLabel(QuickInfoMemberGroup group) => group switch
    {
        QuickInfoMemberGroup.Column => "Columns",
        QuickInfoMemberGroup.Parameter => "Parameters",
        QuickInfoMemberGroup.Returns => "Returns",
        _ => string.Empty,
    };

    private static string KindLabel(SymbolKind kind) => kind switch
    {
        SymbolKind.Table => "Table",
        SymbolKind.View => "View",
        SymbolKind.SystemTable => "System table",
        SymbolKind.Procedure => "Procedure",
        SymbolKind.Function => "Function",
        SymbolKind.Trigger => "Trigger",
        SymbolKind.Domain => "Domain",
        SymbolKind.Exception => "Exception",
        SymbolKind.Sequence => "Generator",
        SymbolKind.Role => "Role",
        SymbolKind.Package => "Package",
        SymbolKind.Index => "Index",
        SymbolKind.Column => "Column",
        SymbolKind.TableReference => "Table reference",
        SymbolKind.Variable => "Variable",
        SymbolKind.Parameter => "Parameter",
        SymbolKind.Cte => "Common table expression",
        SymbolKind.Cursor => "Cursor",
        SymbolKind.RecordAlias => "Record alias",
        _ => string.Empty,
    };
}

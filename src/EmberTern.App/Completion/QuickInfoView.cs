using System.Globalization;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using EmberTern.App.Localization;
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
    /// table's columns / a routine's parameters) so the tooltip stays compact; the overflow is
    /// summarised. Returns a self-contained, themed <see cref="Border"/>.</summary>
    public static Control Build(QuickInfo info, ThemeVariant theme, int maxMembers = 12)
        => Card(BuildContent(info, theme, maxMembers), theme);

    /// <summary>
    /// The quick-info <b>sections only</b>, with no card chrome — so a composite surface can place them
    /// alongside other sections inside ONE card. This is the semantic section of the unified hover
    /// (<see cref="HoverInfoView"/>); <see cref="Build"/> is the same content in its own card, for the
    /// standalone Ctrl+Space Quick Info popup. One content builder, two hosts — never two renderers that
    /// have to be kept looking alike.
    /// </summary>
    public static Control BuildContent(QuickInfo info, ThemeVariant theme, int maxMembers = 12)
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
        return panel;
    }

    /// <summary>The shared editor-popup card chrome. Used by both the standalone Quick Info popup and
    /// the unified hover, so the two read as the same surface — one set of tokens, one place to change
    /// them.</summary>
    public static Control Card(Control content, ThemeVariant theme) => new Border
    {
        Child = content,
        Background = Brush("SurfaceRaisedBrush", theme) ?? Brush("BackgroundBrush", theme),
        BorderBrush = Brush("BorderBrush", theme),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(10, 7),
        MaxWidth = MaxWidth + 24,
    };

    /// <summary>The card's content width budget — shared so a peer section (the unified hover's
    /// diagnostics) wraps at the same measure as the quick-info sections.</summary>
    public static double ContentMaxWidth => MaxWidth;

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
            // ⚠⚠ `Loc.Text(fact.Label)`, never `fact.Label` — the label is a MessageKey (D‑3), and a
            // MessageKey in a string concatenation compiles happily via ToString() and puts the raw KEY on
            // screen. That is how this line looked when the type changed, and nothing failed to build.
            line.Inlines!.Add(new Run(Loc.Text(fact.Label) + "  ") { Foreground = subtle });
            // ⚠ The VALUE is deliberately NOT resolved: it is Firebird's vocabulary (NOT NULL, PRIMARY KEY,
            // BEFORE INSERT), a domain, a type or a count — it must match the DDL the card describes.
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
            box.Children.Add(Subtle(string.Format(CultureInfo.CurrentCulture, UiStrings.QuickInfoMoreFormat, remaining), theme, size: 11, top: 2));
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
                or SymbolKind.Cte or SymbolKind.Cursor => Brush("EditorLocalBrush", theme),
            // Trigger context variables (NEW/OLD) share the editor's context-variable colour.
            SymbolKind.RecordAlias or SymbolKind.TriggerPredicate => Brush("EditorContextVariableBrush", theme),
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
        QuickInfoMemberGroup.Column => UiStrings.QuickInfoGroupColumns,
        QuickInfoMemberGroup.Parameter => UiStrings.QuickInfoGroupParameters,
        QuickInfoMemberGroup.Returns => UiStrings.QuickInfoGroupReturns,
        _ => string.Empty,
    };

    private static string KindLabel(SymbolKind kind) => kind switch
    {
        SymbolKind.Table => UiStrings.ObjectKindTable,
        SymbolKind.View => UiStrings.ObjectKindView,
        SymbolKind.SystemTable => UiStrings.ObjectKindSystemTable,
        SymbolKind.Procedure => UiStrings.ObjectKindProcedure,
        SymbolKind.Function => UiStrings.ObjectKindFunction,
        SymbolKind.Trigger => UiStrings.ObjectKindTrigger,
        SymbolKind.Domain => UiStrings.ObjectKindDomain,
        SymbolKind.Exception => UiStrings.ObjectKindException,
        SymbolKind.Sequence => UiStrings.ObjectKindGenerator,
        SymbolKind.Role => UiStrings.ObjectKindRole,
        SymbolKind.Package => UiStrings.ObjectKindPackage,
        SymbolKind.Index => UiStrings.ObjectKindIndex,
        SymbolKind.Column => UiStrings.ObjectKindColumn,
        SymbolKind.TableReference => UiStrings.ObjectKindTableReference,
        SymbolKind.Variable => UiStrings.ObjectKindVariable,
        SymbolKind.Parameter => UiStrings.ObjectKindParameter,
        SymbolKind.Cte => UiStrings.ObjectKindCte,
        SymbolKind.Cursor => UiStrings.ObjectKindCursor,
        SymbolKind.RecordAlias => UiStrings.ObjectKindRecordAlias,
        _ => string.Empty,
    };
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Hover;

namespace EmberTern.App.Completion;

/// <summary>
/// Renders a Core <see cref="HoverInfo"/> into ONE themed card — the unified hover surface
/// (post-Stage-7, design <c>editor-stage7-diagnostics.md</c> §15). Never two stacked popups: the
/// diagnostics and the semantic Quick Info are <b>sections of a single card</b>.
/// <para>
/// <b>Section order is the contract</b> (the reason no <c>IHoverProvider</c> abstraction exists):
/// <b>diagnostics first</b> — the reason the user hovered a squiggle is the error; the semantic info is
/// supporting context underneath it. When Quick Fixes land they become a third section here.
/// </para>
/// <para>
/// Pure presentation. It composes the EXISTING renderers rather than re-drawing them: the semantic
/// section is <see cref="QuickInfoView.BuildContent"/> and the chrome is <see cref="QuickInfoView.Card"/>,
/// so the unified hover and the standalone Ctrl+Space Quick Info popup cannot drift apart. Severity
/// colours are the SAME theme tokens the squiggle renderer paints and the Diagnostics panel lists with
/// (<c>ErrorBrush</c> / <c>WarningBrush</c> / <c>SubtleForegroundBrush</c>), so an underline, a panel row
/// and this card always agree — no hardcoded colours (UI styling rules).
/// </para>
/// </summary>
internal static class HoverInfoView
{
    /// <summary>Builds the hover card for <paramref name="hover"/>.</summary>
    public static Control Build(HoverInfo hover, ThemeVariant theme)
    {
        var panel = new StackPanel { Spacing = 0 };

        if (hover.HasDiagnostics)
        {
            panel.Children.Add(BuildDiagnostics(hover, theme));
        }

        if (hover.Info is { } info)
        {
            // A divider only when both sections are present — the common squiggle case has no Quick Info
            // at all (an unknown object's reference is unresolved), and a lone section needs no rule.
            if (hover.HasDiagnostics)
            {
                panel.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 7, 0, 6),
                    Background = Brush("BorderBrush", theme),
                });
            }
            panel.Children.Add(QuickInfoView.BuildContent(info, theme));
        }

        return QuickInfoView.Card(panel, theme);
    }

    // One line per finding: a severity-coloured code, then the message. Kept deliberately plainer than
    // the Diagnostics panel's row — the panel is a browsable list (it needs a location column and an
    // icon), whereas here the location IS the thing under the pointer.
    private static Control BuildDiagnostics(HoverInfo hover, ThemeVariant theme)
    {
        var box = new StackPanel { Spacing = 3 };
        foreach (var d in hover.Diagnostics)
        {
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 7 };
            row.Children.Add(new TextBlock
            {
                Text = d.Code,
                FontSize = 11,
                FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
                Margin = new Thickness(0, 1, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Foreground = SeverityBrush(d.Severity, theme),
            });
            row.Children.Add(new TextBlock
            {
                Text = d.Message,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = QuickInfoView.ContentMaxWidth,
                Foreground = Brush("ForegroundBrush", theme),
            });
            box.Children.Add(row);
        }
        return box;
    }

    /// <summary>Severity → theme brush. Deliberately the SAME mapping the squiggle renderer paints with
    /// and <c>DiagnosticRowViewModel.SeverityBrushKey</c> projects, so a squiggle, a panel row and this
    /// card never disagree about how serious a finding is.</summary>
    private static IBrush? SeverityBrush(DiagnosticSeverity severity, ThemeVariant theme) => severity switch
    {
        DiagnosticSeverity.Error => Brush("ErrorBrush", theme),
        DiagnosticSeverity.Warning => Brush("WarningBrush", theme),
        _ => Brush("SubtleForegroundBrush", theme),
    };

    private static IBrush? Brush(string key, ThemeVariant theme)
    {
        if (Application.Current?.Resources.TryGetResource(key, theme, out var v) == true && v is IBrush b)
        {
            return b;
        }
        return null;
    }
}

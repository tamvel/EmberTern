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
    /// <param name="showQuickFixHint">Whether to close with a one-line note that fixes exist here.
    /// An <b>input</b>, decided by the caller — the card neither computes fixes nor offers them. Hover
    /// stays INFORMATION (§15.1.1): the note names the shortcut, and the light bulb and Ctrl+. remain the
    /// only ways to run a code action.</param>
    public static Control Build(HoverInfo hover, ThemeVariant theme, bool showQuickFixHint = false)
    {
        var panel = new StackPanel { Spacing = 0 };

        // Data tip first (spec §9.4): in a paused debugger the live value is the reason you hovered.
        if (hover.DebugValue is { } dv)
        {
            panel.Children.Add(BuildDebugValue(dv, theme));
        }

        if (hover.HasDiagnostics)
        {
            if (hover.DebugValue is not null) panel.Children.Add(Divider(theme));
            panel.Children.Add(BuildDiagnostics(hover, theme));
        }

        if (hover.Info is { } info)
        {
            // A divider only when both sections are present — the common squiggle case has no Quick Info
            // at all (an unknown object's reference is unresolved), and a lone section needs no rule.
            if (hover.HasDiagnostics || hover.DebugValue is not null) panel.Children.Add(Divider(theme));
            panel.Children.Add(QuickInfoView.BuildContent(info, theme));
        }

        // Discoverability, last and quiet: a user who never presses Ctrl+. would otherwise have no way to
        // learn the shortcut exists. Subtle + italic so it reads as a footnote to the explanation above,
        // not as another finding.
        if (showQuickFixHint)
        {
            panel.Children.Add(new TextBlock
            {
                Text = UiStrings.CodeActionsHoverHint,
                Margin = new Thickness(0, 6, 0, 0),
                FontSize = 11,
                FontStyle = FontStyle.Italic,
                Foreground = Brush("SubtleForegroundBrush", theme),
            });
        }

        return QuickInfoView.Card(panel, theme);
    }

    private static Border Divider(ThemeVariant theme) => new()
    {
        Height = 1,
        Margin = new Thickness(0, 7, 0, 6),
        Background = Brush("BorderBrush", theme),
    };

    // The data tip: "NAME = value" — the variable's live value in the paused frame. A null value renders in
    // the subtle foreground (distinct), a real value in the default foreground; the name is accented so it
    // reads as "this identifier".
    private static Control BuildDebugValue(DebugHoverValue dv, ThemeVariant theme)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new TextBlock
        {
            Text = dv.Name,
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
            Foreground = Brush("AccentBrush", theme),
        });
        row.Children.Add(new TextBlock { Text = "=", FontSize = 12, Foreground = Brush("SubtleForegroundBrush", theme) });
        row.Children.Add(new TextBlock
        {
            Text = dv.ValueText,
            FontSize = 12,
            FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = QuickInfoView.ContentMaxWidth,
            FontStyle = dv.IsNull ? FontStyle.Italic : FontStyle.Normal,
            Foreground = Brush(dv.IsNull ? "SubtleForegroundBrush" : "ForegroundBrush", theme),
        });
        return row;
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
                // Resolved here, at the moment of display (D‑3). ⭐ No language hook: the card is dismissed by
                // PointerExited on the TextView AND by any click, and reaching the Language radio needs both —
                // so "the language changes while this card is open" is unreachable (measured, etap C5).
                Text = Localization.Loc.Format(d.Message),
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

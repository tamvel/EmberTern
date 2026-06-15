using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace EmberTern.App.Controls;

/// <summary>
/// Renders a single themeable vector icon from the central geometry dictionary
/// (<c>Themes/IconGeometries.axaml</c>). Geometries are Lucide path data (24×24
/// viewBox, stroke-based); the control's ControlTheme strokes the path with its
/// <see cref="TemplatedControl.Foreground"/> inside a fixed 24×24 Viewbox so every
/// icon scales crisply to a uniform box and recolors live on theme toggle.
///
/// Usage: <c>&lt;controls:SvgIcon Data="{StaticResource Icon.Play}"
/// Foreground="{DynamicResource AccentIconBrush}" /&gt;</c>. Never bake a color into
/// the geometry — color flows through a theme token on <c>Foreground</c>.
/// </summary>
public sealed class SvgIcon : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<SvgIcon, Geometry?>(nameof(Data));

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}

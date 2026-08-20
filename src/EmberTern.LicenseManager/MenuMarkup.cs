using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Shapes = Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace EmberTern.LicenseManager;

/// <summary>
/// Puts an icon in a menu row's icon column: <c>Icon="{lm:MenuIcon Icon.Settings}"</c>.
///
/// <para>⭐ <b>A mirror of EmberTern's <c>MenuIconExtension</c>, and the third such mirror in this
/// application</b> — <c>IconGeometryConverter</c> and <c>ThemeToggleIconConverter</c> came the same way,
/// for the same reason. ⛔ EmberTern's own version cannot be reused or linked: it builds an
/// <c>SvgIcon</c>, a control from <c>EmberTern.App.Controls</c>, which this application does not have and
/// must not acquire.</para>
///
/// <para>⭐ The GEOMETRIES are still shared — they come from the linked
/// <c>Themes/IconGeometries.axaml</c> through this application's one
/// <see cref="IconGeometryConverter"/>. ⛔ No icon is drawn for this menu; only the twenty lines that
/// assemble one are mirrored.</para>
/// </summary>
public sealed class MenuIconExtension
{
    /// <summary>Creates the extension.</summary>
    public MenuIconExtension()
    {
    }

    /// <summary>Creates the extension for a geometry key.</summary>
    public MenuIconExtension(string key) => Key = key;

    /// <summary>A geometry key from the linked <c>IconGeometries.axaml</c>, e.g. <c>Icon.Settings</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Builds the icon, or <see langword="null"/> when the key names no geometry.</summary>
    public object? ProvideValue()
    {
        if (IconGeometryConverter.Instance.Convert(
                Key, typeof(Geometry), null, CultureInfo.InvariantCulture) is not Geometry geometry)
        {
            // ⚠ An unknown key yields no icon rather than an exception — a typo must not take down a
            //   menu. The icon column keeps its width either way, so the labels stay aligned.
            return null;
        }

        var path = new Shapes.Path
        {
            Data = geometry,
            StrokeThickness = 2,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };

        // ⭐⭐ The stroke follows the MENU ITEM's foreground rather than a fixed token, and that is not a
        //    refinement — it is what makes the disabled row correct. `About` ships as a deliberate
        //    disabled placeholder, and `MenuItem:disabled` dims the row's Foreground; bound to
        //    `ForegroundBrush` directly the icon would stay at full strength beside faded text.
        // ⚠ EmberTern gets this for free because SvgIcon INHERITS Foreground; a bare Path does not, so
        //    the inheritance has to be asked for explicitly.
        path[!Shapes.Shape.StrokeProperty] = new Binding
        {
            Path = nameof(TemplatedControl.Foreground),
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor)
            {
                AncestorType = typeof(MenuItem),
            },
        };

        // The geometries are authored on a 24-unit grid, exactly as everywhere else in both applications.
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);

        var box = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };

        // ⭐ `Size.Icon` (14) as a role, not a literal — the menu row is one of the surfaces that role
        //   names. ⚠ DynamicResource rather than a lookup: a theme toggle does not rebuild these objects,
        //   and a binding recomputes itself.
        box[!Layoutable.WidthProperty] = new DynamicResourceExtension("Size.Icon");
        box[!Layoutable.HeightProperty] = new DynamicResourceExtension("Size.Icon");

        return box;
    }
}

using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using EmberTern.App.Commands;
using EmberTern.App.Controls;

namespace EmberTern.App;

/// <summary>
/// Puts a command's gesture on a menu item: <c>InputGesture="{app:CommandGesture Compile}"</c>.
///
/// <para>⭐ This is what makes a context menu show the truth: the key comes from
/// <see cref="CommandCatalog"/>, so re-binding a shortcut updates every menu that offers the command and no
/// menu can drift. Before it, 3 of the app's 142 menu items showed a shortcut and all three were typed by
/// hand.</para>
///
/// <para>⚠ <b><c>MenuItem.InputGesture</c> is display-only</b> — measured, not assumed: pressing the gesture
/// with such an item realized does NOT invoke it. So setting it everywhere cannot double-fire a command, and
/// the item's own <c>Command</c> stays the single path to the action.</para>
///
/// <para>Lives in the root <c>EmberTern.App</c> namespace, beside <see cref="IconGeometryConverter"/>, so
/// every view reaches it through the one <c>app:</c> prefix it already declares.</para>
/// </summary>
public sealed class CommandGestureExtension
{
    public CommandGestureExtension() { }

    public CommandGestureExtension(CommandId id) => Id = id;

    public CommandId Id { get; set; }

    /// <summary>The declared gesture, or null when the command has none — which leaves the menu's gesture
    /// column empty rather than inventing a key.</summary>
    public KeyGesture? ProvideValue() => CommandCatalog.For(Id)?.Gesture;
}

/// <summary>
/// Puts an icon in a menu item's icon column: <c>Icon="{app:MenuIcon Icon.Trash}"</c>, or
/// <c>Icon="{app:MenuIcon Icon.Trash, Brush=DangerIconBrush}"</c> for a destructive action.
///
/// <para>Reuses the app's existing icon system — the geometry keys of
/// <c>Themes/IconGeometries.axaml</c> resolved through the one <see cref="IconGeometryConverter"/>, rendered
/// by the one <see cref="SvgIcon"/>. No second icon mechanism, and nothing new to draw where a mark already
/// exists.</para>
///
/// <para>Colour: by default the icon inherits the menu item's foreground, so a menu reads as one calm block
/// and follows selection and disabled states for free. <see cref="Brush"/> is the deliberate exception for a
/// destructive action, keeping <c>DangerIconBrush</c> in the job Seam 4 gave it — destructive-action icons.
/// It is bound as a DYNAMIC resource, so it still re-colours live on a theme toggle.</para>
/// </summary>
public sealed class MenuIconExtension
{
    public MenuIconExtension() { }

    public MenuIconExtension(string key) => Key = key;

    /// <summary>A geometry key from <c>Themes/IconGeometries.axaml</c>, e.g. <c>Icon.Trash</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Optional theme brush key. Omit to inherit the menu item's foreground.</summary>
    public string? Brush { get; set; }

    public object? ProvideValue()
    {
        if (IconGeometryConverter.Instance.Convert(
                Key, typeof(Geometry), null, CultureInfo.InvariantCulture) is not Geometry geometry)
        {
            // An unknown key yields no icon rather than an exception: a typo must not take down a menu, and
            // the empty icon column still reserves its width, so the text stays aligned.
            return null;
        }

        // ⭐ Rozmiar z roli `Size.Icon` (14), nie literałem — wiersz menu kontekstowego to jedna z
        //   powierzchni, które ta rola nazywa (A‑3, M4). Wartość się nie zmienia; zmienia się to, że
        //   zmiana roli dosięgnie także menu, a nie ominie go po cichu, bo stoi w C#, a nie w XAML.
        // ⚠ `DynamicResource`, nie odczyt `TryFindResource`: przełączenie motywu ani podmiana katalogu
        //   nie przebudowuje tych obiektów, a wiązanie przelicza się samo.
        var icon = new SvgIcon { Data = geometry };
        icon[!Layoutable.WidthProperty] = new DynamicResourceExtension("Size.Icon");
        icon[!Layoutable.HeightProperty] = new DynamicResourceExtension("Size.Icon");
        if (!string.IsNullOrEmpty(Brush))
        {
            icon[!TemplatedControl.ForegroundProperty] = new DynamicResourceExtension(Brush);
        }
        return icon;
    }
}

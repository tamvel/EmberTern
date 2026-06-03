using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace EmberTern.App.Behaviors;

/// <summary>
/// Attached behavior: focuses (and, for TextBox, selects-all) a control whenever it
/// becomes visible. Used by inline-rename TextBoxes that are toggled into the visual
/// tree by an <c>IsVisible="{Binding IsRenaming}"</c> binding — we need to set
/// keyboard focus + selection the moment the TextBox appears so the user can start
/// typing immediately without clicking it first.
/// </summary>
public static class FocusBehavior
{
    public static readonly AttachedProperty<bool> FocusOnVisibleProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "FocusOnVisible", typeof(FocusBehavior));

    static FocusBehavior()
    {
        FocusOnVisibleProperty.Changed.AddClassHandler<Control>(OnFocusOnVisibleChanged);
    }

    public static void SetFocusOnVisible(Control control, bool value)
        => control.SetValue(FocusOnVisibleProperty, value);

    public static bool GetFocusOnVisible(Control control)
        => control.GetValue(FocusOnVisibleProperty);

    private static void OnFocusOnVisibleChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            control.PropertyChanged += OnControlPropertyChanged;
            if (control.IsVisible) FocusAndSelect(control);
        }
        else
        {
            control.PropertyChanged -= OnControlPropertyChanged;
        }
    }

    private static void OnControlPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty && sender is Control c && c.IsVisible)
        {
            FocusAndSelect(c);
        }
    }

    private static void FocusAndSelect(Control control)
    {
        // Dispatch at Background priority so any pending layout/render passes settle
        // first — focus before layout can no-op silently on a control that's not yet
        // measured into the tree.
        Dispatcher.UIThread.Post(() =>
        {
            control.Focus();
            if (control is TextBox tb)
            {
                tb.SelectAll();
            }
        }, DispatcherPriority.Background);
    }
}

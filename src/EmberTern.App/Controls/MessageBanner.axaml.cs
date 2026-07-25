using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace EmberTern.App.Controls;

/// <summary>Severity of a <see cref="MessageBanner"/> — the one thing that decides its stripe/icon tone.
/// Deliberately the SAME severity→brush-key mapping the diagnostics surfaces use
/// (<see cref="ViewModels.DiagnosticRowViewModel.SeverityBrushKey"/>), so a message reads the same
/// everywhere in the IDE.</summary>
public enum MessageSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// The IDE's ONE message surface: a calm, severity-striped bar carrying a single message, with optional
/// Copy / Expand / Dismiss affordances. Generalised from the debugger's Error Bar (D15.2 Seam C), which is
/// its first consumer — every main work surface where the user executes or compiles code presents its
/// errors through this control instead of a locally-styled red line.
/// <para>
/// Purely presentational: it renders the message it is given and owns only its own disclosure state. It has
/// no knowledge of any module's workflow — dismissal is the host's decision (<see cref="DismissCommand"/>),
/// and the host keeps deciding *when* a message is shown (<c>IsVisible</c>).
/// </para>
/// <para>
/// Chrome (background/border) lives on the control itself, so a consumer overrides
/// <see cref="TemplatedControl.BorderThickness"/> / <c>Margin</c> to sit flush in its host without the
/// banner growing a per-host option. Colours are theme tokens only.
/// </para>
/// </summary>
public partial class MessageBanner : UserControl
{
    public static readonly StyledProperty<MessageSeverity> SeverityProperty =
        AvaloniaProperty.Register<MessageBanner, MessageSeverity>(
            nameof(Severity), defaultValue: MessageSeverity.Error);

    public static readonly StyledProperty<string> MessageProperty =
        AvaloniaProperty.Register<MessageBanner, string>(nameof(Message), defaultValue: string.Empty);

    public static readonly StyledProperty<bool> ShowCopyProperty =
        AvaloniaProperty.Register<MessageBanner, bool>(nameof(ShowCopy), defaultValue: true);

    public static readonly StyledProperty<bool> ShowExpandProperty =
        AvaloniaProperty.Register<MessageBanner, bool>(nameof(ShowExpand));

    public static readonly StyledProperty<bool> ShowDismissProperty =
        AvaloniaProperty.Register<MessageBanner, bool>(nameof(ShowDismiss));

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<MessageBanner, bool>(
            nameof(IsExpanded), defaultValue: true,
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MaxExpandedHeightProperty =
        AvaloniaProperty.Register<MessageBanner, double>(nameof(MaxExpandedHeight), defaultValue: 190d);

    public static readonly StyledProperty<ICommand?> DismissCommandProperty =
        AvaloniaProperty.Register<MessageBanner, ICommand?>(nameof(DismissCommand));

    // Derived from Severity (kept as properties, not converters, so the XAML stays one MultiBinding per
    // painted element and the mapping is unit-testable without a UI session). Set only by SyncSeverity.
    public static readonly StyledProperty<string> SeverityBrushKeyProperty =
        AvaloniaProperty.Register<MessageBanner, string>(nameof(SeverityBrushKey), defaultValue: "ErrorBrush");

    public static readonly StyledProperty<string> SeverityGeometryKeyProperty =
        AvaloniaProperty.Register<MessageBanner, string>(
            nameof(SeverityGeometryKey), defaultValue: "Icon.BreakException");

    public MessageBanner()
    {
        InitializeComponent();
        SyncSeverity(Severity);
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    /// <summary>How bad this message is — drives the stripe colour and the icon.</summary>
    public MessageSeverity Severity
    {
        get => GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    /// <summary>The message text, shown verbatim (never re-worded by the banner).</summary>
    public string Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Show the Copy button (copies <see cref="Message"/> to the clipboard). On by default — every
    /// message worth showing is worth copying, and a per-host decision here is exactly the divergence this
    /// control exists to remove.</summary>
    public bool ShowCopy
    {
        get => GetValue(ShowCopyProperty);
        set => SetValue(ShowCopyProperty, value);
    }

    /// <summary>Show the expand/collapse chevron. Without it the banner always shows the full message.</summary>
    public bool ShowExpand
    {
        get => GetValue(ShowExpandProperty);
        set => SetValue(ShowExpandProperty, value);
    }

    /// <summary>Show the dismiss (✕) button; it runs <see cref="DismissCommand"/>, so hiding the banner
    /// stays the host's decision.</summary>
    public bool ShowDismiss
    {
        get => GetValue(ShowDismissProperty);
        set => SetValue(ShowDismissProperty, value);
    }

    /// <summary>True (the default) = the whole message, wrapped + selectable; false = one ellipsised line.
    /// Two-way, so a host that owns the state (e.g. "a new error always re-expands") stays in control.</summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Height cap for the expanded message before it scrolls (~8–10 lines by default).</summary>
    public double MaxExpandedHeight
    {
        get => GetValue(MaxExpandedHeightProperty);
        set => SetValue(MaxExpandedHeightProperty, value);
    }

    /// <summary>Invoked by the dismiss button.</summary>
    public ICommand? DismissCommand
    {
        get => GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    /// <summary>Theme brush key for the stripe + icon, resolved by <see cref="IconBrushConverter"/>.</summary>
    public string SeverityBrushKey
    {
        get => GetValue(SeverityBrushKeyProperty);
        private set => SetValue(SeverityBrushKeyProperty, value);
    }

    /// <summary>Icon geometry key, resolved by <see cref="IconGeometryConverter"/>.</summary>
    public string SeverityGeometryKey
    {
        get => GetValue(SeverityGeometryKeyProperty);
        private set => SetValue(SeverityGeometryKeyProperty, value);
    }

    /// <summary>The severity → theme brush key mapping. Shared with the diagnostics surfaces so one
    /// severity always reads as one colour across the IDE.</summary>
    public static string BrushKeyFor(MessageSeverity severity) => severity switch
    {
        MessageSeverity.Error => "ErrorBrush",
        MessageSeverity.Warning => "WarningBrush",
        MessageSeverity.Success => "SuccessIconBrush",
        _ => "SubtleForegroundBrush",
    };

    /// <summary>The severity → icon geometry key mapping (stop octagon / alert triangle / check / note).</summary>
    public static string GeometryKeyFor(MessageSeverity severity) => severity switch
    {
        MessageSeverity.Error => "Icon.BreakException",
        MessageSeverity.Warning => "Icon.AlertTriangle",
        MessageSeverity.Success => "Icon.Check",
        _ => "Icon.Comment",
    };

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SeverityProperty)
        {
            SyncSeverity(change.GetNewValue<MessageSeverity>());
        }
    }

    private void SyncSeverity(MessageSeverity severity)
    {
        SeverityBrushKey = BrushKeyFor(severity);
        SeverityGeometryKey = GeometryKeyFor(severity);
    }

    // Copy is self-contained: the banner already holds the exact text on screen, so every consumer gets
    // "copy this message" for free instead of hand-writing a clipboard handler per host.
    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _ = CopyMessageAsync();
    }

    private async Task CopyMessageAsync()
    {
        var text = Message;
        if (string.IsNullOrEmpty(text)) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(text);
    }

    private void OnToggleExpandClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        IsExpanded = !IsExpanded;
    }
}

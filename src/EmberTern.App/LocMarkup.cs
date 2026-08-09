using Avalonia.Data;
using EmberTern.App.Localization;

namespace EmberTern.App;

/// <summary>
/// Puts a localized string on any property: <c>Text="{app:Loc SidebarPlaceholderEmpty}"</c>.
///
/// <para>⭐ <b>This replaces <c>{x:Static app:UiStrings.X}</c> everywhere, and the difference is the whole
/// point of the live-switching decision.</b> <c>x:Static</c> is not a binding: it reads a value once, sets it
/// as a local value and never re-evaluates — with it, a language change could only ever take effect on
/// restart. This returns a real <see cref="Binding"/> against <see cref="LocalizationSource"/>, so the
/// property follows the language for the lifetime of the control.</para>
///
/// <para>⚠ <b>What we gave up to get that, stated plainly: compile-time checking of the key.</b>
/// <c>{x:Static}</c> was verified by the compiler, so a typo was a build error; a key here is a string, so a
/// typo is a key rendered on screen. The check is restored by a guard instead of by the compiler —
/// <c>EveryLocKeyInXaml_ExistsInTheCatalog</c> reads every <c>{app:Loc}</c> in every view and fails on a key
/// the English catalog does not have. ⛔ Do not remove that guard; without it this trade is a real
/// regression rather than a considered one.</para>
///
/// <para>⚠ Lives in the root <c>EmberTern.App</c> namespace — beside <see cref="MenuIconExtension"/> and
/// <see cref="CommandGestureExtension"/>, and for the same reason: every view already declares the one
/// <c>app:</c> prefix, and a markup extension in a sub-namespace would need a second xmlns in all 62 views.
/// The implementation it binds to stays in <c>EmberTern.App.Localization</c>.</para>
/// </summary>
public sealed class LocExtension
{
    public LocExtension() { }

    public LocExtension(string key) => Key = key;

    /// <summary>The resource key — the same name the <c>UiStrings</c> member and the <c>.resx</c> entry use.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// A one-way binding to <see cref="LocalizationSource"/>'s indexer.
    ///
    /// <para>⚠ Returning a <see cref="Binding"/> rather than a <see cref="string"/> is what makes this live:
    /// the XAML compiler treats a markup extension returning a binding as a binding, so it works on any
    /// bindable property — including on a control whose <c>DataContext</c> is something else entirely, since
    /// the binding carries its own <c>Source</c>.</para>
    /// </summary>
    public Binding ProvideValue() => new Binding
    {
        Source = LocalizationSource.For(Key),
        Path = nameof(LocalizedString.Value),
        Mode = BindingMode.OneWay,
    };
}

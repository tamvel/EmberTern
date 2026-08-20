using Avalonia.Data;
using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager;

/// <summary>
/// Puts a localized string into XAML: <c>Text="{lm:Loc SettingsWindowTitle}"</c>.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>It returns a BINDING, not a string, and that is the whole mechanism.</b>
/// <c>{x:Static}</c> is not a binding and never re-evaluates, so with it the language could not change
/// without a restart — which is the shape this application's own
/// <c>ManagerSettingsCatalog</c> comment already warns about. ⛔ Never use <c>{x:Static}</c> for a word.</para>
///
/// <para>⚠ <b>The cost, stated plainly: the compiler no longer checks the key.</b> <c>{x:Static}</c> was
/// verified at build time; a key here travels as a string. <c>NoLocKeyInXaml_IsMissingFromTheCatalog</c>
/// is what compensates — ⛔ removing that guard turns a deliberate trade into a regression.</para>
///
/// <para>⭐ Mirrored from EmberTern's <c>LocExtension</c>, the fifth such mirror in this application and the
/// same decision as the other four (L8 decision D‑1).</para>
/// </remarks>
public sealed class LocExtension
{
    /// <summary>Creates the extension.</summary>
    public LocExtension()
    {
    }

    /// <summary>Creates the extension for a key.</summary>
    public LocExtension(string key) => Key = key;

    /// <summary>The resource key — the name of the entry in <c>Localization/Strings.resx</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The binding that keeps this key's text current.</summary>
    public Binding ProvideValue() => new()
    {
        Source = LocalizationSource.For(Key),
        Path = nameof(LocalizedString.Value),
        Mode = BindingMode.OneWay,
    };
}

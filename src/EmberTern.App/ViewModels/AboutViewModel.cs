using System.Globalization;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The About window's content — a <b>projection of <see cref="AppInfo"/></b>, which is itself a projection of
/// the assembly. It holds no state, nothing is settable, and there is nothing to refresh.
///
/// <para>It exists rather than binding <c>{x:Static}</c> straight to <see cref="AppInfo"/> for one reason: the
/// version has to be composed with its label (<c>"Version …"</c>), and the label is a UI string that belongs in
/// <see cref="UiStrings"/> (architecture rule #6) while the number belongs to the assembly. This is the one
/// place those two meet, and doing it here keeps the view free of any formatting logic.</para>
///
/// <para>⛔ No version literal, here or anywhere else in the app — see <see cref="AppInfo"/>.</para>
/// </summary>
public sealed class AboutViewModel
{
    public string Product => AppInfo.Product;

    public string VersionText =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.AboutVersionFormat, AppInfo.Version);

    public string Author => AppInfo.Author;

    public string Copyright => AppInfo.Copyright;
}

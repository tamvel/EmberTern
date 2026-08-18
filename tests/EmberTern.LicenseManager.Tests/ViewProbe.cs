using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// Locating a control by the NAME it was given, for guards shared by more than one test class.
///
/// <para>⭐⭐ <b>It exists because of gotcha #379, and L5.4 met that gotcha again.</b> Four guards about
/// the LICENCE FORM's two date fields identified their subjects as <i>"every
/// <see cref="CalendarDatePicker"/> in the window"</i>. That was true only while the licence form owned
/// the only pickers in the application; the batch renewal card added a third, in a different view, and
/// all four went red at once — none of them about the new control, and two of them reporting a height of
/// zero because the control they had caught belongs to a view that is not showing.</para>
///
/// <para>⛔ Naming the subject is not a weakening. The guards keep every assertion they had; they stop
/// depending on what else the window happens to contain.</para>
/// </summary>
internal static class ViewProbe
{
    /// <summary>
    /// The licence FORM's two date fields, in document order — never every picker in the window.
    /// </summary>
    internal static List<CalendarDatePicker> FormPickers(Window window) =>
        window.GetVisualDescendants()
            .OfType<CalendarDatePicker>()
            .Where(p => p.Name is "LicenseNotBefore" or "LicenseExpiresAt")
            .OrderBy(p => p.Name == "LicenseNotBefore" ? 0 : 1)
            .ToList();

    /// <summary>Every control of a given type carrying this name — one per realised row.</summary>
    internal static List<T> AllNamed<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Where(c => c.Name == name).ToList();

    /// <summary>One named control of a given type, or a failure that says which name was missing.</summary>
    internal static T Named<T>(Window window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
}

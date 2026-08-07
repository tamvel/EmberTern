using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace EmberTern.App.Views;

/// <summary>
/// The one place a data grid's copy action reaches the clipboard. Tiny on purpose — what it owns is not the
/// write but the <b>refusal</b>: the rule that nothing is copied when there is nothing to copy.
///
/// <para>⚠ WHY THAT RULE NEEDS AN OWNER. Every caller obtains its text from
/// <see cref="ViewModels.GridCopyText"/>, which returns <c>null</c> for a request it cannot serve (no result
/// yet, no target row, a stale column index after a re-fetch). Writing that through as an empty string would
/// <b>destroy whatever the user already had on the clipboard</b> and report nothing — a data-loss shape, and
/// exactly the kind of silent failure that looks like "copy stopped working" long after the cause. So a null
/// or empty build result leaves the clipboard untouched.</para>
///
/// <para>⚠ The DECISION — which row, which column, which view model — stays in each view, because it genuinely
/// differs per grid. Only the write is shared; there is no shared "copy command" pretending the four grids
/// resolve their target the same way.</para>
/// </summary>
internal static class GridClipboard
{
    /// <summary>Puts <paramref name="text"/> on the clipboard, or does nothing at all if there is none.</summary>
    public static async Task WriteAsync(Visual host, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (TopLevel.GetTopLevel(host)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }
}

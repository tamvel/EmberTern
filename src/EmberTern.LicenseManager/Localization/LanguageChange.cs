using System;

namespace EmberTern.LicenseManager.Localization;

/// <summary>
/// Subscribes to <see cref="Loc.LanguageChanged"/> WITHOUT keeping the subscriber alive.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>It exists because <see cref="Loc.LanguageChanged"/> is a <c>static</c> event, and a static
/// event is a GC root.</b> A plain <c>Loc.LanguageChanged += OnLanguageChanged</c> in a view model's
/// constructor keeps that view model — and its whole object graph — alive for the life of the process.
/// ⚠ Measured on this application: four of the five message hosts live as long as the window, so the leak
/// would be invisible, but <c>SendLicenceViewModel</c> is built fresh on EVERY send
/// (<c>ShellViewModel.OpenSendLicence</c>), so the operator would accumulate one dead view model per
/// licence sent, each still receiving language notifications.</para>
///
/// <para>⛔ The obvious alternative — an explicit <c>Detach</c> called when the window closes — was
/// rejected: it is correct only while every future short-lived host remembers to call it, and the failure
/// is silent and cumulative. A subscription that cannot outlive its subscriber needs nobody to remember
/// anything.</para>
///
/// <para>⚠⚠ <b>The handler MUST be a <c>static</c> lambda, and the signature is what enforces it.</b> A
/// closure capturing the subscriber would be held by the subscription and would defeat the whole point —
/// the target would be reachable through the delegate, so the <see cref="WeakReference{T}"/> would never
/// come back empty. Passing <c>static</c> makes that a compile error rather than a leak nobody notices:
/// <code>LanguageChange.SubscribeWeak(this, static host => host.Refresh());</code></para>
///
/// <para>⚠ A collected subscriber's entry is removed on the NEXT language change rather than at collection
/// time — there is no cheaper moment without a second timer, and the residue is one small delegate per
/// subscriber until then. ⭐ What matters is that the SUBSCRIBER is collectable; the bookkeeping is not.</para>
/// </remarks>
internal static class LanguageChange
{
    /// <summary>
    /// Calls <paramref name="onLanguageChanged"/> with <paramref name="target"/> whenever the language
    /// really changes, for as long as <paramref name="target"/> is alive.
    /// </summary>
    /// <param name="target">The subscriber. ⭐ Held weakly — subscribing does not keep it alive.</param>
    /// <param name="onLanguageChanged">
    /// ⛔ Must be a <c>static</c> lambda. Anything that captures <paramref name="target"/> re-creates the
    /// leak this method exists to remove.
    /// </param>
    public static void SubscribeWeak<T>(T target, Action<T> onLanguageChanged)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(onLanguageChanged);

        var weak = new WeakReference<T>(target);

        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (weak.TryGetTarget(out var alive))
            {
                onLanguageChanged(alive);
                return;
            }

            // The subscriber is gone; take the subscription with it.
            Loc.LanguageChanged -= handler;
        };

        Loc.LanguageChanged += handler;
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace EmberTern.App.Behaviors;

/// <summary>
/// ⭐ Keeps a <c>SizeToContent</c> dialog whose content can GROW after it opens fully on screen.
///
/// <para><b>The defect it exists for (QA, etap 5b).</b> The settings import dialog is
/// <c>SizeToContent="Height"</c>: it opens compact and gets taller once a file has been opened and the section
/// list appears. Avalonia grows such a window <b>downwards from its existing position</b> — the top-left stays
/// put — so a dialog centred on a 1080-tall screen slid its footer, and therefore its <i>Import</i> button,
/// under the bottom edge. Nothing was broken; the button was simply unreachable without dragging the window.</para>
///
/// <para><b>Two rules, and both are needed.</b></para>
/// <list type="number">
///   <item><description>
///     <b>A ceiling.</b> The window may never be taller than the screen's working area, so on a short screen the
///     content scrolls inside the dialog instead of the dialog overflowing the desktop. That half only works if
///     the host puts its body in a <c>ScrollViewer</c> — the ceiling decides the size, the ScrollViewer decides
///     what happens to the overflow, and a dialog with the first and not the second would simply be clipped.
///   </description></item>
///   <item><description>
///     <b>A nudge, not a jump.</b> After a size change the window is pushed back inside the working area only if
///     it has fallen outside it. ⚠ Deliberately not re-centred: re-centring moves a dialog the user is looking at
///     every time it changes size, which is more disorienting than the problem it fixes. The window stays exactly
///     where it was unless staying there would hide something.
///   </description></item>
/// </list>
///
/// <para>⚠ <b>The units are the trap.</b> <c>Window.Position</c> and <c>Screen.WorkingArea</c> are in PHYSICAL
/// pixels; <c>Window.MaxHeight</c>, <c>ClientSize</c> and <c>FrameSize</c> are in DIPs. On a 150% display, mixing
/// them silently produces a ceiling a third too tall and a clamp a third too eager. Every conversion here goes
/// through the screen's own <c>Scaling</c>.</para>
///
/// <para>The arithmetic is a pure static (<see cref="ClampOnScreen"/>) so it can be asserted without a desktop —
/// the same separation of "the decision" from "the doing" the rest of the app uses.</para>
/// </summary>
public static class GrowingDialogBehavior
{
    /// <summary>Breathing room between the dialog and the edge of the working area, in DIPs. A dialog flush
    /// against the taskbar reads as clipped even when it is not.</summary>
    public const double ScreenMargin = 24;

    /// <summary>
    /// Attaches the two rules to <paramref name="window"/>. Call it from the dialog's constructor, after
    /// <c>InitializeComponent</c>; it unsubscribes nothing because the handlers die with the window.
    /// </summary>
    public static void Attach(Window window)
    {
        if (window is null) return;

        // The ceiling can only be computed once there IS a screen to ask about, which is not true in the
        // constructor. Opened is also before the first user-visible paint, so nothing flickers.
        window.Opened += (_, _) => ApplyCeiling(window);

        // Fires for the growth this class exists for, and for any later change of content.
        window.SizeChanged += (_, _) => KeepOnScreen(window);
    }

    /// <summary>Caps the window at the working area's height so it can never exceed the desktop.</summary>
    public static void ApplyCeiling(Window window)
    {
        if (ScreenFor(window) is not { } screen) return;

        var ceiling = CeilingFor(window.MaxHeight, screen.WorkingArea.Height, screen.Scaling);
        if (!double.IsPositiveInfinity(ceiling))
        {
            window.MaxHeight = ceiling;
        }
    }

    /// <summary>
    /// ⭐⭐ The ceiling is the SMALLER of the dialog's own cap and what the screen allows — never a
    /// replacement for the first. Ratified 2026-08-09 (M4.4).
    ///
    /// <para><b>Why a plain assignment was wrong.</b> A hand-set <c>MaxHeight</c> in a dialog does TWO jobs:
    /// (a) "never exceed the screen", and (b) "do not grow past a comfortable reading size even on a huge
    /// monitor". This class only ever knew about (a), so overwriting the value silently discarded (b) —
    /// measured: on a 1080-tall screen the Execute Procedure dialog's deliberate 720 would have become 1008,
    /// i.e. 288 px taller than its author intended, as a side effect of adding screen protection it did not
    /// previously need.</para>
    ///
    /// <para>⚠ An unset <c>MaxHeight</c> is <see cref="double.PositiveInfinity"/>, so the minimum naturally
    /// degenerates to the screen ceiling for a dialog that declares no cap of its own — which is why both
    /// pre-existing consumers (the settings export/import dialogs, neither of which sets one) are unaffected
    /// to the pixel.</para>
    ///
    /// <para>⚠ Consequently the ceiling only ever moves DOWN: applying it twice is a no-op rather than a
    /// recomputation. That is the safe direction, and it is not a limitation in practice because
    /// <see cref="Attach"/> subscribes it to <c>Opened</c>, which fires once per window.</para>
    /// </summary>
    /// <param name="currentMax">The window's declared cap, or <see cref="double.PositiveInfinity"/> if none.</param>
    /// <param name="workingAreaHeight">The screen's working-area height, in PHYSICAL pixels.</param>
    /// <param name="scaling">The screen's scaling factor, used to reach DIPs.</param>
    /// <returns>The cap to apply, or <see cref="double.PositiveInfinity"/> when there is nothing to apply.</returns>
    public static double CeilingFor(double currentMax, double workingAreaHeight, double scaling)
    {
        if (scaling <= 0) return currentMax;

        var available = workingAreaHeight / scaling - ScreenMargin;
        if (available <= 0) return currentMax;

        return Math.Min(currentMax, available);
    }

    /// <summary>Pushes the window back inside the working area if its current size has taken it outside.</summary>
    public static void KeepOnScreen(Window window)
    {
        if (ScreenFor(window) is not { } screen) return;

        // FrameSize includes the border and title bar — the part that actually falls off the edge. It is null
        // until the platform has one, in which case the client size is the best available answer.
        var size = window.FrameSize ?? window.ClientSize;
        var scaling = screen.Scaling;
        var bounds = new PixelRect(
            window.Position,
            new PixelSize((int)(size.Width * scaling), (int)(size.Height * scaling)));

        var placed = ClampOnScreen(bounds, screen.WorkingArea);
        if (placed != window.Position)
        {
            window.Position = placed;
        }
    }

    /// <summary>
    /// Where <paramref name="window"/> has to sit to be inside <paramref name="workingArea"/>, moving it as
    /// little as possible.
    ///
    /// <para>⚠ The order is the design: push the far edges in FIRST, then clamp the near edges. For a window that
    /// is genuinely larger than the working area the second step wins, so what stays visible is its <b>top-left</b>
    /// — the header and the beginning of the content — rather than its bottom-right. A window can only be in that
    /// state if <see cref="ApplyCeiling"/> could not run, but "which half do we sacrifice" still has a right
    /// answer.</para>
    /// </summary>
    public static PixelPoint ClampOnScreen(PixelRect window, PixelRect workingArea)
    {
        var x = window.X;
        var y = window.Y;

        if (window.Right > workingArea.Right) x = workingArea.Right - window.Width;
        if (window.Bottom > workingArea.Bottom) y = workingArea.Bottom - window.Height;
        if (x < workingArea.X) x = workingArea.X;
        if (y < workingArea.Y) y = workingArea.Y;

        return new PixelPoint(x, y);
    }

    private static Screen? ScreenFor(Window window)
    {
        var screens = window.Screens;
        if (screens is null) return null;
        return screens.ScreenFromWindow(window) ?? screens.Primary;
    }
}

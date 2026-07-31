using Avalonia;
using EmberTern.App.Behaviors;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The placement arithmetic behind <see cref="GrowingDialogBehavior"/> — etap 5b's second QA finding.
///
/// <para>The settings import dialog is <c>SizeToContent</c> and grows once a file has been opened. Avalonia grows
/// such a window <b>downwards from its current position</b>, so a dialog centred on its owner slid its footer, and
/// with it the Import button, under the bottom edge of the screen.</para>
///
/// <para>The doing needs a desktop; the deciding does not, so it lives in a pure static and is asserted here. The
/// cases below are the ones a real screen produces: a dialog that grew past the bottom, one that never left the
/// screen, one taller than the working area at all, and a working area that does not start at the origin (a
/// taskbar, or a second monitor to the left of the primary — where a naive clamp against <c>0,0</c> is wrong in a
/// way nobody notices on a single-monitor developer machine).</para>
/// </summary>
public sealed class GrowingDialogBehaviorTests
{
    // A 1920×1080 screen with a 40px taskbar along the bottom.
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    /// <summary>The reported defect: the dialog grew and its bottom went past the working area. It is pushed up by
    /// exactly the overflow — no more, so the movement is the minimum that solves the problem.</summary>
    [Fact]
    public void ADialogThatGrewPastTheBottom_IsPushedUpByExactlyTheOverflow()
    {
        var grown = new PixelRect(600, 700, 560, 700); // bottom = 1400, i.e. 360 past the working area

        var placed = GrowingDialogBehavior.ClampOnScreen(grown, Work);

        Assert.Equal(600, placed.X);
        Assert.Equal(340, placed.Y);
        Assert.Equal(Work.Bottom, placed.Y + grown.Height);
    }

    /// <summary>⚠ The other half of "nudge, not jump": a dialog that still fits is not moved at all. Re-centring
    /// on every size change would be more disorienting than the defect.</summary>
    [Fact]
    public void ADialogThatStillFits_IsNotMoved()
    {
        var fits = new PixelRect(600, 200, 560, 500);

        Assert.Equal(fits.Position, GrowingDialogBehavior.ClampOnScreen(fits, Work));
    }

    /// <summary>
    /// A window larger than the working area cannot be made to fit, so the question is which half survives. The
    /// top-left does — the header and the start of the content — because a dialog showing its bottom-right is
    /// unreadable, and its title is what tells the user what they are looking at.
    /// </summary>
    [Fact]
    public void AWindowTallerThanTheScreen_KeepsItsTopVisible()
    {
        var huge = new PixelRect(-40, 300, 2000, 1400);

        var placed = GrowingDialogBehavior.ClampOnScreen(huge, Work);

        Assert.Equal(Work.X, placed.X);
        Assert.Equal(Work.Y, placed.Y);
    }

    /// <summary>
    /// ⚠ The working area is not the screen and does not have to start at the origin — a top-docked taskbar, or a
    /// monitor to the left of the primary, gives it a non-zero (and possibly negative) origin. Clamping against
    /// <c>0,0</c> instead of the working area's own edges passes every single-monitor test and puts the dialog
    /// under the taskbar in real use.
    /// </summary>
    [Fact]
    public void TheWorkingAreasOwnOrigin_IsWhatIsClampedAgainst()
    {
        var secondary = new PixelRect(-1920, 48, 1600, 900); // to the LEFT of the primary, taskbar on top
        var above = new PixelRect(-1800, 0, 560, 400);       // started above that working area

        var placed = GrowingDialogBehavior.ClampOnScreen(above, secondary);

        Assert.Equal(-1800, placed.X);
        Assert.Equal(48, placed.Y);
    }

    /// <summary>A dialog pushed off the right edge comes back the same way the bottom does — the rule is written
    /// once for both axes rather than only for the one the defect happened on.</summary>
    [Fact]
    public void TheSameRuleAppliesHorizontally()
    {
        var offRight = new PixelRect(1700, 100, 560, 400);

        var placed = GrowingDialogBehavior.ClampOnScreen(offRight, Work);

        Assert.Equal(Work.Right - 560, placed.X);
        Assert.Equal(100, placed.Y);
    }
}

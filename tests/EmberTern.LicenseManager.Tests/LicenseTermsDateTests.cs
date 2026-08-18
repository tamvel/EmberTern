using System;
using System.Linq;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// QA‑6 — the dates moved from ISO text to a picker, and <b>the domain did not move with them</b>.
///
/// <para>⭐ That is the whole risk of the change. A calendar hands back a local <see cref="DateTime"/>
/// whose <c>Kind</c> is Unspecified; a licence asserts an instant in UTC, and its expiry runs to the END
/// of the day the operator chose. Those two facts were true when the operator typed <c>2027-08-15</c>
/// and have to stay true now that they click it — otherwise a customer is locked out on a date their
/// invoice says they own, or a licence issued in Warsaw means something different from one issued in
/// London.</para>
/// </summary>
public sealed class LicenseTermsDateTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 10, 30, 0, TimeSpan.Zero);

    private readonly ManagerFixture _manager = new(Now);

    private ShellViewModel NewShell()
    {
        var shell = new ShellViewModel(_manager.Register, _manager.Session, _manager.Paths, () => Now);
        shell.NewCustomerCommand.Execute(null);
        shell.CustomerName = "ACME Sp. z o.o.";
        shell.SaveCustomerCommand.Execute(null);
        shell.NewLicenseCommand.Execute(null);
        return shell;
    }

    [Fact]
    public void ANewLicenceOpensOnTodayAndAYearOut()
    {
        var shell = NewShell();

        Assert.Equal(new DateTime(2026, 8, 16), shell.LicenseNotBefore);
        Assert.Equal(new DateTime(2027, 8, 16), shell.LicenseExpiresAt);
    }

    [Fact]
    public void TheChosenExpiryDayRunsToItsEnd()
    {
        // ⭐⭐ THE off-by-one that is not read as a bug until a customer is locked out on a date their
        //     invoice says they own. Storing midnight would expire the licence at the START of the day
        //     the operator picked.
        var shell = NewShell();
        shell.LicenseExpiresAt = new DateTime(2027, 8, 16);
        shell.SaveLicenseCommand.Execute(null);

        var licence = Assert.Single(_manager.Register.GetLicenses(shell.SelectedCustomer!.CustomerId));

        Assert.Equal(new DateTimeOffset(2027, 8, 16, 23, 59, 59, TimeSpan.Zero), licence.ExpiresAt);
    }

    [Fact]
    public void TheChosenStartDayBeginsAtMidnightUtc()
    {
        // ⚠ The picker hands back a DateTime with Kind = Unspecified. Reading it as a UTC calendar day is
        //   what keeps a licence issued in Warsaw meaning the same as one issued in London.
        var shell = NewShell();
        shell.LicenseNotBefore = new DateTime(2026, 9, 1);
        shell.SaveLicenseCommand.Execute(null);

        var licence = Assert.Single(_manager.Register.GetLicenses(shell.SelectedCustomer!.CustomerId));

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), licence.NotBefore);
        Assert.Equal(TimeSpan.Zero, licence.NotBefore.Offset);
    }

    [Fact]
    public void TheTimeOfDayInAPickedValueIsIgnoredRatherThanCarried()
    {
        // ⚠ A picker can hand back a value with a time component. A licence is a calendar decision; letting
        //   10:30 leak into `nbf` would make two licences created on one day differ by minutes.
        var shell = NewShell();
        shell.LicenseNotBefore = new DateTime(2026, 9, 1, 17, 45, 12);
        shell.SaveLicenseCommand.Execute(null);

        var licence = Assert.Single(_manager.Register.GetLicenses(shell.SelectedCustomer!.CustomerId));

        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), licence.NotBefore);
    }

    [Fact]
    public void AClearedStartDateIsRefusedAndNothingIsWritten()
    {
        // ⭐ Empty is now the only date fault that can reach the view model: text that does not parse never
        //   becomes a SelectedDate at all. It still has to be REFUSED rather than defaulted — a licence
        //   quietly starting today because a field was blank is a term nobody agreed to.
        var shell = NewShell();
        shell.LicenseNotBefore = null;

        shell.SaveLicenseCommand.Execute(null);

        Assert.True(shell.IsWarning);
        Assert.Contains("start date", shell.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_manager.Register.GetLicenses(shell.SelectedCustomer!.CustomerId));
    }

    [Fact]
    public void AClearedExpiryIsRefusedAndNothingIsWritten()
    {
        var shell = NewShell();
        shell.LicenseExpiresAt = null;

        shell.SaveLicenseCommand.Execute(null);

        Assert.True(shell.IsWarning);
        Assert.Contains("expiry date", shell.MessageText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_manager.Register.GetLicenses(shell.SelectedCustomer!.CustomerId));
    }

    [Fact]
    public void AnExpiryBeforeTheStartIsStillRefused()
    {
        var shell = NewShell();
        shell.LicenseNotBefore = new DateTime(2027, 1, 1);
        shell.LicenseExpiresAt = new DateTime(2026, 1, 1);

        shell.SaveLicenseCommand.Execute(null);

        Assert.True(shell.IsWarning);
        Assert.Empty(_manager.Register.GetLicenses(shell.SelectedCustomer!.CustomerId));
    }

    [Fact]
    public void AOneDayLicenceIsLegalBecauseTheExpiryRunsToTheEndOfItsDay()
    {
        // ⚠ Start and expiry on the SAME day. With midnight-to-midnight this is an empty interval and the
        //   "expiry must be after the start" check would refuse it; with the end-of-day rule it is a
        //   perfectly ordinary one-day licence.
        var shell = NewShell();
        shell.LicenseNotBefore = new DateTime(2026, 9, 1);
        shell.LicenseExpiresAt = new DateTime(2026, 9, 1);

        shell.SaveLicenseCommand.Execute(null);

        var licence = Assert.Single(_manager.Register.GetLicenses(shell.SelectedCustomer!.CustomerId));
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 23, 59, 59, TimeSpan.Zero), licence.ExpiresAt);
    }

    [Fact]
    public void ReopeningALicenceShowsTheDaysTheOperatorChose()
    {
        // ⚠ The round trip the operator sees: 23:59:59 is stored, but the picker must show the DAY, not a
        //   day that looks like it ends a second early.
        var shell = NewShell();
        shell.LicenseNotBefore = new DateTime(2026, 9, 1);
        shell.LicenseExpiresAt = new DateTime(2027, 9, 1);
        shell.SaveLicenseCommand.Execute(null);

        shell.SelectedLicense = null;
        shell.SelectedLicense = shell.Licenses.Single();

        Assert.Equal(new DateTime(2026, 9, 1), shell.LicenseNotBefore);
        Assert.Equal(new DateTime(2027, 9, 1), shell.LicenseExpiresAt);
    }

    public void Dispose() => _manager.Dispose();
}

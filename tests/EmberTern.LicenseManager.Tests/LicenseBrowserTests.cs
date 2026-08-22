using System;
using System.Linq;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The licences view's own logic, with no window in sight.
///
/// <para>⭐ Everything the operator relies on to trust the list — that a filter means what its label
/// says, that a search narrows rather than reorders, that "expiring within 30 days" does not quietly
/// include people who lapsed last year — is decided here, in a class with no Avalonia types. A window is
/// needed to prove it RENDERS; it is not needed to prove it is RIGHT.</para>
/// </summary>
public sealed class LicenseBrowserTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 10, 0, 0, TimeSpan.Zero);

    private readonly LicenseRegister _register =
        LicenseRegister.OpenInMemory(() => Now, actor: "tester");

    private LicenseBrowserViewModel NewBrowser() => new(_register, () => Now);

    // ── Filters mean what their labels say ──────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultViewShowsEverythingAndSaysSo()
    {
        Seed("c-0001", "ACME", "lid-1");
        Seed("c-0002", "Beta", "lid-2");

        var browser = NewBrowser();

        Assert.Equal(2, browser.Results.Count);
        Assert.True(browser.HasResults);
        Assert.False(browser.IsFiltered);
        Assert.Equal("2 licences.", browser.ResultSummary);
    }

    [Fact]
    public void AnEmptyRegisterSaysSomethingDifferentFromAnEmptyResult()
    {
        // ⚠ "Nothing matches" and "there is nothing" are different facts, and telling an operator the
        //    first when the second is true sends them hunting for a filter they never set.
        var browser = NewBrowser();
        Assert.Equal("The register holds no licences yet.", browser.ResultSummary);

        Seed("c-0001", "ACME", "lid-1");
        browser.Refresh();
        browser.SearchText = "nobody";

        Assert.Equal("No licence matches these filters.", browser.ResultSummary);
        Assert.False(browser.HasResults);
        Assert.True(browser.IsFiltered);
    }

    [Fact]
    public void ExpiringWithinThirtyDaysDoesNotIncludeWhatAlreadyLapsed()
    {
        // ⭐⭐ The distinction the whole expiry filter exists for. A renewal list that silently mixes in
        //     customers who are already locked out is a list an operator stops trusting after one call.
        Seed("c-0001", "Lapsed", "lid-old", expiresAt: Now.AddDays(-20));
        Seed("c-0002", "Soon", "lid-soon", expiresAt: Now.AddDays(10));
        Seed("c-0003", "Later", "lid-later", expiresAt: Now.AddDays(200));

        var browser = NewBrowser();
        browser.SelectedExpiry = browser.ExpiryFilters.Single(f => f.WithinDays == 30);

        Assert.Equal("lid-soon", Assert.Single(browser.Results).Summary.License.LicenseId);
    }

    [Fact]
    public void AlreadyExpiredFindsExactlyTheOnesThatLapsed()
    {
        Seed("c-0001", "Lapsed", "lid-old", expiresAt: Now.AddDays(-20));
        Seed("c-0002", "Soon", "lid-soon", expiresAt: Now.AddDays(10));

        var browser = NewBrowser();
        browser.SelectedExpiry = browser.ExpiryFilters.Single(f => f.Expired);

        Assert.Equal("lid-old", Assert.Single(browser.Results).Summary.License.LicenseId);
    }

    [Fact]
    public void TheExpiryFilterIsReadableAsAQueryBeforeItRuns()
    {
        // ⭐ The filter's narrowing is DATA, so the mapping "label → query" can be asserted directly
        //   instead of inferred from what came back.
        var browser = NewBrowser();

        browser.SelectedExpiry = browser.ExpiryFilters.Single(f => f.WithinDays == 90);
        var window = browser.BuildQuery(Now);
        Assert.Equal(Now, window.ExpiresFrom);
        Assert.Equal(Now.AddDays(90), window.ExpiresBefore);

        browser.SelectedExpiry = browser.ExpiryFilters.Single(f => f.Expired);
        var lapsed = browser.BuildQuery(Now);
        Assert.Null(lapsed.ExpiresFrom);
        Assert.Equal(Now, lapsed.ExpiresBefore);

        browser.SelectedExpiry = browser.ExpiryFilters[0];
        var any = browser.BuildQuery(Now);
        Assert.Null(any.ExpiresFrom);
        Assert.Null(any.ExpiresBefore);
    }

    [Fact]
    public void TheStatusFilterOffersNoSupersededOption()
    {
        // ⭐ D-A, made visible in the UI: superseded is a fact about an ARTIFACT, never about a licence
        //   row. An option here would be one nothing can ever match.
        var browser = NewBrowser();

        Assert.DoesNotContain(
            browser.StatusFilters, f => string.Equals(f.Status, "superseded", StringComparison.Ordinal));
        Assert.Equal(
            ["", LicenseStatuses.Active, LicenseStatuses.Blocked],
            browser.StatusFilters.Select(f => f.Status ?? string.Empty).ToArray());
    }

    [Fact]
    public void TheStatusFilterNarrowsToBlockedLicences()
    {
        Seed("c-0001", "ACME", "lid-1");
        Seed("c-0002", "Beta", "lid-2", status: LicenseStatuses.Blocked);

        var browser = NewBrowser();
        browser.SelectedStatus = browser.StatusFilters.Single(f => f.Status == LicenseStatuses.Blocked);

        Assert.Equal("lid-2", Assert.Single(browser.Results).Summary.License.LicenseId);
    }

    [Fact]
    public void TheIssuingFilterFindsTheLicenceNobodySent()
    {
        Seed("c-0001", "Sent", "lid-sent");
        Seed("c-0002", "Forgotten", "lid-forgotten");
        _register.AppendArtifact(Artifact("lid-sent"));

        var browser = NewBrowser();

        browser.SelectedIssuing = browser.IssuingFilters.Single(f => f.NeverIssued == true);
        Assert.Equal("lid-forgotten", Assert.Single(browser.Results).Summary.License.LicenseId);

        browser.SelectedIssuing = browser.IssuingFilters.Single(f => f.NeverIssued == false);
        Assert.Equal("lid-sent", Assert.Single(browser.Results).Summary.License.LicenseId);
    }

    [Fact]
    public void FiltersCombineRatherThanReplaceEachOther()
    {
        Seed("c-0001", "ACME", "lid-1", expiresAt: Now.AddDays(10));
        Seed("c-0002", "ACME Two", "lid-2", expiresAt: Now.AddDays(10), status: LicenseStatuses.Blocked);
        Seed("c-0003", "Beta", "lid-3", expiresAt: Now.AddDays(10));

        var browser = NewBrowser();
        browser.SearchText = "acme";
        browser.SelectedStatus = browser.StatusFilters.Single(f => f.Status == LicenseStatuses.Active);
        browser.SelectedExpiry = browser.ExpiryFilters.Single(f => f.WithinDays == 30);

        Assert.Equal("lid-1", Assert.Single(browser.Results).Summary.License.LicenseId);
    }

    // ── Search ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SearchingIsLiveAndCaseInsensitiveThroughPolishDiacritics()
    {
        Seed("c-0001", "Łódzka Fabryka Śrub", "lid-1");
        Seed("c-0002", "ACME", "lid-2");

        var browser = NewBrowser();
        browser.SearchText = "łódzka";

        Assert.Equal("lid-1", Assert.Single(browser.Results).Summary.License.LicenseId);
    }

    [Fact]
    public void ClearingRestoresEverythingInOneQuery()
    {
        Seed("c-0001", "ACME", "lid-1", expiresAt: Now.AddDays(-5));
        Seed("c-0002", "Beta", "lid-2");

        var browser = NewBrowser();
        browser.SearchText = "acme";
        browser.SelectedExpiry = browser.ExpiryFilters.Single(f => f.Expired);
        Assert.Single(browser.Results);

        browser.ClearFiltersCommand.Execute(null);

        Assert.Equal(2, browser.Results.Count);
        Assert.False(browser.IsFiltered);
        Assert.Equal(string.Empty, browser.SearchText);
        Assert.Same(browser.ExpiryFilters[0], browser.SelectedExpiry);
    }

    // ── Selection ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSelectionSurvivesTheNextKeystroke()
    {
        // ⚠ Without this, typing one more character clears the detail strip the operator is reading —
        //   the list is rebuilt from scratch on every change, so the selected OBJECT is a different one.
        Seed("c-0001", "ACME Sp. z o.o.", "lid-1");
        Seed("c-0002", "Beta", "lid-2");

        var browser = NewBrowser();
        browser.SearchText = "ACM";
        browser.SelectedLicense = browser.Results.Single();

        browser.SearchText = "ACME";

        Assert.NotNull(browser.SelectedLicense);
        Assert.Equal("lid-1", browser.SelectedLicense!.Summary.License.LicenseId);
        Assert.True(browser.HasSelection);
    }

    [Fact]
    public void ASelectionThatFallsOutOfTheResultsIsDropped()
    {
        Seed("c-0001", "ACME", "lid-1");
        Seed("c-0002", "Beta", "lid-2");

        var browser = NewBrowser();
        browser.SelectedLicense = browser.Results.Single(r => r.Summary.License.LicenseId == "lid-1");

        browser.SearchText = "Beta";

        Assert.Null(browser.SelectedLicense);
        Assert.False(browser.HasSelection);
    }

    [Fact]
    public void TheDetailLineDistinguishesNeverIssuedFromIssuedOnce()
    {
        Seed("c-0001", "ACME", "lid-1");

        var browser = NewBrowser();
        browser.SelectedLicense = browser.Results.Single();
        Assert.Contains("never issued", browser.SelectedDetail, StringComparison.Ordinal);

        _register.AppendArtifact(Artifact("lid-1"));
        browser.Refresh();
        browser.SelectedLicense = browser.Results.Single();
        Assert.Contains("issued once", browser.SelectedDetail, StringComparison.Ordinal);

        _register.AppendArtifact(Artifact("lid-1", Now.AddSeconds(1)));
        browser.Refresh();
        browser.SelectedLicense = browser.Results.Single();
        Assert.Contains("issued 2 times", browser.SelectedDetail, StringComparison.Ordinal);

        Assert.Equal("lid-1", browser.SelectedLicenseId);
    }

    // ── The row an operator scans ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "Expires today")]
    [InlineData(1, "Expires tomorrow")]
    [InlineData(12, "Expires in 12 days")]
    [InlineData(-1, "Expired yesterday")]
    [InlineData(-9, "Expired 9 days ago")]
    public void StandingReadsAsASentenceRatherThanASignedNumber(int inDays, string expected)
    {
        // ⚠ "Expires in 0 days" for something lapsing tonight reads as a bug, and a negative day count
        //    reads as nothing at all.
        Seed("c-0001", "ACME", "lid-1", expiresAt: Now.AddDays(inDays));
        _register.AppendArtifact(Artifact("lid-1"));

        var browser = NewBrowser();

        Assert.Equal(expected, browser.Results.Single().Standing);
    }

    [Fact]
    public void NeverIssuedOutranksTheDate()
    {
        // ⭐ A licence nobody received has no meaningful remaining time — saying "expires in 300 days"
        //   about a file that was never sent is the more misleading of the two true statements.
        Seed("c-0001", "ACME", "lid-1", expiresAt: Now.AddDays(300));

        Assert.Equal("Never issued", NewBrowser().Results.Single().Standing);
    }

    [Fact]
    public void SeatsAreCountedInWordsThatMatchTheNumber()
    {
        Seed("c-0001", "One", "lid-1", seats: 1);
        Seed("c-0002", "Many", "lid-2", seats: 5);

        var browser = NewBrowser();

        Assert.Equal("1 seat", Row(browser, "lid-1").Seats);
        Assert.Equal("5 seats", Row(browser, "lid-2").Seats);
    }

    [Fact]
    public void ALongLicenceIdIsShortenedForTheListAndKeptWholeForTheDetail()
    {
        var id = new string('a', 32);
        Seed("c-0001", "ACME", id);

        var browser = NewBrowser();
        browser.SelectedLicense = browser.Results.Single();

        Assert.Equal(new string('a', 12) + "…", browser.SelectedLicense!.ShortId);
        Assert.Equal(id, browser.SelectedLicenseId);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static LicenseListItem Row(LicenseBrowserViewModel browser, string licenseId) =>
        browser.Results.Single(r => r.Summary.License.LicenseId == licenseId);

    private void Seed(
        string customerId,
        string name,
        string licenseId,
        DateTimeOffset? expiresAt = null,
        string? status = null,
        int seats = 1)
    {
        if (_register.GetCustomer(customerId) is null)
        {
            _register.SaveCustomer(new CustomerRecord { CustomerId = customerId, Name = name });
        }

        _register.SaveLicense(new LicenseRecord
        {
            LicenseId = licenseId,
            CustomerId = customerId,
            Product = "EmberTern",
            Seats = seats,
            NotBefore = Now.AddYears(-1),
            ExpiresAt = expiresAt ?? Now.AddYears(1),
            Status = status ?? LicenseStatuses.Active,
        });
    }

    private static IssuedArtifactRecord Artifact(string licenseId, DateTimeOffset? issuedAt = null) => new()
    {
        LicenseId = licenseId,
        KeyId = "R1",
        IssuedAt = issuedAt ?? Now,
        PayloadJson = """{"lv":1}""",
        Token = "ETL1.payload.signature",
        Reason = IssueReasons.Initial,
    };

    public void Dispose() => _register.Dispose();
}

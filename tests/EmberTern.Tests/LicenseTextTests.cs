using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.App.Licensing;
using EmberTern.App.Localization;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>Architecture rule 12 for licensing, pinned the way Phase 5 taught us to pin it.</b>
///
/// <para>The failure mode that made this necessary is not a missing entry — it is a PERFECT entry that
/// nothing reads. Phase 5 shipped correct Polish and English for the charset guard and still showed a
/// Polish user a fully English paragraph, with a green build and green tests, because the value was
/// wrapped on the way out and the display site read the wrong member. Licensing has the identical shape:
/// a verdict produced in a pure library, surfaced by App.</para>
///
/// <para>⭐ So these tests resolve every message <b>through <see cref="LicenseText"/></b> — the path the UI
/// will actually use — in <b>both</b> languages, and assert the two differ. A translated resource nobody
/// reads therefore fails here rather than reaching a customer.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public class LicenseTextTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> EveryFailure() =>
        Enum.GetValues<LicenseFailure>().Where(f => f != LicenseFailure.None).Select(f => new object[] { f });

    public static IEnumerable<object[]> EveryStatus() =>
        Enum.GetValues<LicenseStatus>().Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(EveryFailure))]
    public void EveryFailureSaysSomethingInBothLanguages(LicenseFailure failure)
    {
        var english = InLanguage("en", () => LicenseText.ExplainFailure(failure));
        var polish = InLanguage("pl", () => LicenseText.ExplainFailure(failure));

        Assert.False(string.IsNullOrWhiteSpace(english), $"{failure} has no English sentence.");
        Assert.False(string.IsNullOrWhiteSpace(polish), $"{failure} has no Polish sentence.");

        // ⭐ The assertion that catches "a perfect entry nothing reads": if the display path did not resolve
        //   through the catalog, both languages would come back identical.
        Assert.NotEqual(english, polish);

        // ⛔ Never a resource key or a code leaking onto the screen.
        Assert.DoesNotContain("Licensing", english, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", polish, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryStatus))]
    public void EveryStatusHasAHeadlineInBothLanguages(LicenseStatus status)
    {
        var verdict = VerdictFor(status);

        var english = InLanguage("en", () => LicenseText.Headline(verdict));
        var polish = InLanguage("pl", () => LicenseText.Headline(verdict));

        Assert.False(string.IsNullOrWhiteSpace(english));
        Assert.False(string.IsNullOrWhiteSpace(polish));
        Assert.NotEqual(english, polish);
    }

    [Fact]
    public void TheValidSentenceCarriesTheLicenseeAndTheExpiryAsArguments()
    {
        // ⚠ Dynamic values travel as ARGUMENTS into a whole sentence from the catalog. ⛔ Never a fragment
        //    concatenated in code: word order is the translator's decision, not English's.
        using var fixtures = new LicenseFixtures();
        var verdict = Verify(fixtures, fixtures.Valid(Moment));

        foreach (var language in new[] { "en", "pl" })
        {
            var sentence = InLanguage(language, () => LicenseText.Explain(verdict));

            Assert.Contains(LicenseFixtures.Licensee, sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("{0}", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("{1}", sentence, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NoExplanationLeavesAPlaceholderUnfilled()
    {
        // ⭐ A format template rendered with the wrong number of arguments leaves `{1}` on screen. Cheap to
        //   check, and impossible to notice in a language you do not read.
        using var fixtures = new LicenseFixtures();

        var verdicts = new[]
        {
            Verify(fixtures, fixtures.Valid(Moment)),
            Verify(fixtures, fixtures.Issue(Moment.AddYears(-1), Moment.AddYears(-1), Moment.AddDays(-3))),
            Verify(fixtures, fixtures.Issue(Moment.AddYears(-1), Moment.AddYears(-1), Moment.AddDays(-30))),
            Verify(fixtures, fixtures.Issue(Moment, Moment.AddDays(10), Moment.AddYears(1))),
            LicenseVerdict.Unlicensed,
        };

        foreach (var verdict in verdicts)
        {
            foreach (var language in new[] { "en", "pl" })
            {
                var sentence = InLanguage(language, () => LicenseText.Explain(verdict));

                Assert.DoesNotContain("{", sentence, StringComparison.Ordinal);
                Assert.False(string.IsNullOrWhiteSpace(sentence));
            }
        }
    }

    [Fact]
    public void TheCopyDetailsTokenIsTechnicalAndNotTranslated()
    {
        // ⛔ `Detail` is never rendered as prose (design §9.1). It exists so a customer can paste something
        //    exact into an e-mail — it is a token for us, not a sentence for them, so it does NOT translate.
        var verdict = LicenseVerdict.Unlicensed;

        var english = InLanguage("en", () => LicenseText.Details(verdict, @"C:\somewhere\license.etlic"));
        var polish = InLanguage("pl", () => LicenseText.Details(verdict, @"C:\somewhere\license.etlic"));

        Assert.Equal(english, polish);
        Assert.Contains("status=", english, StringComparison.Ordinal);
        Assert.Contains("failure=", english, StringComparison.Ordinal);
    }

    private static LicenseVerdict Verify(LicenseFixtures fixtures, string licence) =>
        LicenseVerifier.Verify(
            licence,
            new LicenseVerificationContext(
                fixtures.TrustedKeys,
                Moment,
                LicenseConstants.ProductId,
                LicenseConstants.MaxSupportedPayloadVersion,
                LicenseConstants.DefaultGracePeriod,
                BuildReleaseDate: null));

    private static LicenseVerdict VerdictFor(LicenseStatus status)
    {
        if (status == LicenseStatus.Unlicensed)
        {
            return LicenseVerdict.Unlicensed;
        }

        using var fixtures = new LicenseFixtures();

        return status switch
        {
            LicenseStatus.Valid => Verify(fixtures, fixtures.Valid(Moment)),
            LicenseStatus.Grace => Verify(
                fixtures, fixtures.Issue(Moment.AddYears(-1), Moment.AddYears(-1), Moment.AddDays(-3))),
            LicenseStatus.Expired => Verify(
                fixtures, fixtures.Issue(Moment.AddYears(-1), Moment.AddYears(-1), Moment.AddDays(-30))),
            LicenseStatus.NotYetValid => Verify(
                fixtures, fixtures.Issue(Moment, Moment.AddDays(10), Moment.AddYears(1))),
            LicenseStatus.VersionNotCovered => Verify(
                fixtures,
                fixtures.Issue(Moment, Moment, Moment.AddYears(1), maintenanceUntil: Moment.AddYears(-5))),
            _ => Verify(fixtures, "not a licence"),
        };
    }

    /// <summary>
    /// Resolves a string with the given language in force, then restores the previous one.
    ///
    /// <para>⚠ <c>Loc</c> is process-wide static state, which is why this class joins
    /// <c>HeadlessCollection</c> — the collection that isolates the global language for verification.</para>
    /// </summary>
    private static string InLanguage(string language, Func<string> read)
    {
        var previous = Loc.Culture.TwoLetterISOLanguageName;
        try
        {
            Loc.Apply(language);
            return read();
        }
        finally
        {
            Loc.Apply(previous);
        }
    }
}

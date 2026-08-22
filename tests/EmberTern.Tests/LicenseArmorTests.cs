using System;
using System.Linq;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The <c>-----BEGIN EMBERTERN LICENSE-----</c> wrapper.
///
/// <para>⭐ The armor exists for one measured reason — a licence travels by e-mail and mail clients wrap
/// long lines — so the load-bearing test in this file is not <c>Wrap</c> or <c>TryUnwrap</c> in isolation,
/// it is <see cref="SurvivesTheMangleAnEmailClientApplies"/>. If that one ever goes red the format has
/// stopped doing the job it was chosen for.</para>
/// </summary>
public sealed class LicenseArmorTests
{
    private const string SampleToken = "ETL1.eyJhIjoxfQ.c2lnbmF0dXJlLWJ5dGVz";

    [Fact]
    public void WrapProducesMarkersAndFixedLineEndings()
    {
        var armored = LicenseArmor.Wrap(new string('A', 200));

        Assert.StartsWith(LicenseArmor.BeginMarker, armored, StringComparison.Ordinal);
        Assert.EndsWith(LicenseArmor.EndMarker + LicenseArmor.LineSeparator, armored, StringComparison.Ordinal);

        // ⭐ CRLF, not Environment.NewLine: the file crosses machines and mail transports, so its bytes
        //    must not depend on the platform that produced it.
        Assert.Contains("\r\n", armored, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n", armored, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapKeepsBodyLinesWithinTheLineLength()
    {
        var armored = LicenseArmor.Wrap(new string('A', 500));

        var bodyLines = armored
            .Split(LicenseArmor.LineSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith("-----", StringComparison.Ordinal));

        Assert.All(bodyLines, line => Assert.True(line.Length <= LicenseArmor.LineLength));
    }

    [Fact]
    public void WrapThenUnwrapIsTheIdentity()
    {
        Assert.True(LicenseArmor.TryUnwrap(LicenseArmor.Wrap(SampleToken), out var token, out var failure));
        Assert.Equal(SampleToken, token);
        Assert.Equal(LicenseFailure.None, failure);
    }

    [Fact]
    public void ABareTokenIsAccepted()
    {
        Assert.True(LicenseArmor.TryUnwrap(SampleToken, out var token, out _));
        Assert.Equal(SampleToken, token);
    }

    [Fact]
    public void TextAroundTheArmorIsIgnored()
    {
        var pasted =
            "Dzień dobry,\r\n\r\nw załączeniu licencja.\r\n\r\n" +
            LicenseArmor.Wrap(SampleToken) +
            "\r\n--\r\nPozdrawiam,\r\nGrzegorz\r\n";

        Assert.True(LicenseArmor.TryUnwrap(pasted, out var token, out _));
        Assert.Equal(SampleToken, token);
    }

    [Fact]
    public void SurvivesTheMangleAnEmailClientApplies()
    {
        // ⭐ THE test this whole mechanism exists for. A mail transport re-wrapped the body at a width of
        //    its own choosing, switched to bare LF, and indented some lines. The token must come back
        //    byte-identical anyway.
        var original = "ETL1." + new string('A', 137) + "." + new string('B', 86);

        var mangled = LicenseArmor.BeginMarker + "\n";
        for (var offset = 0; offset < original.Length; offset += 31)
        {
            var length = Math.Min(31, original.Length - offset);
            mangled += "   " + original.Substring(offset, length) + " \n";
        }

        mangled += LicenseArmor.EndMarker + "\n";

        Assert.True(LicenseArmor.TryUnwrap(mangled, out var token, out var failure));
        Assert.Equal(original, token);
        Assert.Equal(LicenseFailure.None, failure);
    }

    [Fact]
    public void QuoteMarkersAreNotStripped()
    {
        // ⚠ The boundary of what the armor repairs: WRAPPING, not vandalism. A '>' from a quoted reply
        //    survives into the token and is refused later, at the base64url alphabet check — an honest
        //    MalformedEnvelope rather than a silent guess about which characters were "meant".
        Assert.True(LicenseArmor.TryUnwrap("> " + SampleToken, out var token, out _));
        Assert.Contains('>', token);
    }

    [Fact]
    public void WhitespaceOnlyBodyIsNotALicense()
    {
        var armored = LicenseArmor.BeginMarker + "\r\n   \r\n" + LicenseArmor.EndMarker;

        Assert.False(LicenseArmor.TryUnwrap(armored, out _, out var failure));
        Assert.Equal(LicenseFailure.NotALicense, failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    \r\n\t ")]
    public void NothingAtAllIsNotALicense(string? text)
    {
        Assert.False(LicenseArmor.TryUnwrap(text, out _, out var failure));
        Assert.Equal(LicenseFailure.NotALicense, failure);
    }

    [Fact]
    public void AnUnbalancedWrapperIsRefusedRatherThanGuessedAt()
    {
        Assert.False(LicenseArmor.TryUnwrap(LicenseArmor.BeginMarker + "\r\nabc\r\n", out _, out var noEnd));
        Assert.Equal(LicenseFailure.MalformedArmor, noEnd);

        Assert.False(LicenseArmor.TryUnwrap("abc\r\n" + LicenseArmor.EndMarker, out _, out var noBegin));
        Assert.Equal(LicenseFailure.MalformedArmor, noBegin);
    }

    [Fact]
    public void TwoLicencesInOneFileAreRefused()
    {
        // ⭐ Picking one would be a guess about which the user meant, and the wrong guess activates the
        //    wrong licence silently. Refusing lets the caller show them the file.
        var two = LicenseArmor.Wrap(SampleToken) + LicenseArmor.Wrap(SampleToken);

        Assert.False(LicenseArmor.TryUnwrap(two, out _, out var failure));
        Assert.Equal(LicenseFailure.MalformedArmor, failure);
    }
}

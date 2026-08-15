using System;
using System.Text;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The <c>ETL1.&lt;payload&gt;.&lt;signature&gt;</c> structure and the bytes a signature covers.
/// </summary>
public sealed class LicenseEnvelopeTests
{
    [Fact]
    public void ComposeAndParseRoundTrip()
    {
        var payloadJson = Encoding.UTF8.GetBytes("""{"lv":1}""");
        var signature = new byte[64];
        Random.Shared.NextBytes(signature);

        var token = LicenseEnvelope.Compose(LicenseEnvelope.EncodePayload(payloadJson), signature);

        Assert.True(LicenseEnvelope.TryParse(token, out var envelope, out var failure));
        Assert.Equal(LicenseFailure.None, failure);
        Assert.Equal(payloadJson, envelope.PayloadJson);
        Assert.Equal(signature, envelope.Signature);
    }

    [Fact]
    public void TheSigningInputCarriesTheMagic()
    {
        // ⭐ Why it matters: the magic inside the signing input is what stops an ETL1 artifact from ever
        //    being replayed under a future envelope generation. An ETL2 verifier computes a different
        //    signing input over the same payload and rejects the old signature.
        var segment = LicenseEnvelope.EncodePayload(Encoding.UTF8.GetBytes("""{"lv":1}"""));
        var token = LicenseEnvelope.Compose(segment, new byte[64]);

        Assert.True(LicenseEnvelope.TryParse(token, out var envelope, out _));

        var expected = Encoding.ASCII.GetBytes(LicenseEnvelope.Magic + "." + segment);
        Assert.Equal(expected, envelope.SigningInput);
        Assert.Equal(expected, LicenseEnvelope.BuildSigningInput(segment));
    }

    [Fact]
    public void TheSignatureCoversTheEncodedSegmentExactlyAsItArrived()
    {
        // ⭐ Architecture rule 11 in a signature format: the verifier must never re-serialise. Two JSON
        //    texts that mean the same thing produce DIFFERENT signing inputs, which is the whole reason
        //    there is no canonicalisation requirement anywhere in this design.
        var compact = LicenseEnvelope.EncodePayload(Encoding.UTF8.GetBytes("""{"lv":1,"kid":"T1"}"""));
        var spaced = LicenseEnvelope.EncodePayload(Encoding.UTF8.GetBytes("""{ "lv": 1, "kid": "T1" }"""));

        Assert.NotEqual(
            LicenseEnvelope.BuildSigningInput(compact),
            LicenseEnvelope.BuildSigningInput(spaced));
    }

    [Theory]
    [InlineData("ETL1.")]
    [InlineData("ETL1.abc")]
    [InlineData("ETL1.abc.def.ghi")]
    [InlineData("ETL1..abc")]
    [InlineData("ETL1.abc.")]
    public void AWrongShapeIsAMalformedEnvelope(string token)
    {
        Assert.False(LicenseEnvelope.TryParse(token, out _, out var failure));
        Assert.Equal(LicenseFailure.MalformedEnvelope, failure);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("etl1.abc.def")]      // the magic is case-sensitive
    [InlineData("ETLX.abc.def")]      // shaped like ours, but no version digits
    [InlineData("ETL.abc.def")]
    public void SomethingElseEntirelyIsNotALicense(string token)
    {
        Assert.False(LicenseEnvelope.TryParse(token, out _, out var failure));
        Assert.Equal(LicenseFailure.NotALicense, failure);
    }

    [Theory]
    [InlineData("ETL2.abc.def")]
    [InlineData("ETL9.abc.def")]
    [InlineData("ETL10.abc.def")]
    public void AFutureEnvelopeGenerationSaysSoRatherThanCryingForgery(string token)
    {
        // ⭐ Both are refusals, but only this one has something useful to tell the user: update the
        //    product. Reporting it as "not a licence" would send them to support with the wrong question.
        Assert.False(LicenseEnvelope.TryParse(token, out _, out var failure));
        Assert.Equal(LicenseFailure.UnsupportedVersion, failure);
    }

    [Theory]
    [InlineData("ETL1.eyJhIjox+Q.YWJj")]   // '+' — standard base64, not base64url
    [InlineData("ETL1.eyJhIjox/Q.YWJj")]   // '/' — likewise
    [InlineData("ETL1.eyJhIjoxfQ==.YWJj")] // padding is not part of the format
    [InlineData("ETL1.eyJhIjoxfQ.YWJ!")]
    [InlineData("ETL1.A.YWJj")]            // length 1 mod 4 — no base64 output has this
    public void AStrictAlphabetIsEnforced(string token)
    {
        // ⛔ A lenient decoder would let two different texts decode to the same bytes, which is the exact
        //    ambiguity that produces signature-confusion bugs in token formats.
        Assert.False(LicenseEnvelope.TryParse(token, out _, out var failure));
        Assert.Equal(LicenseFailure.MalformedEnvelope, failure);
    }
}

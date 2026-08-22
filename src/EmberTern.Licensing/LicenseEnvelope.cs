using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace EmberTern.Licensing;

/// <summary>
/// The <c>ETL1.&lt;payload&gt;.&lt;signature&gt;</c> structure.
///
/// <para>⭐ <b>The magic is inside the signing input.</b> The bytes that get signed are
/// <c>"ETL1." + payloadSegment</c>, not the payload alone, so a token can never be replayed under a future
/// envelope generation — an ETL2 verifier would compute a different signing input over the same payload
/// and reject it.</para>
///
/// <para>⭐ <b>The signature covers the ENCODED segment.</b> <see cref="SigningInput"/> is built from the
/// characters as they arrived, never from a re-serialised object. There is no canonical-JSON requirement,
/// no key-ordering dependency, and no way for a parse/re-serialise round trip to change what was verified.
/// This is what Architecture rule 11 looks like in a signature format.</para>
/// </summary>
public sealed class LicenseEnvelope
{
    /// <summary>The envelope generation this build reads and writes.</summary>
    public const string Magic = "ETL1";

    private const char SegmentSeparator = '.';

    private LicenseEnvelope(string payloadSegment, byte[] payloadJson, byte[] signature, byte[] signingInput)
    {
        PayloadSegment = payloadSegment;
        PayloadJson = payloadJson;
        Signature = signature;
        SigningInput = signingInput;
    }

    /// <summary>The payload segment exactly as it appeared in the token.</summary>
    public string PayloadSegment { get; }

    /// <summary>The decoded payload — UTF-8 JSON. ⚠ Untrusted until the signature has been verified.</summary>
    public byte[] PayloadJson { get; }

    /// <summary>The raw signature. For <see cref="SignatureAlgorithm.EcdsaP256Sha256"/> this is 64 bytes.</summary>
    public byte[] Signature { get; }

    /// <summary>The exact bytes the signature is computed over: ASCII <c>"ETL1." + payloadSegment</c>.</summary>
    public byte[] SigningInput { get; }

    /// <summary>
    /// Parses a bare token (already unwrapped by <see cref="LicenseArmor"/>). Never throws.
    /// </summary>
    public static bool TryParse(
        string token,
        [NotNullWhen(true)] out LicenseEnvelope? envelope,
        out LicenseFailure failure)
    {
        ArgumentNullException.ThrowIfNull(token);
        envelope = null;

        if (!token.StartsWith(Magic + SegmentSeparator, StringComparison.Ordinal))
        {
            // ⭐ Distinguish "a licence from a future EmberTern" from "not a licence at all". Both are
            // refusals, but only the first one has a useful thing to tell the user: update the product.
            failure = LooksLikeAFutureEnvelope(token)
                ? LicenseFailure.UnsupportedVersion
                : LicenseFailure.NotALicense;
            return false;
        }

        var parts = token.Split(SegmentSeparator);
        if (parts.Length != 3)
        {
            failure = LicenseFailure.MalformedEnvelope;
            return false;
        }

        var payloadSegment = parts[1];
        var signatureSegment = parts[2];

        if (payloadSegment.Length == 0 || signatureSegment.Length == 0)
        {
            failure = LicenseFailure.MalformedEnvelope;
            return false;
        }

        if (!Base64Url.TryDecode(payloadSegment, out var payloadJson) ||
            !Base64Url.TryDecode(signatureSegment, out var signature))
        {
            failure = LicenseFailure.MalformedEnvelope;
            return false;
        }

        var signingInput = Encoding.ASCII.GetBytes(Magic + SegmentSeparator + payloadSegment);

        envelope = new LicenseEnvelope(payloadSegment, payloadJson, signature, signingInput);
        failure = LicenseFailure.None;
        return true;
    }

    /// <summary>Builds a token from an already-encoded payload segment and a raw signature.</summary>
    public static string Compose(string payloadSegment, ReadOnlySpan<byte> signature)
    {
        ArgumentException.ThrowIfNullOrEmpty(payloadSegment);
        return Magic + SegmentSeparator + payloadSegment + SegmentSeparator + Base64Url.Encode(signature);
    }

    /// <summary>The bytes a signer must sign for a given encoded payload segment.</summary>
    public static byte[] BuildSigningInput(string payloadSegment)
    {
        ArgumentException.ThrowIfNullOrEmpty(payloadSegment);
        return Encoding.ASCII.GetBytes(Magic + SegmentSeparator + payloadSegment);
    }

    /// <summary>Encodes a UTF-8 JSON payload into its segment form.</summary>
    public static string EncodePayload(ReadOnlySpan<byte> payloadJson) => Base64Url.Encode(payloadJson);

    // "ETL" followed by at least one digit and then a '.' — i.e. shaped like one of our envelopes, but
    // not this generation. Anything else is somebody else's file.
    private static bool LooksLikeAFutureEnvelope(string token)
    {
        if (!token.StartsWith("ETL", StringComparison.Ordinal))
        {
            return false;
        }

        var i = 3;
        while (i < token.Length && char.IsAsciiDigit(token[i]))
        {
            i++;
        }

        return i > 3 && i < token.Length && token[i] == SegmentSeparator;
    }
}

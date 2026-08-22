using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The tamper corpus: a mutated artifact for every way a licence can be wrong, each asserted to be
/// refused <b>for the right reason</b>.
///
/// <para>⭐ <b>"For the right reason" is the whole point, and it is why this is not simply a set of
/// "should be rejected" assertions.</b> A verifier that refuses everything passes the naive version of
/// this test while being useless, and — worse — a verifier that refuses a valid-but-old artifact with
/// "invalid licence" instead of "update EmberTern" sends a paying customer to support with the wrong
/// question. The failure a user is shown is part of the contract, so it is part of the corpus.</para>
///
/// <para>⚠ <b>Adding a check to <see cref="LicenseVerifier"/> means adding cases here.</b> The corpus is
/// the specification of the refusing half of the verifier; the accepting half is
/// <see cref="LicenseVerifierTests"/>.</para>
/// </summary>
public sealed class LicenseTamperCorpusTests
{
    // ⚠ Declaration order is load-bearing: static field initializers run in TEXTUAL order, so anything
    //    Build() touches must be declared above Cases. Written the other way round, BaseFields is still
    //    null when Build() runs and every test in the file dies in the type initializer.
    private static readonly (string Name, string Value)[] BaseFields =
    [
        ("lv", "1"),
        ("kid", "\"T1\""),
        ("alg", "\"ES256-P1363\""),
        ("lid", "\"0191f3c4b2a741d89e0fa21c7d4e3056\""),
        ("prod", "\"EmberTern\""),
        ("lic", "\"ACME Sp. z o.o.\""),
        ("seats", "5"),
        ("iat", "\"2026-08-15T10:00:00Z\""),
        ("nbf", "\"2026-08-15T00:00:00Z\""),
        ("exp", "\"2027-08-15T23:59:59Z\""),
    ];

    private static readonly LicenseTestFactory Factory = new();
    private static readonly Dictionary<string, (string License, LicenseFailure Expected)> Cases = Build();

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Cases.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void EveryMutationIsRefusedForTheRightReason(string caseName)
    {
        var (license, expected) = Cases[caseName];

        var verdict = LicenseVerifier.Verify(license, Factory.Context());

        Assert.True(
            verdict.Status == LicenseStatus.Invalid,
            $"{caseName}: expected Invalid, got {verdict.Status}.");
        Assert.True(
            verdict.Failure == expected,
            $"{caseName}: expected {expected}, got {verdict.Failure} (detail: {verdict.Detail ?? "none"}).");
        Assert.True(
            verdict.Payload is null,
            $"{caseName}: a refused artifact handed up its contents.");
    }

    [Fact]
    public void TheCorpusCoversEveryRefusalTheVerifierCanProduce()
    {
        // ⭐ The guard against the corpus rotting: adding a LicenseFailure without a case here fails the
        //    build. FileMissing is excluded because the verifier cannot produce it — only a host that can
        //    see a filesystem can, and this assembly deliberately cannot.
        var producible = Enum.GetValues<LicenseFailure>()
            .Where(f => f is not (LicenseFailure.None or LicenseFailure.FileMissing));

        var covered = Cases.Values.Select(c => c.Expected).ToHashSet();
        var uncovered = producible.Where(f => !covered.Contains(f)).ToList();

        Assert.True(uncovered.Count == 0, "Refusals with no corpus case: " + string.Join(", ", uncovered));
    }

    [Fact]
    public void TheCorpusIsAtLeastAsLargeAsTheStageRequires()
    {
        Assert.True(Cases.Count >= 40, $"The corpus has only {Cases.Count} cases.");
    }

    // ── The corpus ──────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, (string, LicenseFailure)> Build()
    {
        var cases = new Dictionary<string, (string, LicenseFailure)>(StringComparer.Ordinal);
        var good = Factory.Sign(LicenseTestFactory.DefaultPayload);
        var payloadSegment = LicenseTestFactory.PayloadSegmentOf(good);
        var signature = LicenseTestFactory.SignatureOf(good);

        void Add(string name, string license, LicenseFailure expected) => cases.Add(name, (license, expected));

        // ── Armor ───────────────────────────────────────────────────────────────────────────────────
        Add("armor-empty", string.Empty, LicenseFailure.NotALicense);
        Add("armor-whitespace-only", "   \r\n\t ", LicenseFailure.NotALicense);
        Add("armor-prose", "Dzień dobry, w załączeniu licencja.", LicenseFailure.NotALicense);
        Add("armor-begin-without-end",
            LicenseArmor.BeginMarker + "\r\n" + good + "\r\n", LicenseFailure.MalformedArmor);
        Add("armor-end-without-begin",
            good + "\r\n" + LicenseArmor.EndMarker, LicenseFailure.MalformedArmor);
        Add("armor-two-blocks",
            LicenseArmor.Wrap(good) + LicenseArmor.Wrap(good), LicenseFailure.MalformedArmor);
        Add("armor-empty-body",
            LicenseArmor.BeginMarker + "\r\n\r\n" + LicenseArmor.EndMarker, LicenseFailure.NotALicense);

        // ── Envelope ────────────────────────────────────────────────────────────────────────────────
        Add("envelope-two-segments", "ETL1." + payloadSegment, LicenseFailure.MalformedEnvelope);
        Add("envelope-four-segments", good + ".extra", LicenseFailure.MalformedEnvelope);
        Add("envelope-empty-payload", "ETL1.." + good.Split('.')[2], LicenseFailure.MalformedEnvelope);
        Add("envelope-empty-signature", "ETL1." + payloadSegment + ".", LicenseFailure.MalformedEnvelope);
        Add("envelope-lowercase-magic", "etl1" + good[4..], LicenseFailure.NotALicense);
        Add("envelope-future-generation", "ETL2" + good[4..], LicenseFailure.UnsupportedVersion);
        Add("envelope-standard-base64-plus",
            "ETL1." + "+" + payloadSegment[1..] + "." + good.Split('.')[2],
            LicenseFailure.MalformedEnvelope);
        Add("envelope-padded-payload",
            "ETL1." + payloadSegment + "==." + good.Split('.')[2], LicenseFailure.MalformedEnvelope);
        Add("envelope-signature-not-base64url",
            "ETL1." + payloadSegment + ".abc!def", LicenseFailure.MalformedEnvelope);

        // ── Payload shape ───────────────────────────────────────────────────────────────────────────
        Add("payload-not-json", Factory.SignJson("not json at all"), LicenseFailure.MalformedPayload);
        Add("payload-array-root", Factory.SignJson("[]"), LicenseFailure.MalformedPayload);
        Add("payload-null-root", Factory.SignJson("null"), LicenseFailure.MalformedPayload);
        Add("payload-truncated", Factory.SignJson("{\"lv\":1,"), LicenseFailure.MalformedPayload);
        Add("payload-only-whitespace", Factory.SignJson("   "), LicenseFailure.MalformedPayload);

        foreach (var (name, _) in BaseFields)
        {
            Add($"payload-missing-{name}", Factory.SignJson(Json(without: name)),
                LicenseFailure.MalformedPayload);
        }

        Add("payload-lv-as-string", Factory.SignJson(Json(replace: [("lv", "\"1\"")])),
            LicenseFailure.MalformedPayload);
        Add("payload-lv-zero", Factory.SignJson(Json(replace: [("lv", "0")])),
            LicenseFailure.MalformedPayload);
        Add("payload-seats-as-string", Factory.SignJson(Json(replace: [("seats", "\"5\"")])),
            LicenseFailure.MalformedPayload);
        Add("payload-seats-negative", Factory.SignJson(Json(replace: [("seats", "-1")])),
            LicenseFailure.MalformedPayload);
        Add("payload-seats-fractional", Factory.SignJson(Json(replace: [("seats", "5.5")])),
            LicenseFailure.MalformedPayload);
        Add("payload-licensee-empty", Factory.SignJson(Json(replace: [("lic", "\"\"")])),
            LicenseFailure.MalformedPayload);
        Add("payload-licensee-null", Factory.SignJson(Json(replace: [("lic", "null")])),
            LicenseFailure.MalformedPayload);
        Add("payload-expiry-date-only",
            Factory.SignJson(Json(replace: [("exp", "\"2027-08-15\"")])), LicenseFailure.MalformedPayload);
        Add("payload-expiry-with-offset",
            Factory.SignJson(Json(replace: [("exp", "\"2027-08-15T23:59:59+02:00\"")])),
            LicenseFailure.MalformedPayload);
        Add("payload-expiry-not-a-date",
            Factory.SignJson(Json(replace: [("exp", "\"nigdy\"")])), LicenseFailure.MalformedPayload);

        // ── Version, key, algorithm ─────────────────────────────────────────────────────────────────
        // ⭐ These are signed CORRECTLY. They prove the checks are real gates, not side effects of a
        //    signature that happened to fail anyway.
        Add("version-two", Factory.SignJson(Json(replace: [("lv", "2")])),
            LicenseFailure.UnsupportedVersion);
        Add("version-far-future", Factory.SignJson(Json(replace: [("lv", "99")])),
            LicenseFailure.UnsupportedVersion);
        Add("key-unknown", Factory.SignJson(Json(replace: [("kid", "\"T2\"")])),
            LicenseFailure.UnknownKey);
        Add("key-revoked", Factory.SignJson(Json(replace: [("kid", "\"TR\"")])),
            LicenseFailure.RevokedKey);
        Add("algorithm-eddsa", Factory.SignJson(Json(replace: [("alg", "\"EdDSA\"")])),
            LicenseFailure.AlgorithmMismatch);
        Add("algorithm-bare-es256", Factory.SignJson(Json(replace: [("alg", "\"ES256\"")])),
            LicenseFailure.AlgorithmMismatch);
        Add("algorithm-wrong-case", Factory.SignJson(Json(replace: [("alg", "\"es256-p1363\"")])),
            LicenseFailure.AlgorithmMismatch);
        Add("algorithm-none", Factory.SignJson(Json(replace: [("alg", "\"none\"")])),
            LicenseFailure.AlgorithmMismatch);

        // ── Signature ───────────────────────────────────────────────────────────────────────────────
        var flipped = (byte[])signature.Clone();
        flipped[0] ^= 0x01;
        Add("signature-bit-flipped",
            LicenseTestFactory.WithSignature(good, flipped), LicenseFailure.SignatureInvalid);
        Add("signature-truncated",
            LicenseTestFactory.WithSignature(good, signature[..63]), LicenseFailure.SignatureInvalid);
        Add("signature-padded",
            LicenseTestFactory.WithSignature(good, [.. signature, 0x00]), LicenseFailure.SignatureInvalid);
        Add("signature-zeroed",
            LicenseTestFactory.WithSignature(good, new byte[64]), LicenseFailure.SignatureInvalid);
        Add("signature-from-another-key",
            Factory.SignWithForeignKey(LicenseTestFactory.DefaultPayload), LicenseFailure.SignatureInvalid);
        Add("signature-der-encoded",
            Factory.SignDer(LicenseTestFactory.DefaultPayload), LicenseFailure.SignatureInvalid);

        var otherPayload = Factory.Sign(LicenseTestFactory.DefaultPayload with { Seats = 99 });
        Add("signature-of-a-different-payload",
            LicenseTestFactory.WithSignature(otherPayload, signature), LicenseFailure.SignatureInvalid);

        // ⭐ The canonicalisation test. Same JSON meaning, different bytes — and the signature is over the
        //    bytes. A verifier that re-serialised before checking would accept this, which is exactly the
        //    door this format keeps shut.
        var spaced = LicenseEnvelope.EncodePayload(
            Encoding.UTF8.GetBytes(Json().Replace(",", ", ", StringComparison.Ordinal)));
        Add("payload-reserialised-with-spaces",
            LicenseEnvelope.Compose(spaced, signature), LicenseFailure.SignatureInvalid);

        // ── Product ─────────────────────────────────────────────────────────────────────────────────
        Add("product-different", Factory.SignJson(Json(replace: [("prod", "\"EmberTernPro\"")])),
            LicenseFailure.WrongProduct);
        Add("product-wrong-case", Factory.SignJson(Json(replace: [("prod", "\"embertern\"")])),
            LicenseFailure.WrongProduct);

        return cases;
    }

    private static string Json(string? without = null, (string Name, string Value)[]? replace = null)
    {
        var parts = new List<string>();

        foreach (var (name, value) in BaseFields)
        {
            if (name == without)
            {
                continue;
            }

            var effective = value;
            if (replace is not null)
            {
                foreach (var (replacedName, replacedValue) in replace)
                {
                    if (replacedName == name)
                    {
                        effective = replacedValue;
                    }
                }
            }

            parts.Add($"\"{name}\":{effective}");
        }

        return "{" + string.Join(",", parts) + "}";
    }
}

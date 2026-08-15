using System;
using System.Text;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The payload's shape: what it must carry, what it refuses, and what it deliberately ignores.
/// </summary>
public sealed class LicensePayloadTests
{
    private static LicensePayload Sample => LicenseTestFactory.DefaultPayload;

    [Fact]
    public void WriteThenParseKeepsEveryField()
    {
        Assert.True(LicensePayload.TryParse(Sample.WriteJson(), out var parsed, out var detail));
        Assert.Null(detail);
        Assert.Equal(Sample, parsed);
    }

    [Fact]
    public void MaintenanceIsOmittedWhenAbsentAndRoundTripsWhenPresent()
    {
        Assert.DoesNotContain(
            "maint", Encoding.UTF8.GetString(Sample.WriteJson()), StringComparison.Ordinal);

        var withMaintenance = Sample with
        {
            MaintenanceUntil = new DateTimeOffset(2027, 8, 15, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.True(LicensePayload.TryParse(withMaintenance.WriteJson(), out var parsed, out _));
        Assert.Equal(withMaintenance.MaintenanceUntil, parsed.MaintenanceUntil);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        // ⭐ Forward-compatibility rule 1 (§13.4). It is only SAFE because of rule 2 — anything whose
        //    ignoring would be unsafe travels with an lv bump instead, which UnsupportedVersion enforces.
        var json = """
            {"lv":1,"kid":"T1","alg":"ES256-P1363","lid":"abc","prod":"EmberTern","lic":"ACME",
             "seats":1,"iat":"2026-08-15T10:00:00Z","nbf":"2026-08-15T00:00:00Z",
             "exp":"2027-08-15T23:59:59Z","somethingFromTheFuture":{"nested":[1,2,3]}}
            """;

        Assert.True(LicensePayload.TryParse(Encoding.UTF8.GetBytes(json), out var parsed, out _));
        Assert.Equal("ACME", parsed.Licensee);
    }

    [Fact]
    public void TimestampsAreNormalisedToUtc()
    {
        var local = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(2));

        Assert.Equal("2026-08-15T10:00:00Z", LicensePayload.FormatTimestamp(local));
    }

    [Fact]
    public void OnlyTheOneTimestampShapeIsAccepted()
    {
        // ⛔ A lenient parse is how "2027-08-15" silently becomes midnight in whatever zone the reader
        //    happens to be in — a licence that expires at a different instant on two machines.
        foreach (var bad in new[]
                 {
                     "2027-08-15",
                     "2027-08-15T23:59:59",
                     "2027-08-15T23:59:59+02:00",
                     "2027-08-15T23:59:59.500Z",
                     "15/08/2027",
                     "2027-08-15 23:59:59Z",
                 })
        {
            var json = Encoding.UTF8.GetString(Sample.WriteJson())
                .Replace("2027-08-15T23:59:59Z", bad, StringComparison.Ordinal);

            Assert.False(LicensePayload.TryParse(Encoding.UTF8.GetBytes(json), out _, out var detail));
            Assert.Equal("exp", detail);
        }
    }

    [Fact]
    public void TheDetailNamesTheOffendingFieldAndNothingElse()
    {
        // ⭐ Detail is a technical token for [Copy details] — it must be a field NAME, never prose, and
        //    never a fragment of the artifact (which is attacker-controlled text).
        var json = """{"lv":1,"kid":"T1","alg":"ES256-P1363","lid":"abc","prod":"EmberTern"}""";

        Assert.False(LicensePayload.TryParse(Encoding.UTF8.GetBytes(json), out _, out var detail));
        Assert.Equal("lic", detail);
    }

    [Fact]
    public void ParsingNeverThrows()
    {
        foreach (var garbage in new[]
                 {
                     Array.Empty<byte>(),
                     Encoding.UTF8.GetBytes("null"),
                     Encoding.UTF8.GetBytes("[]"),
                     Encoding.UTF8.GetBytes("{"),
                     Encoding.UTF8.GetBytes("{\"lv\":"),
                     new byte[] { 0xFF, 0xFE, 0x00, 0x01 },
                 })
        {
            Assert.False(LicensePayload.TryParse(garbage, out var parsed, out _));
            Assert.Null(parsed);
        }
    }
}

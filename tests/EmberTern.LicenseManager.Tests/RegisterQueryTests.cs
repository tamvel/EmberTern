using System;
using System.IO;
using System.Linq;
using System.Reflection;
using EmberTern.LicenseManager.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// Reaches past the register's own API, exactly as L3's immutability tests do.
///
/// <para>⭐ Used here for two opposite jobs: to read the <c>artifact_status</c> view the way a tool that is
/// not this application would, and to INJECT the corruption <see cref="LicenseRegister.CheckIntegrity"/>
/// is supposed to notice. A consistency check proved only against states its own writer can produce is a
/// check of the writer, not of the file.</para>
/// </summary>
internal static class RegisterProbe
{
    internal static SqliteConnection Connection(LicenseRegister register) =>
        (SqliteConnection)typeof(LicenseRegister)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(register)!;

    internal static void Execute(LicenseRegister register, string sql)
    {
        using var command = Connection(register).CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    internal static string ScalarText(LicenseRegister register, string sql)
    {
        using var command = Connection(register).CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)
               ?? string.Empty;
    }

    /// <summary>The schema L3 shipped, read off the class rather than retyped, so the upgrade test
    /// upgrades from what actually exists in the field.</summary>
    internal static string SchemaV1 => (string)typeof(LicenseRegister)
        .GetField("SchemaV1", BindingFlags.Static | BindingFlags.NonPublic)!
        .GetRawConstantValue()!;
}

/// <summary>
/// L5.0's reading half: the cross-customer licence query, free-text search, history by subject, the
/// current/superseded projection, and the integrity check they all rest on.
/// </summary>
public sealed class RegisterQueryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly LicenseRegister _register =
        LicenseRegister.OpenInMemory(() => Now, actor: "tester");

    // ── The current/superseded projection ───────────────────────────────────────────────────────────

    [Fact]
    public void TheNewestArtifactIsCurrentAndEveryOlderOneIsSuperseded()
    {
        Seed("c-0001", "ACME", "lid-1");

        _register.AppendArtifact(Artifact("lid-1", Now));
        _register.AppendArtifact(Artifact("lid-1", Now.AddSeconds(1), "ETL1.second"));
        _register.AppendArtifact(Artifact("lid-1", Now.AddSeconds(2), "ETL1.third"));

        var artifacts = _register.GetArtifacts("lid-1");

        Assert.Equal(3, artifacts.Count);
        Assert.Equal(ArtifactStatuses.Current, artifacts[0].Status);
        Assert.Equal(ArtifactStatuses.Superseded, artifacts[1].Status);
        Assert.Equal(ArtifactStatuses.Superseded, artifacts[2].Status);

        // ⭐ Superseded means "no longer the newest", never "deleted" and never "no longer valid" — the
        //    bytes of every older artifact are still here, verbatim.
        Assert.Equal("ETL1.second", artifacts[1].Token);
    }

    [Fact]
    public void TheCurrentArtifactIsTheOneTheRegisterHandsBack()
    {
        Seed("c-0001", "ACME", "lid-1");

        _register.AppendArtifact(Artifact("lid-1", Now));
        var newest = _register.AppendArtifact(Artifact("lid-1", Now.AddSeconds(1), "ETL1.second"));

        var current = _register.GetCurrentArtifact("lid-1");

        Assert.NotNull(current);
        Assert.Equal(newest.ArtifactId, current.ArtifactId);
        Assert.Equal(ArtifactStatuses.Current, current.Status);
    }

    [Fact]
    public void ALicenceThatWasNeverIssuedHasNoCurrentArtifact()
    {
        Seed("c-0001", "ACME", "lid-1");

        Assert.Null(_register.GetCurrentArtifact("lid-1"));
        Assert.Empty(_register.GetArtifacts("lid-1"));
    }

    [Fact]
    public void TheStatusViewAnswersWithoutThisApplication()
    {
        // ⭐⭐ §29's recovery row promises the register stays readable when the License Manager will not
        //     start. A projection only our C# knows how to compute would break that promise quietly, so
        //     the answer is a VIEW — and this test asks it in raw SQL, the way any tool would.
        Seed("c-0001", "ACME", "lid-1");
        _register.AppendArtifact(Artifact("lid-1", Now));
        _register.AppendArtifact(Artifact("lid-1", Now.AddSeconds(1), "ETL1.second"));

        Assert.Equal(
            "current",
            RegisterProbe.ScalarText(
                _register,
                "SELECT status FROM artifact_status WHERE lid = 'lid-1' ORDER BY artifact_id DESC LIMIT 1;"));

        Assert.Equal(
            "superseded",
            RegisterProbe.ScalarText(
                _register,
                "SELECT status FROM artifact_status WHERE lid = 'lid-1' ORDER BY artifact_id ASC LIMIT 1;"));
    }

    [Fact]
    public void TheArtifactTableStillRefusesEveryUpdate()
    {
        // ⚠ The whole reason "current" lives in a pointer table: adding a mutable status column would have
        //    meant relaxing THIS. The guarantee L3 proved must still hold after L5 changed the schema.
        Seed("c-0001", "ACME", "lid-1");
        _register.AppendArtifact(Artifact("lid-1", Now));

        Assert.Throws<SqliteException>(
            () => RegisterProbe.Execute(_register, "UPDATE issued_artifacts SET reason = 'x';"));
        Assert.Throws<SqliteException>(
            () => RegisterProbe.Execute(_register, "DELETE FROM issued_artifacts;"));
    }

    // ── The freshness guard ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnArtifactStampedAtTheSameSecondAsTheCurrentOneIsRefused()
    {
        // ⭐ EmberTern installs a replacement only when incoming.iat > local.iat (§16.4), and the issuer
        //    truncates iat to whole seconds. A double-click would otherwise record as delivered a file
        //    every client silently declines.
        Seed("c-0001", "ACME", "lid-1");
        _register.AppendArtifact(Artifact("lid-1", Now));

        var refused = Assert.Throws<RegisterIntegrityException>(
            () => _register.AppendArtifact(Artifact("lid-1", Now, "ETL1.same-second")));

        Assert.Contains("lid-1", refused.Message, StringComparison.Ordinal);
        Assert.Single(_register.GetArtifacts("lid-1"));
    }

    [Fact]
    public void AnArtifactStampedBeforeTheCurrentOneIsRefused()
    {
        Seed("c-0001", "ACME", "lid-1");
        _register.AppendArtifact(Artifact("lid-1", Now));

        Assert.Throws<RegisterIntegrityException>(
            () => _register.AppendArtifact(Artifact("lid-1", Now.AddSeconds(-30), "ETL1.older")));

        Assert.Single(_register.GetArtifacts("lid-1"));
    }

    // ── The cross-customer query ────────────────────────────────────────────────────────────────────

    [Fact]
    public void LicencesAreListedAcrossEveryCustomerSoonestExpiryFirst()
    {
        // ⭐ The question L3 could not ask at all: GetLicenses takes a customer, and a bulk operation is
        //    selected from everything.
        Seed("c-0001", "ACME", "lid-1", expiresAt: Now.AddDays(90));
        Seed("c-0002", "Beta", "lid-2", expiresAt: Now.AddDays(10));
        Seed("c-0003", "Gamma", "lid-3", expiresAt: Now.AddDays(45));

        var rows = _register.QueryLicenses();

        Assert.Equal(["lid-2", "lid-3", "lid-1"], rows.Select(r => r.License.LicenseId).ToArray());
        Assert.Equal("Beta", rows[0].CustomerName);
    }

    [Fact]
    public void TheExpiryWindowNarrowsToWhatIsAboutToLapse()
    {
        Seed("c-0001", "ACME", "lid-1", expiresAt: Now.AddDays(5));
        Seed("c-0002", "Beta", "lid-2", expiresAt: Now.AddDays(20));
        Seed("c-0003", "Gamma", "lid-3", expiresAt: Now.AddDays(200));

        var soon = _register.QueryLicenses(new LicenseQuery { ExpiresBefore = Now.AddDays(30) });

        Assert.Equal(["lid-1", "lid-2"], soon.Select(r => r.License.LicenseId).ToArray());
    }

    [Fact]
    public void TheExpiryWindowHasTwoEnds()
    {
        Seed("c-0001", "ACME", "lid-1", expiresAt: Now.AddDays(5));
        Seed("c-0002", "Beta", "lid-2", expiresAt: Now.AddDays(20));
        Seed("c-0003", "Gamma", "lid-3", expiresAt: Now.AddDays(200));

        var window = _register.QueryLicenses(
            new LicenseQuery { ExpiresFrom = Now.AddDays(10), ExpiresBefore = Now.AddDays(30) });

        Assert.Equal("lid-2", Assert.Single(window).License.LicenseId);
    }

    [Fact]
    public void TheLicenceNobodyEverSentCanBeFound()
    {
        Seed("c-0001", "ACME", "lid-1");
        Seed("c-0002", "Beta", "lid-2");
        _register.AppendArtifact(Artifact("lid-1", Now));

        Assert.Equal(
            "lid-2",
            Assert.Single(_register.QueryLicenses(new LicenseQuery { NeverIssued = true }))
                .License.LicenseId);

        Assert.Equal(
            "lid-1",
            Assert.Single(_register.QueryLicenses(new LicenseQuery { NeverIssued = false }))
                .License.LicenseId);
    }

    [Fact]
    public void ASummaryCarriesWhatAListNeedsWithoutASecondReadAcrossTheTable()
    {
        Seed("c-0001", "ACME Sp. z o.o.", "lid-1", email: "biuro@acme.pl");
        _register.AppendArtifact(Artifact("lid-1", Now));
        var newest = _register.AppendArtifact(Artifact("lid-1", Now.AddSeconds(1), "ETL1.second"));

        var row = Assert.Single(_register.QueryLicenses());

        Assert.Equal("ACME Sp. z o.o.", row.CustomerName);
        Assert.Equal("biuro@acme.pl", row.CustomerEmail);
        Assert.Equal(2, row.ArtifactCount);
        Assert.Equal(Now.AddSeconds(1), row.LastIssuedAt);
        Assert.Equal(newest.ArtifactId, row.CurrentArtifactId);
        Assert.False(row.NeverIssued);
    }

    [Fact]
    public void TheLimitBoundsWhatAListViewCanPullBack()
    {
        for (var i = 1; i <= 5; i++)
        {
            Seed($"c-000{i}", $"Customer {i}", $"lid-{i}", expiresAt: Now.AddDays(i));
        }

        Assert.Equal(3, _register.QueryLicenses(new LicenseQuery { Limit = 3 }).Count);
    }

    // ── Free text ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FreeTextIsCaseInsensitiveThroughPolishDiacritics()
    {
        // ⭐⭐ THE MEASURED REASON the text match is not a SQL LIKE. SQLite's LIKE and lower() are
        //     case-insensitive for ASCII only, by documented design — so in a register whose customers are
        //     Polish companies, searching "łódzka" would miss "Łódzka" and the operator would conclude the
        //     customer is not in the register. .NET's OrdinalIgnoreCase applies Unicode case folding.
        Seed("c-0001", "Łódzka Fabryka Śrub", "lid-1");
        Seed("c-0002", "ACME", "lid-2");

        Assert.Equal(
            "lid-1",
            Assert.Single(_register.QueryLicenses(new LicenseQuery { Text = "łódzka" }))
                .License.LicenseId);

        Assert.Equal(
            "lid-1",
            Assert.Single(_register.QueryLicenses(new LicenseQuery { Text = "ŚRUB" }))
                .License.LicenseId);
    }

    [Fact]
    public void ThePlainSqlEquivalentWouldHaveMissedIt()
    {
        // ⚠ Not a test of our code — a test of the PREMISE our code rests on. If a future SQLite ever made
        //    LIKE Unicode-aware, this fails and the in-memory match becomes a cost with no benefit, which
        //    is exactly when someone should be told.
        Seed("c-0001", "Łódzka Fabryka Śrub", "lid-1");

        Assert.Equal(
            "0",
            RegisterProbe.ScalarText(
                _register, "SELECT COUNT(*) FROM customers WHERE name LIKE '%łódzka%';"));
    }

    [Fact]
    public void FreeTextFindsALicenceByItsOwnIdentifier()
    {
        Seed("c-0001", "ACME", "lid-abcdef");
        Seed("c-0002", "Beta", "lid-999999");

        Assert.Equal(
            "lid-abcdef",
            Assert.Single(_register.QueryLicenses(new LicenseQuery { Text = "ABCDEF" }))
                .License.LicenseId);
    }

    [Fact]
    public void SearchingCustomersReachesTheContactFieldsAsWellAsTheName()
    {
        _register.SaveCustomer(new CustomerRecord
        {
            CustomerId = "c-0001", Name = "ACME", Email = "biuro@acme.pl", LastName = "Kowalski",
        });
        _register.SaveCustomer(new CustomerRecord { CustomerId = "c-0002", Name = "Beta" });

        Assert.Equal("c-0001", Assert.Single(_register.SearchCustomers("acme.pl")).CustomerId);
        Assert.Equal("c-0001", Assert.Single(_register.SearchCustomers("kowalski")).CustomerId);
        Assert.Equal(2, _register.SearchCustomers().Count);
        Assert.Equal(2, _register.SearchCustomers("   ").Count);
    }

    // ── History by subject ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheHistoryCanBeNarrowedToOneLicence()
    {
        Seed("c-0001", "ACME", "lid-1");
        Seed("c-0002", "Beta", "lid-2");
        _register.AppendArtifact(Artifact("lid-1", Now));

        var entries = _register.GetAudit(new AuditQuery { TargetType = "licence", TargetId = "lid-1" });

        Assert.Equal(2, entries.Count);                      // created + issued
        Assert.All(entries, e => Assert.Equal("lid-1", e.TargetId));
    }

    [Fact]
    public void TheHistoryCanBeNarrowedToOneKindOfAction()
    {
        Seed("c-0001", "ACME", "lid-1");
        Seed("c-0002", "Beta", "lid-2");
        _register.AppendArtifact(Artifact("lid-1", Now));
        _register.AppendArtifact(Artifact("lid-2", Now));

        Assert.Equal(2, _register.GetAudit(new AuditQuery { Action = "licence.issued" }).Count);
        Assert.Equal(2, _register.GetAudit(new AuditQuery { TargetType = "customer" }).Count);
    }

    [Fact]
    public void AnUnnarrowedHistoryStillAnswersTheWayItAlwaysDid()
    {
        Seed("c-0001", "ACME", "lid-1");

        Assert.Equal(2, _register.GetAudit().Count);
        Assert.Single(_register.GetAudit(new AuditQuery { Limit = 1 }));
    }

    // ── Integrity ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ASoundRegisterReportsNothing()
    {
        Seed("c-0001", "ACME", "lid-1");
        _register.AppendArtifact(Artifact("lid-1", Now));
        _register.AppendArtifact(Artifact("lid-1", Now.AddSeconds(1), "ETL1.second"));

        Assert.Empty(_register.CheckIntegrity());
    }

    [Fact]
    public void ArtifactsWithNoCurrentMarkAreReported()
    {
        Seed("c-0001", "ACME", "lid-1");
        _register.AppendArtifact(Artifact("lid-1", Now));

        // Injected past the API — the state a hand-edited or half-restored file arrives in.
        RegisterProbe.Execute(_register, "DELETE FROM license_current_artifact;");

        Assert.Contains(
            _register.CheckIntegrity(),
            p => p.Contains("no current one is marked", StringComparison.Ordinal));
    }

    [Fact]
    public void APointerThatIsNotTheNewestArtifactIsReported()
    {
        Seed("c-0001", "ACME", "lid-1");
        var first = _register.AppendArtifact(Artifact("lid-1", Now));
        _register.AppendArtifact(Artifact("lid-1", Now.AddSeconds(1), "ETL1.second"));

        RegisterProbe.Execute(
            _register,
            $"UPDATE license_current_artifact SET artifact_id = {first.ArtifactId} WHERE lid = 'lid-1';");

        Assert.Contains(
            _register.CheckIntegrity(),
            p => p.Contains("not its newest", StringComparison.Ordinal));
    }

    [Fact]
    public void APointerAtAnotherLicencesArtifactIsReported()
    {
        Seed("c-0001", "ACME", "lid-1");
        Seed("c-0002", "Beta", "lid-2");
        var foreignArtifact = _register.AppendArtifact(Artifact("lid-2", Now));
        _register.AppendArtifact(Artifact("lid-1", Now));

        RegisterProbe.Execute(
            _register,
            $"UPDATE license_current_artifact SET artifact_id = {foreignArtifact.ArtifactId} " +
            "WHERE lid = 'lid-1';");

        Assert.Contains(
            _register.CheckIntegrity(),
            p => p.Contains("does not belong to it", StringComparison.Ordinal));
    }

    // ── Upgrading a register written by L3 ──────────────────────────────────────────────────────────

    [Fact]
    public void AnL3RegisterUpgradesAndItsNewestArtifactBecomesTheCurrentOne()
    {
        // ⭐ The upgrade path that matters: the user's own licenses.db already exists, holds real
        //    artifacts, and knows nothing about a current-artifact pointer. It is built here from the
        //    SchemaV1 constant this class actually shipped, not from a retyped copy that could drift.
        var path = Path.Combine(
            Path.GetTempPath(), "etlm-tests", Guid.NewGuid().ToString("N") + ".db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            WriteAnL3RegisterAt(path);

            using var upgraded = LicenseRegister.Open(path, () => Now, actor: "tester");

            Assert.Equal(LicenseRegister.CurrentSchemaVersion, upgraded.SchemaVersion);

            var artifacts = upgraded.GetArtifacts("lid-1");
            Assert.Equal(2, artifacts.Count);
            Assert.Equal(ArtifactStatuses.Current, artifacts[0].Status);
            Assert.Equal(ArtifactStatuses.Superseded, artifacts[1].Status);
            Assert.Empty(upgraded.CheckIntegrity());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    private static void WriteAnL3RegisterAt(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            """ + RegisterProbe.SchemaV1 + """

            INSERT INTO schema_meta(key, value) VALUES('version', '1');
            INSERT INTO customers(customer_id, name, created_at, updated_at)
              VALUES('c-0001', 'ACME', '2026-08-15T10:00:00Z', '2026-08-15T10:00:00Z');
            INSERT INTO licenses(lid, customer_id, product, seats, not_before, expires_at, status,
                                 created_at, updated_at)
              VALUES('lid-1', 'c-0001', 'EmberTern', 1, '2026-08-15T10:00:00Z', '2027-08-15T10:00:00Z',
                     'active', '2026-08-15T10:00:00Z', '2026-08-15T10:00:00Z');
            INSERT INTO issued_artifacts(lid, kid, issued_at, payload_json, token, reason)
              VALUES('lid-1', 'R1', '2026-08-15T10:00:00Z', '{}', 'ETL1.first', 'initial');
            INSERT INTO issued_artifacts(lid, kid, issued_at, payload_json, token, reason)
              VALUES('lid-1', 'R1', '2026-08-15T10:00:01Z', '{}', 'ETL1.second', 'renewal');
            """;
        command.ExecuteNonQuery();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private void Seed(
        string customerId,
        string name,
        string licenseId,
        DateTimeOffset? expiresAt = null,
        string? email = null)
    {
        if (_register.GetCustomer(customerId) is null)
        {
            _register.SaveCustomer(new CustomerRecord
            {
                CustomerId = customerId, Name = name, Email = email,
            });
        }

        _register.SaveLicense(new LicenseRecord
        {
            LicenseId = licenseId,
            CustomerId = customerId,
            Product = "EmberTern",
            Seats = 1,
            NotBefore = Now,
            ExpiresAt = expiresAt ?? Now.AddYears(1),
            Status = LicenseStatuses.Active,
        });
    }

    private static IssuedArtifactRecord Artifact(
        string licenseId, DateTimeOffset issuedAt, string token = "ETL1.first") => new()
    {
        LicenseId = licenseId,
        KeyId = "R1",
        IssuedAt = issuedAt,
        PayloadJson = """{"lv":1}""",
        Token = token,
        Reason = IssueReasons.Initial,
    };

    public void Dispose() => _register.Dispose();
}

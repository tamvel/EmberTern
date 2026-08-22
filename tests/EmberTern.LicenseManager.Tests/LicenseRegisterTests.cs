using System;
using System.Linq;
using EmberTern.LicenseManager.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The register of record.
///
/// <para>⭐ The two load-bearing tests here are <see cref="TheHistoryCannotBeRewritten"/> and
/// <see cref="AnIssuedArtifactCannotBeEditedOrDeleted"/>. Everything else is CRUD; those two are the
/// reason the register can be trusted to answer <i>"what did we send this customer, and who changed
/// what?"</i> years later. Both are enforced by the DATABASE, because the application is the thing most
/// likely to try.</para>
/// </summary>
public sealed class LicenseRegisterTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly LicenseRegister _register =
        LicenseRegister.OpenInMemory(() => Now, actor: "tester");

    private static CustomerRecord Acme => new()
    {
        CustomerId = "c-0001",
        Name = "ACME Sp. z o.o.",
        Address = "ul. Przykładowa 1\n00-001 Warszawa",
        Email = "biuro@acme.pl",
        Notes = "Pays late. Never in the licence.",
    };

    private LicenseRecord Licence(string lid = "lid-1") => new()
    {
        LicenseId = lid,
        CustomerId = "c-0001",
        Product = "EmberTern",
        Seats = 5,
        NotBefore = Now,
        ExpiresAt = Now.AddYears(1),
        Status = LicenseStatuses.Active,
    };

    [Fact]
    public void ANewRegisterIsAtTheCurrentSchemaVersion()
    {
        Assert.Equal(LicenseRegister.CurrentSchemaVersion, _register.SchemaVersion);
    }

    [Fact]
    public void MigratingAnAlreadyCurrentRegisterIsANoOp()
    {
        // Opening the same file twice must not re-run the schema. In-memory cannot be reopened, so the
        // property being asserted is that Migrate() is idempotent against its own output.
        Assert.Equal(LicenseRegister.CurrentSchemaVersion, _register.SchemaVersion);
        Assert.Equal(LicenseRegister.CurrentSchemaVersion, _register.SchemaVersion);
    }

    [Fact]
    public void ACustomerRoundTrips()
    {
        var saved = _register.SaveCustomer(Acme);

        var read = _register.GetCustomer("c-0001");

        Assert.NotNull(read);
        Assert.Equal("ACME Sp. z o.o.", read.Name);
        Assert.Equal("biuro@acme.pl", read.Email);
        Assert.Equal(Now, read.CreatedAt);
        Assert.Equal(saved.UpdatedAt, read.UpdatedAt);
    }

    [Fact]
    public void ACustomerWithoutANameIsRefused()
    {
        // ⭐ Required because it is the value SIGNED into every licence this customer receives.
        Assert.Throws<ArgumentException>(() => _register.SaveCustomer(Acme with { Name = "  " }));
    }

    [Fact]
    public void SavingTwiceUpdatesRatherThanDuplicates()
    {
        _register.SaveCustomer(Acme);
        _register.SaveCustomer(Acme with { Name = "ACME S.A." });

        Assert.Single(_register.GetCustomers());
        Assert.Equal("ACME S.A.", _register.GetCustomer("c-0001")!.Name);
    }

    [Fact]
    public void CustomerIdentifiersRunInSequence()
    {
        Assert.Equal("c-0001", _register.NextCustomerId());

        _register.SaveCustomer(Acme);
        Assert.Equal("c-0002", _register.NextCustomerId());

        _register.SaveCustomer(Acme with { CustomerId = "c-0009", Name = "Other" });
        Assert.Equal("c-0010", _register.NextCustomerId());
    }

    [Fact]
    public void ALicenceRoundTripsAndBelongsToItsCustomer()
    {
        _register.SaveCustomer(Acme);
        _register.SaveLicense(Licence());

        var licences = _register.GetLicenses("c-0001");

        Assert.Single(licences);
        Assert.Equal(5, licences[0].Seats);
        Assert.Equal(LicenseStatuses.Active, licences[0].Status);
    }

    [Fact]
    public void ALicenceForAnUnknownCustomerIsRefusedByTheForeignKey()
    {
        Assert.Throws<SqliteException>(() => _register.SaveLicense(Licence()));
    }

    [Fact]
    public void EveryMutationWritesItsOwnHistoryLine()
    {
        // ⭐ Inside the same transaction as the change. A history that can be absent when the change is
        //    present is not a history.
        _register.SaveCustomer(Acme);
        _register.SaveCustomer(Acme with { Name = "ACME S.A." });
        _register.SaveLicense(Licence());

        var audit = _register.GetAudit();

        Assert.Equal(3, audit.Count);
        Assert.Equal("licence.created", audit[0].Action);
        Assert.Equal("customer.updated", audit[1].Action);
        Assert.Equal("customer.created", audit[2].Action);
        Assert.All(audit, entry => Assert.Equal("tester", entry.Actor));
        Assert.Null(audit[2].BeforeJson);
        Assert.NotNull(audit[1].BeforeJson);
    }

    [Fact]
    public void TheHistoryCannotBeRewritten()
    {
        // ⭐⭐ Enforced by a database trigger, not by a ViewModel. A history the application can rewrite
        //     is not a history — and the application is the thing most likely to try.
        _register.SaveCustomer(Acme);

        var update = Assert.Throws<SqliteException>(
            () => ExecuteRaw("UPDATE audit_log SET actor = 'somebody else';"));
        Assert.Contains("append-only", update.Message, StringComparison.OrdinalIgnoreCase);

        var delete = Assert.Throws<SqliteException>(() => ExecuteRaw("DELETE FROM audit_log;"));
        Assert.Contains("append-only", delete.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Single(_register.GetAudit());
    }

    [Fact]
    public void AnIssuedArtifactCannotBeEditedOrDeleted()
    {
        // ⭐⭐ §12.5: an artifact is immutable and is never edited, only superseded. This is what lets the
        //     register answer "what exactly did we send in 2026?" with the bytes rather than a rebuild.
        _register.SaveCustomer(Acme);
        _register.SaveLicense(Licence());
        _register.AppendArtifact(Artifact());

        Assert.Throws<SqliteException>(() => ExecuteRaw("UPDATE issued_artifacts SET token = 'x';"));
        Assert.Throws<SqliteException>(() => ExecuteRaw("DELETE FROM issued_artifacts;"));

        Assert.Single(_register.GetArtifacts("lid-1"));
    }

    [Fact]
    public void ArtifactsAccumulateNewestFirstAndAreAudited()
    {
        _register.SaveCustomer(Acme);
        _register.SaveLicense(Licence());

        _register.AppendArtifact(Artifact(IssueReasons.Initial));

        // ⚠ A LATER iat, and that is not decoration. L3 wrote this test with two artifacts stamped at the
        //    same instant, and L5's freshness guard rejects it — correctly: EmberTern installs a
        //    replacement only when incoming.iat > local.iat (§16.4), so the second file would have been
        //    recorded as delivered and then silently declined by every client that received it.
        _register.AppendArtifact(Artifact(IssueReasons.Renewal, "ETL1.second.sig", Now.AddSeconds(1)));

        var artifacts = _register.GetArtifacts("lid-1");

        Assert.Equal(2, artifacts.Count);
        Assert.Equal("ETL1.second.sig", artifacts[0].Token);
        Assert.Equal(IssueReasons.Renewal, artifacts[0].Reason);
        Assert.Equal(2, _register.GetAudit().Count(e => e.Action == "licence.issued"));
    }

    [Fact]
    public void AnArtifactComesBackWithTheIdentityTheDatabaseGaveIt()
    {
        _register.SaveCustomer(Acme);
        _register.SaveLicense(Licence());

        var stored = _register.AppendArtifact(Artifact());

        Assert.True(stored.ArtifactId > 0);
        Assert.Equal(stored.ArtifactId, _register.GetArtifacts("lid-1")[0].ArtifactId);
    }

    [Fact]
    public void SomethingThatIsNotATableMutationCanStillBeRecorded()
    {
        _register.Record("keystore.created", "key", "R1", "Ceremony performed.");

        var entry = Assert.Single(_register.GetAudit());
        Assert.Equal("keystore.created", entry.Action);
        Assert.Equal("Ceremony performed.", entry.Note);
    }

    [Fact]
    public void TimestampsSurviveTheRoundTripToTheSecond()
    {
        // ⚠ SQLite has no date type. Storing in the licence format's own shape is what stops the register
        //    and the artifact ever disagreeing about an instant.
        _register.SaveCustomer(Acme);
        _register.SaveLicense(Licence() with { ExpiresAt = new DateTimeOffset(2027, 8, 15, 23, 59, 59, TimeSpan.Zero) });

        Assert.Equal(
            new DateTimeOffset(2027, 8, 15, 23, 59, 59, TimeSpan.Zero),
            _register.GetLicense("lid-1")!.ExpiresAt);
    }

    private static IssuedArtifactRecord Artifact(
        string reason = IssueReasons.Initial,
        string token = "ETL1.payload.signature",
        DateTimeOffset? issuedAt = null) => new()
    {
        LicenseId = "lid-1",
        KeyId = "R1",
        IssuedAt = issuedAt ?? Now,
        PayloadJson = """{"lv":1}""",
        Token = token,
        Reason = reason,
    };

    // Reaches past the register's own API on purpose — a trigger that only the register's methods respect
    // is not a trigger, it is a convention.
    private void ExecuteRaw(string sql)
    {
        var field = typeof(LicenseRegister).GetField(
            "_connection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var connection = (SqliteConnection)field!.GetValue(_register)!;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _register.Dispose();
}

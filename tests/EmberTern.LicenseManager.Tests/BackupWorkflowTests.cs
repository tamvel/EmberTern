using System;
using System.IO;
using System.Linq;
using System.Text;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// Taking a backup of a real register, and exporting it as JSONL.
///
/// <para>⭐ Every test here runs against a register with a real signing ceremony behind it, two issued
/// artifacts, a moved current-artifact pointer and a populated history — because the things a backup is
/// most likely to lose are exactly the ones an empty register does not have.</para>
/// </summary>
public sealed class BackupWorkflowTests : IDisposable
{
    private const string BackupPassphrase = "a different secret from the keystore";

    private readonly ManagerFixture _fixture = new();
    private readonly string _workspace;

    public BackupWorkflowTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "etlm-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        RegisterSnapshotTests.Seed(_fixture);
    }

    private BackupWorkflow Workflow => new(_fixture.Register, () => _fixture.Now);

    private string Target(string name) => Path.Combine(_workspace, name);

    [Fact]
    public void ABackupOfASoundRegisterIsWrittenAndReportsWhatItHolds()
    {
        var path = Target("register" + RegisterBackup.FileExtension);

        var report = Workflow.CreateBackup(path, BackupPassphrase);

        Assert.True(File.Exists(path));
        Assert.Equal(path, report.Path);
        Assert.Equal(_fixture.Now, report.CreatedAt);
        Assert.Equal(LicenseRegister.CurrentSchemaVersion, report.SchemaVersion);
        Assert.Equal(1, report.Customers);
        Assert.Equal(1, report.Licenses);
        Assert.Equal(2, report.Artifacts);
        Assert.Equal(1, report.CurrentPointers);
        Assert.True(report.AuditEntries > 0);
        Assert.Equal(new FileInfo(path).Length, report.Bytes);
    }

    /// <summary>
    /// ⭐⭐ The stage's central promise: what comes back is the register, not a summary of it. Compared
    /// through the schema-driven dump, so a column nobody remembered is compared too.
    /// </summary>
    [Fact]
    public void TheBackupCarriesEveryRowOfTheWholeRegister()
    {
        var path = Target("full" + RegisterBackup.FileExtension);
        var expected = _fixture.Register.DumpContent();

        Workflow.CreateBackup(path, BackupPassphrase);

        var snapshot = RegisterBackup.Open(File.ReadAllBytes(path), BackupPassphrase);

        Assert.Equal(
            expected,
            RegisterSnapshotTests.WithSnapshotBytes(snapshot, register => register.DumpContent()));
    }

    /// <summary>⭐ The history is not flattened to its outcome — every artifact ever signed comes back.</summary>
    [Fact]
    public void TheBackupCarriesTheWholeIssuingHistoryTheCurrentPointerAndTheAudit()
    {
        var path = Target("history" + RegisterBackup.FileExtension);
        var license = _fixture.Register.GetAllLicenses().Single();

        var artifacts = _fixture.Register.GetArtifacts(license.LicenseId);
        var current = _fixture.Register.GetCurrentArtifact(license.LicenseId)!;
        var audit = _fixture.Register.GetAudit(new AuditQuery { Limit = int.MaxValue });

        Workflow.CreateBackup(path, BackupPassphrase);
        var snapshot = RegisterBackup.Open(File.ReadAllBytes(path), BackupPassphrase);

        var actual = RegisterSnapshotTests.WithSnapshotBytes(snapshot, register => (
            Artifacts: register.GetArtifacts(license.LicenseId)
                .Select(a => (a.ArtifactId, a.Token, a.PayloadJson, a.Reason, a.Status)).ToList(),
            CurrentId: register.GetCurrentArtifact(license.LicenseId)!.ArtifactId,
            Audit: register.GetAudit(new AuditQuery { Limit = int.MaxValue })
                .Select(e => (e.AuditId, e.Action, e.Note)).ToList()));

        Assert.Equal(2, actual.Artifacts.Count);
        Assert.Equal(
            artifacts.Select(a => (a.ArtifactId, a.Token, a.PayloadJson, a.Reason, a.Status)).ToList(),
            actual.Artifacts);
        Assert.Equal(current.ArtifactId, actual.CurrentId);
        Assert.Equal(audit.Select(e => (e.AuditId, e.Action, e.Note)).ToList(), actual.Audit);
    }

    [Fact]
    public void ABackupRecordsItselfInTheRegistersOwnHistory()
    {
        var path = Target("audited" + RegisterBackup.FileExtension);

        Workflow.CreateBackup(path, BackupPassphrase);

        var entry = Assert.Single(_fixture.Register.GetAudit(
            new AuditQuery { Action = BackupWorkflow.BackupAction, Limit = int.MaxValue }));

        Assert.Equal(BackupWorkflow.TargetType, entry.TargetType);
        Assert.Equal(Path.GetFileName(path), entry.TargetId);
        Assert.Contains(path, entry.Note ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPassphraseNeverProducesAFile()
    {
        var path = Target("never" + RegisterBackup.FileExtension);

        Assert.Throws<ArgumentException>(() => Workflow.CreateBackup(path, string.Empty));

        Assert.False(File.Exists(path));
        Assert.Empty(_fixture.Register.GetAudit(
            new AuditQuery { Action = BackupWorkflow.BackupAction, Limit = int.MaxValue }));
    }

    /// <summary>
    /// ⛔ A register that disagrees with itself is not backed up. Carrying a corrupt state forward under a
    /// name that promises safety is worse than refusing, because it is discovered later.
    /// </summary>
    [Fact]
    public void AnInconsistentRegisterIsRefusedAndNothingIsWritten()
    {
        var path = Target("broken" + RegisterBackup.FileExtension);
        BreakTheCurrentPointer(_fixture);

        Assert.NotEmpty(_fixture.Register.CheckIntegrity());

        var error = Assert.Throws<RegisterIntegrityException>(
            () => Workflow.CreateBackup(path, BackupPassphrase));

        Assert.Contains("not backed up", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// ⭐⭐ <b>The snapshot verification, proved against a snapshot that does NOT reproduce the register.</b>
    ///
    /// <para>⚠ It exists because with a correct snapshot the guard never fires, so nothing was watching
    /// it: deleting the comparison would leave the whole suite green. The state it defends against — a
    /// snapshot that silently loses rows — cannot be reached through the public API, which is exactly why
    /// it is reached here, through an <c>InternalsVisibleTo</c> seam.</para>
    ///
    /// <para>⚠ Provenance, stated accurately: this gap was found by reading the code during L5.5 and was
    /// first recorded as a known testability limit. It was NOT established by a fault-injection run — the
    /// L5.5 campaign was interrupted before producing any result, and was then retired by user directive
    /// (gotcha #383 as amended).</para>
    /// </summary>
    [Fact]
    public void ASnapshotThatDoesNotReproduceTheRegisterIsRefusedRowForRow()
    {
        var snapshot = _fixture.Register.CreateSnapshot();
        var truthful = _fixture.Register.DumpContent();

        // A register that has one row MORE than the snapshot carries — i.e. the snapshot lost one.
        var expectedOneMore = truthful.Append("customerscustomer_id=\"c-9999\"").ToList();

        var tooFew = Assert.Throws<RegisterIntegrityException>(
            () => BackupWorkflow.VerifySnapshot(snapshot, expectedOneMore));
        Assert.Contains("Nothing was written", tooFew.Message, StringComparison.Ordinal);

        // …and a snapshot whose row COUNT matches while a value differs.
        var sameCountDifferentValue = truthful.ToList();
        sameCountDifferentValue[0] = sameCountDifferentValue[0] + "-tampered";

        var mismatched = Assert.Throws<RegisterIntegrityException>(
            () => BackupWorkflow.VerifySnapshot(snapshot, sameCountDifferentValue));
        Assert.Contains("row for row", mismatched.Message, StringComparison.Ordinal);

        // ⭐ And the honest case still passes, so the guard is not simply refusing everything.
        Assert.Equal(
            SnapshotCounts.Read(_fixture.Register),
            BackupWorkflow.VerifySnapshot(snapshot, truthful));
    }

    [Fact]
    public void NoPartialFileSurvivesUnderTheFinalName()
    {
        var path = Target("partial" + RegisterBackup.FileExtension);

        Workflow.CreateBackup(path, BackupPassphrase);

        Assert.False(File.Exists(path + ".partial"));
        Assert.NotEmpty(RegisterBackup.Open(File.ReadAllBytes(path), BackupPassphrase));
    }

    // ── JSONL ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheJsonlExportCarriesAllFiveRecordTypes()
    {
        var path = Target("register" + RegisterJsonl.FileExtension);

        var report = Workflow.ExportJsonl(path);

        var lines = File.ReadAllLines(path);
        Assert.Equal(report.Lines, lines.Length);

        foreach (var type in RegisterJsonl.Types)
        {
            Assert.Contains(lines, l => Type(l) == type);
        }
    }

    [Fact]
    public void TheJsonlExportHasOneLinePerRowAcrossEveryTable()
    {
        var path = Target("counts" + RegisterJsonl.FileExtension);
        var counts = SnapshotCounts.Read(_fixture.Register);

        Workflow.ExportJsonl(path);
        var lines = File.ReadAllLines(path);

        Assert.Equal(counts.Customers, lines.Count(l => Type(l) == RegisterJsonl.CustomerType));
        Assert.Equal(counts.Licenses, lines.Count(l => Type(l) == RegisterJsonl.LicenseType));
        Assert.Equal(counts.Artifacts, lines.Count(l => Type(l) == RegisterJsonl.ArtifactType));
        Assert.Equal(
            counts.CurrentPointers, lines.Count(l => Type(l) == RegisterJsonl.CurrentArtifactType));

        // ⚠ The export writes its OWN audit line after reading, so the file holds one more than the
        //    count taken before it ran. Asserting the relationship rather than a number is what keeps
        //    this from breaking every time an unrelated action is audited.
        Assert.Equal(counts.AuditEntries, lines.Count(l => Type(l) == RegisterJsonl.AuditType));
    }

    /// <summary>
    /// ⭐ The escape hatch's whole purpose is being readable without this application. A Polish customer
    /// name rendered as <c>Żółw</c> is technically valid JSON and practically useless.
    /// </summary>
    [Fact]
    public void TheJsonlExportKeepsUnicodeReadable()
    {
        var path = Target("unicode" + RegisterJsonl.FileExtension);

        Workflow.ExportJsonl(path);
        var lines = File.ReadAllLines(path);

        var customerLine = Assert.Single(lines, l => Type(l) == RegisterJsonl.CustomerType);
        Assert.Contains("Żółw Sp. z o.o.", customerLine, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u017B", customerLine, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⚠⚠ <b>The signed payload keeps ITS escapes, and that is not an inconsistency.</b> Measured while
    /// writing the test above: <c>payloadJson</c> carries the customer's name as
    /// <c>Żółw</c>, because that is what the ISSUER serialised and therefore what was
    /// SIGNED. The export stores it verbatim — re-encoding it into readable characters would produce
    /// prettier text and a payload whose signature no longer verifies (rule #11). ⭐ So the file is
    /// readable where the register's own fields are, and byte-exact where a signature depends on it.
    /// </summary>
    [Fact]
    public void TheSignedPayloadIsCarriedVerbatimEscapesIncluded()
    {
        var path = Target("verbatim" + RegisterJsonl.FileExtension);
        var artifact = _fixture.Register.GetAllArtifacts().First();

        Workflow.ExportJsonl(path);
        var line = File.ReadAllLines(path).First(l => Type(l) == RegisterJsonl.ArtifactType);

        using var document = System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal(artifact.PayloadJson, document.RootElement.GetProperty("payloadJson").GetString());
        Assert.Contains("\\u017B", artifact.PayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheJsonlExportIsUtf8WithoutABom()
    {
        var path = Target("bom" + RegisterJsonl.FileExtension);

        Workflow.ExportJsonl(path);
        var bytes = File.ReadAllBytes(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    /// <summary>⚠ A note with newlines and quotes must stay ONE line — that is what makes it JSON Lines.</summary>
    [Fact]
    public void AMultiLineValueStaysOnOneLine()
    {
        var customer = _fixture.Register.GetCustomers().First();
        _fixture.Register.SaveCustomer(customer with
        {
            Notes = "line one\nline two\r\nwith \"quotes\" and a \\ backslash\tand a tab",
        });

        var path = Target("multiline" + RegisterJsonl.FileExtension);
        Workflow.ExportJsonl(path);

        var lines = File.ReadAllLines(path);
        var line = Assert.Single(lines, l => Type(l) == RegisterJsonl.CustomerType);

        using var document = System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal(
            "line one\nline two\r\nwith \"quotes\" and a \\ backslash\tand a tab",
            document.RootElement.GetProperty("notes").GetString());
    }

    [Fact]
    public void TheJsonlExportCarriesWholeTokensAndSignedPayloads()
    {
        var path = Target("tokens" + RegisterJsonl.FileExtension);
        var artifacts = _fixture.Register.GetAllArtifacts();

        Workflow.ExportJsonl(path);
        var lines = File.ReadAllLines(path).Where(l => Type(l) == RegisterJsonl.ArtifactType).ToList();

        Assert.Equal(artifacts.Count, lines.Count);

        foreach (var artifact in artifacts)
        {
            Assert.Contains(lines, line =>
            {
                using var document = System.Text.Json.JsonDocument.Parse(line);
                return document.RootElement.GetProperty("token").GetString() == artifact.Token
                    && document.RootElement.GetProperty("payloadJson").GetString() == artifact.PayloadJson
                    && document.RootElement.GetProperty("artifactId").GetInt64() == artifact.ArtifactId;
            });
        }
    }

    [Fact]
    public void TheJsonlExportWritesDatesInTheFormatTheRestOfTheSystemUses()
    {
        var path = Target("dates" + RegisterJsonl.FileExtension);
        var license = _fixture.Register.GetAllLicenses().Single();

        Workflow.ExportJsonl(path);
        var line = File.ReadAllLines(path).Single(l => Type(l) == RegisterJsonl.LicenseType);

        using var document = System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal(
            EmberTern.Licensing.LicensePayload.FormatTimestamp(license.ExpiresAt),
            document.RootElement.GetProperty("expiresAt").GetString());
    }

    /// <summary>⭐ Without the pointer line, the file cannot say which artifact a customer should hold.</summary>
    [Fact]
    public void TheJsonlExportNamesWhichArtifactIsCurrent()
    {
        var path = Target("pointer" + RegisterJsonl.FileExtension);
        var license = _fixture.Register.GetAllLicenses().Single();
        var current = _fixture.Register.GetCurrentArtifact(license.LicenseId)!;

        Workflow.ExportJsonl(path);
        var line = File.ReadAllLines(path).Single(l => Type(l) == RegisterJsonl.CurrentArtifactType);

        using var document = System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal(license.LicenseId, document.RootElement.GetProperty("lid").GetString());
        Assert.Equal(current.ArtifactId, document.RootElement.GetProperty("artifactId").GetInt64());
    }

    [Fact]
    public void AJsonlExportRecordsItselfAndSaysItIsNotEncrypted()
    {
        var path = Target("audited" + RegisterJsonl.FileExtension);

        Workflow.ExportJsonl(path);

        var entry = Assert.Single(_fixture.Register.GetAudit(
            new AuditQuery { Action = BackupWorkflow.ExportAction, Limit = int.MaxValue }));

        Assert.Contains("Not encrypted", entry.Note ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryExportedLineIsAJsonObjectWithAKnownType()
    {
        var path = Target("shape" + RegisterJsonl.FileExtension);
        Workflow.ExportJsonl(path);

        foreach (var line in File.ReadAllLines(path))
        {
            using var document = System.Text.Json.JsonDocument.Parse(line);
            Assert.Equal(System.Text.Json.JsonValueKind.Object, document.RootElement.ValueKind);
            Assert.Contains(document.RootElement.GetProperty("type").GetString(), RegisterJsonl.Types);
        }
    }

    // ── Shared helpers ──────────────────────────────────────────────────────────────────────────────

    private static string? Type(string line)
    {
        using var document = System.Text.Json.JsonDocument.Parse(line);
        return document.RootElement.GetProperty("type").GetString();
    }

    /// <summary>
    /// Damages the register PAST its own API, by moving the current-artifact pointer to an artifact that
    /// does not exist.
    ///
    /// <para>⭐ Reaching past the API is the point: a check proved only against states the writer can
    /// produce is a check of the writer. This is the same technique L5.0's three corruption tests use.</para>
    /// </summary>
    internal static void BreakTheCurrentPointer(ManagerFixture fixture)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            "Data Source=" + fixture.Paths.Register);
        connection.Open();
        using var command = connection.CreateCommand();
        // ⚠ Not "point it at an artifact that does not exist" — the register defends that with a real
        //    FOREIGN KEY, which is worth knowing and is why this injection had to change. What a foreign
        //    key cannot defend is a pointer aimed at a REAL but no-longer-newest artifact, which is
        //    exactly the "history was edited outside this application" state CheckIntegrity's third
        //    check exists for.
        command.CommandText =
            "UPDATE license_current_artifact SET artifact_id = " +
            "(SELECT MIN(artifact_id) FROM issued_artifacts WHERE lid = license_current_artifact.lid);";
        command.ExecuteNonQuery();
        connection.Close();
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
    }

    public void Dispose()
    {
        _fixture.Dispose();

        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Data;

/// <summary>
/// The register of record — <c>licenses.db</c>.
///
/// <para>⭐ <b>It is the master, not a cache.</b> The signing key is offline by design, so the register and
/// the key belong on the same machine; an issuing record that lives somewhere the issuer cannot reach is a
/// record that will drift. If a backend ever exists it mirrors this file, never the other way round
/// (§18.1).</para>
///
/// <para>⭐ <b>Every mutation writes its own audit row, inside the same transaction.</b> Not beside it and
/// not afterwards: a history that can be absent when the change is present is not a history. And
/// <c>audit_log</c> and <c>issued_artifacts</c> are append-only <b>in the database</b>, by trigger — a
/// history the application can rewrite is not a history either.</para>
///
/// <para>⚠ Timestamps are stored as RFC 3339 UTC text in the licence format's own shape
/// (<see cref="EmberTern.Licensing.LicensePayload.TimestampFormat"/>). SQLite has no date type, and using
/// the format the artifact uses means the register and the artifact can never disagree about an instant.</para>
/// </summary>
public sealed class LicenseRegister : IDisposable
{
    /// <summary>The schema version this build writes.</summary>
    public const int CurrentSchemaVersion = 2;

    private readonly SqliteConnection _connection;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _actor;

    private LicenseRegister(SqliteConnection connection, Func<DateTimeOffset> clock, string actor)
    {
        _connection = connection;
        _clock = clock;
        _actor = actor;
    }

    /// <summary>Opens (creating if necessary) the register at <paramref name="path"/>.</summary>
    public static LicenseRegister Open(
        string path, Func<DateTimeOffset>? clock = null, string? actor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Create($"Data Source={path}", clock, actor);
    }

    /// <summary>
    /// A private in-memory register. ⚠ For tests — the connection is what keeps it alive, so it vanishes
    /// with this instance.
    /// </summary>
    public static LicenseRegister OpenInMemory(Func<DateTimeOffset>? clock = null, string? actor = null) =>
        Create("Data Source=:memory:", clock, actor);

    private static LicenseRegister Create(string connectionString, Func<DateTimeOffset>? clock, string? actor)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        var register = new LicenseRegister(
            connection, clock ?? (() => DateTimeOffset.UtcNow), actor ?? Environment.UserName);

        register.Migrate();
        return register;
    }

    /// <summary>The schema version the open file is at.</summary>
    public int SchemaVersion => int.Parse(
        ReadMeta("version") ?? "0", CultureInfo.InvariantCulture);

    // ── Schema ──────────────────────────────────────────────────────────────────────────────────────

    private void Migrate()
    {
        Execute("PRAGMA foreign_keys = ON;");
        Execute("""
            CREATE TABLE IF NOT EXISTS schema_meta (
              key   TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );
            """);

        var from = SchemaVersion;
        if (from >= CurrentSchemaVersion)
        {
            // ⚠ A file from a NEWER build is refused rather than opened read-only. Silently working
            //    against a schema we do not understand is how a register loses rows nobody notices.
            if (from > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"The register is schema version {from}; this build understands {CurrentSchemaVersion}. " +
                    "Use a newer License Manager.");
            }

            return;
        }

        using var transaction = _connection.BeginTransaction();

        if (from < 1)
        {
            Execute(SchemaV1, transaction);
        }

        if (from < 2)
        {
            Execute(SchemaV2, transaction);

            // ⭐ Backfill for a register written by L3: it only ever appended, so the newest artifact for
            //    each licence is the current one. Writes nothing on a brand-new file, which is why it can
            //    run unconditionally rather than behind a "does it have rows" test nobody would re-read.
            Execute("""
                INSERT INTO license_current_artifact(lid, artifact_id, set_at)
                SELECT lid, MAX(artifact_id), $now FROM issued_artifacts GROUP BY lid;
                """,
                transaction, ("$now", Stamp(_clock())));
        }

        Execute("INSERT INTO schema_meta(key, value) VALUES('version', $v) " +
                "ON CONFLICT(key) DO UPDATE SET value = $v;",
            transaction, ("$v", CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)));

        transaction.Commit();
    }

    private const string SchemaV1 = """
        CREATE TABLE customers (
          customer_id TEXT PRIMARY KEY,
          name        TEXT NOT NULL,
          address     TEXT,
          first_name  TEXT,
          last_name   TEXT,
          email       TEXT,
          notes       TEXT,
          created_at  TEXT NOT NULL,
          updated_at  TEXT NOT NULL
        );

        CREATE TABLE licenses (
          lid          TEXT PRIMARY KEY,
          customer_id  TEXT NOT NULL REFERENCES customers(customer_id),
          product      TEXT NOT NULL DEFAULT 'EmberTern',
          seats        INTEGER NOT NULL,
          not_before   TEXT NOT NULL,
          expires_at   TEXT NOT NULL,
          maint_until  TEXT,
          status       TEXT NOT NULL,
          notes        TEXT,
          created_at   TEXT NOT NULL,
          updated_at   TEXT NOT NULL
        );
        CREATE INDEX ix_licenses_customer ON licenses(customer_id);
        CREATE INDEX ix_licenses_expiry   ON licenses(expires_at);

        CREATE TABLE issued_artifacts (
          artifact_id  INTEGER PRIMARY KEY AUTOINCREMENT,
          lid          TEXT NOT NULL REFERENCES licenses(lid),
          kid          TEXT NOT NULL,
          issued_at    TEXT NOT NULL,
          payload_json TEXT NOT NULL,
          token        TEXT NOT NULL,
          reason       TEXT NOT NULL
        );
        CREATE INDEX ix_artifacts_lid ON issued_artifacts(lid);

        CREATE TABLE audit_log (
          audit_id    INTEGER PRIMARY KEY AUTOINCREMENT,
          at          TEXT NOT NULL,
          actor       TEXT NOT NULL,
          action      TEXT NOT NULL,
          target_type TEXT NOT NULL,
          target_id   TEXT NOT NULL,
          before_json TEXT,
          after_json  TEXT,
          note        TEXT
        );

        -- ⭐ Append-only IN THE DATABASE, not in a ViewModel. A history the application can rewrite is
        --    not a history, and the application is the thing most likely to try.
        CREATE TRIGGER audit_log_is_append_only_update BEFORE UPDATE ON audit_log
        BEGIN SELECT RAISE(ABORT, 'audit_log is append-only'); END;
        CREATE TRIGGER audit_log_is_append_only_delete BEFORE DELETE ON audit_log
        BEGIN SELECT RAISE(ABORT, 'audit_log is append-only'); END;

        -- ⭐ §12.5: an issued artifact is immutable and is never edited, only superseded. That is what
        --    lets the register answer "what exactly did we send this customer in 2026?" with the bytes.
        CREATE TRIGGER issued_artifacts_are_immutable_update BEFORE UPDATE ON issued_artifacts
        BEGIN SELECT RAISE(ABORT, 'issued_artifacts is append-only'); END;
        CREATE TRIGGER issued_artifacts_are_immutable_delete BEFORE DELETE ON issued_artifacts
        BEGIN SELECT RAISE(ABORT, 'issued_artifacts is append-only'); END;
        """;

    // ⭐⭐ SCHEMA 2 — "which artifact is current", kept OUT of issued_artifacts on purpose.
    //
    //    The obvious move is a status column on the artifact. It is not available and should not be made
    //    available: issued_artifacts aborts every UPDATE by trigger, and that trigger is a rule-#11-class
    //    guarantee L3 proved by reaching past this class's own API. Adding a mutable column would have
    //    meant relaxing it — trading the guarantee that an artifact's bytes cannot change for a piece of
    //    bookkeeping about which one is newest.
    //
    //    So the two facts live apart, with the lifetimes they actually have: the bytes are written once,
    //    the pointer is rewritten on every re-issue. ⭐ The view exists so the file answers "which one is
    //    current?" to any SQL tool that opens it without this application — §29's recovery row promises
    //    exactly that, and a projection only our C# knows how to compute would quietly break the promise.
    private const string SchemaV2 = """
        CREATE TABLE license_current_artifact (
          lid         TEXT PRIMARY KEY REFERENCES licenses(lid),
          artifact_id INTEGER NOT NULL REFERENCES issued_artifacts(artifact_id),
          set_at      TEXT NOT NULL
        );

        CREATE VIEW artifact_status AS
        SELECT a.artifact_id, a.lid, a.kid, a.issued_at, a.reason,
               CASE WHEN c.artifact_id = a.artifact_id THEN 'current' ELSE 'superseded' END AS status
        FROM issued_artifacts a
        LEFT JOIN license_current_artifact c ON c.lid = a.lid;
        """;

    // ── Customers ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates or updates a customer, and records it.</summary>
    /// <exception cref="ArgumentException">The name is missing — it is what gets signed.</exception>
    public CustomerRecord SaveCustomer(CustomerRecord customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        if (string.IsNullOrWhiteSpace(customer.Name))
        {
            throw new ArgumentException("A customer name is required.", nameof(customer));
        }

        var now = _clock();
        var existing = GetCustomer(customer.CustomerId);
        var saved = customer with
        {
            Name = customer.Name.Trim(),
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        using var transaction = _connection.BeginTransaction();

        Execute("""
            INSERT INTO customers(customer_id, name, address, first_name, last_name, email, notes,
                                  created_at, updated_at)
            VALUES($id, $name, $address, $first, $last, $email, $notes, $created, $updated)
            ON CONFLICT(customer_id) DO UPDATE SET
              name = $name, address = $address, first_name = $first, last_name = $last,
              email = $email, notes = $notes, updated_at = $updated;
            """,
            transaction,
            ("$id", saved.CustomerId), ("$name", saved.Name), ("$address", saved.Address),
            ("$first", saved.FirstName), ("$last", saved.LastName), ("$email", saved.Email),
            ("$notes", saved.Notes), ("$created", Stamp(saved.CreatedAt)), ("$updated", Stamp(saved.UpdatedAt)));

        AppendAudit(transaction, new AuditEntry
        {
            At = now,
            Actor = _actor,
            Action = existing is null ? "customer.created" : "customer.updated",
            TargetType = "customer",
            TargetId = saved.CustomerId,
            BeforeJson = existing is null ? null : JsonSerializer.Serialize(existing),
            AfterJson = JsonSerializer.Serialize(saved),
        });

        transaction.Commit();
        return saved;
    }

    /// <summary>Every customer, by name.</summary>
    public IReadOnlyList<CustomerRecord> GetCustomers() =>
        Read("SELECT * FROM customers ORDER BY name COLLATE NOCASE;", ReadCustomer);

    /// <summary>One customer, or <see langword="null"/>.</summary>
    public CustomerRecord? GetCustomer(string customerId) => ReadOne(
        "SELECT * FROM customers WHERE customer_id = $id;", ReadCustomer, ("$id", customerId));

    /// <summary>The next free <c>c-NNNN</c> identifier.</summary>
    public string NextCustomerId()
    {
        var highest = 0;
        foreach (var text in Read(
                     "SELECT customer_id FROM customers WHERE customer_id LIKE 'c-%';", r => r.GetString(0)))
        {
            if (text.Length > 2 && int.TryParse(
                    text.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                highest = Math.Max(highest, number);
            }
        }

        return $"c-{highest + 1:0000}";
    }

    // ── Licences ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Creates or updates a licence's terms, and records it.</summary>
    public LicenseRecord SaveLicense(LicenseRecord license)
    {
        ArgumentNullException.ThrowIfNull(license);

        using var transaction = _connection.BeginTransaction();
        var saved = SaveLicenseCore(transaction, license, _clock());
        transaction.Commit();
        return saved;
    }

    // ⭐ The body of a licence save, without owning the transaction — so a single save and a licence's
    //    share of a batch go through EXACTLY this code. A batch that reimplemented the write would be a
    //    second place that decides what a licence update means, and the two would drift on the first
    //    column anyone adds.
    private LicenseRecord SaveLicenseCore(
        SqliteTransaction transaction, LicenseRecord license, DateTimeOffset now, string? note = null)
    {
        var existing = GetLicense(license.LicenseId, transaction);

        // ⭐⭐ A LICENCE'S CUSTOMER IS PART OF ITS IDENTITY, AND THE REGISTER REFUSES TO CHANGE IT.
        //
        //    Every artifact ever signed for this licence carries that customer's NAME (D6) — so moving the
        //    row to another customer would make the register disagree with the files it has already sent,
        //    and it would do so silently. Architecture rule 11 on the administrative side.
        //
        //    ⚠ Not hypothetical: it is exactly what the reported "the Licences view only shows the last
        //    customer's licence" turned out to be. The licence FORM kept the previous customer's licence
        //    id when a new customer was started, so the next Save addressed that row — one licence where
        //    there should have been two, and the first customer quietly lost theirs. The form is cleared
        //    now too, but a form is a habit and this is the guarantee.
        if (existing is not null &&
            !string.Equals(existing.CustomerId, license.CustomerId, StringComparison.Ordinal))
        {
            throw new RegisterIntegrityException(
                StatusCatalog.LicenceBelongsToAnotherCustomer,
                $"Licence {license.LicenseId} belongs to customer {existing.CustomerId} and cannot be " +
                $"moved to {license.CustomerId}. Artifacts already issued for it carry the original " +
                "customer's name, so the register would stop agreeing with what was delivered.",
                license.LicenseId, existing.CustomerId, license.CustomerId);
        }

        var saved = license with
        {
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        Execute("""
            INSERT INTO licenses(lid, customer_id, product, seats, not_before, expires_at, maint_until,
                                 status, notes, created_at, updated_at)
            VALUES($lid, $customer, $product, $seats, $nbf, $exp, $maint, $status, $notes, $created, $updated)
            ON CONFLICT(lid) DO UPDATE SET
              customer_id = $customer, product = $product, seats = $seats, not_before = $nbf,
              expires_at = $exp, maint_until = $maint, status = $status, notes = $notes,
              updated_at = $updated;
            """,
            transaction,
            ("$lid", saved.LicenseId), ("$customer", saved.CustomerId), ("$product", saved.Product),
            ("$seats", saved.Seats), ("$nbf", Stamp(saved.NotBefore)), ("$exp", Stamp(saved.ExpiresAt)),
            ("$maint", saved.MaintenanceUntil is { } m ? Stamp(m) : null), ("$status", saved.Status),
            ("$notes", saved.Notes), ("$created", Stamp(saved.CreatedAt)), ("$updated", Stamp(saved.UpdatedAt)));

        AppendAudit(transaction, new AuditEntry
        {
            At = now,
            Actor = _actor,
            Action = existing is null ? "licence.created" : "licence.updated",
            TargetType = "licence",
            TargetId = saved.LicenseId,
            BeforeJson = existing is null ? null : JsonSerializer.Serialize(existing),
            AfterJson = JsonSerializer.Serialize(saved),
            Note = note,
        });

        return saved;
    }

    /// <summary>A customer's licences, newest expiry first.</summary>
    public IReadOnlyList<LicenseRecord> GetLicenses(string customerId) => Read(
        "SELECT * FROM licenses WHERE customer_id = $id ORDER BY expires_at DESC;",
        ReadLicense, ("$id", customerId));

    /// <summary>One licence, or <see langword="null"/>.</summary>
    public LicenseRecord? GetLicense(string licenseId) => GetLicense(licenseId, null);

    private LicenseRecord? GetLicense(string licenseId, SqliteTransaction? transaction) => ReadOne(
        "SELECT * FROM licenses WHERE lid = $lid;", transaction, ReadLicense, ("$lid", licenseId));

    // ── Artifacts ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Records an artifact that was signed, and audits the act of signing.</summary>
    public IssuedArtifactRecord AppendArtifact(IssuedArtifactRecord artifact, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        using var transaction = _connection.BeginTransaction();
        var stored = AppendArtifactCore(transaction, artifact, _clock(), note);
        transaction.Commit();
        return stored;
    }

    // ⭐ The body of an artifact append: the guard, the row, the current-artifact pointer and the history
    //    line — all four, or none of them. Both the single issue and every unit of a batch come through
    //    here, so "appending an artifact makes it current" is one statement in one place.
    private IssuedArtifactRecord AppendArtifactCore(
        SqliteTransaction transaction, IssuedArtifactRecord artifact, DateTimeOffset now, string? note)
    {
        RefuseAnArtifactThatIsNotFresher(transaction, artifact);

        Execute("""
            INSERT INTO issued_artifacts(lid, kid, issued_at, payload_json, token, reason)
            VALUES($lid, $kid, $issued, $payload, $token, $reason);
            """,
            transaction,
            ("$lid", artifact.LicenseId), ("$kid", artifact.KeyId), ("$issued", Stamp(artifact.IssuedAt)),
            ("$payload", artifact.PayloadJson), ("$token", artifact.Token), ("$reason", artifact.Reason));

        var id = (long)Scalar("SELECT last_insert_rowid();", transaction)!;

        // ⭐ The newest artifact becomes the current one, in the same transaction that created it. There
        //    is no window in which a licence has an artifact but no answer to "which one is current".
        Execute("""
            INSERT INTO license_current_artifact(lid, artifact_id, set_at)
            VALUES($lid, $id, $now)
            ON CONFLICT(lid) DO UPDATE SET artifact_id = $id, set_at = $now;
            """,
            transaction, ("$lid", artifact.LicenseId), ("$id", id), ("$now", Stamp(now)));

        AppendAudit(transaction, new AuditEntry
        {
            At = now,
            Actor = _actor,
            Action = "licence.issued",
            TargetType = "licence",
            TargetId = artifact.LicenseId,
            AfterJson = JsonSerializer.Serialize(new { artifact.KeyId, artifact.Reason, artifact.IssuedAt }),
            Note = note,
        });

        return artifact with { ArtifactId = id, Status = ArtifactStatuses.Current };
    }

    /// <summary>
    /// ⚠⚠ Refuses an artifact whose <c>iat</c> does not beat the current one for the same licence.
    ///
    /// <para>EmberTern installs a replacement only when <c>incoming.iat &gt; local.iat</c> (§16.4). The
    /// issuer truncates <c>iat</c> to whole seconds, so issuing the same licence twice inside one second
    /// — an operator double-clicking, or a batch retried immediately — produces a file the customer's
    /// EmberTern would silently decline to install. ⭐ Refusing here is the project's "refuse, never
    /// repair" rule: the alternative is a register that believes it delivered something the client will
    /// not accept.</para>
    /// </summary>
    private void RefuseAnArtifactThatIsNotFresher(
        SqliteTransaction transaction, IssuedArtifactRecord artifact)
    {
        var current = GetCurrentArtifact(artifact.LicenseId, transaction);
        if (current is null || artifact.IssuedAt > current.IssuedAt)
        {
            return;
        }

        throw new RegisterIntegrityException(
            StatusCatalog.ArtifactIatNotAfterCurrent,
            $"The artifact for licence {artifact.LicenseId} carries iat {Stamp(artifact.IssuedAt)}, which " +
            $"does not come after the current artifact's {Stamp(current.IssuedAt)}. EmberTern would refuse " +
            "to install it as a replacement, so it is refused here instead of being recorded as delivered.",
            artifact.LicenseId, Stamp(artifact.IssuedAt), Stamp(current.IssuedAt));
    }

    /// <summary>Every artifact ever issued for a licence, newest first, each carrying its status.</summary>
    public IReadOnlyList<IssuedArtifactRecord> GetArtifacts(string licenseId) => Read(
        ArtifactSelect + " WHERE a.lid = $lid ORDER BY a.artifact_id DESC;",
        ReadArtifact, ("$lid", licenseId));

    /// <summary>
    /// The artifact currently marked <see cref="ArtifactStatuses.Current"/> for a licence, or
    /// <see langword="null"/> when it has never been issued.
    /// </summary>
    public IssuedArtifactRecord? GetCurrentArtifact(string licenseId) =>
        GetCurrentArtifact(licenseId, null);

    private IssuedArtifactRecord? GetCurrentArtifact(
        string licenseId, SqliteTransaction? transaction) => ReadOne(
        ArtifactSelect + """
             JOIN license_current_artifact cur
               ON cur.lid = a.lid AND cur.artifact_id = a.artifact_id
            WHERE a.lid = $lid;
            """,
        transaction, ReadArtifact, ("$lid", licenseId));

    // ⚠ The status is PROJECTED here, from the same join the artifact_status view uses. It is not a
    //   column on the row, and a reader that expects one is reading a design that was deliberately not
    //   built — see ArtifactStatuses.
    private const string ArtifactSelect = """
        SELECT a.*,
               CASE WHEN c.artifact_id = a.artifact_id THEN 'current' ELSE 'superseded' END AS status
        FROM issued_artifacts a
        LEFT JOIN license_current_artifact c ON c.lid = a.lid
        """;

    private static IssuedArtifactRecord ReadArtifact(SqliteDataReader reader) => new()
    {
        ArtifactId = reader.GetInt64(reader.GetOrdinal("artifact_id")),
        LicenseId = reader.GetString(reader.GetOrdinal("lid")),
        KeyId = reader.GetString(reader.GetOrdinal("kid")),
        IssuedAt = Parse(reader.GetString(reader.GetOrdinal("issued_at"))),
        PayloadJson = reader.GetString(reader.GetOrdinal("payload_json")),
        Token = reader.GetString(reader.GetOrdinal("token")),
        Reason = reader.GetString(reader.GetOrdinal("reason")),
        Status = reader.GetString(reader.GetOrdinal("status")),
    };

    // ── Whole-register reads ──────────────────────────────────────────────────────────

    // ⭐ The three reads below exist for the JSONL export, and they are deliberately NOT the list-view
    //    queries. A list view is allowed to be a projection — LicenseSummary drops maint_until and both
    //    timestamps, GetAudit stops at 200 rows — because a list is a summary. An export that quietly
    //    dropped a column or truncated a history would be a file the operator believes is their register.
    //    ⚠ They are also unlimited on purpose: a LIMIT here is the shape of a silent partial export.

    /// <summary>Every licence in the register, in a stable order. ⚠ No limit — this is for export.</summary>
    public IReadOnlyList<LicenseRecord> GetAllLicenses() => Read(
        "SELECT * FROM licenses ORDER BY lid;", ReadLicense);

    /// <summary>Every artifact ever issued, oldest first, each carrying its projected status.</summary>
    public IReadOnlyList<IssuedArtifactRecord> GetAllArtifacts() => Read(
        ArtifactSelect + " ORDER BY a.artifact_id;", ReadArtifact);

    /// <summary>
    /// Every current-artifact pointer, as stored.
    ///
    /// <para>⭐ The POINTER, not the artifact it names. <see cref="GetCurrentArtifact"/> answers "which
    /// artifact should this customer be holding?" and resolves the join; this answers "what does the
    /// register actually record?", <c>set_at</c> included. An export that carried only the resolved
    /// artifact would lose when the pointer was moved — and the pointer's own history is the difference
    /// between a renewal and a re-export.</para>
    /// </summary>
    public IReadOnlyList<CurrentArtifactPointer> GetCurrentArtifactPointers() => Read(
        "SELECT * FROM license_current_artifact ORDER BY lid;",
        static reader => new CurrentArtifactPointer
        {
            LicenseId = reader.GetString(reader.GetOrdinal("lid")),
            ArtifactId = reader.GetInt64(reader.GetOrdinal("artifact_id")),
            SetAt = Parse(reader.GetString(reader.GetOrdinal("set_at"))),
        });

    // ── Batches ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Records a whole issuing operation — one licence or twenty — as a single unit of work.
    ///
    /// <para><b>This is the answer to "a failure anywhere must not leave a signed artifact without a
    /// record, nor half a batch applied".</b> It works because the two halves of issuing have completely
    /// different natures, and the operation is ordered to exploit that:</para>
    ///
    /// <list type="number">
    /// <item><b>Signing is pure.</b> It is a function of key, terms and clock — no file, no row, nothing
    /// observable. Every artifact in <paramref name="units"/> is therefore ALREADY signed by the time this
    /// method is called, and a failure while signing left the register untouched and produced nothing
    /// anybody could hold.</item>
    /// <item><b>Recording is atomic.</b> Everything below happens in ONE SQLite transaction: each
    /// licence's new terms, each artifact row, each current-artifact pointer, each history line, plus one
    /// line naming the batch. A fault at any point rolls all of it back, and the signed tokens are simply
    /// dropped on the floor — unseen, unrecorded, and reproducible by signing again.</item>
    /// <item><b>Delivery comes after.</b> Writing files is the caller's next step, never part of this one,
    /// and it reads the STORED token. So the only path by which an artifact reaches a customer runs
    /// through a committed row.</item>
    /// </list>
    ///
    /// <para>⛔ The tempting alternative — sign-and-record one licence at a time — was rejected: it is
    /// twenty transactions, and an interruption at ten leaves ten customers extended, ten not, and an
    /// operator with no way to tell which half is which.</para>
    /// </summary>
    /// <param name="units">What to record. ⚠ Each licence may appear at most once.</param>
    /// <param name="note">A remark stored on the batch's own history line.</param>
    /// <exception cref="ArgumentException">The batch is empty.</exception>
    /// <exception cref="RegisterIntegrityException">
    /// A licence appears twice, a unit's terms name a different licence than its artifact, or an
    /// artifact's <c>iat</c> does not come after the current one's.
    /// </exception>
    public IssueBatchResult ApplyIssueBatch(IReadOnlyList<LicenseIssueUnit> units, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (units.Count == 0)
        {
            throw new ArgumentException("A batch must contain at least one licence.", nameof(units));
        }

        // ⭐ Checked BEFORE the transaction opens. These are faults in what was asked for, not in what the
        //    database holds, and refusing them here means the transaction is never opened at all.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in units)
        {
            var lid = unit.Artifact.LicenseId;

            if (!seen.Add(lid))
            {
                throw new RegisterIntegrityException(
                    StatusCatalog.LicenceAppearsTwiceInBatch,
                    $"Licence {lid} appears twice in one batch. Two artifacts issued in the same operation " +
                    "would carry the same iat, and the second could never replace the first in the field.",
                    lid);
            }

            if (unit.UpdatedTerms is { } terms &&
                !string.Equals(terms.LicenseId, lid, StringComparison.Ordinal))
            {
                throw new RegisterIntegrityException(
                    StatusCatalog.BatchUnitPairsMismatchedTerms,
                    $"A batch unit pairs the terms of licence {terms.LicenseId} with an artifact for {lid}.",
                    terms.LicenseId, lid);
            }

            // ⭐ A batch may not be worse to audit than a single issue. The summary is the sentence that
            //    lets `licence.issued` answer "on what terms?" without joining anything, and a blank one
            //    would put the gap back while every test stayed green.
            if (string.IsNullOrWhiteSpace(unit.Summary))
            {
                throw new ArgumentException(
                    $"The batch unit for licence {lid} carries no terms summary, so its audit line could " +
                    "not say what was issued.",
                    nameof(units));
            }
        }

        var now = _clock();
        var batchId = Guid.NewGuid().ToString("N")[..12];
        var batchNote = $"batch {batchId}";
        var stored = new List<IssuedArtifactRecord>(units.Count);

        using var transaction = _connection.BeginTransaction();

        foreach (var unit in units)
        {
            if (unit.UpdatedTerms is { } terms)
            {
                SaveLicenseCore(transaction, terms, now, batchNote);
            }

            // ⭐ The SAME sentence the single issuing path writes, plus the marker that says this one was
            //    part of an operation. ⚠ Appended rather than instead of, exactly as the operator's own
            //    note is on the single path: the summary is the terms, the marker is the correlation, and
            //    dropping either leaves a question the audit can no longer answer on its own.
            stored.Add(AppendArtifactCore(
                transaction, unit.Artifact, now, $"{unit.Summary.Trim()} ({batchNote})"));
        }

        // ⭐ One line that says the operation happened as one act. Without it the history shows forty
        //    unrelated changes at the same timestamp and nothing that explains them as a decision.
        AppendAudit(transaction, new AuditEntry
        {
            At = now,
            Actor = _actor,
            Action = "licence.batch-issued",
            TargetType = "batch",
            TargetId = batchId,
            AfterJson = JsonSerializer.Serialize(units.Select(u => u.Artifact.LicenseId).ToArray()),
            Note = note,
        });

        transaction.Commit();
        return new IssueBatchResult(batchId, stored);
    }

    // ── Queries ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Licences across every customer, soonest expiry first.
    ///
    /// <para>⭐ The query bulk operations select from. ⚠ Structured filters run in SQL; the free-text
    /// match runs in memory — see <see cref="LicenseQuery.Text"/> for the measured reason.</para>
    /// </summary>
    public IReadOnlyList<LicenseSummary> QueryLicenses(LicenseQuery? query = null)
    {
        query ??= LicenseQuery.All;

        var conditions = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            conditions.Add("l.status = $status");
            parameters.Add(("$status", query.Status));
        }

        // ⚠ Compared as TEXT, and that is sound rather than lucky: every timestamp is written through
        //    LicensePayload's format — fixed-width, UTC, "yyyy-MM-ddTHH:mm:ssZ" — so lexicographic order
        //    and chronological order are the same order. A format with an offset or a variable width
        //    would break this silently, which is why the register has exactly one way to write a stamp.
        if (query.ExpiresBefore is { } before)
        {
            conditions.Add("l.expires_at < $before");
            parameters.Add(("$before", Stamp(before)));
        }

        if (query.ExpiresFrom is { } from)
        {
            conditions.Add("l.expires_at >= $from");
            parameters.Add(("$from", Stamp(from)));
        }

        if (query.NeverIssued is { } neverIssued)
        {
            conditions.Add(neverIssued ? "cur.artifact_id IS NULL" : "cur.artifact_id IS NOT NULL");
        }

        var sql = LicenseSummarySelect +
                  (conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions)) +
                  " ORDER BY l.expires_at ASC, c.name COLLATE NOCASE ASC;";

        var rows = Read(sql, null, ReadSummary, parameters.ToArray());

        return Matching(
            rows,
            query.Text,
            query.Limit,
            static (row, text) =>
                Contains(row.CustomerName, text) ||
                Contains(row.CustomerEmail, text) ||
                Contains(row.CustomerFirstName, text) ||
                Contains(row.CustomerLastName, text) ||
                Contains(row.License.CustomerId, text) ||
                Contains(row.License.LicenseId, text) ||
                Contains(row.License.Notes, text));
    }

    /// <summary>Customers matching free text, by name. Blank text returns everyone.</summary>
    public IReadOnlyList<CustomerRecord> SearchCustomers(string? text = null, int limit = 500) =>
        Matching(
            GetCustomers(),
            text,
            limit,
            static (customer, needle) =>
                Contains(customer.Name, needle) ||
                Contains(customer.Email, needle) ||
                Contains(customer.CustomerId, needle) ||
                Contains(customer.FirstName, needle) ||
                Contains(customer.LastName, needle));

    // ⭐ One place decides what "matches the text the operator typed" means, for every list in the
    //    application. Two implementations of that would differ on the first field anybody adds.
    private static IReadOnlyList<T> Matching<T>(
        IReadOnlyList<T> rows, string? text, int limit, Func<T, string, bool> predicate)
    {
        var take = Math.Max(0, limit);

        if (string.IsNullOrWhiteSpace(text))
        {
            return rows.Count <= take ? rows : rows.Take(take).ToArray();
        }

        var needle = text.Trim();
        var matched = new List<T>();

        foreach (var row in rows)
        {
            if (matched.Count == take)
            {
                break;
            }

            if (predicate(row, needle))
            {
                matched.Add(row);
            }
        }

        return matched;
    }

    // ⚠ OrdinalIgnoreCase, not SQLite's LIKE: .NET applies Unicode case folding, so "ŁÓDŹ" finds "Łódź".
    //    SQLite's LIKE and lower() are case-insensitive for ASCII only, by documented design.
    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private const string LicenseSummarySelect = """
        SELECT l.*, c.name AS customer_name, c.email AS customer_email,
               c.first_name AS customer_first_name, c.last_name AS customer_last_name,
               (SELECT COUNT(*)          FROM issued_artifacts a WHERE a.lid = l.lid) AS artifact_count,
               (SELECT MAX(a.issued_at)  FROM issued_artifacts a WHERE a.lid = l.lid) AS last_issued_at,
               cur.artifact_id AS current_artifact_id
        FROM licenses l
        JOIN customers c ON c.customer_id = l.customer_id
        LEFT JOIN license_current_artifact cur ON cur.lid = l.lid
        """;

    private static LicenseSummary ReadSummary(SqliteDataReader reader) => new()
    {
        License = ReadLicense(reader),
        CustomerName = reader.GetString(reader.GetOrdinal("customer_name")),
        CustomerEmail = Text(reader, "customer_email"),
        CustomerFirstName = Text(reader, "customer_first_name"),
        CustomerLastName = Text(reader, "customer_last_name"),
        ArtifactCount = reader.GetInt32(reader.GetOrdinal("artifact_count")),
        LastIssuedAt = Text(reader, "last_issued_at") is { } issued ? Parse(issued) : null,
        CurrentArtifactId = reader.IsDBNull(reader.GetOrdinal("current_artifact_id"))
            ? null
            : reader.GetInt64(reader.GetOrdinal("current_artifact_id")),
    };

    // ── Integrity ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks the register against its own invariants and returns every problem found, or an empty list
    /// when it is sound.
    ///
    /// <para>⭐ It reports rather than throws because the caller decides what a problem means: a list view
    /// warns, ⏭ and a restore (L5.5) refuses. ⛔ There is deliberately no repair — a register that quietly
    /// fixes its own history is a register whose history cannot be trusted.</para>
    /// </summary>
    public IReadOnlyList<LocalizedText> CheckIntegrity()
    {
        var problems = new List<LocalizedText>();

        foreach (var lid in Read(
                     """
                     SELECT DISTINCT a.lid FROM issued_artifacts a
                     LEFT JOIN license_current_artifact c ON c.lid = a.lid
                     WHERE c.lid IS NULL;
                     """,
                     static reader => reader.GetString(0)))
        {
            problems.Add(new LocalizedText(StatusCatalog.IntegrityNoCurrentArtifact, lid));
        }

        foreach (var lid in Read(
                     """
                     SELECT c.lid FROM license_current_artifact c
                     LEFT JOIN issued_artifacts a
                       ON a.artifact_id = c.artifact_id AND a.lid = c.lid
                     WHERE a.artifact_id IS NULL;
                     """,
                     static reader => reader.GetString(0)))
        {
            problems.Add(new LocalizedText(StatusCatalog.IntegrityCurrentNotOwned, lid));
        }

        // ⚠ The pointer must name the NEWEST artifact. Appending only ever moves it forward, so anything
        //    else means the file was edited outside this application.
        foreach (var lid in Read(
                     """
                     SELECT c.lid FROM license_current_artifact c
                     WHERE c.artifact_id <> (
                       SELECT MAX(a.artifact_id) FROM issued_artifacts a WHERE a.lid = c.lid);
                     """,
                     static reader => reader.GetString(0)))
        {
            problems.Add(new LocalizedText(StatusCatalog.IntegrityCurrentNotNewest, lid));
        }

        foreach (var lid in Read(
                     """
                     SELECT l.lid FROM licenses l
                     LEFT JOIN customers cu ON cu.customer_id = l.customer_id
                     WHERE cu.customer_id IS NULL;
                     """,
                     static reader => reader.GetString(0)))
        {
            problems.Add(new LocalizedText(StatusCatalog.IntegrityCustomerMissing, lid));
        }

        return problems;
    }

    // ── Snapshot ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A consistent copy of the whole register, as the bytes of a SQLite file.
    ///
    /// <para>⭐ <b>It is <c>VACUUM INTO</c>, not <c>File.Copy</c>.</b> The register is open — a raw copy of
    /// a live database file can catch it mid-transaction, and the resulting file is not a register, it is
    /// a plausible-looking one. <c>VACUUM INTO</c> is SQLite's own consistent-snapshot operation and reads
    /// through the same connection, so what it writes is the register as of one instant.</para>
    ///
    /// <para>⚠⚠ <b>The snapshot is NOT byte-identical to <c>licenses.db</c>, and that is ratified rather
    /// than overlooked</b> (D‑3): <c>VACUUM</c> defragments, so pages move and the file size changes. What
    /// is promised is the CONTENT — every row of every table, <c>issued_artifacts.artifact_id</c> and the
    /// <c>license_current_artifact</c> pointers included. ⛔ Never assert a backup by hashing the file;
    /// assert it with <see cref="DumpContent"/>.</para>
    /// </summary>
    public byte[] CreateSnapshot()
    {
        // ⚠ VACUUM INTO refuses a destination that already exists, and it cannot run inside a
        //    transaction. A fresh name in the temp folder satisfies both without a policy.
        var scratch = Path.Combine(
            Path.GetTempPath(), "etlm-snapshot-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            Execute("VACUUM INTO $target;", null, ("$target", scratch));
            return File.ReadAllBytes(scratch);
        }
        finally
        {
            try
            {
                File.Delete(scratch);
            }
            catch (IOException)
            {
                // ⚠ A leftover scratch file must not turn a good backup into a failed one. It is in the
                //    OS temp folder, it holds the same data the operator just chose to export, and the
                //    alternative — failing here — would discard a snapshot that already succeeded.
            }
        }
    }

    /// <summary>
    /// Every row of every table, rendered as one comparable line each, sorted.
    ///
    /// <para>⭐⭐ <b>This is the fidelity oracle for backup and restore, and it is deliberately driven by
    /// the SCHEMA rather than by our record types.</b> Columns come from the reader's own metadata, so a
    /// column added to a table is covered the day it is added — where a hand-written projection would
    /// keep passing while silently dropping it. ⛔ Do not reimplement this on top of the JSONL export:
    /// an oracle that shares a mapping with the thing it checks agrees with it about what it forgot.</para>
    ///
    /// <para>⚠ <c>sqlite_sequence</c> is included ON PURPOSE. It carries the AUTOINCREMENT high-water
    /// mark for <c>issued_artifacts</c>, so losing it would let a restored register re-use an
    /// <c>artifact_id</c> that history already spent. It is the one <c>sqlite_</c> table that is data.</para>
    ///
    /// <para>⚠ Rows are sorted here rather than in SQL because <c>VACUUM</c> may renumber the hidden
    /// <c>rowid</c> of a table whose primary key is not an INTEGER — <c>customers</c> and <c>licenses</c>
    /// both qualify — so "the order the file stores them in" is not a property worth comparing. Every
    /// value that IS data is a column, and every column is compared.</para>
    /// </summary>
    public IReadOnlyList<string> DumpContent()
    {
        var tables = Read(
            """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND (name NOT LIKE 'sqlite_%' OR name = 'sqlite_sequence');
            """,
            static reader => reader.GetString(0));

        var lines = new List<string>();

        foreach (var table in tables)
        {
            // ⚠ The table name is an identifier, so it cannot be a parameter. It comes from
            //    sqlite_master rather than from a caller, which is what makes the interpolation safe.
            lines.AddRange(Read(
                $"SELECT * FROM \"{table}\";",
                reader =>
                {
                    var builder = new StringBuilder(table);
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        builder.Append('\u001F').Append(reader.GetName(i)).Append('=');
                        builder.Append(reader.IsDBNull(i)
                            ? "null"
                            : JsonSerializer.Serialize(reader.GetValue(i)));
                    }

                    return builder.ToString();
                }));
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    // ── History ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The history, newest first, optionally narrowed to one subject.</summary>
    public IReadOnlyList<AuditEntry> GetAudit(AuditQuery? query = null)
    {
        query ??= AuditQuery.All;

        var conditions = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        if (!string.IsNullOrWhiteSpace(query.TargetType))
        {
            conditions.Add("target_type = $type");
            parameters.Add(("$type", query.TargetType));
        }

        if (!string.IsNullOrWhiteSpace(query.TargetId))
        {
            conditions.Add("target_id = $id");
            parameters.Add(("$id", query.TargetId));
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            conditions.Add("action = $action");
            parameters.Add(("$action", query.Action));
        }

        parameters.Add(("$limit", Math.Max(0, query.Limit)));

        var sql = "SELECT * FROM audit_log" +
                  (conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions)) +
                  " ORDER BY audit_id DESC LIMIT $limit;";

        return Read(sql, null, ReadAudit, parameters.ToArray());
    }

    private static AuditEntry ReadAudit(SqliteDataReader reader) => new()
    {
        AuditId = reader.GetInt64(reader.GetOrdinal("audit_id")),
        At = Parse(reader.GetString(reader.GetOrdinal("at"))),
        Actor = reader.GetString(reader.GetOrdinal("actor")),
        Action = reader.GetString(reader.GetOrdinal("action")),
        TargetType = reader.GetString(reader.GetOrdinal("target_type")),
        TargetId = reader.GetString(reader.GetOrdinal("target_id")),
        BeforeJson = Text(reader, "before_json"),
        AfterJson = Text(reader, "after_json"),
        Note = Text(reader, "note"),
    };

    /// <summary>
    /// When each licence was last SENT to a customer — the newest <c>licence.sent</c> per <c>lid</c>,
    /// or an empty map when nothing has ever been sent.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ <b>ONE aggregate query for the whole register, and the alternatives were measured
    /// rather than judged.</b> Its consumer is a bulk-send preview that is rebuilt on every keystroke in a
    /// search box, over a selection that may hold hundreds of licences:</para>
    /// <list type="bullet">
    ///   <item>⛔ <b>Not one <see cref="GetAudit"/> per licence.</b> <c>audit_log</c> carries no index on
    ///   <c>(target_type, target_id, action)</c> — only its primary key — so each such call is a full
    ///   table scan, and the shape would be one scan per selected licence per typed character.</item>
    ///   <item>⛔⛔ <b>Not <see cref="GetAudit"/> at all.</b> Its <see cref="AuditQuery.Limit"/>
    ///   defaults to 200, so aggregating over what it returns would answer confidently and WRONGLY on any
    ///   register with a longer history — with no error and no way to notice. That is the specific defect
    ///   <c>AHistoryLongerThanTheAuditQueryLimit_IsNotTruncated</c> exists to make unreachable.</item>
    /// </list>
    /// <para>⚠⚠ <b><c>MAX(at)</c> is a TEXT maximum, and it is only the newest timestamp because the
    /// stored format is fixed-width UTC</b> — <c>LicensePayload.TimestampFormat</c> is
    /// <c>yyyy-MM-dd'T'HH:mm:ss'Z'</c>, so lexicographic order IS chronological order. ⛔ A format that
    /// ever gained a numeric offset or a variable-width field would make this aggregate pick the wrong row
    /// silently. Pinned by <c>TheNewestSendWins_EvenWhenItIsNotTheNewestRow</c>.</para>
    /// <para>⚠ It answers about DELIVERY ATTEMPTS THAT SUCCEEDED and nothing else: a
    /// <c>licence.send-failed</c> line is not a send, and an <c>.eml</c> export
    /// (<c>licence.exported</c>) is deliberately not one either — a file existing is not a message
    /// reaching anybody.</para>
    /// <para>⚠ And "sent" means the SERVER ACCEPTED it. ⛔ Never "the customer received it": with one
    /// recipient per message a provider commonly accepts mail for a bad address and bounces it later,
    /// which this application cannot see at all.</para>
    /// </remarks>
    public IReadOnlyDictionary<string, DateTimeOffset> GetLastSentAt()
    {
        var rows = Read(
            """
            SELECT target_id AS lid, MAX(at) AS last_sent
              FROM audit_log
             WHERE target_type = $type AND action = $action
             GROUP BY target_id;
            """,
            reader => (
                Lid: reader.GetString(reader.GetOrdinal("lid")),
                LastSent: Parse(reader.GetString(reader.GetOrdinal("last_sent")))),
            ("$type", AuditTargets.Licence),
            ("$action", AuditActions.LicenceSent));

        // ⚠ Ordinal, like every other identity comparison over a `lid` in this file: the ids are
        //   generated hex and a culture-aware comparison would be a different question.
        var map = new Dictionary<string, DateTimeOffset>(rows.Count, StringComparer.Ordinal);
        foreach (var row in rows)
        {
            map[row.Lid] = row.LastSent;
        }

        return map;
    }

    /// <summary>Records something that is not a table mutation — a key ceremony, an export.</summary>
    public void Record(string action, string targetType, string targetId, string? note = null)
    {
        using var transaction = _connection.BeginTransaction();
        AppendAudit(transaction, new AuditEntry
        {
            At = _clock(),
            Actor = _actor,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Note = note,
        });
        transaction.Commit();
    }

    private void AppendAudit(SqliteTransaction transaction, AuditEntry entry) => Execute("""
        INSERT INTO audit_log(at, actor, action, target_type, target_id, before_json, after_json, note)
        VALUES($at, $actor, $action, $type, $id, $before, $after, $note);
        """,
        transaction,
        ("$at", Stamp(entry.At)), ("$actor", entry.Actor), ("$action", entry.Action),
        ("$type", entry.TargetType), ("$id", entry.TargetId), ("$before", entry.BeforeJson),
        ("$after", entry.AfterJson), ("$note", entry.Note));

    // ── Plumbing ────────────────────────────────────────────────────────────────────────────────────

    private static string Stamp(DateTimeOffset value) =>
        EmberTern.Licensing.LicensePayload.FormatTimestamp(value);

    private static DateTimeOffset Parse(string text) => DateTimeOffset.ParseExact(
        text, EmberTern.Licensing.LicensePayload.TimestampFormat, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static string? Text(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static CustomerRecord ReadCustomer(SqliteDataReader reader) => new()
    {
        CustomerId = reader.GetString(reader.GetOrdinal("customer_id")),
        Name = reader.GetString(reader.GetOrdinal("name")),
        Address = Text(reader, "address"),
        FirstName = Text(reader, "first_name"),
        LastName = Text(reader, "last_name"),
        Email = Text(reader, "email"),
        Notes = Text(reader, "notes"),
        CreatedAt = Parse(reader.GetString(reader.GetOrdinal("created_at"))),
        UpdatedAt = Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
    };

    private static LicenseRecord ReadLicense(SqliteDataReader reader) => new()
    {
        LicenseId = reader.GetString(reader.GetOrdinal("lid")),
        CustomerId = reader.GetString(reader.GetOrdinal("customer_id")),
        Product = reader.GetString(reader.GetOrdinal("product")),
        Seats = reader.GetInt32(reader.GetOrdinal("seats")),
        NotBefore = Parse(reader.GetString(reader.GetOrdinal("not_before"))),
        ExpiresAt = Parse(reader.GetString(reader.GetOrdinal("expires_at"))),
        MaintenanceUntil = Text(reader, "maint_until") is { } m ? Parse(m) : null,
        Status = reader.GetString(reader.GetOrdinal("status")),
        Notes = Text(reader, "notes"),
        CreatedAt = Parse(reader.GetString(reader.GetOrdinal("created_at"))),
        UpdatedAt = Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
    };

    private string? ReadMeta(string key)
    {
        try
        {
            return Scalar("SELECT value FROM schema_meta WHERE key = $k;", null, ("$k", key)) as string;
        }
        catch (SqliteException)
        {
            return null;   // The table does not exist yet — a brand-new file.
        }
    }

    private void Execute(
        string sql, SqliteTransaction? transaction = null, params (string Name, object? Value)[] parameters)
    {
        using var command = NewCommand(sql, transaction, parameters);
        command.ExecuteNonQuery();
    }

    private object? Scalar(
        string sql, SqliteTransaction? transaction = null, params (string Name, object? Value)[] parameters)
    {
        using var command = NewCommand(sql, transaction, parameters);
        return command.ExecuteScalar();
    }

    // ⚠ The reader is consumed INSIDE this method rather than handed back, so the command is disposed
    //    on every path. A helper that returns a live SqliteDataReader leaves its command to the garbage
    //    collector, and on SQLite an undisposed command holds a prepared statement open.
    private List<T> Read<T>(
        string sql,
        Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] parameters) =>
        Read(sql, null, map, parameters);

    // ⚠⚠ THE TRANSACTION IS THREADED THROUGH THE READS, and L5 is where that stopped being optional.
    //    Microsoft.Data.Sqlite refuses to execute a command whose Transaction does not match the
    //    connection's active one — so a read issued from inside a batch with `null` here does not read
    //    stale data, it THROWS. §36.4 recorded this as "worth knowing before L5 adds bulk operations";
    //    ApplyIssueBatch is that bulk operation, and it reads each licence's prior state inside its own
    //    transaction to build the history's before/after.
    private List<T> Read<T>(
        string sql,
        SqliteTransaction? transaction,
        Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] parameters)
    {
        var results = new List<T>();

        using var command = NewCommand(sql, transaction, parameters);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(map(reader));
        }

        return results;
    }

    private T? ReadOne<T>(
        string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
        where T : class =>
        ReadOne(sql, null, map, parameters);

    private T? ReadOne<T>(
        string sql,
        SqliteTransaction? transaction,
        Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] parameters)
        where T : class
    {
        using var command = NewCommand(sql, transaction, parameters);
        using var reader = command.ExecuteReader();
        return reader.Read() ? map(reader) : null;
    }

    /// <summary>
    /// How many statements this register has prepared since it was opened.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ <b>A MEASUREMENT SEAM, and it exists because the claim it measures cannot be reached
    /// through the public API.</b> <see cref="GetLastSentAt"/> answers for the whole register in one
    /// statement, and the design it replaced was one full <c>audit_log</c> scan PER SELECTED LICENCE per
    /// keystroke (§60.7). ⚠ The property that matters is therefore the STATEMENT COUNT, not a
    /// duration — a wall-clock threshold on a developer machine is a flaky test, and it could not tell
    /// "one query" from "five hundred fast ones".</para>
    /// <para>⚠ It counts commands PREPARED, which for every read path in this class is one per
    /// executed statement, and it deliberately does not distinguish reads from writes: a caller measuring
    /// a cost takes the difference across the call it is measuring.</para>
    /// <para>⛔ Not a diagnostic surface and not public: nothing in the application reads it, and it
    /// must not grow a consumer. Same arrangement, and same honest provenance, as the
    /// <c>InternalsVisibleTo</c> the csproj already carries for <c>BackupWorkflow</c>'s verification path.</para>
    /// </remarks>
    internal long StatementsExecuted { get; private set; }

    private SqliteCommand NewCommand(
        string sql, SqliteTransaction? transaction, (string Name, object? Value)[] parameters)
    {
        StatementsExecuted++;

        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    /// <summary>
    /// Closes the register and RELEASES THE FILE.
    ///
    /// <para>⚠⚠ <c>Close</c> and <c>Dispose</c> are not enough, and the difference is invisible until
    /// something tries to move the file. <c>Microsoft.Data.Sqlite</c> POOLS connections by path: disposing
    /// one returns it to the pool with the operating-system handle still open, so on Windows the next
    /// <c>File.Move</c>, <c>File.Delete</c> or <c>File.ReadAllBytes</c> fails with <i>"used by another
    /// process"</i> against a register the caller believes it has closed.</para>
    ///
    /// <para>⭐ Measured while building L5.5: the restore path stages a register in a temporary folder,
    /// opens it to check its integrity, closes it and then materialises the target — and that last step is
    /// exactly the operation pooling breaks. <c>ManagerFixture</c> had been swallowing the same
    /// <c>IOException</c> in its cleanup since L3.</para>
    /// </summary>
    public void Dispose()
    {
        _connection.Close();
        SqliteConnection.ClearPool(_connection);
        _connection.Dispose();
    }
}

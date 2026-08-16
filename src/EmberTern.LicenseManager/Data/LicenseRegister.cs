using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

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
            $"The artifact for licence {artifact.LicenseId} carries iat {Stamp(artifact.IssuedAt)}, which " +
            $"does not come after the current artifact's {Stamp(current.IssuedAt)}. EmberTern would refuse " +
            "to install it as a replacement, so it is refused here instead of being recorded as delivered.");
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
                    $"Licence {lid} appears twice in one batch. Two artifacts issued in the same operation " +
                    "would carry the same iat, and the second could never replace the first in the field.");
            }

            if (unit.UpdatedTerms is { } terms &&
                !string.Equals(terms.LicenseId, lid, StringComparison.Ordinal))
            {
                throw new RegisterIntegrityException(
                    $"A batch unit pairs the terms of licence {terms.LicenseId} with an artifact for {lid}.");
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

            stored.Add(AppendArtifactCore(transaction, unit.Artifact, now, batchNote));
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
    public IReadOnlyList<string> CheckIntegrity()
    {
        var problems = new List<string>();

        foreach (var lid in Read(
                     """
                     SELECT DISTINCT a.lid FROM issued_artifacts a
                     LEFT JOIN license_current_artifact c ON c.lid = a.lid
                     WHERE c.lid IS NULL;
                     """,
                     static reader => reader.GetString(0)))
        {
            problems.Add($"Licence {lid} has artifacts but no current one is marked.");
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
            problems.Add($"Licence {lid} marks a current artifact that does not belong to it.");
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
            problems.Add($"Licence {lid} marks an artifact that is not its newest.");
        }

        foreach (var lid in Read(
                     """
                     SELECT l.lid FROM licenses l
                     LEFT JOIN customers cu ON cu.customer_id = l.customer_id
                     WHERE cu.customer_id IS NULL;
                     """,
                     static reader => reader.GetString(0)))
        {
            problems.Add($"Licence {lid} belongs to a customer that is not in the register.");
        }

        return problems;
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

    private SqliteCommand NewCommand(
        string sql, SqliteTransaction? transaction, (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    /// <summary>Closes the register.</summary>
    public void Dispose()
    {
        _connection.Close();
        _connection.Dispose();
    }
}

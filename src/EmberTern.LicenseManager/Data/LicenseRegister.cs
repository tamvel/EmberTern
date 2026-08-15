using System;
using System.Collections.Generic;
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
    public const int CurrentSchemaVersion = 1;

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

        var now = _clock();
        var existing = GetLicense(license.LicenseId);
        var saved = license with
        {
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        using var transaction = _connection.BeginTransaction();

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
        });

        transaction.Commit();
        return saved;
    }

    /// <summary>A customer's licences, newest expiry first.</summary>
    public IReadOnlyList<LicenseRecord> GetLicenses(string customerId) => Read(
        "SELECT * FROM licenses WHERE customer_id = $id ORDER BY expires_at DESC;",
        ReadLicense, ("$id", customerId));

    /// <summary>One licence, or <see langword="null"/>.</summary>
    public LicenseRecord? GetLicense(string licenseId) => ReadOne(
        "SELECT * FROM licenses WHERE lid = $lid;", ReadLicense, ("$lid", licenseId));

    // ── Artifacts ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Records an artifact that was signed, and audits the act of signing.</summary>
    public IssuedArtifactRecord AppendArtifact(IssuedArtifactRecord artifact, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var now = _clock();
        using var transaction = _connection.BeginTransaction();

        Execute("""
            INSERT INTO issued_artifacts(lid, kid, issued_at, payload_json, token, reason)
            VALUES($lid, $kid, $issued, $payload, $token, $reason);
            """,
            transaction,
            ("$lid", artifact.LicenseId), ("$kid", artifact.KeyId), ("$issued", Stamp(artifact.IssuedAt)),
            ("$payload", artifact.PayloadJson), ("$token", artifact.Token), ("$reason", artifact.Reason));

        var id = (long)Scalar("SELECT last_insert_rowid();", transaction)!;

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

        transaction.Commit();
        return artifact with { ArtifactId = id };
    }

    /// <summary>Every artifact ever issued for a licence, newest first.</summary>
    public IReadOnlyList<IssuedArtifactRecord> GetArtifacts(string licenseId) => Read(
        "SELECT * FROM issued_artifacts WHERE lid = $lid ORDER BY artifact_id DESC;",
        static reader => new IssuedArtifactRecord
        {
            ArtifactId = reader.GetInt64(reader.GetOrdinal("artifact_id")),
            LicenseId = reader.GetString(reader.GetOrdinal("lid")),
            KeyId = reader.GetString(reader.GetOrdinal("kid")),
            IssuedAt = Parse(reader.GetString(reader.GetOrdinal("issued_at"))),
            PayloadJson = reader.GetString(reader.GetOrdinal("payload_json")),
            Token = reader.GetString(reader.GetOrdinal("token")),
            Reason = reader.GetString(reader.GetOrdinal("reason")),
        },
        ("$lid", licenseId));

    // ── History ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The history, newest first.</summary>
    public IReadOnlyList<AuditEntry> GetAudit(int limit = 200) => Read(
        "SELECT * FROM audit_log ORDER BY audit_id DESC LIMIT $limit;",
        static reader => new AuditEntry
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
        },
        ("$limit", limit));

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
        params (string Name, object? Value)[] parameters)
    {
        var results = new List<T>();

        using var command = NewCommand(sql, null, parameters);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(map(reader));
        }

        return results;
    }

    private T? ReadOne<T>(
        string sql, Func<SqliteDataReader, T> map, params (string Name, object? Value)[] parameters)
        where T : class
    {
        using var command = NewCommand(sql, null, parameters);
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

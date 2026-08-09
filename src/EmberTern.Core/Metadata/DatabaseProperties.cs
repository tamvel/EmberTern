using System;

namespace EmberTern.Core.Metadata;

/// <summary>
/// What the Database Properties window shows about ONE connected database.
///
/// <para>⭐ <b>Every field here was verified to exist on BOTH ends of EmberTern's declared support range</b>
/// (Firebird 3.0.13 and 5.0.3, <c>DatabasePropertiesProbe</c> §H), which is why the reader needs no version
/// gate at all. ⛔ Do not add a field because the catalog offers one: <c>MON$GUID</c>, <c>MON$FILE_ID</c>,
/// <c>MON$SEC_DATABASE</c>, <c>MON$CRYPT_STATE</c>, <c>MON$BACKUP_STATE</c>, <c>MON$REPLICA_MODE</c>,
/// <c>MON$NEXT_*</c> and <c>RDB$SQL_SECURITY</c> were measured as available and are deliberately OUT of V1
/// (ratified scope) — several of them are also FB4/FB5-only, so adding one silently drops FB3 support.</para>
/// </summary>
public sealed record DatabaseProperties
{
    /// <summary>
    /// The database as the USER knows it — the path from the connection profile.
    /// <para>⚠ Deliberately NOT <c>MON$DATABASE_NAME</c> (ratified): the engine returns its own normalised,
    /// upper-cased form (<c>C:\TEMP\…</c>), which on a remote server is a path on the SERVER's filesystem.
    /// That is the engine's technical path, not a substitute for what the user configured.</para>
    /// </summary>
    public required string DatabasePath { get; init; }

    /// <summary>From <c>MON$OWNER</c>. ⚠ Trimmed by the reader — the column is a padded CHAR.</summary>
    public required string Owner { get; init; }

    /// <summary>
    /// From <c>RDB$GET_CONTEXT('SYSTEM','ENGINE_VERSION')</c> — e.g. <c>5.0.3</c>.
    /// <para>⚠⚠ Deliberately NOT <c>FbConnection.ServerVersion</c>, and this reverses an earlier
    /// "reuse before create" recommendation. Measured: the driver property returns the full banner
    /// <c>WI-V5.0.3.1683 Firebird 5.0/tcp (HOSTNAME)/P16:C</c> — which leaks the SERVER'S MACHINE NAME into a
    /// field labelled "engine version". The two are not interchangeable.</para>
    /// </summary>
    public required string EngineVersion { get; init; }

    /// <summary>On-disk structure version, <c>MON$ODS_MAJOR</c>.<c>MON$ODS_MINOR</c> (12.0 on FB3, 13.1 on FB5).</summary>
    public required int OdsMajor { get; init; }

    /// <inheritdoc cref="OdsMajor"/>
    public required int OdsMinor { get; init; }

    /// <summary>
    /// From <c>MON$SQL_DIALECT</c>. ⛔ <b>Read-only by RATIFIED PRODUCT DECISION, not by technical limit.</b>
    /// The probe measured <c>SetSqlDialectAsync</c> succeeding ONLINE (3 → 1 → 3 with an attachment open), so
    /// anyone reading only the driver would conclude this is an oversight. It is not: changing the dialect
    /// changes the SQL EmberTern itself runs against the database.
    /// </summary>
    public required int Dialect { get; init; }

    /// <summary>From <c>RDB$CHARACTER_SET_NAME</c>. ⚠ Trimmed — padded CHAR, like <see cref="Owner"/>.</summary>
    public required string Charset { get; init; }

    /// <summary>From <c>MON$CREATION_DATE</c>.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>From <c>MON$PAGE_SIZE</c>.</summary>
    public required int PageSize { get; init; }

    /// <summary>From <c>MON$PAGES</c>.</summary>
    public required long Pages { get; init; }

    /// <summary>
    /// From <c>MON$PAGE_BUFFERS</c> — <b>informational in V1</b>.
    ///
    /// <para>⚠⚠ <b>This is the RUNNING cache of the open database instance, not the stored header value</b>
    /// (measured: write 1024 → a fresh attachment still reads 51200 while the database stays in use → 1024
    /// only after the database is fully released). So read and write do not refer to the same thing, and
    /// <c>MON$</c> cannot tell "inherited from the server default" from "explicitly pinned to that number".</para>
    ///
    /// <para>⛔ <b>That is exactly why it is not editable in V1</b> (ratified): a field seeded from this value
    /// would show the inherited server default, and Apply — with nothing edited — would pin it to this
    /// database permanently.</para>
    /// </summary>
    public required int PageBuffers { get; init; }

    /// <summary>
    /// From <c>RDB$LINGER</c>. ⚠ <b>null means "not set", which is NOT the same as 0</b> — measured NULL on a
    /// freshly created database on both FB3 and FB5. Collapsing the two would state a configured value the
    /// database does not have.
    /// </summary>
    public required int? LingerSeconds { get; init; }

    // ⛔ NO Read-only member, and that is a RATIFIED REMOVAL rather than an omission. Measured:
    //    SetAccessModeAsync needs exclusive access (SQLSTATE 40001 with one attachment open, success with
    //    none), and EmberTern holds 2–3 attachments per profile while this window is only reachable WHILE
    //    CONNECTED. So the control could never be used, and a value nobody can act on is not worth a row —
    //    the user's call: "pokazywanie kontrolki, której użytkownik nigdy nie może użyć, nie ma wartości".
    //    ⛔ Do not re-add it "just as information", and do not build a disconnect-and-retry workflow for it.

    // ── Editable in V1 — measured writable ONLINE, with no exclusivity, visible immediately ──────────────

    /// <summary>From <c>MON$SWEEP_INTERVAL</c>. Editable.</summary>
    public required int SweepInterval { get; init; }

    /// <summary>From <c>MON$FORCED_WRITES</c>. Editable.</summary>
    public required bool ForcedWrites { get; init; }

    /// <summary>From <c>MON$RESERVE_SPACE</c>. Editable.</summary>
    public required bool ReserveSpace { get; init; }

    /// <summary>Database size in bytes — <see cref="Pages"/> × <see cref="PageSize"/>.</summary>
    public long SizeBytes => Pages * PageSize;
}

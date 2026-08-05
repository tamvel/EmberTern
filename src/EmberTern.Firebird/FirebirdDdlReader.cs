using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

public sealed class FirebirdDdlReader
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly MetadataLane _lane;

    public FirebirdDdlReader(FirebirdConnectionService connectionService)
        : this(connectionService, new MetadataLane(connectionService))
    {
    }

    public FirebirdDdlReader(FirebirdConnectionService connectionService, MetadataLane lane)
    {
        _connectionService = connectionService;
        _lane = lane;
    }

    // Connection + lock for this reader's lane (metadata in production), so DDL browsing
    // runs on the metadata attachment. Falls back to the data connection when no
    // transaction service is injected (tests).
    private FbConnection LaneConnection()
        => _lane.RequireOpenConnection();
    private SemaphoreSlim LaneLock()
        => _lane.CommandLock;

    public async Task<string> FetchDdlAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var serverMajor = ParseServerMajor(connection.ServerVersion);
        // Source BLOBs in this database may be a mix of UTF-8 (modern tools) and the connection
        // charset (older IBExpert writes). DecodeSourceBlob tries strict UTF-8 first and falls
        // back to this encoding when the bytes aren't valid UTF-8.
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);

        // Readers never open their own transaction. Attach to the user's working
        // tx when one is active; otherwise the managed driver runs each command
        // in an implicit read tx, auto-committed per statement. The connection's
        // CommandLock is held across the entire DDL build — many of these kinds
        // issue multiple commands (table builder reads RDB$RELATION_FIELDS,
        // RDB$RELATION_CONSTRAINTS, RDB$INDICES separately), and FbConnection
        // is single-threaded.
        var tx = _lane.TransactionForCommand;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return obj.Kind switch
            {
                MetadataObjectKind.Table => await BuildTableDdlAsync(connection, tx, obj.Name, cancellationToken).ConfigureAwait(false),
                MetadataObjectKind.View => await BuildViewDdlAsync(connection, tx, obj.Name, fallback, orAlter: false, cancellationToken).ConfigureAwait(false),
                MetadataObjectKind.Procedure => await BuildProcedureDdlAsync(connection, tx, obj.Name, serverMajor, fallback, cancellationToken).ConfigureAwait(false),
                MetadataObjectKind.Trigger => await BuildTriggerDdlAsync(connection, tx, obj.Name, fallback, cancellationToken).ConfigureAwait(false),
                MetadataObjectKind.Function => await BuildFunctionDdlAsync(connection, tx, obj.Name, serverMajor, fallback, cancellationToken).ConfigureAwait(false),
                MetadataObjectKind.Generator => await BuildGeneratorDdlAsync(connection, tx, obj.Name, cancellationToken).ConfigureAwait(false),
                // SystemTable reuses the table builder — the underlying SQL is identical against RDB$RELATION_FIELDS.
                MetadataObjectKind.SystemTable => await BuildTableDdlAsync(connection, tx, obj.Name, cancellationToken).ConfigureAwait(false),
                MetadataObjectKind.Exception => await BuildExceptionDdlAsync(connection, tx, obj.Name, fallback, cancellationToken).ConfigureAwait(false),
                MetadataObjectKind.Role => BuildRoleDdl(obj.Name),
                // DDL reconstruction for these is non-trivial / partially unavailable from the catalog;
                // V1.x ships a placeholder rather than throwing so the user can still browse.
                MetadataObjectKind.Domain => BuildPlaceholderDdl("DOMAIN", obj.Name),
                MetadataObjectKind.Package => await BuildPackageDdlAsync(connection, tx, obj.Name, fallback, cancellationToken).ConfigureAwait(false),
                MetadataObjectKind.User => BuildPlaceholderDdl("USER", obj.Name),
                MetadataObjectKind.Index => BuildPlaceholderDdl("INDEX", obj.Name),
                _ => throw new ArgumentOutOfRangeException(nameof(obj), obj.Kind, null),
            };
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read DDL for {obj.Kind} {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // -- Tables ---------------------------------------------------------------

    private static async Task<string> BuildTableDdlAsync(FbConnection connection, FbTransaction? tx, string name, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE ").Append(Quote(name)).AppendLine(" (");

        bool first = true;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = SqlForTableColumns;
            cmd.Parameters.AddWithValue("@name", name);

            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var col = TrimOrEmpty(reader.GetString(0));
                short? fieldType = SafeShort(reader, 1);
                short? fieldSubType = SafeShort(reader, 2);
                short? fieldLength = SafeShort(reader, 3);
                short? precision = SafeShort(reader, 4);
                short? scale = SafeShort(reader, 5);
                short? charLength = SafeShort(reader, 6);
                short? colNullFlag = SafeShort(reader, 7);
                short? domainNullFlag = SafeShort(reader, 8);
                var columnDefault = SafeString(reader, 9);
                var domainDefault = SafeString(reader, 10);
                var computedSource = SafeString(reader, 11);
                var charsetName = TrimOrEmpty(SafeString(reader, 12));
                var collationName = TrimOrEmpty(SafeString(reader, 13));
                var domainName = TrimOrEmpty(SafeString(reader, 14));

                if (!first) sb.AppendLine(",");
                first = false;

                sb.Append("  ").Append(Quote(col)).Append(' ');

                if (!string.IsNullOrWhiteSpace(computedSource))
                {
                    sb.Append("COMPUTED BY ").Append(computedSource.Trim());
                }
                else
                {
                    // Use the domain name when it's user-defined (not the implicit RDB$xxx domain).
                    if (IsUserDomain(domainName))
                    {
                        sb.Append(Quote(domainName));
                    }
                    else
                    {
                        sb.Append(FormatType(fieldType, fieldSubType, fieldLength, precision, scale, charLength));
                        if (charsetName.Length > 0 && IsCharType(fieldType))
                        {
                            sb.Append(" CHARACTER SET ").Append(charsetName);
                        }
                    }

                    var effectiveDefault = !string.IsNullOrWhiteSpace(columnDefault) ? columnDefault : domainDefault;
                    if (!string.IsNullOrWhiteSpace(effectiveDefault))
                    {
                        sb.Append(' ').Append(effectiveDefault.Trim());
                    }

                    short? effectiveNull = colNullFlag ?? domainNullFlag;
                    if (effectiveNull == 1)
                    {
                        sb.Append(" NOT NULL");
                    }

                    if (collationName.Length > 0 && IsCharType(fieldType))
                    {
                        sb.Append(" COLLATE ").Append(collationName);
                    }
                }
            }
        }

        sb.AppendLine().AppendLine(");");

        // Primary key (separate ALTER for clarity; matches what isql -x produces).
        var pk = await ReadConstraintAsync(connection, tx, name, "PRIMARY KEY", ct).ConfigureAwait(false);
        if (pk is not null)
        {
            sb.AppendLine();
            sb.Append("ALTER TABLE ").Append(Quote(name))
              .Append(" ADD CONSTRAINT ").Append(Quote(pk.ConstraintName))
              .Append(" PRIMARY KEY (").Append(JoinQuoted(pk.Columns)).AppendLine(");");
        }

        // Unique constraints.
        await foreach (var uq in ReadConstraintsAsync(connection, tx, name, "UNIQUE", ct).ConfigureAwait(false))
        {
            sb.AppendLine();
            sb.Append("ALTER TABLE ").Append(Quote(name))
              .Append(" ADD CONSTRAINT ").Append(Quote(uq.ConstraintName))
              .Append(" UNIQUE (").Append(JoinQuoted(uq.Columns)).AppendLine(");");
        }

        // Foreign keys.
        await foreach (var fk in ReadForeignKeysAsync(connection, tx, name, ct).ConfigureAwait(false))
        {
            sb.AppendLine();
            sb.Append("ALTER TABLE ").Append(Quote(name))
              .Append(" ADD CONSTRAINT ").Append(Quote(fk.ConstraintName))
              .Append(" FOREIGN KEY (").Append(JoinQuoted(fk.Columns)).Append(") REFERENCES ")
              .Append(Quote(fk.ReferencedTable)).Append(" (").Append(JoinQuoted(fk.ReferencedColumns)).Append(')');
            if (!string.IsNullOrEmpty(fk.UpdateRule) && fk.UpdateRule != "RESTRICT")
            {
                sb.Append(" ON UPDATE ").Append(fk.UpdateRule);
            }
            if (!string.IsNullOrEmpty(fk.DeleteRule) && fk.DeleteRule != "RESTRICT")
            {
                sb.Append(" ON DELETE ").Append(fk.DeleteRule);
            }
            sb.AppendLine(";");
        }

        // Standalone indexes (i.e. not backing a PK/UQ/FK constraint).
        await foreach (var idx in ReadIndexesAsync(connection, tx, name, ct).ConfigureAwait(false))
        {
            sb.AppendLine();
            sb.Append("CREATE ");
            if (idx.IsUnique) sb.Append("UNIQUE ");
            if (idx.IsDescending) sb.Append("DESCENDING ");
            sb.Append("INDEX ").Append(Quote(idx.IndexName))
              .Append(" ON ").Append(Quote(name))
              .Append(" (").Append(JoinQuoted(idx.Columns)).AppendLine(");");
        }

        return sb.ToString();
    }

    // -- Views ----------------------------------------------------------------

    /// <summary>
    /// Fetches a view's source rebuilt as an editable <c>CREATE OR ALTER VIEW</c>
    /// statement — the working surface for the View Detail SQL tab. Reuses the
    /// same blob + column-list logic as the read-only <see cref="FetchDdlAsync"/>
    /// DDL path; the only difference is the <c>OR ALTER</c> verb so re-Compiling
    /// alters the view in place. Same lane/lock + tx-attach pattern as every
    /// other read here.
    /// </summary>
    public async Task<string> FetchViewSourceAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await BuildViewDdlAsync(connection, tx, obj.Name, fallback, orAlter: true, cancellationToken).ConfigureAwait(false);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read source for VIEW {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    private static async Task<string> BuildViewDdlAsync(FbConnection connection, FbTransaction? tx, string name, Encoding fallback, bool orAlter, CancellationToken ct)
    {
        var source = await ReadBlobAsync(connection, tx,
            "SELECT RDB$VIEW_SOURCE FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @name",
            name, fallback, ct).ConfigureAwait(false);

        var columns = new StringBuilder();
        bool firstCol = true;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT TRIM(RDB$FIELD_NAME) FROM RDB$RELATION_FIELDS " +
                "WHERE RDB$RELATION_NAME = @name ORDER BY RDB$FIELD_POSITION";
            cmd.Parameters.AddWithValue("@name", name);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (reader.IsDBNull(0)) continue;
                if (!firstCol) columns.Append(", ");
                firstCol = false;
                columns.Append(Quote(reader.GetString(0).Trim()));
            }
        }

        var sb = new StringBuilder();
        sb.Append(orAlter ? "CREATE OR ALTER VIEW " : "CREATE VIEW ").Append(Quote(name));
        if (columns.Length > 0)
        {
            sb.Append(" (").Append(columns).Append(')');
        }
        sb.AppendLine(" AS");
        sb.AppendLine(string.IsNullOrWhiteSpace(source) ? "/* view source unavailable */" : source.Trim());
        sb.AppendLine(";");
        return sb.ToString();
    }

    // -- Procedures -----------------------------------------------------------

    /// <summary>
    /// Fetches a procedure's source rebuilt as an editable
    /// <c>CREATE OR ALTER PROCEDURE</c> statement — the working surface for the
    /// Procedure Detail Editor tab. Reuses the same reconstruction as the
    /// read-only <see cref="FetchDdlAsync"/> DDL path (a procedure always rebuilds
    /// as CREATE OR ALTER, so source and DDL are identical in V1). Same lane/lock +
    /// tx-attach pattern as every other read here.
    /// </summary>
    public async Task<string> FetchProcedureSourceAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var serverMajor = ParseServerMajor(connection.ServerVersion);
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await BuildProcedureDdlAsync(connection, tx, obj.Name, serverMajor, fallback, cancellationToken).ConfigureAwait(false);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read source for PROCEDURE {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>
    /// Reconstructs a standalone procedure's full <c>CREATE OR ALTER PROCEDURE</c> source on a
    /// <b>caller-supplied</b> connection + transaction (Stage X / D8: the debugger reads a stepped-into
    /// callee's source on its own debug session, holding the session's command lock across the multi-command
    /// build). Reuses the exact reconstruction of <see cref="FetchProcedureSourceAsync"/> — this is only a
    /// seam so the debugger can drive it on the debug attachment/tx instead of the metadata lane, avoiding a
    /// second DDL reconstruction. The caller serializes wire access (this issues several reads and does not
    /// lock).
    /// </summary>
    internal static Task<string> BuildProcedureSourceAsync(
        FbConnection connection, FbTransaction? tx, string name, int serverMajor, Encoding fallback, CancellationToken ct)
        => BuildProcedureDdlAsync(connection, tx, name, serverMajor, fallback, ct);

    /// <summary>Reads a package's raw body source (<c>RDB$PACKAGE_BODY_SOURCE</c> — a <c>BEGIN … END</c> blob of
    /// member routine declarations) on a <b>caller-supplied</b> connection + transaction (Stage X / D11: the
    /// debugger reads a stepped-into package member's source on its own debug session). Reuses the shared blob
    /// reader; null when the package has no body. The caller serializes wire access.</summary>
    internal static Task<string?> ReadPackageBodySourceAsync(
        FbConnection connection, FbTransaction? tx, string packageName, Encoding fallback, CancellationToken ct)
        => ReadBlobAsync(connection, tx,
            "SELECT RDB$PACKAGE_BODY_SOURCE FROM RDB$PACKAGES WHERE RDB$PACKAGE_NAME = @name",
            packageName.ToUpperInvariant(), fallback, ct);

    /// <summary>
    /// Fetches a procedure's BODY alone — <c>RDB$PROCEDURE_SOURCE</c> is exactly
    /// the text after <c>AS</c> (the DECLARE…BEGIN…END), with no header. This is
    /// what Procedure Detail Easy mode edits, alongside catalog-derived params, so
    /// no PSQL parsing is needed to LOAD the procedure into structured form.
    /// </summary>
    public async Task<string> FetchProcedureBodyAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var serverMajor = ParseServerMajor(connection.ServerVersion);
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var body = await ReadBlobAsync(connection, tx,
                "SELECT RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @name" +
                StandalonePackageFilter(serverMajor),
                obj.Name, fallback, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read body for PROCEDURE {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // -- Packages -------------------------------------------------------------
    //
    // A package has TWO source artifacts. RDB$PACKAGES.RDB$PACKAGE_HEADER_SOURCE holds
    // the header (declarations) and RDB$PACKAGE_BODY_SOURCE the body (implementation),
    // each the text after AS (like RDB$PROCEDURE_SOURCE — gotcha #114). StripLeadingAs
    // (gotcha #139) makes reconstruction robust regardless of whether the stored BLOB
    // includes a leading AS. The Package Detail editor fetches each separately and
    // compiles them as one logical object (header first, then body).

    /// <summary>Fetches a package's HEADER rebuilt as an editable
    /// <c>CREATE OR ALTER PACKAGE name AS …</c> statement — the Package tab.</summary>
    public async Task<string> FetchPackageHeaderSourceAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var header = await ReadBlobAsync(connection, tx,
                "SELECT RDB$PACKAGE_HEADER_SOURCE FROM RDB$PACKAGES WHERE RDB$PACKAGE_NAME = @name",
                obj.Name, fallback, cancellationToken).ConfigureAwait(false);
            return DdlGenerator.BuildCreateOrAlterPackageHeader(obj.Name, StripLeadingAs(header).Trim());
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read header for PACKAGE {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Fetches a package's BODY rebuilt as an editable
    /// <c>RECREATE PACKAGE BODY name AS …</c> statement — the Body tab. Returns an
    /// empty string when the package has no body yet (the editor stays empty; Compile
    /// skips the body step).</summary>
    public async Task<string> FetchPackageBodySourceAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var body = await ReadBlobAsync(connection, tx,
                "SELECT RDB$PACKAGE_BODY_SOURCE FROM RDB$PACKAGES WHERE RDB$PACKAGE_NAME = @name",
                obj.Name, fallback, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body)
                ? string.Empty
                : DdlGenerator.BuildRecreatePackageBody(obj.Name, StripLeadingAs(body).Trim());
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read body for PACKAGE {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>Reconstructs a package PROCEDURE member's standalone <c>CREATE PROCEDURE …</c> source (Stage X /
    /// D11 seam C) so the debugger can launch it as a ROOT frame with the SAME machinery a stored routine uses:
    /// reads the raw <c>RDB$PACKAGE_BODY_SOURCE</c> blob and slices out the member via the one shared reconstructor
    /// (<see cref="EmberTern.Core.Sql.Language.SqlParser.ReconstructPackageMemberSource(string?, string, EmberTern.Core.Sql.Language.Ast.SubroutineKind)"/>).
    /// Returns null when the package has no readable body or has no such member of that kind. Both PROCEDURE and
    /// FUNCTION members are launchable (Seam D added the function root); <paramref name="kind"/> selects which,
    /// defaulting to PROCEDURE for the D11 callers. The reconstruction itself is kind-generic — a FUNCTION member
    /// slice includes its <c>RETURNS</c>, so it reconstructs as a valid standalone <c>CREATE FUNCTION</c>.</summary>
    public async Task<string?> FetchPackageMemberSourceAsync(
        string packageName, string memberName,
        EmberTern.Core.Sql.Language.Ast.SubroutineKind kind = EmberTern.Core.Sql.Language.Ast.SubroutineKind.Procedure,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        var connection = LaneConnection();
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var body = await ReadPackageBodySourceAsync(connection, tx, packageName, fallback, cancellationToken)
                .ConfigureAwait(false);
            return EmberTern.Core.Sql.Language.SqlParser.ReconstructPackageMemberSource(body, memberName, kind);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException(
                $"Could not read body for PACKAGE {packageName}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    private static async Task<string> BuildPackageDdlAsync(FbConnection connection, FbTransaction? tx, string name, Encoding fallback, CancellationToken ct)
    {
        var header = await ReadBlobAsync(connection, tx,
            "SELECT RDB$PACKAGE_HEADER_SOURCE FROM RDB$PACKAGES WHERE RDB$PACKAGE_NAME = @name",
            name, fallback, ct).ConfigureAwait(false);
        var body = await ReadBlobAsync(connection, tx,
            "SELECT RDB$PACKAGE_BODY_SOURCE FROM RDB$PACKAGES WHERE RDB$PACKAGE_NAME = @name",
            name, fallback, ct).ConfigureAwait(false);

        var headerText = StripLeadingAs(header).Trim();
        var bodyText = string.IsNullOrWhiteSpace(body) ? null : StripLeadingAs(body).Trim();
        return DdlGenerator.BuildPackageDdl(name, headerText, bodyText);
    }

    private static async Task<string> BuildProcedureDdlAsync(FbConnection connection, FbTransaction? tx, string name, int serverMajor, Encoding fallback, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER PROCEDURE ").Append(Quote(name));

        var inputs = await ReadProcedureParamsAsync(connection, tx, name, paramType: 0, serverMajor, ct).ConfigureAwait(false);
        var outputs = await ReadProcedureParamsAsync(connection, tx, name, paramType: 1, serverMajor, ct).ConfigureAwait(false);

        if (inputs.Count > 0)
        {
            sb.AppendLine().Append('(').Append(string.Join(",\n ", inputs)).Append(')');
        }
        sb.AppendLine();

        if (outputs.Count > 0)
        {
            sb.Append("RETURNS (").Append(string.Join(",\n         ", outputs)).AppendLine(")");
        }

        sb.AppendLine("AS");

        var source = await ReadBlobAsync(connection, tx,
            "SELECT RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @name" +
            StandalonePackageFilter(serverMajor),
            name, fallback, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(source))
        {
            sb.AppendLine(serverMajor <= 2
                ? "/* Procedure source unavailable on Firebird 2.5 (only compiled BLR is stored). */"
                : "/* Procedure source not available. */");
            sb.AppendLine("BEGIN");
            sb.AppendLine("  /* body */");
            sb.AppendLine("END");
        }
        else
        {
            sb.Append(source.Trim());
            if (!source.TrimEnd().EndsWith("END", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    // -- Triggers -------------------------------------------------------------

    /// <summary>
    /// Fetches a trigger's source rebuilt as an editable <c>CREATE OR ALTER TRIGGER</c>
    /// statement — the working surface for the Trigger Detail Editor (Source mode).
    /// Reuses the same reconstruction as the read-only <see cref="FetchDdlAsync"/> DDL
    /// path (a trigger always rebuilds as CREATE OR ALTER, so source and DDL match).
    /// Same lane/lock + tx-attach pattern as every other read here.
    /// </summary>
    public async Task<string> FetchTriggerSourceAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await BuildTriggerDdlAsync(connection, tx, obj.Name, fallback, cancellationToken).ConfigureAwait(false);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read source for TRIGGER {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>
    /// Fetches a trigger's BODY alone — the DECLARE…BEGIN…END text after <c>AS</c>,
    /// with any leading <c>AS</c> keyword stripped (some databases store
    /// <c>RDB$TRIGGER_SOURCE</c> beginning with <c>AS</c>, others don't). This is what
    /// Trigger Detail Easy mode edits, alongside the catalog-derived header — so the
    /// body splits into the structured Variables model with no header parsing.
    /// </summary>
    public async Task<string> FetchTriggerBodyAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var source = await ReadBlobAsync(connection, tx,
                "SELECT RDB$TRIGGER_SOURCE FROM RDB$TRIGGERS WHERE RDB$TRIGGER_NAME = @name",
                obj.Name, fallback, cancellationToken).ConfigureAwait(false);
            return StripLeadingAs(source).Trim();
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read body for TRIGGER {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    // Some Firebird databases store RDB$TRIGGER_SOURCE beginning with the AS keyword
    // (the body after the header), others store just the DECLARE…BEGIN…END. Strip a
    // single leading AS word so BuildTriggerDdlAsync (which re-adds AS) doesn't emit a
    // double AS, and so the Easy-mode body splitter sees only DECLARE…BEGIN…END.
    internal static string StripLeadingAs(string? source)
    {
        var s = (source ?? string.Empty).TrimStart();
        if (s.Length >= 2 && (s[0] is 'A' or 'a') && (s[1] is 'S' or 's')
            && (s.Length == 2 || char.IsWhiteSpace(s[2])))
        {
            return s.Substring(2);
        }
        return s;
    }

    private static async Task<string> BuildTriggerDdlAsync(FbConnection connection, FbTransaction? tx, string name, Encoding fallback, CancellationToken ct)
    {
        string? relation = null;
        long? triggerType = null;   // RDB$TRIGGER_TYPE is BIGINT — DB-level/DDL triggers carry values > short.
        short? sequence = null;
        short? inactive = null;

        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT TRIM(RDB$RELATION_NAME), RDB$TRIGGER_TYPE, RDB$TRIGGER_SEQUENCE, RDB$TRIGGER_INACTIVE " +
                "FROM RDB$TRIGGERS WHERE RDB$TRIGGER_NAME = @name";
            cmd.Parameters.AddWithValue("@name", name);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                relation = reader.IsDBNull(0) ? null : reader.GetString(0);
                triggerType = reader.IsDBNull(1) ? null : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
                sequence = SafeShort(reader, 2);
                inactive = SafeShort(reader, 3);
            }
        }

        var source = await ReadBlobAsync(connection, tx,
            "SELECT RDB$TRIGGER_SOURCE FROM RDB$TRIGGERS WHERE RDB$TRIGGER_NAME = @name",
            name, fallback, ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER TRIGGER ").Append(Quote(name));

        var dbEvent = DescribeDatabaseTriggerEvent(triggerType);
        if (dbEvent is not null)
        {
            // Database-level trigger (ON CONNECT / ON DISCONNECT / ON TRANSACTION …):
            // no FOR clause, no BEFORE/AFTER — "[INACTIVE] ON <event> [POSITION n]".
            sb.AppendLine();
            if (inactive == 1) sb.Append("INACTIVE ");
            sb.Append(dbEvent);
            if (sequence is > 0) sb.Append(" POSITION ").Append(sequence.Value);
        }
        else
        {
            // Relation (table/view) trigger. Firebird's grammar is: FOR <table> FIRST,
            // then "[ACTIVE|INACTIVE] {BEFORE|AFTER} <events> [POSITION n]". Emitting
            // INACTIVE before FOR (the old order) produced "… INACTIVE FOR STANMAG …"
            // → -104 "Token unknown … FOR".
            if (!string.IsNullOrEmpty(relation)) sb.Append(" FOR ").Append(Quote(relation));
            sb.AppendLine();
            if (inactive == 1) sb.Append("INACTIVE ");
            // Trigger type encodes timing + event(s); shorthand for the common cases,
            // numeric-comment fallback for a non-standard encoding.
            var timing = triggerType is >= 0 and < 8192 ? DescribeTriggerType((short)triggerType.Value) : null;
            if (!string.IsNullOrEmpty(timing)) sb.Append(timing);
            else if (triggerType.HasValue) sb.Append("/* trigger type ").Append(triggerType.Value).Append(" */");
            if (sequence is > 0) sb.Append(" POSITION ").Append(sequence.Value);
        }

        sb.AppendLine();
        sb.AppendLine("AS");
        var body = StripLeadingAs(source).Trim();
        sb.AppendLine(string.IsNullOrWhiteSpace(body) ? "BEGIN\n  /* trigger body unavailable */\nEND" : body);
        return sb.ToString();
    }

    // Maps a database-level trigger's RDB$TRIGGER_TYPE (8192-8196) to its ON-event clause.
    // Returns null for relation triggers (< 8192) and DDL triggers (other encodings) —
    // relation triggers are described by DescribeTriggerType instead.
    internal static string? DescribeDatabaseTriggerEvent(long? triggerType) => triggerType switch
    {
        8192 => "ON CONNECT",
        8193 => "ON DISCONNECT",
        8194 => "ON TRANSACTION START",
        8195 => "ON TRANSACTION COMMIT",
        8196 => "ON TRANSACTION ROLLBACK",
        _ => null,
    };

    // -- Functions ------------------------------------------------------------

    /// <summary>
    /// Fetches a PSQL function's source rebuilt as an editable
    /// <c>CREATE OR ALTER FUNCTION</c> statement — the working surface for the Function
    /// Detail Editor (Source mode). Reuses the same reconstruction as the read-only
    /// <see cref="FetchDdlAsync"/> DDL path. Same lane/lock + tx-attach pattern as the
    /// procedure source fetcher.
    /// </summary>
    public async Task<string> FetchFunctionSourceAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var serverMajor = ParseServerMajor(connection.ServerVersion);
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await BuildFunctionDdlAsync(connection, tx, obj.Name, serverMajor, fallback, cancellationToken).ConfigureAwait(false);
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read source for FUNCTION {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    /// <summary>
    /// Fetches a function's BODY alone — <c>RDB$FUNCTION_SOURCE</c> is exactly the text
    /// after <c>AS</c> (DECLARE…BEGIN…END), with no header. This is what Function Detail
    /// Easy mode edits, alongside catalog-derived arguments + return type.
    /// </summary>
    public async Task<string> FetchFunctionBodyAsync(MetadataObject obj, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obj);

        var connection = LaneConnection();
        var fallback = CharsetCatalog.Resolve(_connectionService.ActiveProfile?.Charset);
        var tx = _lane.TransactionForCommand;
        var serverMajor = ParseServerMajor(connection.ServerVersion);
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var body = await ReadBlobAsync(connection, tx,
                "SELECT RDB$FUNCTION_SOURCE FROM RDB$FUNCTIONS WHERE RDB$FUNCTION_NAME = @name" +
                StandalonePackageFilter(serverMajor),
                obj.Name, fallback, cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read body for FUNCTION {obj.Name}: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }

    private static async Task<string> BuildFunctionDdlAsync(FbConnection connection, FbTransaction? tx, string name, int serverMajor, Encoding fallback, CancellationToken ct)
    {
        if (serverMajor <= 2)
        {
            return $"/* Function {name} is a UDF declaration (Firebird 2.5). Source DDL not stored in the catalog. */\n";
        }

        // The return value is the RDB$FUNCTION_ARGUMENTS row at RDB$RETURN_ARGUMENT;
        // the input arguments are every other row. RDB$DETERMINISTIC_FLAG gates the
        // DETERMINISTIC keyword.
        int returnArgPos = 0;
        bool deterministic = false;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT RDB$RETURN_ARGUMENT, RDB$DETERMINISTIC_FLAG FROM RDB$FUNCTIONS WHERE RDB$FUNCTION_NAME = @name" +
                StandalonePackageFilter(serverMajor);
            cmd.CommandTimeout = 0;
            cmd.Parameters.AddWithValue("@name", name);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                returnArgPos = r.IsDBNull(0) ? 0 : Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture);
                deterministic = !r.IsDBNull(1) && Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture) != 0;
            }
        }

        var (inputs, returnType) = await ReadFunctionArgsAsync(connection, tx, name, returnArgPos, serverMajor, ct).ConfigureAwait(false);

        var source = await ReadBlobAsync(connection, tx,
            "SELECT RDB$FUNCTION_SOURCE FROM RDB$FUNCTIONS WHERE RDB$FUNCTION_NAME = @name" +
            StandalonePackageFilter(serverMajor),
            name, fallback, ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER FUNCTION ").Append(Quote(name));
        if (inputs.Count > 0)
        {
            sb.AppendLine().Append('(').Append(string.Join(",\n ", inputs)).Append(')');
        }
        sb.AppendLine();
        sb.Append("RETURNS ").Append(string.IsNullOrWhiteSpace(returnType) ? "/* unknown */" : returnType);
        if (deterministic) sb.Append(" DETERMINISTIC");
        sb.AppendLine();
        sb.AppendLine("AS");
        if (string.IsNullOrWhiteSpace(source))
        {
            sb.AppendLine("BEGIN");
            sb.AppendLine("  /* function body unavailable */");
            sb.AppendLine("END");
        }
        else
        {
            sb.Append(source.Trim()).AppendLine();
        }
        return sb.ToString();
    }

    // Reads a function's arguments, splitting off the return value (the row at
    // returnArgPos). Each input is formatted "NAME TYPE [NOT NULL]"; the return is the
    // formatted type alone. Mirrors ReadProcedureParamsAsync — the type resolves to the
    // underlying field type (a domain argument shows its resolved type, same as procs).
    private static async Task<(System.Collections.Generic.List<string> Inputs, string ReturnType)> ReadFunctionArgsAsync(
        FbConnection connection, FbTransaction? tx, string funcName, int returnArgPos, int serverMajor, CancellationToken ct)
    {
        var inputs = new System.Collections.Generic.List<string>();
        var returnType = string.Empty;
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = InsertBeforeOrderBy(SqlForFunctionArgs, StandalonePackageFilter(serverMajor, "fa."));
        cmd.Parameters.AddWithValue("@name", funcName);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var pos = reader.IsDBNull(0) ? -1 : Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            var argName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
            var baseType = FormatType(
                SafeShort(reader, 2), SafeShort(reader, 3), SafeShort(reader, 4),
                SafeShort(reader, 5), SafeShort(reader, 6), SafeShort(reader, 7));
            var ownNull = SafeShort(reader, 8);
            var fieldSource = TrimOrEmpty(SafeString(reader, 9));
            var domainNull = SafeShort(reader, 10);
            var type = TypeTextForField(fieldSource, baseType);
            if (pos == returnArgPos)
            {
                // ⚠ No NOT NULL on the RETURNS position: Firebird's function result carries the
                // nullability of its own type (and a domain carries it itself), and `RETURNS X NOT NULL`
                // is not the shape the header uses. Unchanged from before the domain fix.
                returnType = type;
            }
            else
            {
                var sb = new StringBuilder();
                sb.Append(Quote(argName)).Append(' ').Append(type);
                if (EmitsNotNull(fieldSource, ownNull, domainNull)) sb.Append(" NOT NULL");
                inputs.Add(sb.ToString());
            }
        }
        return (inputs, returnType);
    }

    // -- Generators / Sequences ----------------------------------------------

    private static async Task<string> BuildGeneratorDdlAsync(FbConnection connection, FbTransaction? tx, string name, CancellationToken ct)
    {
        long? currentValue = null;

        // GEN_ID(name, 0) returns the current value without bumping it.
        // Some FB versions/permissions block GEN_ID on system-owned sequences; the catch
        // just degrades to a CREATE-only snippet.
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT GEN_ID({Quote(name)}, 0) FROM RDB$DATABASE";
            cmd.CommandTimeout = 0;
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (result is not null && result != DBNull.Value)
            {
                currentValue = Convert.ToInt64(result, CultureInfo.InvariantCulture);
            }
        }
        catch (FbException)
        {
            // best-effort — DDL still useful without the current value
        }

        var sb = new StringBuilder();
        sb.Append("CREATE SEQUENCE ").Append(Quote(name)).AppendLine(";");
        if (currentValue is { } v)
        {
            sb.Append("/* current value: ").Append(v.ToString(CultureInfo.InvariantCulture)).AppendLine(" */");
        }
        return sb.ToString();
    }

    // -- Exceptions ----------------------------------------------------------

    private static async Task<string> BuildExceptionDdlAsync(FbConnection connection, FbTransaction? tx, string name, Encoding fallback, CancellationToken ct)
    {
        string? message = null;
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT RDB$MESSAGE FROM RDB$EXCEPTIONS WHERE RDB$EXCEPTION_NAME = @name";
            cmd.Parameters.AddWithValue("@name", name);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false) && !reader.IsDBNull(0))
            {
                message = reader.GetString(0);
            }
        }

        var literal = (message ?? string.Empty).Replace("'", "''");
        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER EXCEPTION ").Append(Quote(name))
          .Append(" '").Append(literal).AppendLine("';");
        return sb.ToString();
    }

    // -- Roles ---------------------------------------------------------------

    internal static string BuildRoleDdl(string name)
        => $"CREATE ROLE {Quote(name)};\n";

    // -- Placeholder DDL (V1.x: catalog reconstruction not yet implemented) -------

    internal static string BuildPlaceholderDdl(string keyword, string name)
        => $"/* DDL for {keyword} {Quote(name)} is not reconstructed in this build. */\n";

    // -- Helpers -------------------------------------------------------------

    // RDB$COMPUTED_SOURCE is on RDB$FIELDS (the domain table), NOT on RDB$RELATION_FIELDS.
    // For computed columns Firebird creates an anonymous domain and stores the source there.
    internal const string SqlForTableColumns =
        "SELECT TRIM(rf.RDB$FIELD_NAME), " +
        "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
        "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$CHARACTER_LENGTH, " +
        "       rf.RDB$NULL_FLAG, f.RDB$NULL_FLAG, " +
        "       rf.RDB$DEFAULT_SOURCE, f.RDB$DEFAULT_SOURCE, " +
        "       f.RDB$COMPUTED_SOURCE, " +
        "       TRIM(cs.RDB$CHARACTER_SET_NAME), TRIM(co.RDB$COLLATION_NAME), " +
        "       TRIM(rf.RDB$FIELD_SOURCE) " +
        "FROM RDB$RELATION_FIELDS rf " +
        "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
        "LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID " +
        "LEFT JOIN RDB$COLLATIONS co ON co.RDB$COLLATION_ID = rf.RDB$COLLATION_ID " +
        "                            AND co.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID " +
        "WHERE rf.RDB$RELATION_NAME = @name " +
        "ORDER BY rf.RDB$FIELD_POSITION";

    // Procedure parameters for the DDL reconstruction. Columns: 0=name, 1..6=base-type attributes,
    // 7=the PARAMETER's own null flag, 8=default, 9=the field source (the domain, when a user domain
    // governs the type), 10=the DOMAIN's null flag.
    //
    // ⚠⚠ The two null flags are selected SEPARATELY and are not COALESCEd in SQL any more. They answer
    // two different questions — "did this parameter declare NOT NULL" vs "is its domain NOT NULL" — and
    // which one applies depends on which TYPE is emitted (see EmitsNotNull). Collapsing them in the
    // query, as this used to, makes that decision unrepresentable.
    internal const string SqlForProcedureParams =
        "SELECT TRIM(pp.RDB$PARAMETER_NAME), " +
        "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
        "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$CHARACTER_LENGTH, " +
        "       pp.RDB$NULL_FLAG, pp.RDB$DEFAULT_SOURCE, " +
        "       TRIM(pp.RDB$FIELD_SOURCE), f.RDB$NULL_FLAG " +
        "FROM RDB$PROCEDURE_PARAMETERS pp " +
        "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = pp.RDB$FIELD_SOURCE " +
        "WHERE pp.RDB$PROCEDURE_NAME = @name AND pp.RDB$PARAMETER_TYPE = @pt " +
        "ORDER BY pp.RDB$PARAMETER_NUMBER";

    // Function arguments for the DDL reconstruction. Columns: 0=position, 1=name, 2..7=base-type
    // attributes, 8=the ARGUMENT's own null flag, 9=field source, 10=the DOMAIN's null flag.
    // The row at RDB$RETURN_ARGUMENT is the RETURNS type; every other row is an input argument — and
    // BOTH carry a domain (measured on FB5: RDB$FUNCTION_ARGUMENTS holds 'D_CODE' on the argument and
    // 'D_NAME' on the return position), so the domain had to be restored in both places.
    internal const string SqlForFunctionArgs =
        "SELECT fa.RDB$ARGUMENT_POSITION, TRIM(fa.RDB$ARGUMENT_NAME), " +
        "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
        "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$CHARACTER_LENGTH, " +
        "       fa.RDB$NULL_FLAG, TRIM(fa.RDB$FIELD_SOURCE), f.RDB$NULL_FLAG " +
        "FROM RDB$FUNCTION_ARGUMENTS fa " +
        "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = fa.RDB$FIELD_SOURCE " +
        "WHERE fa.RDB$FUNCTION_NAME = @name " +
        "ORDER BY fa.RDB$ARGUMENT_POSITION";

    /// <summary>
    /// Whether <paramref name="fieldSource"/> (an <c>RDB$FIELD_SOURCE</c> value) names a <b>user</b>
    /// domain rather than one of the anonymous backing domains Firebird creates for an inline type
    /// (<c>RDB$134</c>). The ONE owner of that question — table columns, procedure parameters, function
    /// arguments and the debugger's base-type resolution all read it.
    /// <para>
    /// ⚠ It was three copies of one expression before the 2026-08-05 stabilization sprint (here inline
    /// in the table-column loop, and privately in <c>FirebirdDebugMetadata</c>), and the third copy was
    /// about to be written for parameters — exactly the partial-copy-of-one-fact defect gotcha #302
    /// records. Keep it here, beside <see cref="FormatType"/>: the two are the two halves of the same
    /// question, "what names this field's type".
    /// </para>
    /// </summary>
    internal static bool IsUserDomain(string? fieldSource)
    {
        var s = fieldSource?.Trim();
        return !string.IsNullOrEmpty(s) && !s.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The type text a reconstructed declaration must carry for a field: the <b>domain name</b> when a
    /// user domain governs the type, else the resolved base type. <paramref name="baseType"/> is what
    /// <see cref="FormatType"/> produced for the joined <c>RDB$FIELDS</c> row.
    /// <para>
    /// ⭐⭐ This is a rule #11 (never lose information) decision, not a presentation nicety. A parameter
    /// declared <c>P_CODE D_CODE</c> stores <c>D_CODE</c> in <c>RDB$FIELD_SOURCE</c> (measured on FB5,
    /// 2026-08-05); resolving it to <c>CHAR(8)</c> discards the domain link, and the object editors
    /// reassemble the whole <c>CREATE OR ALTER</c> from what the read returned — so a user who opened a
    /// procedure to edit its body and pressed Compile would silently rewrite every domain-typed
    /// parameter as a base type. That is gotcha #175's shape, one object kind further along.
    /// </para>
    /// </summary>
    internal static string TypeTextForField(string? fieldSource, string baseType)
        => IsUserDomain(fieldSource) ? Quote(fieldSource!.Trim()) : baseType;

    /// <summary>
    /// Whether a reconstructed declaration must spell <c>NOT NULL</c> explicitly.
    /// <para>
    /// ⭐ <b>The nullability source follows the TYPE source, and that is measured, not chosen.</b> On FB5
    /// (2026-08-05): for <c>A D_NAME</c> — a domain that is itself <c>NOT NULL</c> — the parameter's own
    /// <c>RDB$NULL_FLAG</c> is <c>NULL</c> and the domain's is <c>1</c>; for <c>C D_CODE NOT NULL</c> it
    /// is the other way round. So when the emitted type is the <b>domain name</b>, the domain already
    /// carries its own <c>NOT NULL</c> and only the parameter's own flag may add one — otherwise
    /// <c>A D_NAME</c> would come back as <c>A D_NAME NOT NULL</c>, a clause the original declaration
    /// never had. When the emitted type is the <b>base type</b>, the domain's flag MUST be materialised
    /// or the reconstruction would lose the constraint instead.
    /// </para>
    /// </summary>
    internal static bool EmitsNotNull(string? fieldSource, short? ownNullFlag, short? domainNullFlag)
        => IsUserDomain(fieldSource) ? ownNullFlag == 1 : (ownNullFlag ?? domainNullFlag) == 1;

    internal static string FormatType(short? fieldType, short? fieldSubType, short? fieldLength, short? precision, short? scale, short? charLength)
    {
        if (fieldType is null) return "/* unknown type */";

        // NUMERIC / DECIMAL: encoded as SMALLINT / INTEGER / BIGINT with a non-zero sub-type
        // OR with a negative scale (older catalogs). Trust precision/scale from RDB$FIELDS.
        bool isExactNumeric = (fieldType is 7 or 8 or 16) && ((fieldSubType is > 0) || (scale is < 0));
        if (isExactNumeric)
        {
            var prec = precision is > 0 ? precision.Value : DefaultPrecision(fieldType.Value);
            var sc = scale is < 0 ? -scale.Value : 0;
            var typeName = fieldSubType == 2 ? "DECIMAL" : "NUMERIC";
            return sc > 0 ? $"{typeName}({prec},{sc})" : $"{typeName}({prec})";
        }

        return fieldType switch
        {
            7 => "SMALLINT",
            8 => "INTEGER",
            10 => "FLOAT",
            12 => "DATE",
            13 => "TIME",
            14 => $"CHAR({(charLength ?? fieldLength ?? 1)})",
            16 => "BIGINT",
            23 => "BOOLEAN",
            26 => "INT128",
            27 => "DOUBLE PRECISION",
            28 => "TIME WITH TIME ZONE",
            29 => "TIMESTAMP WITH TIME ZONE",
            35 => "TIMESTAMP",
            37 => $"VARCHAR({(charLength ?? fieldLength ?? 1)})",
            261 => fieldSubType switch
            {
                1 => "BLOB SUB_TYPE TEXT",
                0 => "BLOB SUB_TYPE BINARY",
                _ => $"BLOB SUB_TYPE {fieldSubType ?? 0}",
            },
            _ => $"/* field_type={fieldType} */",
        };
    }

    private static short DefaultPrecision(short fieldType) => fieldType switch
    {
        7 => 4,
        8 => 9,
        16 => 18,
        _ => 18,
    };

    private static bool IsCharType(short? fieldType) => fieldType is 14 or 37 or 261;

    // Trigger types: encoded in RDB$TRIGGER_TYPE.
    //   bit 0: 1 = BEFORE, 0 = AFTER
    //   Single-event: 1=BEFORE INSERT, 2=AFTER INSERT, 3=BEFORE UPDATE, 4=AFTER UPDATE,
    //                 5=BEFORE DELETE, 6=AFTER DELETE
    //   Multi-event (FB 1.5+): 17/18 = INS+UPD, 25/26 = INS+DEL, 27/28 = UPD+DEL,
    //                          113/114 = INS+UPD+DEL  (odd=BEFORE, even=AFTER)
    //   DB-event / DDL triggers: codes ≥ 8192 — we don't decode those, fall back.
    internal static string? DescribeTriggerType(short? triggerType)
    {
        if (triggerType is null) return null;
        int t = triggerType.Value;
        if (t >= 8192) return null;

        // For 1..6, the single-event lookup is direct.
        var single = t switch
        {
            1 => "BEFORE INSERT",
            2 => "AFTER INSERT",
            3 => "BEFORE UPDATE",
            4 => "AFTER UPDATE",
            5 => "BEFORE DELETE",
            6 => "AFTER DELETE",
            _ => null,
        };
        if (single is not null) return single;

        // Multi-event encodings as observed in real Firebird databases.
        return t switch
        {
            17 => "BEFORE INSERT OR UPDATE",
            18 => "AFTER INSERT OR UPDATE",
            25 => "BEFORE INSERT OR DELETE",
            26 => "AFTER INSERT OR DELETE",
            27 => "BEFORE UPDATE OR DELETE",
            28 => "AFTER UPDATE OR DELETE",
            113 => "BEFORE INSERT OR UPDATE OR DELETE",
            114 => "AFTER INSERT OR UPDATE OR DELETE",
            _ => null,
        };
    }

    private static async Task<string?> ReadBlobAsync(FbConnection connection, FbTransaction? tx, string sql, string nameParam, Encoding fallback, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        cmd.Parameters.AddWithValue("@name", nameParam);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false) || reader.IsDBNull(0))
        {
            return null;
        }
        return DecodeSourceBlob(reader, 0, fallback);
    }

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // System-catalog source BLOBs (RDB$PROCEDURE_SOURCE / RDB$TRIGGER_SOURCE / RDB$VIEW_SOURCE /
    // RDB$FUNCTION_SOURCE) are stored as opaque bytes (CHARACTER SET NONE in older catalogs).
    // In one and the same database we've seen *both* UTF-8 bytes (from modern tools / FB3+
    // PSQL) and connection-charset bytes (from older IBExpert writes with Charset=WIN1250).
    // Strategy: read raw bytes, try strict UTF-8 first — if the bytes are valid UTF-8 we trust
    // it; if strict UTF-8 throws DecoderFallbackException the bytes are a single-byte encoding
    // so fall back to the connection's charset. Final fallback to GetString covers drivers
    // that refuse byte access on TEXT blobs.
    internal static string DecodeSourceBlob(System.Data.Common.DbDataReader reader, int ordinal, Encoding fallback)
    {
        byte[]? buffer = null;
        int read = 0;
        try
        {
            var length = reader.GetBytes(ordinal, 0L, null!, 0, 0);
            if (length > 0)
            {
                buffer = new byte[length];
                long offset = 0;
                while (offset < length)
                {
                    var n = reader.GetBytes(ordinal, offset, buffer, (int)offset, (int)(length - offset));
                    if (n <= 0) break;
                    offset += n;
                }
                read = (int)offset;
            }
            else
            {
                return string.Empty;
            }
        }
        catch (InvalidCastException)
        {
            return reader.GetString(ordinal);
        }
        catch (NotSupportedException)
        {
            return reader.GetString(ordinal);
        }

        return DecodeBytes(buffer!, read, fallback);
    }

    // Pure-function decode: try strict UTF-8, fall back to the supplied encoding on invalid bytes.
    // Exposed internal for tests so we can pin the discriminator behaviour without a live FB.
    internal static string DecodeBytes(byte[] buffer, int length, Encoding fallback)
    {
        try
        {
            return StrictUtf8.GetString(buffer, 0, length);
        }
        catch (DecoderFallbackException)
        {
            return fallback.GetString(buffer, 0, length);
        }
    }

    private sealed record ConstraintInfo(string ConstraintName, string[] Columns);
    private sealed record ForeignKeyInfo(string ConstraintName, string[] Columns, string ReferencedTable, string[] ReferencedColumns, string UpdateRule, string DeleteRule);
    private sealed record IndexInfo(string IndexName, string[] Columns, bool IsUnique, bool IsDescending);

    private static async Task<ConstraintInfo?> ReadConstraintAsync(FbConnection connection, FbTransaction? tx, string relation, string type, CancellationToken ct)
    {
        await foreach (var c in ReadConstraintsAsync(connection, tx, relation, type, ct).ConfigureAwait(false))
        {
            return c;
        }
        return null;
    }

    private static async System.Collections.Generic.IAsyncEnumerable<ConstraintInfo> ReadConstraintsAsync(
        FbConnection connection, FbTransaction? tx, string relation, string type,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var pairs = new System.Collections.Generic.List<(string Name, string Col, short Pos)>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT TRIM(rc.RDB$CONSTRAINT_NAME), TRIM(s.RDB$FIELD_NAME), s.RDB$FIELD_POSITION " +
                "FROM RDB$RELATION_CONSTRAINTS rc " +
                "JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME " +
                "WHERE rc.RDB$RELATION_NAME = @rel AND rc.RDB$CONSTRAINT_TYPE = @ct " +
                "ORDER BY rc.RDB$CONSTRAINT_NAME, s.RDB$FIELD_POSITION";
            cmd.Parameters.AddWithValue("@rel", relation);
            cmd.Parameters.AddWithValue("@ct", type);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                pairs.Add((reader.GetString(0).Trim(), reader.GetString(1).Trim(), reader.GetInt16(2)));
            }
        }

        foreach (var grp in System.Linq.Enumerable.GroupBy(pairs, p => p.Name))
        {
            var cols = new System.Collections.Generic.List<string>();
            foreach (var p in System.Linq.Enumerable.OrderBy(grp, p => p.Pos)) cols.Add(p.Col);
            yield return new ConstraintInfo(grp.Key, cols.ToArray());
        }
    }

    private static async System.Collections.Generic.IAsyncEnumerable<ForeignKeyInfo> ReadForeignKeysAsync(
        FbConnection connection, FbTransaction? tx, string relation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // FK metadata is spread across three system tables: RDB$RELATION_CONSTRAINTS (FK side),
        // RDB$REF_CONSTRAINTS (rules + parent constraint pointer), and RDB$INDEX_SEGMENTS (columns).
        var pairs = new System.Collections.Generic.List<(string Name, string Col, short Pos, string RefTable, string RefCol, string UpdRule, string DelRule)>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
                "SELECT TRIM(rc.RDB$CONSTRAINT_NAME), TRIM(s.RDB$FIELD_NAME), s.RDB$FIELD_POSITION, " +
                "       TRIM(rc2.RDB$RELATION_NAME), TRIM(s2.RDB$FIELD_NAME), " +
                "       TRIM(refc.RDB$UPDATE_RULE), TRIM(refc.RDB$DELETE_RULE) " +
                "FROM RDB$RELATION_CONSTRAINTS rc " +
                "JOIN RDB$REF_CONSTRAINTS refc ON refc.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME " +
                "JOIN RDB$RELATION_CONSTRAINTS rc2 ON rc2.RDB$CONSTRAINT_NAME = refc.RDB$CONST_NAME_UQ " +
                "JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME " +
                "JOIN RDB$INDEX_SEGMENTS s2 ON s2.RDB$INDEX_NAME = rc2.RDB$INDEX_NAME " +
                "                          AND s2.RDB$FIELD_POSITION = s.RDB$FIELD_POSITION " +
                "WHERE rc.RDB$RELATION_NAME = @rel AND rc.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY' " +
                "ORDER BY rc.RDB$CONSTRAINT_NAME, s.RDB$FIELD_POSITION";
            cmd.Parameters.AddWithValue("@rel", relation);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                pairs.Add((
                    reader.GetString(0).Trim(),
                    reader.GetString(1).Trim(),
                    reader.GetInt16(2),
                    reader.GetString(3).Trim(),
                    reader.GetString(4).Trim(),
                    reader.IsDBNull(5) ? "RESTRICT" : reader.GetString(5).Trim(),
                    reader.IsDBNull(6) ? "RESTRICT" : reader.GetString(6).Trim()));
            }
        }

        foreach (var grp in System.Linq.Enumerable.GroupBy(pairs, p => p.Name))
        {
            var cols = new System.Collections.Generic.List<string>();
            var refCols = new System.Collections.Generic.List<string>();
            string refTable = string.Empty, upd = "RESTRICT", del = "RESTRICT";
            foreach (var p in System.Linq.Enumerable.OrderBy(grp, p => p.Pos))
            {
                cols.Add(p.Col);
                refCols.Add(p.RefCol);
                refTable = p.RefTable;
                upd = p.UpdRule;
                del = p.DelRule;
            }
            yield return new ForeignKeyInfo(grp.Key, cols.ToArray(), refTable, refCols.ToArray(), upd, del);
        }
    }

    private static async System.Collections.Generic.IAsyncEnumerable<IndexInfo> ReadIndexesAsync(
        FbConnection connection, FbTransaction? tx, string relation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var pairs = new System.Collections.Generic.List<(string Name, string Col, short Pos, bool Unique, bool Desc)>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            // RDB$INDICES.RDB$INDEX_TYPE = 1 → descending; UNIQUE_FLAG = 1 → unique.
            // Skip indexes that back a constraint (RDB$RELATION_CONSTRAINTS.RDB$INDEX_NAME) —
            // those are emitted as ALTER TABLE ADD CONSTRAINT above.
            cmd.CommandText =
                "SELECT TRIM(i.RDB$INDEX_NAME), TRIM(s.RDB$FIELD_NAME), s.RDB$FIELD_POSITION, " +
                "       COALESCE(i.RDB$UNIQUE_FLAG, 0), COALESCE(i.RDB$INDEX_TYPE, 0) " +
                "FROM RDB$INDICES i " +
                "JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = i.RDB$INDEX_NAME " +
                "WHERE i.RDB$RELATION_NAME = @rel " +
                "  AND COALESCE(i.RDB$SYSTEM_FLAG, 0) = 0 " +
                "  AND NOT EXISTS (SELECT 1 FROM RDB$RELATION_CONSTRAINTS rc WHERE rc.RDB$INDEX_NAME = i.RDB$INDEX_NAME) " +
                "ORDER BY i.RDB$INDEX_NAME, s.RDB$FIELD_POSITION";
            cmd.Parameters.AddWithValue("@rel", relation);
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                pairs.Add((
                    reader.GetString(0).Trim(),
                    reader.GetString(1).Trim(),
                    reader.GetInt16(2),
                    reader.GetInt16(3) == 1,
                    reader.GetInt16(4) == 1));
            }
        }

        foreach (var grp in System.Linq.Enumerable.GroupBy(pairs, p => p.Name))
        {
            var cols = new System.Collections.Generic.List<string>();
            bool unique = false, desc = false;
            foreach (var p in System.Linq.Enumerable.OrderBy(grp, p => p.Pos))
            {
                cols.Add(p.Col);
                unique = p.Unique;
                desc = p.Desc;
            }
            yield return new IndexInfo(grp.Key, cols.ToArray(), unique, desc);
        }
    }

    private static async Task<System.Collections.Generic.List<string>> ReadProcedureParamsAsync(
        FbConnection connection, FbTransaction? tx, string procName, short paramType, int serverMajor, CancellationToken ct)
    {
        var rows = new System.Collections.Generic.List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = InsertBeforeOrderBy(SqlForProcedureParams, StandalonePackageFilter(serverMajor, "pp."));
        cmd.Parameters.AddWithValue("@name", procName);
        cmd.Parameters.AddWithValue("@pt", paramType);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var pn = TrimOrEmpty(reader.GetString(0));
            var baseType = FormatType(
                SafeShort(reader, 1), SafeShort(reader, 2), SafeShort(reader, 3),
                SafeShort(reader, 4), SafeShort(reader, 5), SafeShort(reader, 6));
            var ownNull = SafeShort(reader, 7);
            // RDB$DEFAULT_SOURCE keeps the leading token as written ("= 1" or "DEFAULT 1");
            // strip it so we re-emit exactly one "= value" (only valid on input params).
            var def = FirebirdTableDetailReader.StripDefaultPrefix(SafeString(reader, 8));
            var fieldSource = TrimOrEmpty(SafeString(reader, 9));
            var domainNull = SafeShort(reader, 10);
            var sb = new StringBuilder();
            sb.Append(Quote(pn)).Append(' ').Append(TypeTextForField(fieldSource, baseType));
            if (EmitsNotNull(fieldSource, ownNull, domainNull)) sb.Append(" NOT NULL");
            if (paramType == 0 && !string.IsNullOrWhiteSpace(def)) sb.Append(" = ").Append(def.Trim());
            rows.Add(sb.ToString());
        }
        return rows;
    }

    // FB3+ packaged procedures/functions live in the same catalog tables as standalone
    // ones, keyed by the same RDB$*_NAME (and RDB$*_PARAMETERS/ARGUMENTS carry the same
    // RDB$PACKAGE_NAME column). A by-name read for a STANDALONE routine must exclude
    // packaged rows or a packaged namesake doubles/mismatches the result — e.g. a doubled
    // argument list "A, A, B, B" → -901 "duplicate specification". The column is FB3+ only,
    // so the filter is gated on the server major (there's nothing to exclude on 2.5).
    // Insert this before any ORDER BY (via InsertBeforeOrderBy) so the WHERE stays valid.
    // Leading + trailing space so it drops cleanly between a WHERE clause and a trailing
    // "ORDER BY …", or appends to a WHERE-terminated query.
    internal static string StandalonePackageFilter(int serverMajor, string alias = "")
        => serverMajor >= 3 ? $" AND {alias}RDB$PACKAGE_NAME IS NULL " : string.Empty;

    internal static string InsertBeforeOrderBy(string sql, string clause)
    {
        if (clause.Length == 0) return sql;
        int idx = sql.IndexOf(" ORDER BY", StringComparison.OrdinalIgnoreCase);
        return idx < 0 ? sql + clause : sql.Insert(idx, clause);
    }

    internal static int ParseServerMajor(string? serverVersion)
    {
        if (string.IsNullOrEmpty(serverVersion)) return 0;
        // Format examples: "WI-V3.0.7.33374 Firebird 3.0", "WI-V5.0.0.1306 Firebird 5.0".
        // Pull the first digit after "V" or the first "Firebird N" — whichever comes first.
        var match = System.Text.RegularExpressions.Regex.Match(serverVersion, @"V(\d+)\.");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var v)) return v;
        match = System.Text.RegularExpressions.Regex.Match(serverVersion, @"Firebird\s+(\d+)\.");
        if (match.Success && int.TryParse(match.Groups[1].Value, out v)) return v;
        return 0;
    }

    internal static string Quote(string identifier)
    {
        // V1 keeps it simple: only quote identifiers that need it (have lowercase letters,
        // start with a digit, contain spaces, or collide with reserved words). For typical
        // SHOUTY_SNAKE_CASE Firebird names we emit them unquoted, which matches isql -x.
        if (string.IsNullOrEmpty(identifier)) return "\"\"";
        bool needsQuote = false;
        for (int i = 0; i < identifier.Length; i++)
        {
            var c = identifier[i];
            if (i == 0 && !char.IsLetter(c) && c != '_') { needsQuote = true; break; }
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '$')) { needsQuote = true; break; }
            if (char.IsLower(c)) { needsQuote = true; break; }
        }
        return needsQuote ? "\"" + identifier.Replace("\"", "\"\"") + "\"" : identifier;
    }

    private static string JoinQuoted(System.Collections.Generic.IEnumerable<string> names)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var n in names)
        {
            if (!first) sb.Append(", ");
            first = false;
            sb.Append(Quote(n));
        }
        return sb.ToString();
    }

    private static string TrimOrEmpty(string? s) => s?.Trim() ?? string.Empty;
    private static string? SafeString(System.Data.Common.DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static short? SafeShort(System.Data.Common.DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : Convert.ToInt16(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
}

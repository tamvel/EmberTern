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
    private readonly TransactionService? _transactionService;

    public FirebirdDdlReader(FirebirdConnectionService connectionService)
        : this(connectionService, null)
    {
    }

    public FirebirdDdlReader(FirebirdConnectionService connectionService, TransactionService? transactionService)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    // Connection + lock for this reader's lane (metadata in production), so DDL browsing
    // runs on the metadata attachment. Falls back to the data connection when no
    // transaction service is injected (tests).
    private FbConnection LaneConnection()
        => _transactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
    private SemaphoreSlim LaneLock()
        => _transactionService?.CommandLock ?? _connectionService.CommandLock;

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
        var tx = _transactionService?.ActiveTransaction;
        var commandLock = LaneLock();
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return obj.Kind switch
            {
                MetadataObjectKind.Table => await BuildTableDdlAsync(connection, tx, obj.Name, cancellationToken).ConfigureAwait(false),
                MetadataObjectKind.View => await BuildViewDdlAsync(connection, tx, obj.Name, fallback, cancellationToken).ConfigureAwait(false),
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
                MetadataObjectKind.Package => BuildPlaceholderDdl("PACKAGE", obj.Name),
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
                    if (domainName.Length > 0 && !domainName.StartsWith("RDB$", StringComparison.OrdinalIgnoreCase))
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

    private static async Task<string> BuildViewDdlAsync(FbConnection connection, FbTransaction? tx, string name, Encoding fallback, CancellationToken ct)
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
        sb.Append("CREATE VIEW ").Append(Quote(name));
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

    private static async Task<string> BuildProcedureDdlAsync(FbConnection connection, FbTransaction? tx, string name, int serverMajor, Encoding fallback, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER PROCEDURE ").Append(Quote(name));

        var inputs = await ReadProcedureParamsAsync(connection, tx, name, paramType: 0, ct).ConfigureAwait(false);
        var outputs = await ReadProcedureParamsAsync(connection, tx, name, paramType: 1, ct).ConfigureAwait(false);

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
            "SELECT RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @name",
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

    private static async Task<string> BuildTriggerDdlAsync(FbConnection connection, FbTransaction? tx, string name, Encoding fallback, CancellationToken ct)
    {
        string? relation = null;
        short? triggerType = null;
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
                triggerType = SafeShort(reader, 1);
                sequence = SafeShort(reader, 2);
                inactive = SafeShort(reader, 3);
            }
        }

        var source = await ReadBlobAsync(connection, tx,
            "SELECT RDB$TRIGGER_SOURCE FROM RDB$TRIGGERS WHERE RDB$TRIGGER_NAME = @name",
            name, fallback, ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER TRIGGER ").Append(Quote(name));
        if (inactive == 1) sb.Append(" INACTIVE");

        if (!string.IsNullOrEmpty(relation))
        {
            sb.Append(" FOR ").Append(Quote(relation));
        }

        // Trigger type encodes timing + event(s); use shorthand for the common cases,
        // fall back to a numeric comment when the encoding is non-standard.
        var header = DescribeTriggerType(triggerType);
        if (!string.IsNullOrEmpty(header))
        {
            sb.Append(' ').Append(header);
        }
        else if (triggerType.HasValue)
        {
            sb.Append(" /* trigger type ").Append(triggerType.Value).Append(" */");
        }

        if (sequence is > 0)
        {
            sb.Append(" POSITION ").Append(sequence.Value);
        }

        sb.AppendLine();
        sb.AppendLine("AS");
        sb.AppendLine(string.IsNullOrWhiteSpace(source) ? "BEGIN\n  /* trigger body unavailable */\nEND" : source.Trim());
        return sb.ToString();
    }

    // -- Functions ------------------------------------------------------------

    private static async Task<string> BuildFunctionDdlAsync(FbConnection connection, FbTransaction? tx, string name, int serverMajor, Encoding fallback, CancellationToken ct)
    {
        if (serverMajor <= 2)
        {
            return $"/* Function {name} is a UDF declaration (Firebird 2.5). Source DDL not stored in the catalog. */\n";
        }

        var source = await ReadBlobAsync(connection, tx,
            "SELECT RDB$FUNCTION_SOURCE FROM RDB$FUNCTIONS WHERE RDB$FUNCTION_NAME = @name",
            name, fallback, ct).ConfigureAwait(false);

        var sb = new StringBuilder();
        sb.Append("CREATE OR ALTER FUNCTION ").Append(Quote(name)).AppendLine();
        sb.AppendLine("/* (params and return type omitted; reconstruct from RDB$FUNCTION_ARGUMENTS for richer DDL) */");
        sb.AppendLine("AS");
        sb.AppendLine(string.IsNullOrWhiteSpace(source) ? "BEGIN\n  /* function body unavailable */\nEND" : source.Trim());
        return sb.ToString();
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
        FbConnection connection, FbTransaction? tx, string procName, short paramType, CancellationToken ct)
    {
        var rows = new System.Collections.Generic.List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT TRIM(pp.RDB$PARAMETER_NAME), " +
            "       f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, f.RDB$FIELD_LENGTH, " +
            "       f.RDB$FIELD_PRECISION, f.RDB$FIELD_SCALE, f.RDB$CHARACTER_LENGTH, " +
            "       COALESCE(pp.RDB$NULL_FLAG, f.RDB$NULL_FLAG) " +
            "FROM RDB$PROCEDURE_PARAMETERS pp " +
            "JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = pp.RDB$FIELD_SOURCE " +
            "WHERE pp.RDB$PROCEDURE_NAME = @name AND pp.RDB$PARAMETER_TYPE = @pt " +
            "ORDER BY pp.RDB$PARAMETER_NUMBER";
        cmd.Parameters.AddWithValue("@name", procName);
        cmd.Parameters.AddWithValue("@pt", paramType);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var pn = TrimOrEmpty(reader.GetString(0));
            var type = FormatType(
                SafeShort(reader, 1), SafeShort(reader, 2), SafeShort(reader, 3),
                SafeShort(reader, 4), SafeShort(reader, 5), SafeShort(reader, 6));
            var nf = SafeShort(reader, 7);
            var sb = new StringBuilder();
            sb.Append(Quote(pn)).Append(' ').Append(type);
            if (nf == 1) sb.Append(" NOT NULL");
            rows.Add(sb.ToString());
        }
        return rows;
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

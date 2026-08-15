using System;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Reads the Database Properties window's content off the connected database.
///
/// <para>⭐ <b>Metadata lane</b> — read-only catalog browsing with implicit per-command transactions
/// (<see cref="ConnectionRole.Metadata"/>), exactly as <see cref="FirebirdSessionReader"/> already reads
/// <c>MON$DATABASE</c>. Nothing here touches the user's working transaction on the Data lane.</para>
///
/// <para>⭐⭐ <b>No version gate, and that is measured rather than assumed.</b> Every column below was probed
/// on BOTH ends of the declared support range — Firebird 3.0.13 (ODS 12.0) and 5.0.3 (ODS 13.1) — and all of
/// them exist on both (<c>DatabasePropertiesProbe</c> §H). ⛔ The FB4/FB5-only columns (<c>MON$GUID</c>,
/// <c>MON$FILE_ID</c>, <c>MON$SEC_DATABASE</c>, <c>MON$CRYPT_STATE</c>, <c>MON$REPLICA_MODE</c>,
/// <c>MON$NEXT_*</c>) are deliberately absent: naming one here would break FB3 with a plain "column unknown",
/// which is gotcha #146 in a new place.</para>
/// </summary>
public sealed class FirebirdDatabasePropertiesReader
{
    private readonly FirebirdConnectionService _connectionService;

    public FirebirdDatabasePropertiesReader(FirebirdConnectionService connectionService)
        => _connectionService = connectionService;

    /// <summary>
    /// The one query behind the window. ⚠ Kept as a constant so a guard can read it: the FB3 compatibility of
    /// this feature is a property of THIS COLUMN LIST, and nothing else enforces it.
    /// </summary>
    internal const string PropertiesSql =
        "SELECT d.MON$PAGE_SIZE, d.MON$ODS_MAJOR, d.MON$ODS_MINOR, d.MON$PAGE_BUFFERS, " +
        "d.MON$SQL_DIALECT, d.MON$SWEEP_INTERVAL, d.MON$FORCED_WRITES, " +
        "d.MON$RESERVE_SPACE, d.MON$CREATION_DATE, d.MON$PAGES, d.MON$OWNER, " +
        "r.RDB$CHARACTER_SET_NAME, r.RDB$LINGER, " +
        "RDB$GET_CONTEXT('SYSTEM','ENGINE_VERSION') " +
        "FROM MON$DATABASE d CROSS JOIN RDB$DATABASE r";

    /// <param name="profile">
    /// The connected profile. ⚠ Its <see cref="ConnectionProfile.DatabasePath"/> — not
    /// <c>MON$DATABASE_NAME</c> — becomes the window's <c>Database</c> value (ratified): the engine's own form
    /// is upper-cased and, on a remote server, is a path on the SERVER's filesystem.
    /// </param>
    public async Task<DatabaseProperties> ReadAsync(
        ConnectionProfile profile, CancellationToken cancellationToken = default)
    {
        var connection = _connectionService.RequireOpenConnection(ConnectionRole.Metadata);
        var commandLock = _connectionService.GetCommandLock(ConnectionRole.Metadata);
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateGuardedCommand(PropertiesSql);
            cmd.CommandTimeout = 0;

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new MetadataReadException("MON$DATABASE returned no row.");
            }

            return new DatabaseProperties
            {
                DatabasePath = profile.DatabasePath,
                PageSize = reader.GetInt32(0),
                OdsMajor = Convert.ToInt32(reader.GetValue(1)),
                OdsMinor = Convert.ToInt32(reader.GetValue(2)),
                PageBuffers = reader.GetInt32(3),
                Dialect = Convert.ToInt32(reader.GetValue(4)),
                SweepInterval = reader.GetInt32(5),
                ForcedWrites = Convert.ToInt32(reader.GetValue(6)) != 0,
                ReserveSpace = Convert.ToInt32(reader.GetValue(7)) != 0,
                CreatedAt = reader.GetDateTime(8),
                Pages = Convert.ToInt64(reader.GetValue(9)),
                // ⚠ Both CHAR columns come back PADDED (measured on FB3 and FB5) — untrimmed they render as a
                // name followed by a long run of spaces, which reads as a layout defect.
                Owner = reader.IsDBNull(10) ? string.Empty : reader.GetString(10).TrimEnd(),
                Charset = reader.IsDBNull(11) ? string.Empty : reader.GetString(11).TrimEnd(),
                // ⚠ NULL is "not set", NOT 0 — measured NULL on a database that never configured linger, on
                // both servers. Mapping it to 0 would claim a configured value the database does not have.
                LingerSeconds = reader.IsDBNull(12) ? null : Convert.ToInt32(reader.GetValue(12)),
                EngineVersion = reader.IsDBNull(13) ? string.Empty : reader.GetString(13).TrimEnd(),
            };
        }
        catch (FbException ex)
        {
            throw new MetadataReadException($"Could not read the database properties: {ex.Message}", ex);
        }
        finally
        {
            commandLock.Release();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Connections;
using EmberTern.Core.Metadata;
using FirebirdSql.Data.FirebirdClient;
using FirebirdSql.Data.Services;

namespace EmberTern.Firebird;

/// <summary>
/// Applies the three editable database settings through Firebird's <b>Services API</b>.
///
/// <para>⭐ <b>This runs OUTSIDE all three connection lanes</b> — the Services API opens its own connection to
/// the service manager. So it cannot touch the user's working transaction, and equally: <b>it is not
/// rollbackable</b>. That is the whole reason the window has an explicit Apply instead of Settings Center's
/// apply-on-change — this writes to a shared production database, not to a local file.</para>
///
/// <para>⚠⚠ <b>Every setting is its own Services call, so a PARTIAL success is a reachable state.</b> Hence
/// one outcome per setting rather than a single success flag; the caller must be able to say which changes
/// are live. Measured contract (<c>DatabasePropertiesProbe</c> §B): a single
/// <c>ctor(string connectionString)</c>, no <c>Database</c> property, and
/// <c>SetAccessModeAsync</c> takes a <c>bool</c> — the design had assumed an enum.</para>
/// </summary>
public sealed class FirebirdDatabaseConfigurationWriter
{
    /// <summary>
    /// Builds the Services connection string for ONE database.
    ///
    /// <para>⛔ <b><see cref="FirebirdTraceService.BuildServiceConnectionString"/> cannot be reused here.</b>
    /// It deliberately builds a NO-DATABASE service string (Trace attaches to the server), and the probe
    /// measured that a database-scoped configuration call refuses that with <i>"Action should be executed
    /// against a specific database"</i>. Same API, different requirement — so this is a second builder by
    /// necessity, not by oversight.</para>
    /// </summary>
    internal static string BuildServiceConnectionString(ConnectionProfile profile)
        => new FbConnectionStringBuilder
        {
            DataSource = string.IsNullOrWhiteSpace(profile.Host) ? "localhost" : profile.Host,
            Port = profile.Port > 0 ? profile.Port : 3050,
            Database = profile.DatabasePath,
            UserID = profile.Username,
            Password = profile.Password,
            ServerType = FbServerType.Default,
        }.ToString();

    /// <summary>
    /// Whether an Apply can even be attempted.
    /// <para>⭐ Measured: with no password the driver refuses before reaching the server
    /// (<i>"No user password was specified."</i>). That is knowable UP FRONT, so refusing before the attempt
    /// is honest — unlike the <c>USE_GFIX_UTILITY</c> privilege, which is NOT knowable without trying and is
    /// therefore deliberately left to surface as a server error (no pre-check, ratified).</para>
    /// </summary>
    public static bool CanAttempt(ConnectionProfile profile) => !string.IsNullOrEmpty(profile.Password);

    /// <summary>
    /// Sends only what changed. Never throws for a server refusal — a refusal is a per-setting OUTCOME,
    /// because the caller has to report which of the changes did land.
    /// </summary>
    public async Task<DatabaseConfigurationResult> ApplyAsync(
        ConnectionProfile profile,
        DatabaseConfigurationChange change,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<DatabaseSettingOutcome>();
        var connectionString = BuildServiceConnectionString(profile);

        if (change.SweepInterval is { } sweep)
        {
            outcomes.Add(await RunAsync(
                DatabaseSetting.SweepInterval, connectionString,
                (svc, ct) => svc.SetSweepIntervalAsync(sweep, ct), cancellationToken).ConfigureAwait(false));
        }

        if (change.ForcedWrites is { } forced)
        {
            outcomes.Add(await RunAsync(
                DatabaseSetting.ForcedWrites, connectionString,
                (svc, ct) => svc.SetForcedWritesAsync(forced, ct), cancellationToken).ConfigureAwait(false));
        }

        if (change.ReserveSpace is { } reserve)
        {
            outcomes.Add(await RunAsync(
                DatabaseSetting.ReserveSpace, connectionString,
                (svc, ct) => svc.SetReserveSpaceAsync(reserve, ct), cancellationToken).ConfigureAwait(false));
        }

        return new DatabaseConfigurationResult(outcomes);
    }

    private static async Task<DatabaseSettingOutcome> RunAsync(
        DatabaseSetting setting,
        string connectionString,
        Func<FbConfiguration, CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            var service = new FbConfiguration(connectionString);
            await operation(service, cancellationToken).ConfigureAwait(false);
            return new DatabaseSettingOutcome(setting);
        }
        catch (FbException ex)
        {
            // ⚠ SQLSTATE and the GDS vector travel with the message so the App can recognise a case WITHOUT
            // reading the text — the DebugErrorClassifier rule. The raw message is always kept: it is the
            // server's own words and the user may need to quote it.
            return new DatabaseSettingOutcome(
                setting, ex.Message, ex.SQLSTATE, ex.Errors.Cast<FbError>().Select(e => e.Number).ToArray());
        }
        catch (Exception ex)
        {
            // ⚠ The driver refuses some cases before reaching the server (an absent password), and those
            // arrive as a plain exception with NO SQLSTATE and NO GDS codes — measured. Losing them to an
            // FbException-only catch would report "nothing happened" for an Apply that never ran.
            return new DatabaseSettingOutcome(setting, ex.Message);
        }
    }
}

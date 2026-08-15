using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

/// <summary>
/// Inline data editor for the TableDetail "Dane" tab.
///
/// Auto-begins the user's working transaction via <see cref="TransactionService"/>
/// when none is active (mirrors the F5 executor's auto-begin path). Never opens
/// its own raw tx on the connection — the user controls Commit / Rollback through
/// the existing toolbar.
///
/// All commands hold <see cref="FirebirdConnectionService"/>'s CommandLock for
/// the duration of <c>ExecuteNonQueryAsync</c> so we don't race against metadata
/// reads, F5, or other readers on the single-statement <c>FbConnection</c>.
/// </summary>
public sealed class FirebirdDataEditor
{
    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService _transactionService;

    public FirebirdDataEditor(FirebirdConnectionService connectionService, TransactionService transactionService)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    public async Task UpdateCellAsync(
        string tableName,
        string columnName,
        object? newValue,
        IReadOnlyList<KeyValuePair<string, object?>> primaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("Table name is required.", nameof(tableName));
        if (string.IsNullOrEmpty(columnName)) throw new ArgumentException("Column name is required.", nameof(columnName));
        if (primaryKey is null || primaryKey.Count == 0)
            throw new InvalidOperationException("Cannot UPDATE without a primary key.");

        var sql = BuildUpdateSql(tableName, columnName, ExtractKeys(primaryKey));
        await EnsureTransactionAsync().ConfigureAwait(true);
        await ExecuteAsync(sql, cmd =>
        {
            cmd.AddGuardedParameter("@newValue", newValue ?? DBNull.Value);
            for (int i = 0; i < primaryKey.Count; i++)
            {
                cmd.AddGuardedParameter("@pk" + i.ToString(CultureInfo.InvariantCulture), primaryKey[i].Value ?? DBNull.Value);
            }
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task InsertRowAsync(
        string tableName,
        IReadOnlyList<KeyValuePair<string, object?>> values,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("Table name is required.", nameof(tableName));
        if (values is null || values.Count == 0)
            throw new InvalidOperationException("Cannot INSERT a row with no column values.");

        var sql = BuildInsertSql(tableName, ExtractKeys(values));
        await EnsureTransactionAsync().ConfigureAwait(true);
        await ExecuteAsync(sql, cmd =>
        {
            for (int i = 0; i < values.Count; i++)
            {
                cmd.AddGuardedParameter("@v" + i.ToString(CultureInfo.InvariantCulture), values[i].Value ?? DBNull.Value);
            }
        }, cancellationToken).ConfigureAwait(true);
    }

    public async Task DeleteRowAsync(
        string tableName,
        IReadOnlyList<KeyValuePair<string, object?>> primaryKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tableName)) throw new ArgumentException("Table name is required.", nameof(tableName));
        if (primaryKey is null || primaryKey.Count == 0)
            throw new InvalidOperationException("Cannot DELETE without a primary key.");

        var sql = BuildDeleteSql(tableName, ExtractKeys(primaryKey));
        await EnsureTransactionAsync().ConfigureAwait(true);
        await ExecuteAsync(sql, cmd =>
        {
            for (int i = 0; i < primaryKey.Count; i++)
            {
                cmd.AddGuardedParameter("@pk" + i.ToString(CultureInfo.InvariantCulture), primaryKey[i].Value ?? DBNull.Value);
            }
        }, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// If no working transaction is active, kicks one off via
    /// <see cref="TransactionService.BeginTransactionAsync"/> (the same path the
    /// F5 executor uses). Idempotent — returns a completed task when a tx is
    /// already active. Public so callers can pre-open a transaction before a
    /// sequence of edits (e.g. <c>AddRow</c> in the data tab) so the toolbar
    /// reflects active-tx state immediately, not only after the first INSERT.
    /// </summary>
    public Task EnsureTransactionAsync()
    {
        if (_transactionService.IsActive) return Task.CompletedTask;
        return _transactionService.BeginTransactionAsync();
    }

    private async Task ExecuteAsync(string sql, Action<FbCommand> bindParameters, CancellationToken cancellationToken)
    {
        // Data lane: inline edits run on the data attachment under the data working tx.
        var connection = _transactionService.RequireOpenConnection();
        var commandLock = _transactionService.CommandLock;
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            await using var cmd = connection.CreateGuardedCommand(sql);
            cmd.CommandTimeout = 0;
            cmd.Transaction = _transactionService.ActiveTransaction;
            bindParameters(cmd);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (FbException ex)
        {
            throw new DataEditException(ex.Message, ex);
        }
        catch (CharsetRepresentationException ex)
        {
            // An inline grid edit whose text this connection cannot carry: refused, nothing written.
            throw new DataEditException(ex.Message, ex);
        }
        finally
        {
            commandLock.Release();
        }

        // Counter tick happens after release so the transaction-bar update doesn't
        // sit behind the lock.
        _transactionService.NotifyStatementExecuted();
    }

    private static IReadOnlyList<string> ExtractKeys(IReadOnlyList<KeyValuePair<string, object?>> pairs)
    {
        var keys = new string[pairs.Count];
        for (int i = 0; i < pairs.Count; i++) keys[i] = pairs[i].Key;
        return keys;
    }

    // Internal SQL builders — pure, easy to unit-test. Identifiers are quoted with
    // doubled internal quotes; parameter names are positional (@newValue, @pk0..N,
    // @v0..N) so the binder doesn't have to dedupe references across the same SQL.
    internal static string BuildUpdateSql(string tableName, string columnName, IReadOnlyList<string> pkColumns)
    {
        var sb = new StringBuilder();
        sb.Append("UPDATE ");
        AppendIdentifier(sb, tableName);
        sb.Append(" SET ");
        AppendIdentifier(sb, columnName);
        sb.Append(" = @newValue WHERE ");
        AppendWhereClause(sb, pkColumns);
        return sb.ToString();
    }

    internal static string BuildInsertSql(string tableName, IReadOnlyList<string> columns)
    {
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ");
        AppendIdentifier(sb, tableName);
        sb.Append(" (");
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            AppendIdentifier(sb, columns[i]);
        }
        sb.Append(") VALUES (");
        for (int i = 0; i < columns.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append("@v").Append(i.ToString(CultureInfo.InvariantCulture));
        }
        sb.Append(')');
        return sb.ToString();
    }

    internal static string BuildDeleteSql(string tableName, IReadOnlyList<string> pkColumns)
    {
        var sb = new StringBuilder();
        sb.Append("DELETE FROM ");
        AppendIdentifier(sb, tableName);
        sb.Append(" WHERE ");
        AppendWhereClause(sb, pkColumns);
        return sb.ToString();
    }

    private static void AppendWhereClause(StringBuilder sb, IReadOnlyList<string> pkColumns)
    {
        for (int i = 0; i < pkColumns.Count; i++)
        {
            if (i > 0) sb.Append(" AND ");
            AppendIdentifier(sb, pkColumns[i]);
            sb.Append(" = @pk").Append(i.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendIdentifier(StringBuilder sb, string identifier)
    {
        sb.Append('"').Append(identifier.Replace("\"", "\"\"")).Append('"');
    }
}

public sealed class DataEditException : Exception
{
    public DataEditException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

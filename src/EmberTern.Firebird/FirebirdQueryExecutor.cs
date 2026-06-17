using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Query;
using FirebirdSql.Data.FirebirdClient;

namespace EmberTern.Firebird;

public sealed class FirebirdQueryExecutor
{
    public const int DefaultRowLimit = 5000;

    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService? _transactionService;

    public FirebirdQueryExecutor(FirebirdConnectionService connectionService, TransactionService? transactionService = null)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    public int RowLimit { get; init; } = DefaultRowLimit;

    public Task<QueryResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(sql, null, cancellationToken);

    /// <summary>Executes a parameterized statement — used by Execute Procedure so
    /// input values are bound (never embedded as SQL literals). Parameter names in
    /// <paramref name="sql"/> must match <see cref="QueryParameter.Name"/> (e.g.
    /// <c>@p0</c>). A null <see cref="QueryParameter.Value"/> binds SQL NULL.</summary>
    public Task<QueryResult> ExecuteAsync(string sql, IReadOnlyList<QueryParameter> parameters, CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(sql, parameters, cancellationToken);

    private async Task<QueryResult> ExecuteCoreAsync(string sql, IReadOnlyList<QueryParameter>? parameters, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new QueryExecutionException("Query is empty.");
        }

        if (_transactionService is { IsActive: false })
        {
            try
            {
                await _transactionService.BeginTransactionAsync().ConfigureAwait(false);
            }
            catch (TransactionFailedException ex)
            {
                throw new QueryExecutionException(ex.Message, ex);
            }
        }

        var sw = Stopwatch.StartNew();

        bool lockHeld = false;
        // Run on this executor's lane: the data connection for F5, the metadata
        // connection for "Execute on Metadata" (Shift+F5). The lock resolves without
        // throwing; the connection is resolved inside the try so a missing connection
        // surfaces as a clean QueryExecutionException.
        var commandLock = _transactionService?.CommandLock ?? _connectionService.CommandLock;
        try
        {
            var connection = _transactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
            // Serialize against in-flight reader commands (metadata eager-load, DDL fetch,
            // autocomplete column fetch, TableDetail load). FbConnection is single-threaded.
            await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockHeld = true;
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 0;
            if (_transactionService?.ActiveTransaction is { } tx)
            {
                cmd.Transaction = tx;
            }
            if (parameters is { Count: > 0 })
            {
                foreach (var p in parameters)
                {
                    cmd.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value);
                }
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

            if (reader.FieldCount == 0)
            {
                sw.Stop();
                _transactionService?.NotifyStatementExecuted();
                return new QueryResult
                {
                    Elapsed = sw.Elapsed,
                    RecordsAffected = reader.RecordsAffected >= 0 ? reader.RecordsAffected : null,
                };
            }

            var columns = new QueryColumn[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns[i] = new QueryColumn(reader.GetName(i), reader.GetFieldType(i));
            }

            var rows = new List<object?[]>();
            var truncated = false;

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (rows.Count >= RowLimit)
                {
                    truncated = true;
                    break;
                }

                var row = new object?[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                rows.Add(row);
            }

            sw.Stop();
            _transactionService?.NotifyStatementExecuted();

            return new QueryResult
            {
                Columns = columns,
                Rows = rows,
                Elapsed = sw.Elapsed,
                Truncated = truncated,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FbException ex)
        {
            throw new QueryExecutionException(FormatFbError(ex), ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new QueryExecutionException(ex.Message, ex);
        }
        finally
        {
            if (lockHeld)
            {
                commandLock.Release();
            }
        }
    }

    private static string FormatFbError(FbException ex)
    {
        var msg = ex.Message?.Trim() ?? "Unknown Firebird error.";

        if (ex.Errors is { } errors)
        {
            foreach (FbError first in errors)
            {
                if (first.LineNumber > 0)
                {
                    return $"Line {first.LineNumber}: {msg}";
                }
                break;
            }
        }
        return msg;
    }
}

public sealed class QueryExecutionException : Exception
{
    public QueryExecutionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

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
    public const int DefaultRowLimit = ExecutionDefaults.PreviewLimit;

    // Report streamed progress this often during a Full read (so the "Loading… N rows"
    // counter ticks without flooding the UI thread on a multi-million-row load).
    private const int ProgressReportEvery = 2000;

    private readonly FirebirdConnectionService _connectionService;
    private readonly TransactionService? _transactionService;

    public FirebirdQueryExecutor(FirebirdConnectionService connectionService, TransactionService? transactionService = null)
    {
        _connectionService = connectionService;
        _transactionService = transactionService;
    }

    /// <summary>Preview row cap used by the legacy string overloads (F5 / proc / func / script).
    /// Full uses <see cref="ExecutionRequest.FullSafetyCeiling"/> instead.</summary>
    public int RowLimit { get; init; } = DefaultRowLimit;

    public Task<QueryResult> ExecuteAsync(string sql, CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(new ExecutionRequest { Sql = sql, PreviewLimit = RowLimit }, null, null, cancellationToken);

    /// <summary>Executes a parameterized statement — used by Execute Procedure so
    /// input values are bound (never embedded as SQL literals). Parameter names in
    /// <paramref name="sql"/> must match <see cref="QueryParameter.Name"/> (e.g.
    /// <c>@p0</c>). A null <see cref="QueryParameter.Value"/> binds SQL NULL.</summary>
    public Task<QueryResult> ExecuteAsync(string sql, IReadOnlyList<QueryParameter> parameters, CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(new ExecutionRequest { Sql = sql, Parameters = parameters, PreviewLimit = RowLimit }, null, null, cancellationToken);

    /// <summary>The streaming entry point. Preview stops at <see cref="ExecutionRequest.PreviewLimit"/>
    /// (→ <see cref="QueryResult.Truncated"/>); Full streams every row up to the hard
    /// <see cref="ExecutionRequest.FullSafetyCeiling"/> (→ <see cref="QueryResult.CeilingHit"/>),
    /// reporting <paramref name="progress"/> (rows read so far) as it goes so a long load can show a
    /// live counter. When <paramref name="onSoftThreshold"/> is supplied (Full), it is invoked ONCE
    /// the first time <see cref="ExecutionRequest.SoftThreshold"/> rows have been read and more remain;
    /// returning <c>false</c> stops the load and keeps the partial (flagged <see cref="QueryResult.Truncated"/>),
    /// <c>true</c> continues to the hard ceiling. Cancellation throws <see cref="OperationCanceledException"/>
    /// — the caller keeps whatever it had before (Load-all does not replace the grid on cancel).</summary>
    public Task<QueryResult> ExecuteAsync(
        ExecutionRequest request,
        IProgress<long>? progress = null,
        Func<long, Task<bool>>? onSoftThreshold = null,
        CancellationToken cancellationToken = default)
        => ExecuteCoreAsync(request, progress, onSoftThreshold, cancellationToken);

    private async Task<QueryResult> ExecuteCoreAsync(
        ExecutionRequest request,
        IProgress<long>? progress,
        Func<long, Task<bool>>? onSoftThreshold,
        CancellationToken cancellationToken)
    {
        var sql = request.Sql;
        var parameters = request.Parameters;
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

            // Preview stops at PreviewLimit (→ Truncated); Full streams to the hard
            // FullSafetyCeiling (→ CeilingHit). Benchmark counts but discards rows (reserved;
            // no UI path yet). The cap is checked when a (cap+1)th row is available, so we read
            // exactly `cap` rows and know there is more.
            long cap = request.Intent == ExecutionIntent.Preview
                ? request.PreviewLimit
                : request.FullSafetyCeiling;
            bool discard = request.Intent == ExecutionIntent.Benchmark;

            var rows = new List<object?[]>();
            long produced = 0;
            var truncated = false;
            var ceilingHit = false;
            var softAsked = false;

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (produced >= cap)
                {
                    if (request.Intent == ExecutionIntent.Preview) truncated = true;
                    else ceilingHit = true;
                    break;
                }

                // Smart soft threshold (Full): once SoftThreshold rows are read and more remain,
                // ask once whether to keep loading. "Stop here" keeps the partial (Truncated → the
                // notice bar reappears offering Load-all again).
                if (onSoftThreshold is not null && !softAsked && produced >= request.SoftThreshold)
                {
                    softAsked = true;
                    var keepLoading = await onSoftThreshold(produced).ConfigureAwait(false);
                    if (!keepLoading)
                    {
                        truncated = true;
                        break;
                    }
                }

                if (!discard)
                {
                    var row = new object?[reader.FieldCount];
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    rows.Add(row);
                }

                produced++;
                if (progress is not null && produced % ProgressReportEvery == 0)
                {
                    progress.Report(produced);
                }
            }

            sw.Stop();
            _transactionService?.NotifyStatementExecuted();
            progress?.Report(produced);

            return new QueryResult
            {
                Columns = columns,
                Rows = rows,
                Elapsed = sw.Elapsed,
                Truncated = truncated,
                CeilingHit = ceilingHit,
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

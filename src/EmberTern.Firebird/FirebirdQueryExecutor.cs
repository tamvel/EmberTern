using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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

    /// <summary>
    /// Captures the server's per-column provenance for <paramref name="sql"/> — signal A for the SQL
    /// export formats. Returns null when it cannot be read (no connection, or the statement will not
    /// prepare), which the caller treats as "the formats are unavailable", never as an error to show.
    /// <para>
    /// <b>Never call this on the execution path.</b> <c>GetSchemaTable()</c> costs ~7 ms — about 5.6× a
    /// small query — so doing it on every F5 to serve an occasional menu action would be a silent,
    /// across-the-board regression of the editor and its execution timer. It runs lazily, on the first
    /// Copy-as-INSERT/UPDATE, and <see cref="CommandBehavior.SchemaOnly"/> means one prepare and no rows:
    /// the grid already holds the data, so only the SHAPE is re-derived.
    /// </para>
    /// <para>
    /// Runs on the <b>Data lane</b> — the attachment that ran the query — under its command lock, because
    /// one <c>FbConnection</c> allows one transaction at a time and concurrent commands must be
    /// serialized. Deliberately NOT the Metadata lane: a statement may reference an object created but
    /// not yet committed in the Data lane's transaction, which is invisible to any other attachment, so a
    /// Metadata-lane prepare would fail exactly when the user is iterating on new DDL.
    /// </para>
    /// </summary>
    public async Task<System.Data.DataTable?> CaptureSchemaTableAsync(
        string sql,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql)) return null;

        FbConnection connection;
        try
        {
            connection = _transactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
        }
        catch (InvalidOperationException)
        {
            return null;
        }

        // Capture the lane-resolving lock accessor into a LOCAL once — re-invoking it at release time
        // would leak one semaphore and over-release another if the lane flipped mid-call (gotcha #98).
        var commandLock = _transactionService?.CommandLock ?? _connectionService.CommandLock;
        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateGuardedCommand(sql);
            if (_transactionService?.ActiveTransaction is { } tx) cmd.Transaction = tx;

            await using var reader = await cmd.ExecuteReaderAsync(
                CommandBehavior.SchemaOnly, cancellationToken).ConfigureAwait(false);
            return reader.GetSchemaTable();
        }
        catch (FbException)
        {
            // The statement no longer prepares (the object was dropped, the transaction rolled back, …).
            // That is a perfectly ordinary "no provenance available", not an error worth a dialog.
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        finally
        {
            commandLock.Release();
        }
    }

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

    /// <summary>
    /// Streams every row of the statement one at a time (<see cref="IAsyncEnumerable{T}"/>) instead of
    /// materializing a <see cref="QueryResult"/> — the export path (a truncated preview's "All rows")
    /// consumes this straight into the file/clipboard writer, so there is no second buffer and it is
    /// <b>not</b> bounded by <see cref="ExecutionRequest.FullSafetyCeiling"/> (that ceiling is a grid-memory
    /// backstop; an export streams to disk and may legitimately exceed it). Runs on this executor's lane,
    /// attaches to the working transaction if one is active (so it reflects the user's uncommitted edits —
    /// the desired "current snapshot"), and holds <see cref="FirebirdConnectionService.CommandLock"/> for the
    /// whole enumeration (FbConnection is single-threaded). <see cref="OperationCanceledException"/> propagates
    /// on cancel; Firebird errors surface as <see cref="QueryExecutionException"/>.
    /// </summary>
    public IAsyncEnumerable<object?[]> StreamAsync(ExecutionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
        {
            throw new QueryExecutionException("Query is empty.");
        }
        return StreamIterator(request, cancellationToken);
    }

    private async IAsyncEnumerable<object?[]> StreamIterator(
        ExecutionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
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

        var commandLock = _transactionService?.CommandLock ?? _connectionService.CommandLock;
        FbConnection connection;
        try
        {
            connection = _transactionService?.RequireOpenConnection() ?? _connectionService.RequireOpenConnection();
        }
        catch (InvalidOperationException ex)
        {
            throw new QueryExecutionException(ex.Message, ex);
        }

        await commandLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // The charset guard can refuse both the statement text and any bound parameter, before the driver
            // encodes anything. `yield` forbids a try-with-catch around the enumeration itself, so the
            // translation is done here, around just the part that can raise it.
            //
            // ⚠ NO Release() in the catch: the outer `finally` below owns this lock. Releasing in both places
            // over-releases the semaphore, which leaks a permit for the rest of the process (gotcha #98/#120).
            FbCommand cmd;
            try
            {
                cmd = connection.CreateGuardedCommand(request.Sql);
                cmd.CommandTimeout = 0;
                if (_transactionService?.ActiveTransaction is { } tx)
                {
                    cmd.Transaction = tx;
                }
                if (request.Parameters is { Count: > 0 })
                {
                    foreach (var p in request.Parameters)
                    {
                        cmd.AddGuardedParameter(p.Name, p.Value ?? DBNull.Value);
                    }
                }
            }
            catch (CharsetRepresentationException ex)
            {
                throw new QueryExecutionException(ex.Message, ex);
            }

            await using var cmdScope = cmd.ConfigureAwait(false);

            // Cancellation must reach the SERVER, not just this task. A CancellationToken alone
            // cannot interrupt a round-trip that is blocked while Firebird computes (a heavy
            // join/aggregate produces no rows for a long time, so no await ever observes the
            // token) — Cancel then appears dead. FbCommand.Cancel() issues fb_cancel_operation,
            // which aborts the running statement server-side. Registering it on the token is what
            // makes Cancel take effect on the FIRST click. (gotcha: cancel-token != query-cancel)
            using var cancelReg = RegisterServerCancel(cmd, cancellationToken);

            // Open the reader in a try/catch so a prepare/execute FbException is wrapped; the `yield`
            // below stays OUTSIDE any catch (C# forbids yield inside try-with-catch), and per-row
            // ReadAsync gets its own catch for the rare mid-stream error.
            DbDataReader reader;
            try
            {
                reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (FbException ex)
            {
                throw new QueryExecutionException(FormatFbError(ex), ex);
            }

            await using (reader.ConfigureAwait(false))
            {
                if (reader.FieldCount == 0)
                {
                    yield break;
                }

                int fieldCount = reader.FieldCount;
                while (true)
                {
                    bool hasRow;
                    try
                    {
                        hasRow = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (FbException ex)
                    {
                        throw new QueryExecutionException(FormatFbError(ex), ex);
                    }

                    if (!hasRow) break;

                    var row = new object?[fieldCount];
                    for (int i = 0; i < fieldCount; i++)
                    {
                        row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    }
                    yield return row;
                }

                _transactionService?.NotifyStatementExecuted();
            }
        }
        finally
        {
            commandLock.Release();
        }
    }

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
            await using var cmd = connection.CreateGuardedCommand(sql);
            cmd.CommandTimeout = 0;
            if (_transactionService?.ActiveTransaction is { } tx)
            {
                cmd.Transaction = tx;
            }
            if (parameters is { Count: > 0 })
            {
                foreach (var p in parameters)
                {
                    cmd.AddGuardedParameter(p.Name, p.Value ?? DBNull.Value);
                }
            }

            // See RegisterServerCancel: without this, Cancel cannot interrupt a query that is
            // still executing server-side (no await observes the token until the first row).
            using var cancelReg = RegisterServerCancel(cmd, cancellationToken);

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
        catch (CharsetRepresentationException ex)
        {
            // Refused before the driver encoded anything — reported through this module's own exception so
            // every existing execution-error surface keeps working. Original preserved as InnerException.
            throw new QueryExecutionException(ex.Message, ex);
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

    /// <summary>
    /// Bridges .NET cancellation to Firebird's own statement cancellation. A
    /// <see cref="CancellationToken"/> is cooperative: it is only observed at an await that
    /// actually yields, so it CANNOT interrupt a query still executing on the server (a heavy
    /// join/aggregate returns no rows for a long time — nothing observes the token, and Cancel
    /// looks dead no matter how many times it is clicked). <c>FbCommand.Cancel()</c> sends
    /// <c>fb_cancel_operation</c>, which aborts the running statement server-side; the pending
    /// ExecuteReader/Read then faults and unwinds normally.
    /// <para>Returns a registration the caller disposes when the command completes, so the token
    /// never holds a reference to a disposed command. Best-effort: a Cancel() that races
    /// completion (or a server that refuses) must never surface as an error.</para>
    /// </summary>
    private static CancellationTokenRegistration RegisterServerCancel(FbCommand cmd, CancellationToken ct)
        => ct.CanBeCanceled
            ? ct.Register(static state =>
            {
                try { ((FbCommand)state!).Cancel(); }
                catch { /* already finished / not cancellable — the token still unwinds the task */ }
            }, cmd)
            : default;

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

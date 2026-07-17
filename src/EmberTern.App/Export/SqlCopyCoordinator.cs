using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Export;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;

namespace EmberTern.App.Export;

/// <summary>
/// Turns "the user right-clicked a row and picked Copy as INSERT" into SQL text — the one place the
/// lazy provenance capture, the resolver, and the statement builder are joined for a grid.
/// <para>
/// It exists so the SQL Editor, Table Data and Procedure Results all get the SAME answer to "can I copy
/// this, and if not why?" — the alternative is three grids each deciding for themselves, which is how
/// the two parallel clipboard paths already in this codebase came about.
/// </para>
/// <para>
/// <b>One mechanism, two ways to get the facts.</b> Every grid resolves through the same capture → cache →
/// resolve → warm-retry path; they differ only in <em>how signal A/B is obtained</em>, which each grid
/// supplies as a <see cref="ResultOrigin"/> capturer:
/// <list type="bullet">
/// <item>the SQL Editor re-prepares its executed statement (<c>OriginShape.Statement</c> — the AST decides
/// whether the server's provenance can be trusted);</item>
/// <item>Table Data captures its <c>SELECT *</c> schema and declares <c>OriginShape.DirectTable</c> — the
/// grid IS a table, so nothing is inferred and it is strictly safer.</item>
/// </list>
/// Both feed the identical <c>FirebirdResultOriginReader</c> → <c>FbDbType</c> → <c>SqlValueKind</c>
/// pipeline; there is no second, formatted-string type classifier.
/// </para>
/// <para>
/// <b>Provenance is captured once per result set and cached.</b> The first Copy-as-INSERT pays ~7 ms for
/// a <c>SchemaOnly</c> prepare; every later question about the same result is free. The coordinator's
/// lifetime IS the result set's — the SQL Editor calls <see cref="Reset"/> when a new statement runs;
/// Table Data never resets (a table tab's provenance is fixed for the tab's life).
/// </para>
/// </summary>
public sealed class SqlCopyCoordinator
{
    private readonly Func<ISqlMetadataProvider> _catalog;

    // How this grid obtains signals A + B. Invoked at most once per result set (its answer is cached in
    // _origin); a grid that cannot supply provenance returns ResultOrigin.None(reason) rather than null.
    private readonly Func<CancellationToken, Task<ResultOrigin>> _captureOrigin;

    private ResultOrigin? _origin;
    private TargetResolution? _resolution;

    /// <summary>Loads a table's columns into the catalog. Set by the host so a cold catalog is warmed and
    /// retried rather than reported as "no primary key" — the difference between "I haven't looked" and
    /// "there isn't one", which E2 keeps apart precisely so this can happen.</summary>
    public Func<string, Task>? WarmColumns { get; set; }

    /// <summary>The SQL Editor grid: provenance is derived by re-preparing the executed statement.</summary>
    /// <param name="executedSql">The statement whose rows the grid holds, or null when the grid is not a
    /// statement's result.</param>
    /// <param name="catalog">Signal C. Read lazily: it warms in the background, and a cold read must not
    /// be baked in as "this table has no primary key".</param>
    /// <param name="executor">The Data lane's executor, for the lazy capture.</param>
    public SqlCopyCoordinator(
        Func<string?> executedSql,
        Func<ISqlMetadataProvider> catalog,
        Func<FirebirdQueryExecutor?> executor)
    {
        _catalog = catalog;
        _captureOrigin = ct => CaptureFromStatementAsync(executedSql, executor, ct);
    }

    /// <summary>A grid that supplies its own provenance (Table Data captures a <c>DirectTable</c> origin —
    /// that grid IS a table, so signal B is satisfied by construction). The capturer is still invoked
    /// lazily and cached, so the ~7 ms schema read happens on the first Copy, never on data load.</summary>
    public SqlCopyCoordinator(
        Func<CancellationToken, Task<ResultOrigin>> captureOrigin,
        Func<ISqlMetadataProvider> catalog)
    {
        _catalog = catalog;
        _captureOrigin = captureOrigin;
    }

    /// <summary>Drops the cached provenance and verdict — call when the result set is replaced.</summary>
    public void Reset()
    {
        _origin = null;
        _resolution = null;
    }

    /// <summary>Whether <paramref name="format"/> can run on this result, and if not, why. Safe to call
    /// as often as a menu opens: the expensive part happens once.</summary>
    public async Task<FormatAvailability> GetAvailabilityAsync(
        ExportFormat format,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveAsync(cancellationToken).ConfigureAwait(true);
        return format switch
        {
            ExportFormat.InsertScript => SqlFormatAvailability.ForInsert(resolution),
            ExportFormat.UpdateScript => SqlFormatAvailability.ForUpdate(resolution),
            _ => FormatAvailability.Available,
        };
    }

    /// <summary>Builds the statement for one row, or returns the reason it cannot be built.</summary>
    public async Task<SqlStatementResult> BuildAsync(
        ExportFormat format,
        IReadOnlyList<object?> row,
        CancellationToken cancellationToken = default)
    {
        var resolution = await ResolveAsync(cancellationToken).ConfigureAwait(true);
        if (resolution is not TargetResolution.Resolved target)
            return SqlStatementResult.Unavailable(((TargetResolution.Unavailable)resolution).Reason);

        return format == ExportFormat.UpdateScript
            ? SqlStatementBuilder.BuildUpdate(target, row)
            : SqlStatementBuilder.BuildInsert(target, row);
    }

    private async Task<TargetResolution> ResolveAsync(CancellationToken cancellationToken)
    {
        _origin ??= await _captureOrigin(cancellationToken).ConfigureAwait(true);
        var origin = _origin;

        // The verdict is NOT cached alongside the origin when the catalog was cold: the origin is a
        // property of the result and never changes, but a CatalogNotLoaded verdict is transient — the
        // metadata warms in the background, and caching that answer would make the menu say "not loaded"
        // forever. Re-resolving is a dictionary lookup; only the capture is expensive.
        if (_resolution is { } cached && cached is not TargetResolution.Unavailable
            { Reason.Code: ExportUnavailableCode.CatalogNotLoaded })
        {
            return cached;
        }

        var resolution = ResultOriginResolver.Resolve(origin, _catalog());

        // Warm-then-retry. CatalogNotLoaded is the one refusal with an obvious remedy: the table is real,
        // we simply have not read its columns yet. Warming and asking again turns "CUSTOMERS's metadata
        // is still loading" into a real verdict on the first right-click, instead of making the user
        // click twice for no reason they could possibly infer.
        if (resolution is TargetResolution.Unavailable { Reason.Code: ExportUnavailableCode.CatalogNotLoaded } cold
            && WarmColumns is { } warm
            && cold.Reason.Names.Count > 0)
        {
            await warm(cold.Reason.Names[0]).ConfigureAwait(true);
            resolution = ResultOriginResolver.Resolve(origin, _catalog());
        }

        _resolution = resolution;
        return _resolution;
    }

    private static async Task<ResultOrigin> CaptureFromStatementAsync(
        Func<string?> executedSql,
        Func<FirebirdQueryExecutor?> executor,
        CancellationToken cancellationToken)
    {
        var sql = executedSql();
        var exec = executor();
        if (string.IsNullOrWhiteSpace(sql) || exec is null)
        {
            return ResultOrigin.None(
                ExportUnavailableReason.Of(ExportUnavailableCode.StatementNotUnderstood));
        }

        var schema = await exec.CaptureSchemaTableAsync(sql!, cancellationToken).ConfigureAwait(true);
        if (schema is null)
        {
            return ResultOrigin.None(
                ExportUnavailableReason.Of(ExportUnavailableCode.StatementNotUnderstood));
        }

        return new ResultOrigin(
            FirebirdResultOriginReader.ReadColumnOrigins(schema),
            StatementShapeReader.Read(sql!));
    }
}

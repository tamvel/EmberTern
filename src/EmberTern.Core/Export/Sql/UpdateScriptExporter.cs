using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlFormatter = EmberTern.Core.Sql.SqlFormatter;

namespace EmberTern.Core.Export.Sql;

/// <summary>
/// Serializes rows as <c>UPDATE</c> statements. The sibling of <see cref="InsertScriptExporter"/> and
/// just as thin: the verdict, the key verification, the statement and the literals all belong to the
/// shared pieces below it. It differs from INSERT in exactly one way that matters — it is
/// <b>unavailable strictly more often</b>, because it additionally needs a key proven to identify one
/// row.
/// <para>
/// Layout goes through the shared <see cref="SqlFormatter"/>, so output is lowercase like every other
/// piece of SQL EmberTern produces — one formatting language, not an export-local style.
/// </para>
/// </summary>
public sealed class UpdateScriptExporter : IExporter
{
    private readonly TargetResolution.Resolved _target;
    private readonly SqlLiteralLimits _limits;
    private readonly bool _format;

    public UpdateScriptExporter(
        TargetResolution.Resolved target,
        SqlLiteralLimits? limits = null,
        bool format = true)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _limits = limits ?? SqlLiteralLimits.Default;
        _format = format;
    }

    /// <summary>The reason the export stopped early, or null when it completed.</summary>
    public ExportUnavailableReason? Failure { get; private set; }

    public async Task<long> ExportAsync(
        IReadOnlyList<ExportColumn> columns,
        IAsyncEnumerable<object?[]> rows,
        IExportSink sink,
        IProgress<long>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(sink);

        long written = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var statement = SqlStatementBuilder.BuildUpdate(_target, row, _limits);
            if (!statement.IsBuilt)
            {
                Failure = statement.Reason;
                return written;
            }

            await sink.Writer.WriteLineAsync(Render(statement.Sql!)).ConfigureAwait(false);
            written++;
            progress?.Report(written);
        }

        return written;
    }

    private string Render(string sql)
    {
        if (!_format) return sql;
        var formatted = SqlFormatter.Format(sql);
        return string.IsNullOrWhiteSpace(formatted) ? sql : formatted.TrimEnd('\r', '\n');
    }
}

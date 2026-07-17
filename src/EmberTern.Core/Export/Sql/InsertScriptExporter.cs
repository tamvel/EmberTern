using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlFormatter = EmberTern.Core.Sql.SqlFormatter;

namespace EmberTern.Core.Export.Sql;

/// <summary>
/// Serializes rows as <c>INSERT</c> statements. Thin by construction: the verdict is
/// <see cref="ResultOriginResolver"/>'s, the statement is <see cref="SqlStatementBuilder"/>'s, the
/// literals are <see cref="SqlLiteralWriter"/>'s, and the layout is <see cref="SqlFormatter"/>'s. All
/// this class does is drive them per row and stream the result.
/// <para>
/// <b>Layout goes through the shared <see cref="SqlFormatter"/>, so generated SQL is lowercase</b> — the
/// same formatting language as everything else EmberTern produces, rather than a second export-local
/// style. Lowercase is semantically identical in Firebird (an unquoted identifier folds to uppercase
/// anyway), so nothing is lost but the look; and §0's checked lexeme-preservation invariant means the
/// formatter either reproduces every token or returns our input unchanged — it cannot corrupt generated
/// SQL. If UPPERCASE output is ever wanted, that is a <see cref="SqlFormatter"/> option applying
/// everywhere, never a switch here: an export-local casing rule is precisely how a second style is born.
/// </para>
/// <para>
/// <b>One statement per row, deliberately.</b> Firebird does <em>not</em> support the multi-row
/// <c>values (1,'a'),(2,'b')</c> constructor — measured, not assumed: it is a Dynamic SQL Error on FB5.
/// The portable multi-row form is <c>INSERT … SELECT … UNION ALL …</c>, which is a different statement
/// shape and a separate decision, not a formatting variant of this one.
/// </para>
/// </summary>
public sealed class InsertScriptExporter : IExporter
{
    private readonly TargetResolution.Resolved _target;
    private readonly SqlLiteralLimits _limits;
    private readonly bool _format;

    /// <param name="target">The proven target. An exporter is only ever constructed for a resolved
    /// result — an unavailable one never reaches here, because the format is gated first
    /// (<see cref="FormatAvailability"/>).</param>
    /// <param name="limits">Per-value ceilings.</param>
    /// <param name="format">Run each statement through <see cref="SqlFormatter"/>. Formatting is
    /// per-statement, so an N-row copy is N parses — fine for a clipboard copy of a few rows, and the
    /// escape hatch for a future large file export.</param>
    public InsertScriptExporter(
        TargetResolution.Resolved target,
        SqlLiteralLimits? limits = null,
        bool format = true)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _limits = limits ?? SqlLiteralLimits.Default;
        _format = format;
    }

    /// <summary>The reason the first row could not be rendered, when <see cref="ExportAsync"/> stopped
    /// early. Null when the export completed.</summary>
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
            var statement = SqlStatementBuilder.BuildInsert(_target, row, _limits);
            if (!statement.IsBuilt)
            {
                // A row whose values cannot be rendered faithfully stops the export rather than being
                // skipped: silently omitting a row from a copy is a quieter, worse failure than saying
                // nothing could be produced. The caller reports Failure.
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

        // §0 makes this free of risk: the formatter's checked invariant preserves every lexeme or
        // returns the input unchanged, so a formatting failure degrades to canonical SQL, never to
        // corrupted SQL.
        var formatted = SqlFormatter.Format(sql);
        return string.IsNullOrWhiteSpace(formatted) ? sql : formatted.TrimEnd('\r', '\n');
    }
}

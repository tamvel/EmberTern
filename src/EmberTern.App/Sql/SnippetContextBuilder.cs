using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Templates;

namespace EmberTern.App.Sql;

/// <summary>
/// Loads exactly the metadata a dropped object needs and materializes a
/// <see cref="SnippetContext"/> for generation. Called <b>after</b> the user picks a
/// template — the drop flyout is built from <see cref="SqlTemplateRegistry.DescriptorsForKind"/>
/// with no reader call, so the action list appears instantly; only on pick does this
/// builder hit the catalog (once per object, via the app's existing caches).
/// <para>
/// The reader calls are injected as delegates (the codebase's "parameterize the worker"
/// seam) so this is unit-testable without a live Firebird: production wires them to
/// <c>FirebirdTableDetailReader</c> + the column cache; tests pass fakes.
/// </para>
/// </summary>
public sealed class SnippetContextBuilder
{
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<FieldInfo>>> _loadColumns;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<ConstraintInfo>>> _loadConstraints;
    private readonly Func<string, int, CancellationToken, Task<IReadOnlyList<ProcedureParameterInfo>>> _loadParameters;
    private readonly Func<string, CancellationToken, Task<FunctionSignatureInfo?>> _loadFunction;
    private readonly SnippetOptions _options;

    public SnippetContextBuilder(
        Func<string, CancellationToken, Task<IReadOnlyList<FieldInfo>>> loadColumns,
        Func<string, CancellationToken, Task<IReadOnlyList<ConstraintInfo>>> loadConstraints,
        Func<string, int, CancellationToken, Task<IReadOnlyList<ProcedureParameterInfo>>> loadParameters,
        Func<string, CancellationToken, Task<FunctionSignatureInfo?>> loadFunction,
        SnippetOptions? options = null)
    {
        _loadColumns = loadColumns ?? throw new ArgumentNullException(nameof(loadColumns));
        _loadConstraints = loadConstraints ?? throw new ArgumentNullException(nameof(loadConstraints));
        _loadParameters = loadParameters ?? throw new ArgumentNullException(nameof(loadParameters));
        _loadFunction = loadFunction ?? throw new ArgumentNullException(nameof(loadFunction));
        _options = options ?? SnippetOptions.Default;
    }

    public async Task<SnippetContext> BuildAsync(
        MetadataObject obj,
        SnippetInsertionContext insertion = SnippetInsertionContext.PlainSql,
        CancellationToken cancellationToken = default)
    {
        switch (obj.Kind)
        {
            case MetadataObjectKind.Table:
            {
                var columns = await _loadColumns(obj.Name, cancellationToken).ConfigureAwait(false);
                var constraints = await _loadConstraints(obj.Name, cancellationToken).ConfigureAwait(false);
                return new SnippetContext
                {
                    Object = obj,
                    Insertion = insertion,
                    Options = _options,
                    Columns = columns,
                    PrimaryKey = PrimaryKeyFromConstraints(constraints),
                };
            }

            case MetadataObjectKind.View:
            {
                // Views have no PK/constraints and the MVP view templates (SELECT * /
                // SELECT columns) never need one — load columns only.
                var columns = await _loadColumns(obj.Name, cancellationToken).ConfigureAwait(false);
                return new SnippetContext
                {
                    Object = obj,
                    Insertion = insertion,
                    Options = _options,
                    Columns = columns,
                };
            }

            case MetadataObjectKind.Procedure:
            {
                var inputs = await _loadParameters(obj.Name, 0, cancellationToken).ConfigureAwait(false);
                var outputs = await _loadParameters(obj.Name, 1, cancellationToken).ConfigureAwait(false);
                return new SnippetContext
                {
                    Object = obj,
                    Insertion = insertion,
                    Options = _options,
                    Inputs = inputs,
                    Outputs = outputs,
                    // Cheap proxy for "selectable" — a proc that returns rows has output
                    // params. Avoids fetching the body just to regex for SUSPEND; the
                    // generated SELECT is valid SQL regardless (see the loading-behaviour
                    // decision for Phase 2).
                    ProcedureIsSelectable = outputs.Count > 0,
                };
            }

            case MetadataObjectKind.Function:
            {
                var signature = await _loadFunction(obj.Name, cancellationToken).ConfigureAwait(false);
                return new SnippetContext
                {
                    Object = obj,
                    Insertion = insertion,
                    Options = _options,
                    Function = signature,
                };
            }

            default:
                // Generators (and any other kind) need no detailed metadata.
                return new SnippetContext { Object = obj, Insertion = insertion, Options = _options };
        }
    }

    /// <summary>
    /// Extract the primary-key column names from the PRIMARY KEY constraint (never the
    /// per-field flag — see architecture gotcha #103). Empty when the table has no PK.
    /// </summary>
    public static IReadOnlyList<string> PrimaryKeyFromConstraints(IEnumerable<ConstraintInfo> constraints)
    {
        var pk = constraints.FirstOrDefault(
            c => string.Equals(c.ConstraintType.Trim(), "PRIMARY KEY", StringComparison.OrdinalIgnoreCase));
        if (pk is null || string.IsNullOrWhiteSpace(pk.Fields))
            return Array.Empty<string>();

        return pk.Fields
            .Split(',')
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .ToArray();
    }
}

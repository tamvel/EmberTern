using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>
/// The <b>ONE</b> answer to two questions about a <c>FROM</c> entry: <em>which columns does this source
/// contribute?</em> and <em>do we know them yet?</em>
///
/// <para>⭐⭐ <b>A selectable procedure's columns are its OUTPUT parameters.</b> Firebird lets a procedure
/// stand where a table stands (<c>FROM MY_PROC(:a) y</c>), and then the routine's <c>RETURNS</c> list *is*
/// <c>y</c>'s column set — the same fact the catalog stores in a different table. Nothing in the language
/// layer knew that: <c>ResolveColumn</c> asked <see cref="ISqlMetadataProvider.GetColumns"/>, which for a
/// procedure is legitimately empty, so <b>every</b> <c>y.column</c> came back unresolved.</para>
///
/// <para>⚠ The visible half was a false <c>ET0002 "unknown column"</c> on a procedure that compiles (user
/// report 2026-08-12). The quieter half was worse, and is why this type exists rather than a patch in the
/// diagnostics engine: <b>completion after <c>y.</c> offered zero items</b>, and Quick Info and navigation
/// had nothing to show either. A diagnostics-side fix would have silenced the squiggle and left the other
/// three broken — the defect is in <em>resolution</em>, so the fix belongs where resolution happens.</para>
///
/// <para>⭐ Hence one owner. Three call sites independently asked "the columns of X" —
/// <c>SemanticBinder.ResolveColumn</c>, <c>CompletionEngine</c>'s dot path, and its implicit-single-table
/// path — and a fourth asked "are they known" (<c>DiagnosticsEngine</c>). They all route here, so the
/// answer cannot diverge again (the one-responsibility-one-owner rule from
/// <c>editor-language-expansion.md</c> §9.1).</para>
///
/// <para>⚠ The decision is taken from the <b>resolved target symbol</b>, never from a fresh name lookup.
/// The binder has already committed to one interpretation of the name via <c>ResolveObject</c>, and in
/// Firebird a table and a procedure <em>may</em> share a name — so re-deriving the kind here could disagree
/// with the symbol the rest of the model is built on. ⭐ It is also why this does not key on the AST's
/// <c>RoutineTableReference</c>, which was the tempting structural signal: Firebird admits
/// <c>FROM MY_NOARG_PROC</c> with no parentheses, which parses as a plain <c>TableReference</c> and is
/// indistinguishable from a table in the text (that node's own docstring says so). The catalog knows; the
/// text does not.</para>
/// </summary>
internal static class FromSourceColumns
{
    /// <summary>
    /// True when a <c>FROM</c> entry's resolved target is a procedure — i.e. a selectable procedure, whose
    /// contributed columns are its <see cref="ParameterDirection.Output"/> parameters.
    /// </summary>
    public static bool IsSelectableProcedure(Symbol? target)
        => target is SchemaObjectSymbol { Kind: SymbolKind.Procedure };

    /// <summary>The columns <paramref name="name"/> contributes as a <c>FROM</c> source.</summary>
    public static IReadOnlyList<ColumnMetadata> Of(ISqlMetadataProvider metadata, string name, Symbol? target)
        => IsSelectableProcedure(target) ? Outputs(metadata, name) : metadata.GetColumns(name);

    /// <summary>
    /// Whether the snapshot has LOADED what <see cref="Of"/> reads — so an absent name means "no such
    /// column" rather than "not warmed yet". Both sides are warmed lazily, so both need the question asked.
    /// </summary>
    public static bool AreKnown(ISqlMetadataProvider metadata, string name, Symbol? target)
        => IsSelectableProcedure(target)
            ? metadata.KnowsRoutineParameters(name)
            : metadata.KnowsColumns(name);

    // A routine's OUTPUT parameters, presented as columns. ⛔ Only outputs: an input parameter is an
    // argument of the invocation, never a column of the result — offering P_ID_MELDUNEK as a column of
    // `y` would be a wrong answer, not merely a noisy one.
    private static IReadOnlyList<ColumnMetadata> Outputs(ISqlMetadataProvider metadata, string routine)
    {
        var all = metadata.GetRoutineParameters(routine);
        if (all.Count == 0) return System.Array.Empty<ColumnMetadata>();

        List<ColumnMetadata>? cols = null;
        foreach (var p in all)
        {
            if (p.Direction != ParameterDirection.Output) continue;
            cols ??= new List<ColumnMetadata>(all.Count);
            cols.Add(new ColumnMetadata(p.Name, p.Type)
            {
                Nullable = p.Nullable,
                Description = p.Description,
            });
        }
        return cols ?? (IReadOnlyList<ColumnMetadata>)System.Array.Empty<ColumnMetadata>();
    }
}

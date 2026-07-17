using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>The variables a statement reads and the variables it may write (spec §3.5). Names are folded
/// (Firebird's catalog convention), matching the frame's variable names.</summary>
public sealed record ReadWriteSet(IReadOnlyList<string> Reads, IReadOnlyList<string> Writes);

/// <summary>
/// Computes a statement's read/write set from the <see cref="SemanticModel"/> — the read/write-set-driven
/// injection of spec §3.5. It consumes the binder's resolved references (Architecture rule #1/#2: never
/// re-parse, never re-resolve — the binder is the one name resolver, and it records every local reference
/// with its name). Pure Core.
/// <para>
/// <b>Reads</b> = the variables/parameters the statement references (inject their current values, R4). Over-
/// inclusion is safe (§3.5: "correct, chattier") — a variable appearing only as an assignment target is
/// harmless to inject (it is overwritten), and R1 skips a <c>NULL</c> value anyway.
/// </para>
/// <para>
/// <b>Writes</b> = the variables the statement may mutate (return them for write-back, R4). A single
/// statement — absent a sub-routine call that mutates a captured outer variable — changes only variables it
/// references, so the reads are a correct superset; the assignment case is narrowed precisely (the target
/// only). A control-flow condition (<c>IF</c>/<c>WHILE</c>) assigns nothing, so its write set is empty.
/// </para>
/// <para>
/// <b>Deliberately NOT here:</b> the <b>transitive fixpoint over the sub-routine call graph</b> belongs to
/// D9 (the flagship, where local routines become frames), and the sub-routine <i>declarations</i> are always
/// carried in full by the harness regardless (R5), so nothing is silently lost meanwhile. The §3.5
/// <b>inject-all-in-scope</b> fallback is exposed as the named primitive <see cref="InScopeLocals"/> for a
/// caller that genuinely cannot compute a precise set (e.g. a Watch on an arbitrary expression the model did
/// not bind — D5), rather than an auto-branch here: the binder never emits an unresolved-<i>local</i> signal
/// (an unrecognised bare identifier is not a frame variable — it is a column/function/typo and is correctly
/// dropped), so an auto-fallback would be unreachable dead code.
/// </para>
/// </summary>
public static class ReadWriteSetAnalyzer
{
    /// <summary>Computes the read/write set of <paramref name="statement"/> against <paramref name="model"/>.</summary>
    public static ReadWriteSet Analyze(SqlNode statement, SemanticModel model)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(model);

        int start = statement.Start;
        int end = statement.Start + statement.Length;

        var localRefs = new List<SymbolReference>();
        foreach (var r in model.References)
        {
            if (r.Span.Start < start || r.Span.End > end || r.IsDefinition)
            {
                continue; // outside the statement, or a declaration (not a use)
            }
            if (r.Role is ReferenceRole.Variable or ReferenceRole.Parameter)
            {
                localRefs.Add(r);
            }
        }

        var reads = Distinct(NamesOf(localRefs));
        var writes = ComputeWrites(statement, localRefs, reads);
        return new ReadWriteSet(reads, writes);
    }

    /// <summary>Every PSQL local (variable / parameter) in scope at <paramref name="offset"/> — the §3.5
    /// "inject all in-scope" fallback as a named primitive, for a caller that cannot compute a precise read
    /// set (e.g. a Watch on an arbitrary expression). Correct, chattier; never a guess.</summary>
    public static IReadOnlyList<string> InScopeLocals(SemanticModel model, int offset)
    {
        ArgumentNullException.ThrowIfNull(model);
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in model.SymbolsInScope(offset))
        {
            if (s.Kind is SymbolKind.Variable or SymbolKind.Parameter && seen.Add(s.Name))
            {
                names.Add(s.Name);
            }
        }
        return names;
    }

    private static IReadOnlyList<string> ComputeWrites(
        SqlNode statement, List<SymbolReference> localRefs, IReadOnlyList<string> reads)
    {
        // A control-flow condition assigns nothing.
        if (statement is IfStatement or WhileStatement)
        {
            return Array.Empty<string>();
        }

        // An assignment writes exactly its target — the leftmost local l-value (`V = …`). A `NEW.col = …`
        // has no local l-value, so it writes no frame variable (the record field is a trigger concern, D10).
        if (statement is PsqlLeafStatement { Kind: PsqlLeafKind.Assignment })
        {
            SymbolReference? leftmost = null;
            foreach (var r in localRefs)
            {
                if (leftmost is null || r.Span.Start < leftmost.Span.Start)
                {
                    leftmost = r;
                }
            }
            return leftmost is null ? Array.Empty<string>() : new[] { NameOf(leftmost) };
        }

        // Any other statement (DML, SELECT … INTO, EXECUTE): a single statement changes only variables it
        // references, so the reads are a correct superset (§3.5 "chattier but correct"). Precise narrowing
        // (only the actually-assigned targets) is a perf refinement, not a correctness requirement.
        return reads;
    }

    private static IEnumerable<string> NamesOf(IEnumerable<SymbolReference> refs)
    {
        foreach (var r in refs)
        {
            yield return NameOf(r);
        }
    }

    private static string NameOf(SymbolReference r)
        => r.Symbol is { } s ? s.Name : r.Text.ToUpperInvariant();

    private static IReadOnlyList<string> Distinct(IEnumerable<string> names)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in names)
        {
            if (seen.Add(n)) result.Add(n);
        }
        return result;
    }
}

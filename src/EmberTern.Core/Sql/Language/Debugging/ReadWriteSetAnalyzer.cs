using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language;
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
/// <b>The transitive fixpoint over the sub-routine call graph (D9 seam b Part 2).</b> When a
/// <see cref="SubroutineCatalog"/> of in-scope local sub-routines is supplied and the statement calls one, the
/// callee's transitively-referenced <b>captured</b> variables (those visible at the call site) are folded into
/// both reads and writes — so a step-<i>over</i> of a local call injects the outer values the callee captures
/// and reads its mutations back (spec §3.5/§6.2b). Without it, a call with direct arguments narrows to just
/// those arguments and silently drops the callee's hidden captures (a §F divergence). Over-inclusion stays safe
/// (a returned-but-unchanged variable writes back its own value; an injected-but-unused value is harmless). The
/// walk reuses the binder's references (span-collected per callee body — inherently transitive for a
/// <i>nested</i> sub-routine, whose body lies within its parent's span) and recurses into called catalog
/// siblings, with a visited set for mutual recursion. Call detection is a conservative name-membership check
/// against the AST-authoritative catalog — not a variable resolver (Architecture rule #2 governs variable
/// references, which still come only from the binder; the AST models the call graph but the binder does not yet
/// resolve local calls as symbols — the seam-a-part-1 binder note).
/// </para>
/// <para>
/// The §3.5 <b>inject-all-in-scope</b> fallback is exposed as the named primitive <see cref="InScopeLocals"/>
/// for a caller that genuinely cannot compute a precise set (e.g. a Watch on an arbitrary expression the model
/// did not bind — D5), rather than an auto-branch here.
/// </para>
/// </summary>
public static class ReadWriteSetAnalyzer
{
    /// <summary>Computes the read/write set of <paramref name="statement"/> against <paramref name="model"/>.
    /// When <paramref name="subroutines"/> is supplied (D9 seam b Part 2), a call to an in-scope local
    /// sub-routine folds in that callee's transitively-captured variables (the fixpoint above); the default
    /// (null) is the direct-reference set only.</summary>
    public static ReadWriteSet Analyze(SqlNode statement, SemanticModel model, SubroutineCatalog? subroutines = null)
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

        if (subroutines is null || subroutines.IsEmpty)
        {
            return new ReadWriteSet(reads, writes);
        }
        return FoldTransitiveCaptures(statement, model, subroutines, reads, writes);
    }

    // Folds the transitive captured read/write set of any in-scope local sub-routine this statement calls into
    // the statement's set (spec §3.5 — the sub-routine call-graph fixpoint). Additive: the direct set is kept,
    // and only captured variables visible at the call site are added (a callee's own params/locals are out of
    // scope here and drop out). No call → the direct set unchanged.
    private static ReadWriteSet FoldTransitiveCaptures(
        SqlNode statement, SemanticModel model, SubroutineCatalog subroutines,
        IReadOnlyList<string> reads, IReadOnlyList<string> writes)
    {
        var called = CalledSubroutines(statement, subroutines);
        if (called.Count == 0)
        {
            return new ReadWriteSet(reads, writes);
        }

        var captured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in called)
        {
            CollectTransitiveReferencedVars(name, subroutines, model, captured, visited);
        }

        // Keep only the captures visible at the call site — a callee's own params/locals are not in scope here.
        var inScope = new HashSet<string>(InScopeLocals(model, statement.Start), StringComparer.OrdinalIgnoreCase);

        var newReads = new List<string>(reads);
        var newWrites = new List<string>(writes);
        var seenR = new HashSet<string>(reads, StringComparer.OrdinalIgnoreCase);
        var seenW = new HashSet<string>(writes, StringComparer.OrdinalIgnoreCase);
        foreach (var name in captured)
        {
            if (!inScope.Contains(name)) continue;
            if (seenR.Add(name)) newReads.Add(name); // inject the captured outer value the callee reads
            if (seenW.Add(name)) newWrites.Add(name); // return the captured outer value the callee may write
        }
        return new ReadWriteSet(newReads, newWrites);
    }

    // Every variable/parameter a sub-routine references transitively: its own body's references (span-collected
    // — this already covers a NESTED sub-routine, whose body lies within this one's span) plus the references
    // of every catalog sibling it calls (recursively). The visited set terminates mutual recursion. Over-
    // approximate on purpose (§3.5 "chattier but correct"): the reads superset is a safe write superset too.
    private static void CollectTransitiveReferencedVars(
        string subName, SubroutineCatalog catalog, SemanticModel model,
        HashSet<string> captured, HashSet<string> visited)
    {
        if (!visited.Add(subName)) return;
        if (!catalog.TryGet(subName, out var sub) || sub.Body is null) return;

        int bodyStart = sub.Body.Start;
        int bodyEnd = sub.Body.End;
        foreach (var r in model.References)
        {
            if (r.IsDefinition) continue;
            if (r.Role is not (ReferenceRole.Variable or ReferenceRole.Parameter)) continue;
            if (r.Span.Start < bodyStart || r.Span.End > bodyEnd) continue;
            captured.Add(NameOf(r));
        }

        foreach (var nested in CalledSubroutines(sub, catalog))
        {
            CollectTransitiveReferencedVars(nested, catalog, model, captured, visited);
        }
    }

    // The in-scope local sub-routines a node's token stream names (a call site). A conservative name-membership
    // check against the AST-authoritative catalog — a bare identifier matching a known local sub-routine name is
    // a call to it (over-detection, e.g. a coincidental match, only adds that callee's captures, which is safe).
    // This detects both an EXECUTE PROCEDURE proc call and an expression-embedded function call, which the AST
    // does not model deeper and the binder does not resolve as call symbols.
    private static IReadOnlyList<string> CalledSubroutines(SqlNode node, SubroutineCatalog catalog)
    {
        List<string>? result = null;
        HashSet<string>? seen = null;
        foreach (var tok in TokensOf(node))
        {
            if (tok.Kind is not (TokenKind.Identifier or TokenKind.Keyword)) continue;
            var name = tok.Text.ToUpperInvariant();
            if (!catalog.Contains(name)) continue;
            seen ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!seen.Add(name)) continue;
            (result ??= new List<string>()).Add(name);
        }
        return (IReadOnlyList<string>?)result ?? Array.Empty<string>();
    }

    private static IReadOnlyList<SqlToken> TokensOf(SqlNode node) => node switch
    {
        SqlStatement s => s.Tokens,
        PsqlStatement p => p.Tokens,
        _ => Array.Empty<SqlToken>(),
    };

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

using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// The local sub-routines in scope at a point in a routine body (Stage X / D9 seam b Part 2) — the authoritative
/// name → <see cref="SubroutineDeclaration"/> map, built from the AST (<see cref="BlockStatement.LocalRoutines"/>
/// up the lexical chain), that the read/write-set <b>transitive fixpoint</b> walks. It is <b>scope</b>, not a
/// name resolver: it says "these names are in-scope local sub-routines whose bodies I can read", so a statement
/// that calls one can have that callee's captured read/write set folded in (spec §3.5 — a fixpoint over the
/// sub-routine call graph). Variable/parameter <i>references</i> still come exclusively from the binder
/// (Architecture rule #2); this only carves out the call graph the AST already models but the binder does not
/// yet resolve as call symbols (the D9 seam-a-part-1 binder note). Only a real definition (a non-null
/// <see cref="SubroutineDeclaration.Body"/>) with a name is catalogued — a forward declaration is skipped.
/// </summary>
public sealed class SubroutineCatalog
{
    private readonly Dictionary<string, SubroutineDeclaration> _byName;

    /// <summary>The empty catalog (a routine with no in-scope local sub-routines — the D2–D8 case).</summary>
    public static SubroutineCatalog Empty { get; } = new(Array.Empty<SubroutineDeclaration>());

    /// <summary>Builds the catalog from a set of local sub-routine declarations (nearest scope first — an inner
    /// declaration shadows a like-named outer one, so the first occurrence of a name wins).</summary>
    public SubroutineCatalog(IEnumerable<SubroutineDeclaration> routines)
    {
        ArgumentNullException.ThrowIfNull(routines);
        _byName = new Dictionary<string, SubroutineDeclaration>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in routines)
        {
            if (r.Body is null || string.IsNullOrEmpty(r.Name)) continue; // a forward declaration is not runnable
            _byName.TryAdd(r.Name!, r); // first-seen (nearest scope) wins — an inner shadows a like-named outer
        }
    }

    /// <summary>True when the catalog holds no runnable sub-routine (no fixpoint work to do).</summary>
    public bool IsEmpty => _byName.Count == 0;

    /// <summary>True when <paramref name="name"/> is an in-scope local sub-routine (folded match).</summary>
    public bool Contains(string name) => _byName.ContainsKey(name);

    /// <summary>Resolves an in-scope local sub-routine by name.</summary>
    public bool TryGet(string name, out SubroutineDeclaration declaration) => _byName.TryGetValue(name, out declaration!);
}

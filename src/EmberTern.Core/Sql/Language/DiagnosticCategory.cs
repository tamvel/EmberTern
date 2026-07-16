namespace EmberTern.Core.Sql.Language;

/// <summary>
/// The category of a semantic <see cref="Diagnostic"/> — Stage 7 (Diagnostics). Every category is a
/// <b>conservative</b>, node-precise finding computed by the <see cref="DiagnosticsEngine"/> as a pure
/// client of the Semantic Model (the project's "prefer silence over false positives" rule). One flat
/// discriminator, mirroring <see cref="Semantics.SymbolKind"/>, so callers/tests/UI switch on it and a
/// future filter/quick-fix maps off it.
/// <para>Only the categories the engine emits in Stage 7 / Milestone S1 are defined here; later
/// milestones extend the set (S2: <c>InsertCountMismatch</c> / <c>AmbiguousColumn</c>; S6: PSQL-specific)
/// as the deepened AST makes them expressible — see
/// <see href="../../docs/design/editor-stage7-diagnostics.md">editor-stage7-diagnostics.md</see> §6/§11.</para>
/// </summary>
public enum DiagnosticCategory
{
    /// <summary>No category — the neutral default for a diagnostic that predates categorisation (e.g.
    /// the parser-recovery channel, empty by design at this grammar depth). Never emitted by the
    /// <see cref="DiagnosticsEngine"/>.</summary>
    None = 0,

    /// <summary>A referenced schema object (table/view/procedure/function/…) that does not exist in the
    /// live metadata snapshot. Requires a metadata connection — never emitted against
    /// <see cref="Semantics.EmptyMetadataProvider"/>.</summary>
    UnknownObject,

    /// <summary>A qualified column reference (<c>alias.col</c> / <c>NEW.col</c>) whose table resolved
    /// correctly but whose column does not exist on that table. Requires a metadata connection.</summary>
    UnknownColumn,

    /// <summary>A PSQL local-variable reference (a <c>:name</c> in a routine body) that binds to no
    /// declared variable. Local scope only — needs no connection.</summary>
    UnresolvedVariable,

    /// <summary>A PSQL parameter reference in a routine body that binds to no declared parameter. Local
    /// scope only — needs no connection.</summary>
    UnresolvedParameter,

    // ── S2 ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>A bare (unqualified) column reference that a metadata-resolved query can match to a column
    /// on ≥2 of its FROM tables — the binder cannot pick one, so the column is ambiguous. Requires a
    /// metadata connection.</summary>
    AmbiguousColumn,

    /// <summary>An <c>INSERT INTO t (cols) VALUES (vals)</c> whose explicit column list and single VALUES
    /// row have different lengths. A definite error at the statement — needs no connection (a pure
    /// count of the two lists).</summary>
    InsertCountMismatch,

    // ── S6 (PSQL-specific) ─────────────────────────────────────────────────────────────────────

    /// <summary>A cursor operation (<c>OPEN</c> / <c>FETCH</c> / <c>CLOSE</c>) naming a cursor that no
    /// in-scope <c>DECLARE … CURSOR</c> / <c>FOR … AS CURSOR</c> declares. Local scope only — needs no
    /// connection.</summary>
    UnknownCursor,

    /// <summary>A <c>SUSPEND</c> in a context where it can never be valid — a trigger (never selectable)
    /// or a PSQL function (returns a scalar via <c>RETURN</c>). Conservative: a procedure / EXECUTE BLOCK
    /// may be selectable, so <c>SUSPEND</c> there is not flagged. Needs no connection.</summary>
    SuspendOutsideSelectable,
}

namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>
/// What a <see cref="Symbol"/> denotes. The first group are <b>schema objects</b> (defined in the
/// database catalog / by a DDL statement in the script); the second group are <b>local
/// declarations</b> introduced by the script itself (aliases, PSQL variables/parameters, CTEs,
/// cursors, trigger record aliases). One flat enum keeps callers, tests, and a future LSP mapping
/// able to switch on a single discriminator.
/// </summary>
public enum SymbolKind
{
    // ── Schema objects (from metadata or a DDL definition) ───────────────────────────────────
    Table,
    View,
    SystemTable,
    Procedure,
    Function,
    Trigger,
    Domain,
    Exception,
    Sequence,
    Role,
    Package,
    Index,

    /// <summary>A column of a table or view.</summary>
    Column,

    // ── Local declarations (introduced by the script) ────────────────────────────────────────

    /// <summary>A FROM/JOIN entry — a table/view/CTE/derived-table bound to a (possibly implicit)
    /// alias. This is the <c>k</c> in <c>FROM KONTRAHENT k</c>.</summary>
    TableReference,

    /// <summary>A PSQL local variable (<c>DECLARE VARIABLE …</c> / <c>DECLARE name type …</c>).</summary>
    Variable,

    /// <summary>A procedure / function / EXECUTE BLOCK parameter (input or output).</summary>
    Parameter,

    /// <summary>A common-table-expression name (<c>WITH name AS (…)</c>).</summary>
    Cte,

    /// <summary>A PSQL cursor (<c>DECLARE … CURSOR …</c> or <c>FOR SELECT … AS CURSOR c</c>).</summary>
    Cursor,

    /// <summary>A trigger record alias — <c>NEW</c> / <c>OLD</c> — bound to the trigger's table.</summary>
    RecordAlias,

    /// <summary>A trigger boolean context predicate — <c>INSERTING</c> / <c>UPDATING</c> /
    /// <c>DELETING</c>. Valid only inside a trigger body; carries no target (unlike a record
    /// alias, it has no columns), it is purely a language construct to be recognised and coloured.</summary>
    TriggerPredicate,

    /// <summary>A symbol whose kind could not be determined.</summary>
    Unknown,
}

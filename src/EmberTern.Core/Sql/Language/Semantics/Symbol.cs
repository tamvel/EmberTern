using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language.Semantics;

/// <summary>
/// The universal symbol — one entry in the semantic model for anything a name can denote: a schema
/// object, a column, a table alias, a PSQL variable/parameter, a CTE, a cursor, a NEW/OLD record
/// alias. Carries as many facts as are known; unknown facts are <c>null</c>. Concrete subclasses add
/// the structured links a given kind needs (a column's owning table, an alias's target, a
/// parameter's direction, …).
/// <para>
/// This is the stable, reusable, LSP-ready currency of the semantic layer: navigation, find-refs,
/// rename, quick-info, diagnostics, completion, and any future AI all consume <see cref="Symbol"/>s.
/// The <em>binder</em> that produces them may deepen over time (token-walk today, deep-AST later)
/// without changing this public shape.
/// </para>
/// <para>Immutable except for <see cref="Scope"/>, which the binder wires up as it declares the
/// symbol into its scope. Pure — no Avalonia, no Firebird driver.</para>
/// </summary>
public class Symbol
{
    public Symbol(SymbolKind kind, string name)
    {
        Kind = kind;
        Name = name ?? string.Empty;
    }

    /// <summary>What this symbol denotes.</summary>
    public SymbolKind Kind { get; }

    /// <summary>The symbol's name. Unquoted names are stored folded to upper-case (Firebird's
    /// catalog convention); quoted identifiers keep their literal case.</summary>
    public string Name { get; }

    /// <summary>Where the symbol is declared in the script, when it is declared there (a table
    /// alias, a PSQL variable, a CTE …). <c>null</c> for a schema object that exists only in the
    /// database catalog and is merely referenced by the script.</summary>
    public TextSpan? DeclarationSpan { get; init; }

    /// <summary>The top-level AST statement the declaration lives in, when applicable. The finest
    /// AST granularity today is the statement (Etap 2 "statement skeleton"); when the parser
    /// deepens, this can point at a finer node without changing the field.</summary>
    public SqlStatement? DeclaringStatement { get; init; }

    /// <summary>The scope this symbol was declared into. <c>null</c> for a catalog schema object
    /// (which belongs to no script scope). Set by the binder.</summary>
    public Scope? Scope { get; internal set; }

    /// <summary>Formatted SQL data type when known (e.g. <c>INTEGER</c>, <c>VARCHAR(50)</c>,
    /// <c>NUMERIC(15,2)</c>), else <c>null</c>.</summary>
    public string? DataType { get; init; }

    /// <summary>Backing domain name when the type is a domain, else <c>null</c>.</summary>
    public string? Domain { get; init; }

    /// <summary><c>true</c> = declared NULLable, <c>false</c> = NOT NULL, <c>null</c> = unknown.</summary>
    public bool? Nullable { get; init; }

    /// <summary>Default-value expression text when known, else <c>null</c>.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Human description / comment when known, else <c>null</c>.</summary>
    public string? Description { get; init; }

    /// <summary>Owner / creator when known, else <c>null</c>.</summary>
    public string? Owner { get; init; }

    public override string ToString() => $"{Kind} {Name}";
}

/// <summary>A database schema object referenced (or defined) by the script — a table, view,
/// procedure, function, trigger, domain, exception, sequence, role, package, or index.</summary>
public sealed class SchemaObjectSymbol : Symbol
{
    public SchemaObjectSymbol(SymbolKind kind, string name) : base(kind, name) { }
}

/// <summary>A column of a table or view.</summary>
public sealed class ColumnSymbol : Symbol
{
    public ColumnSymbol(string name) : base(SymbolKind.Column, name) { }

    /// <summary>The name of the table/view that owns the column.</summary>
    public string? OwningTable { get; init; }

    public bool IsPrimaryKey { get; init; }
    public bool IsForeignKey { get; init; }

    /// <summary>The referenced table for a foreign-key column, else <c>null</c>.</summary>
    public string? ForeignKeyTable { get; init; }

    public bool IsComputed { get; init; }
    public bool IsIdentity { get; init; }
}

/// <summary>
/// A FROM/JOIN entry — the binding at the heart of column resolution. <see cref="Symbol.Name"/> is
/// the name the rest of the query uses to qualify columns (an explicit alias, or the table name
/// itself when no alias is given). <see cref="TargetName"/> is the underlying table/view/CTE it
/// refers to; <see cref="Target"/> is the resolved symbol for it when known.
/// <para>Example: <c>FROM KONTRAHENT k</c> → Name = <c>K</c>, <see cref="TargetName"/> = <c>KONTRAHENT</c>.</para>
/// </summary>
public sealed class TableReferenceSymbol : Symbol
{
    public TableReferenceSymbol(string name) : base(SymbolKind.TableReference, name) { }

    /// <summary>The underlying table/view/CTE name this reference resolves to (folded case), or
    /// <c>null</c> for a derived table (an aliased subquery) with no single source name.</summary>
    public string? TargetName { get; init; }

    /// <summary>The resolved symbol for <see cref="TargetName"/> (a <see cref="SchemaObjectSymbol"/>
    /// or <see cref="CteSymbol"/>) when it could be resolved, else <c>null</c>.</summary>
    public Symbol? Target { get; init; }

    /// <summary><c>true</c> when <see cref="Symbol.Name"/> is an explicit alias distinct from
    /// <see cref="TargetName"/> (e.g. <c>k</c> for <c>KONTRAHENT</c>); <c>false</c> when the table
    /// is referenced by its own name.</summary>
    public bool IsAlias { get; init; }

    /// <summary><c>true</c> when this reference is a derived table — an aliased subquery in the FROM
    /// clause — rather than a named table/view/CTE. Its <see cref="TargetName"/>/<see cref="Target"/>
    /// are <c>null</c> (columns come from the subquery projection, not the catalog).</summary>
    public bool IsDerived { get; init; }
}

/// <summary>A PSQL local variable.</summary>
public sealed class VariableSymbol : Symbol
{
    public VariableSymbol(string name) : base(SymbolKind.Variable, name) { }
}

/// <summary>Whether a routine parameter is an input or an output.</summary>
public enum ParameterDirection
{
    Input,
    Output,
}

/// <summary>A procedure / function / EXECUTE BLOCK parameter.</summary>
public sealed class ParameterSymbol : Symbol
{
    public ParameterSymbol(string name) : base(SymbolKind.Parameter, name) { }

    public ParameterDirection Direction { get; init; } = ParameterDirection.Input;
}

/// <summary>A common-table-expression name.</summary>
public sealed class CteSymbol : Symbol
{
    public CteSymbol(string name) : base(SymbolKind.Cte, name) { }

    /// <summary>The explicitly-declared column names (<c>WITH c (a, b) AS …</c>), when given.</summary>
    public IReadOnlyList<string> Columns { get; init; } = System.Array.Empty<string>();

    /// <summary>The scope of the CTE's inner query, when bound.</summary>
    public Scope? QueryScope { get; init; }
}

/// <summary>A PSQL cursor.</summary>
public sealed class CursorSymbol : Symbol
{
    public CursorSymbol(string name) : base(SymbolKind.Cursor, name) { }
}

/// <summary>A trigger record alias — <c>NEW</c> or <c>OLD</c> — bound to the trigger's table so
/// <c>NEW.col</c> / <c>OLD.col</c> resolve to that table's columns.</summary>
public sealed class RecordAliasSymbol : Symbol
{
    public RecordAliasSymbol(string name) : base(SymbolKind.RecordAlias, name) { }

    /// <summary>The table whose columns this record alias exposes.</summary>
    public string? TargetTable { get; init; }
}

/// <summary>A trigger boolean context predicate — <c>INSERTING</c> / <c>UPDATING</c> /
/// <c>DELETING</c>. Declared into the trigger's routine-body scope so a bare occurrence resolves and
/// is recognised as a language construct (coloured like the other trigger context variables). It has
/// no members and no navigation target — unlike <see cref="RecordAliasSymbol"/>.</summary>
public sealed class TriggerPredicateSymbol : Symbol
{
    public TriggerPredicateSymbol(string name) : base(SymbolKind.TriggerPredicate, name) { }
}

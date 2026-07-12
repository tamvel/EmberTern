using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.QuickInfo;

/// <summary>
/// Produces <see cref="QuickInfo"/> — a modern "quick documentation" of the symbol under the caret —
/// from a <see cref="SemanticModel"/> and a caret offset (Etap 6, design §5.12 / §8A / P9). Like the
/// completion and signature engines it is a pure Core client of the language front-end: it reads
/// <b>only</b> the model (the identifier the caret is on, the <see cref="Symbol"/> it resolved to,
/// and the metadata snapshot for that symbol's members) — never a fresh text scan and never a
/// parallel fetch path (§22.0). The App renders the result as the Ctrl-hover tooltip and the
/// completion detail pane.
/// <para>Read-only, so §0 (never lose information) holds by construction — Quick Info never modifies
/// code. Error-tolerant: it never throws; an unresolved offset yields <c>null</c>.</para>
/// </summary>
public static class QuickInfoEngine
{
    /// <summary>The quick-info for the identifier at <paramref name="offset"/>, or <c>null</c> when
    /// the offset is not on an identifier that resolved to a symbol. Never throws.</summary>
    public static QuickInfo? GetQuickInfo(SemanticModel model, int offset)
    {
        if (model is null) return null;
        var reference = model.ReferenceAt(offset);
        var symbol = reference?.Symbol;
        if (symbol is null) return null;
        return ForSymbol(symbol, model.Metadata);
    }

    /// <summary>Builds the quick-info for a resolved <paramref name="symbol"/>, using
    /// <paramref name="metadata"/> to fill member lists (a table's columns, a routine's parameters)
    /// when they are loaded. Never returns <c>null</c> for a real symbol — at worst a header-only
    /// card. Pure; never throws.</summary>
    public static QuickInfo ForSymbol(Symbol symbol, ISqlMetadataProvider? metadata = null)
    {
        metadata ??= EmptyMetadataProvider.Instance;
        return symbol switch
        {
            ColumnSymbol c => ForColumn(c),
            TableReferenceSymbol t => ForTableReference(t, metadata),
            RecordAliasSymbol r => ForRecordAlias(r, metadata),
            CteSymbol cte => ForCte(cte),
            VariableSymbol v => ForVariable(v),
            ParameterSymbol p => ForParameter(p),
            CursorSymbol cur => ForLocal(cur, "Cursor"),
            SchemaObjectSymbol o => ForSchemaObject(o, metadata),
            _ => ForGeneric(symbol),
        };
    }

    // ── Columns (the headline case — "check a column without opening its table") ──────────────

    private static QuickInfo ForColumn(ColumnSymbol c)
    {
        var header = string.IsNullOrEmpty(c.DataType) ? c.Name : $"{c.Name} : {c.DataType}";

        var facts = new List<QuickInfoFact>();
        if (!string.IsNullOrEmpty(c.OwningTable)) facts.Add(new QuickInfoFact("Table", c.OwningTable!));
        if (!string.IsNullOrEmpty(c.Domain)) facts.Add(new QuickInfoFact("Domain", c.Domain!));
        if (c.Nullable is { } nn) facts.Add(new QuickInfoFact("Nullability", nn ? "NULL" : "NOT NULL"));
        if (!string.IsNullOrEmpty(c.DefaultValue)) facts.Add(new QuickInfoFact("Default", c.DefaultValue!));
        if (c.IsPrimaryKey) facts.Add(new QuickInfoFact("Key", "PRIMARY KEY"));
        if (c.IsForeignKey)
        {
            facts.Add(new QuickInfoFact("Key",
                string.IsNullOrEmpty(c.ForeignKeyTable) ? "FOREIGN KEY" : $"FOREIGN KEY → {c.ForeignKeyTable}"));
        }
        if (c.IsIdentity) facts.Add(new QuickInfoFact("Generated", "Identity"));
        if (c.IsComputed) facts.Add(new QuickInfoFact("Generated", "Computed"));

        return new QuickInfo(SymbolKind.Column, header, c.Description, facts);
    }

    // ── Schema objects (tables/views/routines/domains/exceptions/…) ────────────────────────────

    private static QuickInfo ForSchemaObject(SchemaObjectSymbol o, ISqlMetadataProvider metadata)
    {
        var facts = new List<QuickInfoFact>();
        if (!string.IsNullOrEmpty(o.Owner)) facts.Add(new QuickInfoFact("Owner", o.Owner!));

        var members = new List<QuickInfoMember>();
        switch (o.Kind)
        {
            case SymbolKind.Table:
            case SymbolKind.View:
            case SymbolKind.SystemTable:
                AddColumns(members, metadata, o.Name);
                break;

            case SymbolKind.Procedure:
            case SymbolKind.Function:
                AddRoutineParameters(members, metadata, o.Name);
                break;
        }

        return new QuickInfo(o.Kind, o.Name, o.Description, facts, members);
    }

    // ── FROM/JOIN aliases → the underlying table's info ────────────────────────────────────────

    private static QuickInfo ForTableReference(TableReferenceSymbol t, ISqlMetadataProvider metadata)
    {
        if (t.IsDerived)
        {
            var derivedHeader = string.IsNullOrEmpty(t.Name) ? "(derived table)" : $"{t.Name} (derived table)";
            return new QuickInfo(SymbolKind.TableReference, derivedHeader);
        }

        var target = t.TargetName ?? t.Name;
        var header = t.IsAlias && !string.IsNullOrEmpty(target) && !string.Equals(t.Name, target)
            ? $"{t.Name} → {target}"
            : target;

        var facts = new List<QuickInfoFact>();
        if (t.Target is { } tgt)
        {
            facts.Add(new QuickInfoFact("Kind", KindLabel(tgt.Kind)));
            if (!string.IsNullOrEmpty(tgt.Owner)) facts.Add(new QuickInfoFact("Owner", tgt.Owner!));
        }

        var members = new List<QuickInfoMember>();
        if (!string.IsNullOrEmpty(target)) AddColumns(members, metadata, target);

        var description = (t.Target as SchemaObjectSymbol)?.Description;
        return new QuickInfo(SymbolKind.TableReference, header, description, facts, members);
    }

    // ── NEW / OLD → the trigger table's columns ────────────────────────────────────────────────

    private static QuickInfo ForRecordAlias(RecordAliasSymbol r, ISqlMetadataProvider metadata)
    {
        var header = string.IsNullOrEmpty(r.TargetTable) ? r.Name : $"{r.Name} → {r.TargetTable}";
        var members = new List<QuickInfoMember>();
        if (!string.IsNullOrEmpty(r.TargetTable)) AddColumns(members, metadata, r.TargetTable!);
        return new QuickInfo(SymbolKind.RecordAlias, header, description: null, facts: null, members);
    }

    // ── CTE ────────────────────────────────────────────────────────────────────────────────────

    private static QuickInfo ForCte(CteSymbol cte)
    {
        var members = new List<QuickInfoMember>();
        foreach (var col in cte.Columns) members.Add(new QuickInfoMember(col, QuickInfoMemberGroup.Column));
        var facts = new[] { new QuickInfoFact("Kind", "Common table expression") };
        return new QuickInfo(SymbolKind.Cte, cte.Name, description: null, facts, members);
    }

    // ── PSQL locals ────────────────────────────────────────────────────────────────────────────

    private static QuickInfo ForVariable(VariableSymbol v)
    {
        var header = string.IsNullOrEmpty(v.DataType) ? v.Name : $"{v.Name} : {v.DataType}";
        var facts = new List<QuickInfoFact> { new("Kind", "Variable") };
        if (!string.IsNullOrEmpty(v.DefaultValue)) facts.Add(new QuickInfoFact("Default", v.DefaultValue!));
        return new QuickInfo(SymbolKind.Variable, header, v.Description, facts);
    }

    private static QuickInfo ForParameter(ParameterSymbol p)
    {
        var header = string.IsNullOrEmpty(p.DataType) ? p.Name : $"{p.Name} : {p.DataType}";
        var facts = new List<QuickInfoFact>
        {
            new("Kind", p.Direction == ParameterDirection.Output ? "Output parameter" : "Input parameter"),
        };
        if (p.Nullable == false) facts.Add(new QuickInfoFact("Nullability", "NOT NULL"));
        if (!string.IsNullOrEmpty(p.DefaultValue)) facts.Add(new QuickInfoFact("Default", p.DefaultValue!));
        return new QuickInfo(SymbolKind.Parameter, header, p.Description, facts);
    }

    private static QuickInfo ForLocal(Symbol symbol, string kindLabel)
    {
        var facts = new[] { new QuickInfoFact("Kind", kindLabel) };
        return new QuickInfo(symbol.Kind, symbol.Name, symbol.Description, facts);
    }

    private static QuickInfo ForGeneric(Symbol symbol)
    {
        var facts = new[] { new QuickInfoFact("Kind", KindLabel(symbol.Kind)) };
        return new QuickInfo(symbol.Kind, symbol.Name, symbol.Description, facts);
    }

    // ── Member helpers ─────────────────────────────────────────────────────────────────────────

    private static void AddColumns(List<QuickInfoMember> members, ISqlMetadataProvider metadata, string table)
    {
        foreach (var col in metadata.GetColumns(table))
        {
            var text = string.IsNullOrEmpty(col.Type) ? col.Name : $"{col.Name} {col.Type}";
            members.Add(new QuickInfoMember(text, QuickInfoMemberGroup.Column));
        }
    }

    private static void AddRoutineParameters(List<QuickInfoMember> members, ISqlMetadataProvider metadata, string routine)
    {
        foreach (var p in metadata.GetRoutineParameters(routine))
        {
            var text = string.IsNullOrEmpty(p.Type) ? p.Name : $"{p.Name} {p.Type}";
            var group = p.Direction == ParameterDirection.Output
                ? QuickInfoMemberGroup.Returns
                : QuickInfoMemberGroup.Parameter;
            members.Add(new QuickInfoMember(text, group));
        }
    }

    private static string KindLabel(SymbolKind kind) => kind switch
    {
        SymbolKind.Table => "Table",
        SymbolKind.View => "View",
        SymbolKind.SystemTable => "System table",
        SymbolKind.Procedure => "Procedure",
        SymbolKind.Function => "Function",
        SymbolKind.Trigger => "Trigger",
        SymbolKind.Domain => "Domain",
        SymbolKind.Exception => "Exception",
        SymbolKind.Sequence => "Generator",
        SymbolKind.Role => "Role",
        SymbolKind.Package => "Package",
        SymbolKind.Index => "Index",
        SymbolKind.Column => "Column",
        SymbolKind.TableReference => "Table reference",
        SymbolKind.Variable => "Variable",
        SymbolKind.Parameter => "Parameter",
        SymbolKind.Cte => "Common table expression",
        SymbolKind.Cursor => "Cursor",
        SymbolKind.RecordAlias => "Record alias",
        _ => "Object",
    };
}

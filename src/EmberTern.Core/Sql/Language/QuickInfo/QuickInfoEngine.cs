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
        if (!string.IsNullOrEmpty(c.OwningTable)) facts.Add(new QuickInfoFact(QuickInfoMessages.Table, c.OwningTable!));
        if (!string.IsNullOrEmpty(c.Domain)) facts.Add(new QuickInfoFact(QuickInfoMessages.Domain, c.Domain!));
        if (c.Nullable is { } nn) facts.Add(new QuickInfoFact(QuickInfoMessages.Nullability, nn ? "NULL" : "NOT NULL"));
        if (!string.IsNullOrEmpty(c.DefaultValue)) facts.Add(new QuickInfoFact(QuickInfoMessages.Default, c.DefaultValue!));
        if (c.IsPrimaryKey) facts.Add(new QuickInfoFact(QuickInfoMessages.Key, "PRIMARY KEY"));
        if (c.IsForeignKey)
        {
            facts.Add(new QuickInfoFact(QuickInfoMessages.Key,
                string.IsNullOrEmpty(c.ForeignKeyTable) ? "FOREIGN KEY" : $"FOREIGN KEY → {c.ForeignKeyTable}"));
        }
        if (c.IsIdentity) facts.Add(new QuickInfoFact(QuickInfoMessages.Generated, "Identity"));
        if (c.IsComputed) facts.Add(new QuickInfoFact(QuickInfoMessages.Generated, "Computed"));

        return new QuickInfo(SymbolKind.Column, header, c.Description, facts);
    }

    // ── Schema objects (tables/views/routines/domains/exceptions/…) ────────────────────────────

    private static QuickInfo ForSchemaObject(SchemaObjectSymbol o, ISqlMetadataProvider metadata)
    {
        // The rich, warmed facts (description, function return type, trigger header) live on the
        // snapshot's ObjectMetadata — an in-memory lookup, never a DB query at display time (Package 5).
        var meta = metadata.FindObject(o.Name);
        var description = string.IsNullOrEmpty(meta?.Description) ? o.Description : meta!.Description;
        var owner = string.IsNullOrEmpty(meta?.Owner) ? o.Owner : meta!.Owner;

        var facts = new List<QuickInfoFact>();
        if (!string.IsNullOrEmpty(owner)) facts.Add(new QuickInfoFact(QuickInfoMessages.Owner, owner!));

        var members = new List<QuickInfoMember>();
        switch (o.Kind)
        {
            case SymbolKind.Table:
            case SymbolKind.View:
            case SymbolKind.SystemTable:
                AddColumns(members, metadata, o.Name);
                AddColumnCounts(facts, metadata, o.Name);
                break;

            case SymbolKind.Procedure:
                AddRoutineParameters(members, metadata, o.Name);
                AddRoutineCounts(facts, metadata, o.Name, isFunction: false);
                break;

            case SymbolKind.Function:
                AddRoutineParameters(members, metadata, o.Name);
                AddRoutineCounts(facts, metadata, o.Name, isFunction: true);
                if (!string.IsNullOrEmpty(meta?.ReturnType)) facts.Add(new QuickInfoFact(QuickInfoMessages.Returns, meta!.ReturnType!));
                break;

            case SymbolKind.Trigger:
                AddTriggerFacts(facts, meta?.Trigger);
                break;

            case SymbolKind.Sequence:
                AddGeneratorFacts(facts, meta?.Generator);
                break;
        }

        return new QuickInfo(o.Kind, o.Name, description, facts, members);
    }

    // Table/view summary counts, derived from the already-warmed column list (no new query). Skipped
    // until the columns are warmed, so a not-yet-loaded table never shows a misleading "0 columns".
    private static void AddColumnCounts(List<QuickInfoFact> facts, ISqlMetadataProvider metadata, string table)
    {
        var cols = metadata.GetColumns(table);
        if (cols.Count == 0) return;
        int pk = 0, fk = 0;
        foreach (var c in cols)
        {
            if (c.IsPrimaryKey) pk++;
            if (c.IsForeignKey) fk++;
        }
        facts.Add(new QuickInfoFact(QuickInfoMessages.Columns, Num(cols.Count)));
        if (pk > 0) facts.Add(new QuickInfoFact(QuickInfoMessages.PrimaryKey, pk == 1 ? "1 column" : $"{Num(pk)} columns"));
        if (fk > 0) facts.Add(new QuickInfoFact(QuickInfoMessages.ForeignKeys, Num(fk)));
    }

    // Routine parameter summary. Functions count only inputs (the output is the return type, shown
    // separately); procedures show in/out. Derived from the warmed parameter list — no new query.
    private static void AddRoutineCounts(List<QuickInfoFact> facts, ISqlMetadataProvider metadata, string routine, bool isFunction)
    {
        var ps = metadata.GetRoutineParameters(routine);
        if (ps.Count == 0) return;
        int inputs = 0, outputs = 0;
        foreach (var p in ps)
        {
            if (p.Direction == ParameterDirection.Output) outputs++;
            else inputs++;
        }
        if (isFunction)
        {
            facts.Add(new QuickInfoFact(QuickInfoMessages.Parameters, Num(inputs)));
        }
        else
        {
            facts.Add(new QuickInfoFact(QuickInfoMessages.Parameters, outputs > 0 ? $"{Num(inputs)} in, {Num(outputs)} out" : $"{Num(inputs)} in"));
        }
    }

    // Trigger header facts (Package 5, Stage C): table, timing + events, position, active state.
    private static void AddTriggerFacts(List<QuickInfoFact> facts, TriggerDetail? trigger)
    {
        if (trigger is null) return;
        if (!string.IsNullOrEmpty(trigger.Table)) facts.Add(new QuickInfoFact(QuickInfoMessages.Table, trigger.Table!));

        var events = new List<string>(3);
        if (trigger.FiresInsert) events.Add("INSERT");
        if (trigger.FiresUpdate) events.Add("UPDATE");
        if (trigger.FiresDelete) events.Add("DELETE");
        var timing = trigger.IsBefore ? "BEFORE" : "AFTER";
        facts.Add(new QuickInfoFact(QuickInfoMessages.Fires, events.Count > 0 ? $"{timing} {string.Join(" OR ", events)}" : timing));

        facts.Add(new QuickInfoFact(QuickInfoMessages.Position, Num(trigger.Position)));
        facts.Add(new QuickInfoFact(QuickInfoMessages.State, trigger.Active ? "Active" : "Inactive"));
    }

    // Generator/sequence static facts (Package 5): the defining increment and start value. Shown only
    // when non-default (increment ≠ 1, start ≠ 0) so a plain generator isn't cluttered with "1"/"0";
    // the dynamic current value is deliberately never shown (it would be stale the moment it's read).
    private static void AddGeneratorFacts(List<QuickInfoFact> facts, GeneratorDetail? generator)
    {
        if (generator is null) return;
        if (generator.Increment != 1) facts.Add(new QuickInfoFact(QuickInfoMessages.Increment, Num(generator.Increment)));
        if (generator.StartValue != 0) facts.Add(new QuickInfoFact(QuickInfoMessages.Start, Num(generator.StartValue)));
    }

    private static string Num(long n) => n.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
            facts.Add(new QuickInfoFact(QuickInfoMessages.Kind, KindLabel(tgt.Kind)));
            if (!string.IsNullOrEmpty(tgt.Owner)) facts.Add(new QuickInfoFact(QuickInfoMessages.Owner, tgt.Owner!));
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
        var facts = new[] { new QuickInfoFact(QuickInfoMessages.Kind, "Common table expression") };
        return new QuickInfo(SymbolKind.Cte, cte.Name, description: null, facts, members);
    }

    // ── PSQL locals ────────────────────────────────────────────────────────────────────────────

    private static QuickInfo ForVariable(VariableSymbol v)
    {
        var header = string.IsNullOrEmpty(v.DataType) ? v.Name : $"{v.Name} : {v.DataType}";
        var facts = new List<QuickInfoFact> { new(QuickInfoMessages.Kind, "Variable") };
        if (!string.IsNullOrEmpty(v.DefaultValue)) facts.Add(new QuickInfoFact(QuickInfoMessages.Default, v.DefaultValue!));
        return new QuickInfo(SymbolKind.Variable, header, v.Description, facts);
    }

    private static QuickInfo ForParameter(ParameterSymbol p)
    {
        var header = string.IsNullOrEmpty(p.DataType) ? p.Name : $"{p.Name} : {p.DataType}";
        var facts = new List<QuickInfoFact>
        {
            new(QuickInfoMessages.Kind, p.Direction == ParameterDirection.Output ? "Output parameter" : "Input parameter"),
        };
        if (p.Nullable == false) facts.Add(new QuickInfoFact(QuickInfoMessages.Nullability, "NOT NULL"));
        if (!string.IsNullOrEmpty(p.DefaultValue)) facts.Add(new QuickInfoFact(QuickInfoMessages.Default, p.DefaultValue!));
        return new QuickInfo(SymbolKind.Parameter, header, p.Description, facts);
    }

    private static QuickInfo ForLocal(Symbol symbol, string kindLabel)
    {
        var facts = new[] { new QuickInfoFact(QuickInfoMessages.Kind, kindLabel) };
        return new QuickInfo(symbol.Kind, symbol.Name, symbol.Description, facts);
    }

    private static QuickInfo ForGeneric(Symbol symbol)
    {
        var facts = new[] { new QuickInfoFact(QuickInfoMessages.Kind, KindLabel(symbol.Kind)) };
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

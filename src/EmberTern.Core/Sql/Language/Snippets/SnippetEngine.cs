using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Templates;

namespace EmberTern.Core.Sql.Language.Snippets;

/// <summary>
/// One keyword-prefix live template — <c>if</c> → an IF/THEN skeleton, <c>execute</c> → an EXECUTE
/// BLOCK skeleton, etc. Reuses the existing <see cref="SqlSnippet"/> / <see cref="SqlPlaceholder"/>
/// primitives (tab-stop offsets); the App turns them into AvaloniaEdit interactive snippets.
/// </summary>
public sealed class SnippetTemplate
{
    private readonly Func<SqlSnippet> _generate;

    internal SnippetTemplate(string keyword, string displayText, bool psqlOnly, Func<SqlSnippet> generate)
    {
        Keyword = keyword;
        DisplayText = displayText;
        PsqlOnly = psqlOnly;
        _generate = generate;
    }

    /// <summary>The trigger word typed to surface the snippet (also the completion filter key,
    /// e.g. <c>if</c>, <c>for select</c>, <c>create procedure</c>).</summary>
    public string Keyword { get; }

    /// <summary>A short one-line shape shown in the completion list (e.g. <c>if (…) then … end</c>).</summary>
    public string DisplayText { get; }

    /// <summary>True for templates only valid inside a PSQL body (IF/WHILE/FOR/BEGIN/CASE/DECLARE) —
    /// gated to a <see cref="ScopeKind.RoutineBody"/>/<see cref="ScopeKind.Block"/> scope.</summary>
    public bool PsqlOnly { get; }

    /// <summary>Builds a fresh snippet (text + tab-stops). Pure.</summary>
    public SqlSnippet Create() => _generate();
}

/// <summary>
/// The keyword live-template engine — Etap 5 / M8 (design §5.11 / §22). Given a
/// <see cref="SemanticModel"/> and a caret offset it returns the templates valid there: PSQL
/// control-flow inside a routine body, top-level DDL/EXECUTE-BLOCK skeletons elsewhere. Pure Core;
/// the App surfaces the results as completion items and expands the picked one with Tab-between-stops
/// (§0: expansion only inserts text — it never rewrites surrounding code).
/// <para>This is a <b>parallel</b> path to the shipped object-driven drag-drop templates
/// (<c>SqlSnippetDropTarget</c> + the <c>ISqlTemplate</c> registry), which stay untouched; it only
/// reuses the <see cref="SqlSnippet"/> primitives (§22.3).</para>
/// </summary>
public static class SnippetEngine
{
    /// <summary>Every template, regardless of context (for tests / enumeration).</summary>
    public static IReadOnlyList<SnippetTemplate> AllTemplates { get; } = BuildTemplates();

    /// <summary>The templates applicable at <paramref name="offset"/>: PSQL control-flow when the
    /// caret is inside a routine body / block, the top-level (DDL / EXECUTE BLOCK) set otherwise.
    /// Never throws; returns an empty list for a null model.</summary>
    public static IReadOnlyList<SnippetTemplate> GetSnippets(SemanticModel model, int offset)
    {
        if (model is null) return Array.Empty<SnippetTemplate>();

        var scopeKind = model.ScopeAt(offset).Kind;
        bool inPsql = scopeKind is ScopeKind.RoutineBody or ScopeKind.Block;

        var result = new List<SnippetTemplate>();
        foreach (var t in AllTemplates)
        {
            if (t.PsqlOnly == inPsql) result.Add(t);
        }
        return result;
    }

    // ── Template library ─────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<SnippetTemplate> BuildTemplates() => new[]
    {
        // PSQL control flow (routine body / block only).
        Psql("if", "if (…) then begin … end", () => new SqlSnippetBuilder()
            .Add("if (").Placeholder("condition", "condition").Add(") then\nbegin\n  ")
            .Placeholder("statement", "statement").Add(";\nend").Build()),

        Psql("while", "while (…) do begin … end", () => new SqlSnippetBuilder()
            .Add("while (").Placeholder("condition", "condition").Add(") do\nbegin\n  ")
            .Placeholder("statement", "statement").Add(";\nend").Build()),

        Psql("for select", "for select … into … do begin … end", () => new SqlSnippetBuilder()
            .Add("for select ").Placeholder("columns", "columns")
            .Add("\n  from ").Placeholder("source", "source")
            .Add("\n  into ").Placeholder("targets", ":variable")
            .Add("\ndo\nbegin\n  ").Placeholder("statement", "statement").Add(";\nend").Build()),

        Psql("begin", "begin … end", () => new SqlSnippetBuilder()
            .Add("begin\n  ").Placeholder("statement", "statement").Add(";\nend").Build()),

        Psql("case", "case when … then … else … end", () => new SqlSnippetBuilder()
            .Add("case\n  when ").Placeholder("condition", "condition").Add(" then\n    ")
            .Placeholder("thenBody", "statement").Add(";\n  else\n    ")
            .Placeholder("elseBody", "statement").Add(";\nend").Build()),

        Psql("declare", "declare variable … …;", () => new SqlSnippetBuilder()
            .Add("declare variable ").Placeholder("name", "name").Add(" ")
            .Placeholder("type", "type").Add(";").Build()),

        // Top-level DDL / EXECUTE BLOCK skeletons (outside a PSQL body).
        Ddl("execute", "execute block as begin … end", () => new SqlSnippetBuilder()
            .Add("execute block\nas\nbegin\n  ").Placeholder("statement", "statement").Add(";\nend").Build()),

        Ddl("create procedure", "create procedure … as begin … end", () => new SqlSnippetBuilder()
            .Add("create procedure ").Placeholder("name", "name").Add("\nas\nbegin\n  ")
            .Placeholder("statement", "statement").Add(";\nend").Build()),

        Ddl("create function", "create function … returns … as begin … end", () => new SqlSnippetBuilder()
            .Add("create function ").Placeholder("name", "name").Add("\nreturns ")
            .Placeholder("type", "type").Add("\nas\nbegin\n  ")
            .Placeholder("statement", "statement").Add(";\nend").Build()),

        Ddl("create trigger", "create trigger … for … before insert as begin … end", () => new SqlSnippetBuilder()
            .Add("create trigger ").Placeholder("name", "name").Add(" for ").Placeholder("table", "table")
            .Add("\nbefore insert\nas\nbegin\n  ").Placeholder("statement", "statement").Add(";\nend").Build()),

        Ddl("create exception", "create exception … '…';", () => new SqlSnippetBuilder()
            .Add("create exception ").Placeholder("name", "name").Add(" '").Placeholder("message", "message").Add("';").Build()),

        Ddl("create domain", "create domain … as …;", () => new SqlSnippetBuilder()
            .Add("create domain ").Placeholder("name", "name").Add(" as ").Placeholder("type", "type").Add(";").Build()),

        Ddl("create index", "create index … on … (…);", () => new SqlSnippetBuilder()
            .Add("create index ").Placeholder("name", "name").Add(" on ").Placeholder("table", "table")
            .Add(" (").Placeholder("column", "column").Add(");").Build()),
    };

    private static SnippetTemplate Psql(string keyword, string display, Func<SqlSnippet> gen)
        => new(keyword, display, psqlOnly: true, gen);

    private static SnippetTemplate Ddl(string keyword, string display, Func<SqlSnippet> gen)
        => new(keyword, display, psqlOnly: false, gen);
}

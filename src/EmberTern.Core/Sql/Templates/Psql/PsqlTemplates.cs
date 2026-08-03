using EmberTern.Core.Metadata;
using static EmberTern.Core.Sql.Templates.TemplateHelpers;

namespace EmberTern.Core.Sql.Templates.Psql;

// ⭐⭐ EVERY SCAFFOLD IN THIS FILE IS OFFERED IN EVERY EDITOR (`SnippetContexts.Any`) — ratified by the user
// (2026-08-03) after asking twice for `FOR SELECT … INTO` in the SQL Editor, in their words: *"To, że jest
// oznaczony jako Plain SQL, nie jest wystarczającym argumentem do ukrywania tego szablonu, ponieważ w SQL
// Editorze normalnie tworzy się także EXECUTE BLOCK, CREATE PROCEDURE, CREATE TRIGGER i inne konstrukcje PSQL."*
//
// ⚠ These were all `PsqlOnly`, which hid them from the editor where most PSQL actually gets written. Two narrower
// answers were tried and both were wrong: widening only the one template that had been reported (an exception, not
// a rule), and deriving the context from whether the drop offset sits inside a `BEGIN … END` (which fails for the
// case that matters — a scaffold is what you reach for to START a body, so there is no block yet). The reasoning
// applies to all of them equally, so the rule is one line and has no exceptions.
//
// ⚠ `SnippetInsertionContext` / `SnippetContexts.PsqlOnly` remain in the model with **no template using them**.
// That is deliberate rather than dead: the catalog already declares user/plugin templates as the next step, and a
// third-party scaffold may legitimately need to be body-only. ⛔ Do not re-gate a built-in with it to "give it a
// consumer" — that would undo the decision above.

/// <summary>
/// <c>FOR SELECT &lt;cols&gt; FROM t INTO :cols DO BEGIN … END</c> — a cursor loop over a
/// table, generated from its columns.
/// </summary>
public sealed class TableForSelectTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.for-select", "FOR SELECT", SqlTemplateGroup.PsqlScaffold, 90,
            new[] { MetadataObjectKind.Table }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var nl = ctx.Options.NewLine;
        var cols = ctx.Columns;
        var b = new SqlSnippetBuilder().Add("FOR").Add(nl).Add("    SELECT").Add(nl);
        for (var i = 0; i < cols.Count; i++)
        {
            b.Add("        ").Add(Q(cols[i].Name));
            if (i < cols.Count - 1) b.Add(",");
            b.Add(nl);
        }
        b.Add("    FROM ").Add(Q(ctx.Object.Name)).Add(nl).Add("    INTO").Add(nl);
        for (var i = 0; i < cols.Count; i++)
        {
            b.Add("        ").Param(ctx, cols[i].Name);
            if (i < cols.Count - 1) b.Add(",");
            b.Add(nl);
        }
        b.Add("DO").Add(nl).Add("BEGIN").Add(nl).Add(nl).Add("END");
        return b.Build();
    }
}

/// <summary>
/// <c>DECLARE VARIABLE V_col TYPE OF COLUMN t.col;</c> per column — declares locals typed
/// to a table's columns.
/// </summary>
public sealed class TableDeclareVariablesTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("table.declare-vars", "DECLARE VARIABLES", SqlTemplateGroup.PsqlScaffold, 100,
            new[] { MetadataObjectKind.Table }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.Columns.Count > 0;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var nl = ctx.Options.NewLine;
        var table = Q(ctx.Object.Name);
        var b = new SqlSnippetBuilder();
        for (var i = 0; i < ctx.Columns.Count; i++)
        {
            var col = ctx.Columns[i].Name;
            b.Add("DECLARE VARIABLE ")
                .Placeholder(col, ctx.Options.VarPrefix + col.Trim())
                .Add(" TYPE OF COLUMN ").Add(table).Add(".").Add(Q(col)).Add(";");
            if (i < ctx.Columns.Count - 1) b.Add(nl);
        }
        return b.Build();
    }
}

/// <summary>
/// <c>FOR SELECT &lt;outputs&gt; FROM p(:in) INTO :outputs DO BEGIN … END</c> — a cursor loop
/// over a selectable procedure. Needs output parameters.
///
/// <para>⭐ <b>This is the template the user asked for twice</b> (2026-08-03) — the reason the file-level rule
/// above exists. It is what a developer reaches for <b>to START writing</b> a report body, which is precisely why
/// gating it on already being inside a <c>BEGIN … END</c> was the wrong answer: there is no block yet at the
/// moment it is wanted.</para>
/// </summary>
public sealed class ProcedureForSelectFromTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("procedure.for-select-from", "FOR SELECT … INTO", SqlTemplateGroup.PsqlScaffold, 130,
            new[] { MetadataObjectKind.Procedure }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.ProcedureIsSelectable;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var nl = ctx.Options.NewLine;
        var outs = ctx.Outputs;
        var b = new SqlSnippetBuilder().Add("FOR").Add(nl).Add("    SELECT").Add(nl);
        for (var i = 0; i < outs.Count; i++)
        {
            b.Add("        ").Add(Q(outs[i].Name));
            if (i < outs.Count - 1) b.Add(",");
            b.Add(nl);
        }

        b.Add("    FROM ").Add(Q(ctx.Object.Name));
        if (ctx.Inputs.Count > 0)
        {
            b.Add("(");
            for (var i = 0; i < ctx.Inputs.Count; i++)
            {
                if (i > 0) b.Add(", ");
                b.Param(ctx, ctx.Inputs[i].Name);
            }
            b.Add(")");
        }
        b.Add(nl).Add("    INTO").Add(nl);
        for (var i = 0; i < outs.Count; i++)
        {
            b.Add("        ").Param(ctx, outs[i].Name);
            if (i < outs.Count - 1) b.Add(",");
            b.Add(nl);
        }
        b.Add("DO").Add(nl).Add("BEGIN").Add(nl).Add(nl).Add("END");
        return b.Build();
    }
}

/// <summary><c>EXCEPTION exception_name;</c> — raise an exception.</summary>
public sealed class ExceptionRaiseTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("exception.raise", "EXCEPTION", SqlTemplateGroup.PsqlScaffold, 400,
            new[] { MetadataObjectKind.Exception }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
        => SqlSnippet.Plain($"EXCEPTION {Q(ctx.Object.Name)};");
}

/// <summary>
/// <c>EXCEPTION exception_name 'message';</c> — raise with a custom message (FB 2.0+).
/// The message literal is a tab-stop.
/// </summary>
public sealed class ExceptionRaiseMessageTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("exception.raise-message", "EXCEPTION with message", SqlTemplateGroup.PsqlScaffold, 410,
            new[] { MetadataObjectKind.Exception }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
        => new SqlSnippetBuilder()
            .Add("EXCEPTION ").Add(Q(ctx.Object.Name)).Add(" ")
            .Placeholder("message", "'message'").Add(";")
            .Build();
}

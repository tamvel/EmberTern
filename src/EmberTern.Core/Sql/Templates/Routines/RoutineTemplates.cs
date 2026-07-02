using EmberTern.Core.Metadata;
using static EmberTern.Core.Sql.Templates.TemplateHelpers;

namespace EmberTern.Core.Sql.Templates.Routines;

/// <summary><c>EXECUTE PROCEDURE p(:in)</c> — all input parameters as tab-stops.</summary>
public sealed class ProcedureExecuteTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("procedure.execute", "EXECUTE PROCEDURE", SqlTemplateGroup.Call, 110,
            new[] { MetadataObjectKind.Procedure }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var b = new SqlSnippetBuilder().Add("EXECUTE PROCEDURE ").Add(Q(ctx.Object.Name));
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
        return b.Build();
    }
}

/// <summary>
/// <c>SELECT &lt;outputs&gt; FROM p(:in)</c> — only for selectable procedures (SUSPEND).
/// Output columns are literal; input parameters are tab-stops.
/// </summary>
public sealed class ProcedureSelectFromTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("procedure.select-from", "SELECT FROM procedure", SqlTemplateGroup.Call, 120,
            new[] { MetadataObjectKind.Procedure }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx) && ctx.ProcedureIsSelectable;

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var b = new SqlSnippetBuilder().Add("SELECT ");
        if (ctx.Outputs.Count > 0)
        {
            for (var i = 0; i < ctx.Outputs.Count; i++)
            {
                if (i > 0) b.Add(", ");
                b.Add(Q(ctx.Outputs[i].Name));
            }
        }
        else
        {
            b.Add("*");
        }

        b.Add(" FROM ").Add(Q(ctx.Object.Name));
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
        return b.Build();
    }
}

/// <summary><c>SELECT f(:args) FROM RDB$DATABASE</c> — arguments as tab-stops.</summary>
public sealed class FunctionCallTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("function.call", "Function call", SqlTemplateGroup.Call, 210,
            new[] { MetadataObjectKind.Function }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
    {
        var args = ctx.Function?.Arguments ?? System.Array.Empty<ProcedureParameterInfo>();
        var b = new SqlSnippetBuilder().Add("SELECT ").Add(Q(ctx.Object.Name)).Add("(");
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0) b.Add(", ");
            b.Param(ctx, args[i].Name);
        }
        b.Add(") FROM RDB$DATABASE");
        return b.Build();
    }
}

/// <summary><c>NEXT VALUE FOR g</c>.</summary>
public sealed class GeneratorNextValueTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("generator.next-value", "NEXT VALUE FOR", SqlTemplateGroup.Sequence, 310,
            new[] { MetadataObjectKind.Generator }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
        => SqlSnippet.Plain($"NEXT VALUE FOR {Q(ctx.Object.Name)}");
}

/// <summary><c>GEN_ID(g, 1)</c> — the increment is a tab-stop.</summary>
public sealed class GeneratorGenIdTemplate : ISqlTemplate
{
    public SqlTemplateDescriptor Descriptor { get; } =
        new("generator.gen-id", "GEN_ID", SqlTemplateGroup.Sequence, 320,
            new[] { MetadataObjectKind.Generator }, SnippetContexts.Any);

    public bool AppliesTo(SnippetContext ctx) => KindAndContext(Descriptor, ctx);

    public SqlSnippet Generate(SnippetContext ctx)
        => new SqlSnippetBuilder()
            .Add("GEN_ID(").Add(Q(ctx.Object.Name)).Add(", ").Placeholder("increment", "1").Add(")")
            .Build();
}

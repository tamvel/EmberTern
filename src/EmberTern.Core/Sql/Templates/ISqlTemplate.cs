namespace EmberTern.Core.Sql.Templates;

/// <summary>
/// One SQL generator (SELECT / INSERT / EXECUTE PROCEDURE / …). Many small
/// implementations, composed by <see cref="SqlTemplateRegistry"/> — no switch
/// statement anywhere, and the interface is justified by having many concrete impls.
/// <see cref="AppliesTo"/> is the single applicability gate (object kind, loaded
/// metadata, insertion context, selectable-proc), consulted both to build the drop
/// menu and before generation. <see cref="Generate"/> is pure and synchronous.
/// </summary>
public interface ISqlTemplate
{
    SqlTemplateDescriptor Descriptor { get; }

    bool AppliesTo(SnippetContext ctx);

    SqlSnippet Generate(SnippetContext ctx);
}

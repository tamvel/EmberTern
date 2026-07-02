using System.Collections.Generic;
using System.Text;

namespace EmberTern.Core.Sql.Templates;

/// <summary>
/// Fluent builder that accumulates snippet text while recording each placeholder's
/// exact offset — so templates never compute character positions by hand. A placeholder
/// spans the literal token appended for it (e.g. <c>:CUSTOMER_ID</c>): the editor selects
/// that token as the first tab-stop, and the user either keeps the bound param or types
/// a value over it.
/// </summary>
internal sealed class SqlSnippetBuilder
{
    private readonly StringBuilder _sb = new();
    private readonly List<SqlPlaceholder> _placeholders = new();

    public SqlSnippetBuilder Add(string text)
    {
        _sb.Append(text);
        return this;
    }

    /// <summary>Append <paramref name="token"/> and mark its span as a tab-stop named <paramref name="name"/>.</summary>
    public SqlSnippetBuilder Placeholder(string name, string token)
    {
        _placeholders.Add(new SqlPlaceholder(name, _sb.Length, token.Length));
        _sb.Append(token);
        return this;
    }

    /// <summary>Append a named-parameter tab-stop (<c>:name</c>) using the context's prefix.</summary>
    public SqlSnippetBuilder Param(SnippetContext ctx, string name)
        => Placeholder(name.Trim(), ctx.Options.ParamPrefix + name.Trim());

    public SqlSnippet Build() => new(_sb.ToString(), _placeholders);
}

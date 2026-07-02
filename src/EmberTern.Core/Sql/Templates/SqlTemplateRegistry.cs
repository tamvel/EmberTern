using System;
using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Sql.Templates;

/// <summary>
/// Holds the set of <see cref="ISqlTemplate"/>s and answers "which apply to this object,
/// in this order." The App layer builds one registry (via <see cref="SqlTemplateCatalog"/>),
/// asks <see cref="DescriptorsFor"/> to populate the drop flyout, and calls
/// <see cref="Generate"/> on the chosen id. Plugin/user templates can be appended to the
/// same list later without touching consumers.
/// </summary>
public sealed class SqlTemplateRegistry
{
    private readonly IReadOnlyList<ISqlTemplate> _templates;

    public SqlTemplateRegistry(IEnumerable<ISqlTemplate> templates)
    {
        _templates = templates.OrderBy(t => t.Descriptor.SortOrder).ToArray();
    }

    /// <summary>Templates applicable to the given context, in <c>SortOrder</c>.</summary>
    public IReadOnlyList<ISqlTemplate> ApplicableTo(SnippetContext ctx)
        => _templates.Where(t => t.AppliesTo(ctx)).ToArray();

    /// <summary>Descriptors for the applicable templates — data-aware (needs a full context).</summary>
    public IReadOnlyList<SqlTemplateDescriptor> DescriptorsFor(SnippetContext ctx)
        => ApplicableTo(ctx).Select(t => t.Descriptor).ToArray();

    /// <summary>
    /// Descriptors applicable to an object <em>kind</em> in a given insertion context, in
    /// <c>SortOrder</c> — the metadata-free menu filter. The drop flyout uses this the instant
    /// an object is dropped, before any reader call; detailed metadata is loaded only after
    /// the user picks a template. PSQL-only templates are excluded in a plain SQL editor.
    /// </summary>
    public IReadOnlyList<SqlTemplateDescriptor> DescriptorsForKind(
        MetadataObjectKind kind, SnippetInsertionContext insertion)
        => _templates
            .Where(t => t.Descriptor.Kinds.Contains(kind) && t.Descriptor.Contexts.Contains(insertion))
            .Select(t => t.Descriptor)
            .ToArray();

    /// <summary>
    /// True when any template targets this object kind (in any context) — the drag-start
    /// gate: whether the object is draggable onto an editor at all. Context is unknown until
    /// the drop lands, so this ignores it.
    /// </summary>
    public bool HasTemplatesForKind(MetadataObjectKind kind)
        => _templates.Any(t => t.Descriptor.Kinds.Contains(kind));

    public ISqlTemplate? Find(string id)
        => _templates.FirstOrDefault(t => t.Descriptor.Id == id);

    /// <summary>Generate the snippet for a chosen template id against the context.</summary>
    public SqlSnippet Generate(string id, SnippetContext ctx)
    {
        var template = Find(id)
            ?? throw new ArgumentException($"Unknown SQL template id '{id}'.", nameof(id));
        return template.Generate(ctx);
    }
}

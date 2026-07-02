using System.Collections.Generic;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Sql.Templates;

/// <summary>Coarse grouping for the drop flyout (submenu headers / ordering).</summary>
public enum SqlTemplateGroup
{
    Dml,
    Fragment,
    Call,
    Sequence,
    PsqlScaffold,
}

/// <summary>
/// The menu-facing description of a template: stable <see cref="Id"/> (used to look the
/// template up on selection), display <see cref="Title"/>, <see cref="Group"/>, a global
/// <see cref="SortOrder"/>, the object <see cref="Kinds"/> it can appear for, and the
/// insertion <see cref="Contexts"/> it is valid in (plain SQL editor vs a PSQL body editor).
/// <para>
/// <see cref="Kinds"/> + <see cref="Contexts"/> are the <b>cheap, metadata-free</b> menu
/// filter: the drop flyout is built from them the instant an object is dropped, with no
/// reader call. Full applicability (loaded columns, selectable proc) is decided by
/// <see cref="ISqlTemplate.AppliesTo"/> at generation time.
/// </para>
/// </summary>
public sealed record SqlTemplateDescriptor(
    string Id,
    string Title,
    SqlTemplateGroup Group,
    int SortOrder,
    IReadOnlyList<MetadataObjectKind> Kinds,
    IReadOnlyList<SnippetInsertionContext> Contexts);

/// <summary>Reusable insertion-context sets for descriptor construction.</summary>
public static class SnippetContexts
{
    /// <summary>Valid in both a plain SQL editor and a PSQL body.</summary>
    public static readonly IReadOnlyList<SnippetInsertionContext> Any =
        new[] { SnippetInsertionContext.PlainSql, SnippetInsertionContext.PsqlBody };

    /// <summary>Valid only inside a PSQL body (procedure / trigger / function / package).</summary>
    public static readonly IReadOnlyList<SnippetInsertionContext> PsqlOnly =
        new[] { SnippetInsertionContext.PsqlBody };
}

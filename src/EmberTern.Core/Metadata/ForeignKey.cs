using System;
using System.Collections.Generic;

namespace EmberTern.Core.Metadata;

/// <summary>
/// Referential-action enum for FK <c>ON UPDATE</c> / <c>ON DELETE</c>. V1
/// supports the three most-used variants:
///   - <see cref="NoAction"/> — server-side default (omits the clause in DDL,
///     matches how <c>FirebirdTableDetailReader.ForeignKeyRule</c> suppresses
///     RESTRICT for display).
///   - <see cref="Cascade"/> — referenced changes propagate.
///   - <see cref="SetNull"/> — referencing field becomes NULL on referenced
///     change (the field MUST be nullable).
///
/// Future expansion: <c>SetDefault</c> + <c>Restrict</c>. Adding them requires
/// (1) the enum value, (2) the rendering branch in
/// <see cref="DdlGenerator.BuildAddForeignKey"/>, (3) the
/// <c>AvailableActions</c> list in <c>ForeignKeyDialogViewModel</c>. No other
/// callers need to change — keep the enum closed against unknown values.
/// </summary>
public enum ForeignKeyAction
{
    NoAction,
    Cascade,
    SetNull,
}

/// <summary>
/// Foreign-key spec consumed by <see cref="DdlGenerator.BuildAddForeignKey"/>.
/// Init-only properties — the dialog builds one of these and hands it to
/// <c>TableDetailTabViewModel.ExecuteCreateForeignKeyAsync</c>.
///
/// Field ordering matters: <see cref="LocalFields"/>[i] maps to
/// <see cref="ReferencedFields"/>[i]. Counts MUST match.
/// </summary>
public sealed class ForeignKeySpec
{
    /// <summary>Constraint name (e.g. <c>FK_ZAMOWIENIA_KONTRAHENCI</c>).
    /// Always required — the dialog auto-derives a default but the user can
    /// override.</summary>
    public string ConstraintName { get; init; } = string.Empty;

    /// <summary>Columns in the owning (referencing) table.</summary>
    public IReadOnlyList<string> LocalFields { get; init; } = Array.Empty<string>();

    /// <summary>The referenced table — usually the table whose primary key is
    /// being pointed at.</summary>
    public string ReferencedTable { get; init; } = string.Empty;

    /// <summary>Columns in the referenced table. Count must match
    /// <see cref="LocalFields"/>.</summary>
    public IReadOnlyList<string> ReferencedFields { get; init; } = Array.Empty<string>();

    public ForeignKeyAction OnUpdate { get; init; } = ForeignKeyAction.NoAction;
    public ForeignKeyAction OnDelete { get; init; } = ForeignKeyAction.NoAction;
}

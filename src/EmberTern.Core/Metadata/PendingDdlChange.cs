namespace EmberTern.Core.Metadata;

/// <summary>
/// What kind of structural change a <see cref="PendingDdlChange"/> represents.
/// Used by the TableDetail VM to surface the right label and to gate batch
/// behaviours (e.g. moves are renumbered in one Compile pass).
/// </summary>
public enum PendingDdlChangeKind
{
    AddField,
    DropField,
    MoveField,
    Other,
}

/// <summary>
/// One pending DDL statement collected on the TableDetail edit toolbar. Stays
/// in memory until the user presses ⚡ Compile, which executes the statements
/// in order. The statement payload can span multiple top-level commands
/// separated by semicolons (e.g. add-with-autoincrement emits CREATE GENERATOR
/// + CREATE TRIGGER alongside the column add) — the executor splits on
/// semicolon at the boundary.
/// </summary>
public sealed class PendingDdlChange
{
    public PendingDdlChangeKind Kind { get; init; }

    /// <summary>Short summary for the user (e.g. "Add field NAZWA").</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The DDL statement(s) to execute. Multiple statements joined by ';'.</summary>
    public string Sql { get; init; } = string.Empty;
}

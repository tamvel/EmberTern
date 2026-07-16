namespace EmberTern.Core.Sql.Language.Ast;

/// <summary>
/// Marks a node the future <b>debugger</b> can treat as a single <b>executable step</b> — something a
/// breakpoint can attach to and stepping can stop on. From milestone B1 onward it is implemented by the
/// PSQL leaf statements under <see cref="PsqlStatement"/> <em>and</em> by the DML statement nodes
/// (<see cref="InsertStatement"/> / <see cref="UpdateStatement"/> / … ) when they appear inside a body,
/// so a debugger can enumerate step points across a routine regardless of a statement's place in the
/// class hierarchy. A marker interface is used precisely because a DML node is a
/// <see cref="SqlStatement"/> while a block/if/for node is a <see cref="PsqlStatement"/>: they share no
/// base, but they share this role.
/// <para>
/// The span members below are satisfied automatically by <see cref="SqlNode"/>'s
/// <see cref="SqlNode.Start"/>/<see cref="SqlNode.Length"/>/<see cref="SqlNode.End"/> on every
/// implementer, so a consumer can read a step's location through this interface alone (e.g. to place a
/// breakpoint marker). <b>Extension point — Etap 6.9, added in milestone B0</b>; no node implements it
/// until B1 (see <c>docs/design/editor-ast-deepening.md</c>).
/// </para>
/// </summary>
public interface IExecutableStatement
{
    /// <summary>Absolute source offset where the executable statement begins.</summary>
    int Start { get; }

    /// <summary>Length of the statement's source span, in characters.</summary>
    int Length { get; }

    /// <summary>Absolute source offset just past the statement's span.</summary>
    int End { get; }
}

using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Ast;

/// <summary>
/// The abstract base of every <b>PSQL body statement</b> node — a <c>BEGIN … END</c> block, an
/// <c>IF</c>/<c>WHILE</c>/<c>FOR</c> control-flow statement, or an executable leaf (assignment,
/// <c>EXECUTE</c>, <c>SUSPEND</c>, <c>EXCEPTION</c>, a DML statement inside a body, …). Introduced as
/// the future <b>debugger's step unit</b>: each concrete PSQL statement carries a stable absolute
/// <see cref="SqlNode.Start"/>/<see cref="SqlNode.Length"/> span, so a debugger can set breakpoints,
/// step, and map an engine-reported position back to a node without re-scanning text — and diagnostics
/// / folding / breadcrumbs read the same nodes.
/// <para>
/// <b>Extension point — Etap 6.9 (Structural AST Deepening), added in milestone B0.</b> Abstract base
/// only; the concrete PSQL body tree (Block / If / While / ForSelect + executable leaves) is added in
/// B1, and routine / <c>EXECUTE BLOCK</c> bodies in B5 (see <c>docs/design/editor-ast-deepening.md</c>).
/// Firebird reports PSQL positions <em>relative to the routine body</em>, so B1 producers additionally
/// expose a body-relative offset for the debugger; the absolute span lives here on the common base.
/// </para>
/// <para>
/// A PSQL leaf that a debugger can stop on also implements <see cref="IExecutableStatement"/> — the
/// marker is an interface (not this base) because the DML statement nodes (which are
/// <see cref="SqlStatement"/>s, not <see cref="PsqlStatement"/>s) are step points too when they appear
/// inside a body. §0 holds by construction: a structural overlay on the token stream.
/// </para>
/// </summary>
public abstract class PsqlStatement : SqlNode
{
    private protected PsqlStatement(int start, int length, IReadOnlyList<SqlToken> tokens)
        : base(start, length)
    {
        Tokens = tokens;
    }

    /// <summary>The significant tokens this PSQL statement spans (including nested statements' tokens,
    /// like <see cref="SqlStatement.Tokens"/>). Consumers read a node's details from here without
    /// re-scanning the whole body; the byte-for-byte round-trip still comes from the owning
    /// <see cref="SqlScript"/>'s flat token stream, never from these overlays.</summary>
    public IReadOnlyList<SqlToken> Tokens { get; }
}

using System;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql;

/// <summary>
/// Which transaction lane a free-text SQL statement should run on. The SQL Editor
/// uses this to auto-route a single Execute (F5): data operations to the Data lane
/// (connection #1, data profile), structural operations to the Metadata lane
/// (connection #2, metadata profile).
/// <para>
/// <see cref="Ambiguous"/> is reported when the leading statement can't be classified
/// confidently (e.g. SET TERM, SET TRANSACTION, an unrecognised keyword, or empty
/// input). The caller routes Ambiguous to the Data lane — the safest choice
/// (read_committed + nowait never blocks tables or metadata). The three-valued enum
/// is kept so callers/tests can distinguish a confident Data verdict from a fallback.
/// </para>
/// </summary>
public enum StatementLane
{
    Data,
    Metadata,
    Ambiguous,
}

/// <summary>
/// Classifies a free-text SQL statement into a <see cref="StatementLane"/> by the kind of its
/// leading statement. Re-expressed as an AST query (Etap 2): it parses via <see cref="SqlParser"/>
/// and inspects the first <see cref="SqlStatement"/> node, rather than carrying its own scanner.
/// </summary>
/// <remarks>
/// Classification is by the FIRST statement only. The query executor sends one command to the
/// driver per Execute, so a multi-statement script run with a single F5 is already a degenerate
/// case; we classify by its leading statement.
/// <para>
/// EXECUTE BLOCK is classified as Data: Firebird PSQL cannot contain DDL inside a block, and an
/// EXECUTE BLOCK is a data/result-set construct. The one residual gap — dynamic DDL via
/// <c>EXECUTE STATEMENT 'CREATE …'</c> built from a variable — is statically undecidable and
/// vanishingly rare; it runs harmlessly on the Data lane.
/// </para>
/// </remarks>
public static class SqlStatementClassifier
{
    public static StatementLane Classify(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return StatementLane.Ambiguous;
        }

        var statements = SqlParser.Parse(sql!).Root.Statements;
        return statements.Count == 0 ? StatementLane.Ambiguous : LaneOf(statements[0]);
    }

    private static StatementLane LaneOf(SqlStatement statement) => statement switch
    {
        // Reads + DML + procedure/block/statement execution.
        SelectStatement or InsertStatement or UpdateStatement or UpdateOrInsertStatement
            or DeleteStatement or MergeStatement
            or ExecuteBlockStatement or ExecuteProcedureStatement or ExecuteStatementStatement
            => StatementLane.Data,

        // DDL + DCL (permission structure).
        DdlStatement or CommentStatement or DeclareStatement or GrantStatement or RevokeStatement
            => StatementLane.Metadata,

        // SET GENERATOR / SET STATISTICS are structural; SET TERM / SET TRANSACTION / others are
        // directives or session-level → ambiguous.
        SetStatement set => IsStructuralSet(set.Target) ? StatementLane.Metadata : StatementLane.Ambiguous,

        // EmptyStatement, RawStatement, and anything else.
        _ => StatementLane.Ambiguous,
    };

    private static bool IsStructuralSet(string? target)
        => string.Equals(target, "GENERATOR", StringComparison.OrdinalIgnoreCase)
        || string.Equals(target, "STATISTICS", StringComparison.OrdinalIgnoreCase);
}

using System;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql;

/// <summary>
/// What a free-text SQL statement DOES: does it touch data, or does it change the schema?
///
/// <para><b>This does not decide where a statement runs.</b> It used to: the SQL Editor auto-routed
/// each F5 by this verdict, sending DDL to a second attachment with its own hidden transaction.
/// That routing is gone — the SQL Editor is a classic console (one attachment, one transaction,
/// NOWAIT), so <em>every</em> statement runs in the user's transaction regardless of this verdict.
/// The classifier survives for one honest purpose: a REFRESH HINT. A transaction that ran
/// <see cref="Schema"/> statements changes the catalog, so the metadata tree must be reloaded when
/// it settles (uncommitted DDL is deliberately invisible to the read-only metadata attachment, so
/// Commit is the moment a new object first appears).</para>
///
/// <para><see cref="Ambiguous"/> means the leading statement can't be classified confidently
/// (SET TERM, SET TRANSACTION, an unrecognised keyword, empty input). It is treated as
/// "no schema change" — the safe assumption, since a spurious refresh costs a storm of catalog
/// reads (gotcha #119) while a missed one costs a manual refresh.</para>
/// </summary>
public enum SqlStatementCategory
{
    /// <summary>Reads + DML + procedure/block execution.</summary>
    Data,
    /// <summary>DDL + DCL — changes the catalog.</summary>
    Schema,
    Ambiguous,
}

/// <summary>
/// Classifies a free-text SQL statement into a <see cref="SqlStatementCategory"/> by the kind of its
/// leading statement. Re-expressed as an AST query (Etap 2): it parses via <see cref="SqlParser"/>
/// and inspects the first <see cref="SqlStatement"/> node, rather than carrying its own scanner.
/// </summary>
/// <remarks>
/// The WHOLE script is classified, not just its leading statement: if ANY statement changes the
/// schema the verdict is <see cref="SqlStatementCategory.Schema"/>. A mixed migration script
/// (<c>CREATE TABLE … ; INSERT … ; SELECT …</c>) does change the catalog, and a first-statement-only
/// verdict would call that one "Data" and skip the tree reload. (It also USED to decide which
/// attachment the script ran on, where a first-statement verdict was an outright latent bug — one
/// more reason routing is gone.)
/// <para>
/// EXECUTE BLOCK is Data: Firebird PSQL cannot contain DDL inside a block, and an EXECUTE BLOCK is
/// a data/result-set construct. The one residual gap — dynamic DDL via
/// <c>EXECUTE STATEMENT 'CREATE …'</c> built from a variable — is statically undecidable and
/// vanishingly rare; it costs at most a missed tree refresh (press Refresh), never a wrong result.
/// </para>
/// </remarks>
public static class SqlStatementClassifier
{
    public static SqlStatementCategory Classify(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return SqlStatementCategory.Ambiguous;
        }

        var statements = SqlParser.Parse(sql!).Root.Statements;
        if (statements.Count == 0) return SqlStatementCategory.Ambiguous;

        // Any schema statement anywhere in the script wins — the catalog changes either way.
        var verdict = SqlStatementCategory.Ambiguous;
        foreach (var statement in statements)
        {
            var category = CategoryOf(statement);
            if (category == SqlStatementCategory.Schema) return SqlStatementCategory.Schema;
            if (category == SqlStatementCategory.Data) verdict = SqlStatementCategory.Data;
        }
        return verdict;
    }

    private static SqlStatementCategory CategoryOf(SqlStatement statement) => statement switch
    {
        // Reads + DML + procedure/block/statement execution.
        SelectStatement or InsertStatement or UpdateStatement or UpdateOrInsertStatement
            or DeleteStatement or MergeStatement
            or ExecuteBlockStatement or ExecuteProcedureStatement or ExecuteStatementStatement
            => SqlStatementCategory.Data,

        // DDL + DCL (permission structure) — both change the catalog.
        DdlStatement or CommentStatement or DeclareStatement or GrantStatement or RevokeStatement
            => SqlStatementCategory.Schema,

        // SET GENERATOR / SET STATISTICS are structural; SET TERM / SET TRANSACTION / others are
        // directives or session-level → ambiguous.
        SetStatement set => IsStructuralSet(set.Target) ? SqlStatementCategory.Schema : SqlStatementCategory.Ambiguous,

        // EmptyStatement, RawStatement, and anything else.
        _ => SqlStatementCategory.Ambiguous,
    };

    private static bool IsStructuralSet(string? target)
        => string.Equals(target, "GENERATOR", StringComparison.OrdinalIgnoreCase)
        || string.Equals(target, "STATISTICS", StringComparison.OrdinalIgnoreCase);
}

using EmberTern.Core.Sql;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap 6.9 formatter convergence — queries embedded in OTHER statements now lay out through the
/// AST-walking query core: an INSERT … SELECT source, a scalar subquery in VALUES, embedded subqueries
/// in UPDATE/DELETE, a MERGE USING (…) source query, and a CREATE VIEW … AS body (incl. WITH / set
/// operations). Each assertion doubles as proof the §0 lexeme net did not fire; every case is idempotent.
/// </summary>
public class SqlFormatterEmbeddedQueryTests
{
    private static void Idempotent(string sql)
    {
        var once = SqlFormatter.Format(sql);
        Assert.Equal(once, SqlFormatter.Format(once));
    }

    [Fact]
    public void InsertSelect_SourceQuery_LaidOutWithNestedBlock()
    {
        Assert.Equal(
            "insert into t (a, b)\nselect x, y\nfrom s\nwhere z in (\n    select k\n    from u\n)",
            SqlFormatter.Format("INSERT INTO T (A, B) SELECT X, Y FROM S WHERE Z IN (SELECT K FROM U)"));
    }

    [Fact]
    public void InsertSelect_Returning_OnOwnLine()
    {
        Assert.Equal(
            "insert into t (a, b)\nselect x, y\nfrom s\nreturning id",
            SqlFormatter.Format("INSERT INTO T (A, B) SELECT X, Y FROM S RETURNING ID"));
    }

    [Fact]
    public void Update_ScalarSubqueryInSet_And_ExistsInWhere_BothExpand()
    {
        Assert.Equal(
            "update t set a = (\n    select max(x)\n    from u\n)\nwhere exists (\n    select 1\n    from v\n    where v.k = t.id\n)",
            SqlFormatter.Format("UPDATE T SET A = (SELECT MAX(X) FROM U) WHERE EXISTS (SELECT 1 FROM V WHERE V.K = T.ID)"));
    }

    [Fact]
    public void Delete_InSubquery_Expands()
    {
        Assert.Equal(
            "delete\nfrom t\nwhere x in (\n    select y\n    from u\n    where u.z > 0\n)",
            SqlFormatter.Format("DELETE FROM T WHERE X IN (SELECT Y FROM U WHERE U.Z > 0)"));
    }

    [Fact]
    public void Merge_UsingSubquery_SourceExpandsAsBlock()
    {
        Assert.Equal(
            "merge into t using (\n    select id, v\n    from s\n) src on t.id = src.id when matched then update set t.v = src.v when not matched then insert (id, v) values (src.id, src.v)",
            SqlFormatter.Format(
                "MERGE INTO T USING (SELECT ID, V FROM S) SRC ON T.ID = SRC.ID WHEN MATCHED THEN UPDATE SET T.V = SRC.V "
                + "WHEN NOT MATCHED THEN INSERT (ID, V) VALUES (SRC.ID, SRC.V)"));
    }

    [Fact]
    public void CreateView_WithCteBody_LaysOutBodyThroughAst()
    {
        Assert.Equal(
            "create view v (\n    n)\nas\nwith c\nas (\n    select id\n    from t\n)\nselect id\nfrom c",
            SqlFormatter.Format("CREATE VIEW V (N) AS WITH C AS (SELECT ID FROM T) SELECT ID FROM C"));
    }

    [Fact]
    public void CreateView_SetOperationBody_BreaksAtUnion()
    {
        Assert.Equal(
            "create or alter view v\nas\nselect a\nfrom t\nunion all\nselect a\nfrom u",
            SqlFormatter.Format("CREATE OR ALTER VIEW V AS SELECT A FROM T UNION ALL SELECT A FROM U"));
    }

    [Fact]
    public void EmbeddedQueries_AreIdempotent()
    {
        Idempotent("INSERT INTO T (A, B) SELECT X, Y FROM S WHERE Z IN (SELECT K FROM U)");
        Idempotent("INSERT INTO T (A) VALUES ((SELECT MAX(ID) FROM U))");
        Idempotent("UPDATE T SET A = (SELECT MAX(X) FROM U) WHERE EXISTS (SELECT 1 FROM V WHERE V.K = T.ID)");
        Idempotent("DELETE FROM T WHERE X IN (SELECT Y FROM U WHERE U.Z > 0)");
        Idempotent("MERGE INTO T USING (SELECT ID, V FROM S) SRC ON T.ID = SRC.ID WHEN MATCHED THEN UPDATE SET T.V = SRC.V WHEN NOT MATCHED THEN INSERT (ID, V) VALUES (SRC.ID, SRC.V)");
        Idempotent("CREATE VIEW V (N) AS WITH C AS (SELECT ID FROM T) SELECT ID FROM C");
        Idempotent("CREATE OR ALTER VIEW V AS SELECT A FROM T UNION ALL SELECT A FROM U");
    }
}

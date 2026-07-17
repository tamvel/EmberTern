using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Tests;

/// <summary>
/// The one shared SQL/PSQL corpus the language-front-end tests draw from — a single source of
/// representative statements so the formatter §0 invariants
/// (<see cref="SqlFormatterInvariantsTests"/>) and the Etap-6.9 structural-deepening differential
/// harness (<see cref="StructuralAstDifferentialTests"/>) test the same code, without each keeping a
/// parallel list.
/// <para>
/// <b>Representative</b> is the broad statement-kind + edge-case set (was previously private to the
/// formatter invariants). <b>StructuralConstructs</b> adds the nested/structural shapes that Etap 6.9
/// deepens (nested CTEs, CASE, derived tables, EXISTS, scalar subqueries, FOR SELECT, nested IF/WHILE)
/// — as each milestone B1–B5 gives one of these a real AST node, the differential harness proves the
/// byte-for-byte round-trip and the tree stay intact. Add new construct cases here, once.
/// </para>
/// </summary>
public static class SqlTestCorpus
{
    /// <summary>Broad, representative corpus exercising every statement kind + edge cases (comments,
    /// incomplete input, erroneous input, unusual fragments, multi-statement scripts).</summary>
    public static readonly IReadOnlyList<string> Representative = new[]
    {
        // SELECT / DML
        "SELECT A, B FROM T WHERE X = 1 AND Y = 2 ORDER BY A",
        "select n.id, count(p.amount) from nagl n join pozycje p on p.id_nagl = n.id group by n.id having count(*) > 1",
        "WITH c AS (SELECT id FROM t) SELECT * FROM c WHERE id IN (1, 2, 3)",
        "INSERT INTO T (A, B, C) VALUES (1, 'x', :p)",
        "INSERT INTO T (A, B) SELECT X, Y FROM S WHERE Z = 1",
        "UPDATE T SET A = 1, B = 'y' WHERE ID = 10",
        "UPDATE OR INSERT INTO T (A, B) VALUES (1, 2) MATCHING (A)",
        "DELETE FROM T WHERE X IN (1, 2, 3)",
        "MERGE INTO T USING S ON T.ID = S.ID WHEN MATCHED THEN UPDATE SET T.V = S.V WHEN NOT MATCHED THEN INSERT (ID, V) VALUES (S.ID, S.V)",
        // Execution / PSQL
        "EXECUTE PROCEDURE MY_PROC(1, 'two', :three)",
        "EXECUTE BLOCK RETURNS (R INTEGER) AS BEGIN R = 1; SUSPEND; END",
        "CREATE OR ALTER PROCEDURE P (A INTEGER) RETURNS (V INTEGER) AS DECLARE VARIABLE T INTEGER; BEGIN V = case when A > 0 then 1 else 0 end; SUSPEND; END",
        "CREATE OR ALTER FUNCTION F (A INTEGER) RETURNS INTEGER AS BEGIN RETURN A * 2; END",
        "CREATE OR ALTER TRIGGER TR FOR NAGL ACTIVE BEFORE INSERT POSITION 0 AS BEGIN NEW.ID = GEN_ID(G, 1); END",
        "begin x = 1; if (a = 1) then b = 2; else b = 3; end",
        "declare variable x integer; declare variable y varchar(5); begin x = 1; end",
        // DDL
        "CREATE TABLE T (ID INTEGER NOT NULL, NAME VARCHAR(50), PRIMARY KEY (ID))",
        "CREATE OR ALTER VIEW V (A, B) AS SELECT X.A, X.B FROM T X",
        "ALTER TABLE T ADD CONSTRAINT FK1 FOREIGN KEY (PID) REFERENCES P (ID)",
        "DROP INDEX IX_T_NAME",
        "COMMENT ON TABLE T IS 'a table'",
        "SET GENERATOR G TO 100",
        "GRANT SELECT ON T TO PUBLIC",
        // Comments
        "SELECT a -- trailing comment\nFROM t",
        "SELECT /* inline */ a FROM t",
        "begin x = 1; -- note\n y = 2; end",
        "/* leading */ SELECT 1 FROM RDB$DATABASE",
        // Literals / identifiers preserved
        "SELECT 'It''s ok', \"From\", 0x1F, 3.14 FROM t",
        // Incomplete / mid-typing (error tolerance)
        "SELECT ",
        "SELECT * FROM",
        "CREATE PROCEDURE P AS BEGIN",
        // Unusual / unrecognised (RawStatement — verbatim)
        "FROBNICATE THE WIDGET",
        "(SELECT 1)",
        "a , b , c",
        // Multi-statement
        "SELECT 1 FROM T; DELETE FROM U; INSERT INTO V VALUES (1)",
    };

    /// <summary>The nested / structural shapes Etap 6.9 deepens into real AST nodes. Today they parse
    /// at "statement skeleton" depth (interiors are tokens); these cases pin that the round-trip and
    /// tree stay intact as each construct gains a node in B1–B5.</summary>
    public static readonly IReadOnlyList<string> StructuralConstructs = new[]
    {
        // Nested CTE (a CTE body that itself has a WITH) — B3
        "WITH a AS (WITH b AS (SELECT 1 AS n FROM t) SELECT n FROM b) SELECT n FROM a",
        // CASE as a SELECT expression, multi-WHEN — B4
        "SELECT CASE WHEN x > 0 THEN 'pos' WHEN x < 0 THEN 'neg' ELSE 'zero' END AS s FROM t",
        // Derived table in FROM — B3
        "SELECT d.n FROM (SELECT id AS n FROM t WHERE x = 1) d WHERE d.n > 0",
        // EXISTS correlated subquery — B3
        "SELECT * FROM t WHERE EXISTS (SELECT 1 FROM u WHERE u.tid = t.id)",
        // Scalar subquery in the SELECT list — B3
        "SELECT id, (SELECT COUNT(*) FROM u WHERE u.tid = t.id) AS cnt FROM t",
        // FOR SELECT … INTO … DO inside a routine body — B1
        "CREATE PROCEDURE P AS DECLARE VARIABLE i INTEGER; BEGIN FOR SELECT id FROM t INTO :i DO BEGIN SUSPEND; END END",
        // Nested IF / WHILE control flow — B1
        "begin if (a = 1) then begin while (b < 10) do begin b = b + 1; end end end",
        // Set operation joining two queries — B2
        "SELECT a FROM t UNION ALL SELECT a FROM u ORDER BY 1",
        // Query clause / join shapes — B2
        "SELECT FIRST 10 SKIP 5 DISTINCT a, b FROM t WHERE x = 1 GROUP BY a, b HAVING COUNT(*) > 1 ORDER BY a DESC",
        "SELECT 1 FROM a LEFT OUTER JOIN b ON a.id = b.id RIGHT JOIN c ON b.id = c.id",
        "SELECT 1 FROM a CROSS JOIN b, c NATURAL JOIN d",
        "SELECT a FROM t INTERSECT SELECT a FROM u EXCEPT SELECT a FROM v",
        "SELECT p.n FROM (SELECT n FROM t) p JOIN (SELECT n FROM u) q ON p.n = q.n",
        // Recursive query shapes — B3
        "SELECT 1 FROM t a JOIN u b ON a.id = b.id AND EXISTS (SELECT 1 FROM v WHERE v.k = a.id)",
        "SELECT id, (SELECT COUNT(*) FROM u WHERE u.tid = t.id) c FROM t WHERE x IN (SELECT y FROM w)",
        "WITH RECURSIVE r (n) AS (SELECT 1 FROM rdb$database UNION ALL SELECT n + 1 FROM r WHERE n < 10) SELECT n FROM r",
        // Queries embedded in OTHER statements — B3.1
        "INSERT INTO T (A, B) SELECT X, Y FROM S WHERE Z IN (SELECT K FROM U)",
        "INSERT INTO T (A) VALUES ((SELECT MAX(ID) FROM U))",
        "INSERT INTO T (A, B) SELECT X, Y FROM S RETURNING ID",
        "UPDATE T SET A = (SELECT MAX(X) FROM U) WHERE EXISTS (SELECT 1 FROM V WHERE V.K = T.ID)",
        "DELETE FROM T WHERE X IN (SELECT Y FROM U WHERE U.Z > 0)",
        "MERGE INTO T USING (SELECT ID, V FROM S) SRC ON T.ID = SRC.ID WHEN MATCHED THEN UPDATE SET T.V = SRC.V WHEN NOT MATCHED THEN INSERT (ID, V) VALUES (SRC.ID, SRC.V)",
        "CREATE VIEW V (N) AS WITH C AS (SELECT ID FROM T) SELECT ID FROM C",
        "CREATE OR ALTER VIEW V AS SELECT A FROM T UNION ALL SELECT A FROM U",
        // PSQL cursor queries — B3.1 (FOR SELECT cursor, DECLARE CURSOR, FOR EXECUTE STATEMENT)
        "CREATE PROCEDURE P AS DECLARE C CURSOR FOR (SELECT ID FROM T WHERE X = 1); BEGIN OPEN C; END",
        "begin for select id, name from t where x > 0 into :i, :n do suspend; end",
        "begin for execute statement 'select 1 from rdb$database' into :i do suspend; end",
        // CASE — B4 (simple, searched, nested, subquery in a branch, PSQL assignment)
        "SELECT CASE X WHEN 1 THEN 'one' WHEN 2 THEN 'two' ELSE '?' END AS S FROM T",
        "SELECT CASE WHEN X > 0 THEN (SELECT MAX(Y) FROM U WHERE U.K = T.ID) ELSE 0 END AS M FROM T",
        "SELECT CASE WHEN A THEN CASE WHEN B THEN 1 ELSE 2 END ELSE 3 END AS N FROM T",
        "UPDATE T SET S = CASE WHEN X IS NULL THEN 0 ELSE X END WHERE ID = 1",
        "begin v = case when a > 0 then 1 else 0 end; if (case when b then 1 else 0 end = 1) then suspend; end",
        // PSQL exception handlers — Stage X / P1 (WHEN … DO; all forms, single + multi-condition, block
        // body, nested handler section, malformed WHEN → the lossless Other valve).
        "begin insert into t values (1); when any do exception e; end",
        "begin x = 1; when exception my_exc do x = 2; when sqlcode -803 do x = 3; end",
        "begin x = 1; when gdscode grant_obj_notfound, gdscode grant_fld_notfound do begin x = 2; exit; end end",
        "begin insert into t values (1); when sqlstate '23000' do begin exception dup; end when any do exception other; end",
        "create procedure p as begin for select id from t into :i do begin when any do exit; end end",
        "begin x = 1; when do x = 2; end",
    };

    /// <summary>Representative + structural-construct cases.</summary>
    public static readonly IReadOnlyList<string> All =
        Representative.Concat(StructuralConstructs).ToArray();

    /// <summary><see cref="Representative"/> as xUnit <c>[MemberData]</c> rows.</summary>
    public static IEnumerable<object[]> RepresentativeData() => Representative.Select(s => new object[] { s });

    /// <summary><see cref="All"/> as xUnit <c>[MemberData]</c> rows.</summary>
    public static IEnumerable<object[]> AllData() => All.Select(s => new object[] { s });
}

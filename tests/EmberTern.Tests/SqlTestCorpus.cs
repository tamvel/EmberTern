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
        // Local sub-routines — Stage X / D9 (DECLARE PROCEDURE/FUNCTION with a body, own local variables,
        // interleaved with variable declarations, a forward declaration, and a stray one mid-body).
        "create procedure p (n integer) returns (r integer) as declare procedure sp (a integer) returns (o integer) as begin o = a * 2; end begin execute procedure sp(n) returning_values r; end",
        "create procedure p as declare function f (a integer) returns integer as begin return a + 1; end begin r = f(1); end",
        "create procedure p as declare variable v1 integer; declare procedure sp as declare variable t integer; begin t = 1; end declare variable v2 integer; begin end",
        "create procedure p as declare procedure sp (a integer) returns (o integer); declare procedure sp (a integer) returns (o integer) as begin o = a; end begin end",
        // Lone-call operands — Stage X / D9 seam c (§6.4): assignment RHS, RETURN operand, whole IF/WHILE
        // condition (recognised), plus excluded shapes that must stay token fragments (round-trip unaffected).
        "create procedure p as declare function f (a integer) returns integer as begin return f(a - 1); end begin r = f(1); if (f(r)) then suspend; while (f(r)) do r = f(r); end",
        "begin r = f(g(x)); r = f(x) + 1; if (f(x) and g(x)) then r = 1; end",
    };

    /// <summary>
    /// ⭐ The <b>Firebird Language Reference conformance corpus</b> — the constructs whose grammar puts a
    /// BARE WORD where an ordinary expression would otherwise be read, walked chapter by chapter rather
    /// than collected from bug reports (2026-08-07).
    /// <para>
    /// It exists because the previous four fixes in this area were each a single reported syntax
    /// (<c>NEXT VALUE FOR</c>, then <c>GEN_ID</c>, then <c>EXTRACT</c>, then …), and a list grown from
    /// reports can only ever be as complete as the reporting. Every entry here is a shape the Language
    /// Reference defines, whether or not anyone has hit it.
    /// </para>
    /// <para>
    /// ⚠ Its <b>diagnostics</b> guard is <c>FirebirdGrammarCorpusTests</c> (zero false findings, against a
    /// metadata snapshot deliberately seeded with colliding column names). Being part of
    /// <see cref="All"/> also puts every entry through the formatter's §0 round-trip + idempotency
    /// invariants and the structural-AST differential harness — one corpus, three guards.
    /// </para>
    /// <para>
    /// The constructs named in the 2026-08-07 report — <c>DATEADD(… MONTH …)</c>, <c>DATEDIFF</c>,
    /// <c>EXTRACT</c>, <c>IN AUTONOMOUS TRANSACTION</c>, <c>EXECUTE STATEMENT</c>,
    /// <c>OVERLAY … PLACING</c> and the window functions — are all here, but they are a subset: 26 of the
    /// 80 entries failed on the pre-fix binder, and most of those had never been reported.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyList<string> LanguageReference = new[]
    {
        // ── Date/time part words: EXTRACT / DATEADD / DATEDIFF / FIRST_DAY / LAST_DAY ─────────────
        // Not reserved in Firebird (a column may be called MONTH), so they lex as IDENTIFIERS.
        "begin v = extract(year from current_date); end",
        "begin v = extract(month from d) + extract(day from d) + extract(week from d); end",
        "begin v = extract(hour from t) * extract(minute from t) * extract(second from t); end",
        "begin v = extract(millisecond from t) + extract(weekday from d) + extract(yearday from d); end",
        "begin v = extract(quarter from d) + extract(timezone_hour from t) + extract(timezone_minute from t); end",
        "begin v = dateadd(month, 1, d); end",
        "begin v = dateadd(1 month to d); end",
        "begin v = dateadd(-2 year to current_timestamp); end",
        "begin v = datediff(month, a, b); end",
        "begin v = datediff(day from a to b); end",
        "begin v = first_day(of month from d); end",
        "begin v = last_day(of year from d); end",
        "select extract(month from o.d) m, dateadd(day, 1, o.d) n from orders o",
        "select datediff(week from a.d to b.d) from orders a join orders b on a.id = b.id",
        // ── String functions with syntactic-word slots ────────────────────────────────────────────
        "begin v = overlay(s placing r from 2 for 3); end",
        "begin v = overlay(s placing r from 2); end",
        "begin v = position(sub in s); end",
        "begin v = substring(s from 2 for 3); end",
        "begin v = substring(s similar p escape '#'); end",
        "begin v = trim(leading '0' from s); end",
        "begin v = trim(both from s); end",
        // ── CAST / type context ───────────────────────────────────────────────────────────────────
        "begin v = cast(x as varchar(10)); end",
        "begin v = cast(x as type of column orders.amount); end",
        "begin v = cast(x as d_amount); end",
        // ── Cryptographic / hash functions: USING <algorithm>, MODE, KEY, IV, CTR_LENGTH ──────────
        "begin v = hash(s using sha256); end",
        "begin v = crypt_hash(s using md5); end",
        "begin v = encrypt(s using aes mode ofb key k iv i); end",
        "begin v = decrypt(s using sha256 key k); end",
        // ── FB4 time zones: AT TIME ZONE / AT LOCAL ───────────────────────────────────────────────
        "begin v = current_timestamp at time zone 'Europe/Warsaw'; end",
        "begin v = t at local; end",
        // ── Window functions and the frame grammar ────────────────────────────────────────────────
        "select row_number() over (partition by k order by d) from orders",
        "select sum(a) over (order by d rows between unbounded preceding and current row) from orders",
        "select sum(a) over (order by d range between 1 preceding and 1 following) from orders",
        "select sum(a) over (order by d groups between unbounded preceding and unbounded following) from orders",
        "select sum(a) over (order by d rows between current row and unbounded following exclude no others) from orders",
        "select sum(a) over w from orders window w as (partition by k order by d)",
        "select count(*) filter (where a > 0) over (partition by k) from orders",
        "select nth_value(a, 2) from first over (order by d) from orders",
        "select lag(a, 1, 0) over (order by d), lead(a) over (order by d) from orders",
        "select ntile(4) over (order by d), percent_rank() over (order by d), cume_dist() over (order by d) from orders",
        // ── PSQL: autonomous transaction, EXECUTE STATEMENT and its option grammar ────────────────
        "begin in autonomous transaction do insert into audit_log (msg) values ('x'); end",
        "begin in autonomous transaction do begin v = 1; insert into audit_log (msg) values ('y'); end end",
        "begin execute statement 'select 1 from rdb$database' into :v; end",
        "begin execute statement ('select :a from rdb$database') (a := 1) into :v; end",
        "begin execute statement s with autonomous transaction into :v; end",
        "begin execute statement s with common transaction with caller privileges into :v; end",
        "begin execute statement s as user 'SYSDBA' password 'x' role 'ADMIN' into :v; end",
        "begin execute statement s on external data source 'srv:/db' as user 'SYSDBA' password 'p' into :v; end",
        "begin for execute statement s on external 'srv:/db' into :v do suspend; end",
        // ── PSQL: exception handling vocabulary (symbolic GDS codes lex as identifiers) ───────────
        "begin x = 1; when gdscode lock_conflict do x = 2; end",
        "begin x = 1; when gdscode deadlock, gdscode lock_timeout do x = 2; end",
        "begin exception e_low using ('too low', v); end",
        "begin x = 1; when any do begin x = gdscode; y = sqlcode; z = sqlstate; end end",
        "begin insert into t values (1); v = row_count; end",
        // ── PSQL: labelled loops, cursors, RETURN, SUSPEND ────────────────────────────────────────
        "begin outer_loop: while (i < 10) do begin i = i + 1; leave outer_loop; end end",
        "create procedure p as declare c cursor for (select id from orders); begin open c; fetch c into :i; close c; end",
        "create procedure p as declare c scroll cursor for (select id from orders); begin open c; fetch last from c into :i; close c; end",
        // ── Query clauses whose words are not reserved ────────────────────────────────────────────
        "select a from orders order by a nulls first",
        "select a from orders order by a desc nulls last",
        "select a from orders rows 10 to 20",
        "select a from orders offset 5 rows fetch next 10 rows only",
        "select a from orders with lock",
        "select a from orders o left join lateral (select 1 x from orders i where i.id = o.id) l on true",
        "select a from orders where b is distinct from c",
        "select a from orders where b similar to 'x%' escape '#'",
        "merge into t using s on t.id = s.id when not matched by source then delete",
        // ── DDL vocabulary that is not reserved ───────────────────────────────────────────────────
        "create table t (id integer generated always as identity (start with 1 increment by 2), n computed by (id * 2))",
        "create table t (c varchar(10) character set win1250 collate pxw_plk)",
        "create or alter trigger tr for orders active before insert or update position 5 as begin new.id = 1; end",
        "create trigger tr_db active on connect position 0 as begin post_event 'x'; end",
        "alter table t add constraint fk1 foreign key (pid) references p (id) on delete cascade on update no action",
    };

    /// <summary>Representative + structural-construct + Language-Reference cases.</summary>
    public static readonly IReadOnlyList<string> All =
        Representative.Concat(StructuralConstructs).Concat(LanguageReference).ToArray();

    /// <summary><see cref="Representative"/> as xUnit <c>[MemberData]</c> rows.</summary>
    public static IEnumerable<object[]> RepresentativeData() => Representative.Select(s => new object[] { s });

    /// <summary><see cref="All"/> as xUnit <c>[MemberData]</c> rows.</summary>
    public static IEnumerable<object[]> AllData() => All.Select(s => new object[] { s });

    /// <summary><see cref="LanguageReference"/> as xUnit <c>[MemberData]</c> rows.</summary>
    public static IEnumerable<object[]> LanguageReferenceData() =>
        LanguageReference.Select(s => new object[] { s });
}

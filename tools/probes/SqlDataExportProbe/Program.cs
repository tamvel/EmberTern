using System.Data;
using System.Globalization;
using System.Text;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;

// DEVELOPER VERIFICATION TOOL — NOT PART OF THE PRODUCT. See tools/probes/README.md.
//
// The live-engine gates for the SQL Data Export milestone (docs/design/sql-data-export.md). It answers
// the questions that a unit test structurally cannot, because the authority is the engine, not us:
//
//   1. E1 — does every SqlLiteralWriter literal survive a real round-trip? It generates each literal
//      with the REAL writer, executes it, reads the value back, and compares. A string-equality test
//      would happily pin '2024-03-15T13:45:59' — which the engine rejects.
//   2. E1 — the actual string/hex literal ceilings SqlLiteralLimits must match, and whether edge values
//      the writer currently ACCEPTS are really acceptable (long.MinValue is the suspect: Firebird parses
//      '-9223372036854775808' as a negation of a positive literal that overflows BIGINT).
//   3. E2 — what GetSchemaTable() exposes, and specifically which column carries the declared FbDbType.
//      SqlValueKind needs the DECLARED type (DATE and TIMESTAMP are the same CLR type), and the design
//      never established how to read it. Also re-confirms the provenance traps the design rests on.
//   4. E3 — how RDB$IDENTITY_TYPE is encoded (the codebase collapses it to a bool today, so the
//      ALWAYS/BY DEFAULT distinction OVERRIDING SYSTEM VALUE needs does not exist anywhere yet).
//   5. Is the multi-row `values (1,'a'),(2,'b')` constructor supported, and if not, what is the
//      portable alternative?
//
// Runs pl-PL — the user's real culture, and the one that turns 123456789.1234 into invalid SQL.
// Throwaway scratch DB at an ASCII path (gotcha #149); the lab DB is never touched.
//
// TWO WAYS TO RUN, and they answer slightly different questions:
//
//   * SERVER (preferred) — ET_LAB_PWD set. Goes over the wire through the pure-managed driver: the
//     EXACT path EmberTern ships (FbServerType.Default never loads fbclient.dll). The password comes
//     from the environment, so no secret is written to disk.
//
//       $env:ET_LAB_PWD = "<local dev SYSDBA password>"
//       dotnet run --project tools\probes\SqlDataExportProbe
//
//   * EMBEDDED (automatic fallback) — no password needed: Embedded bypasses authentication and runs
//     the same Firebird engine in-process via fbclient.dll + plugins\engine13.dll. Every question this
//     probe asks is answered by the ENGINE's parser (literal ceilings, temporal formats, identity
//     encoding) or by the DRIVER's own code (the GetSchemaTable shape) — neither of which the
//     transport changes. Use it to unblock work; re-run over the server before trusting anything
//     transport-shaped.
//
//       dotnet run --project tools\probes\SqlDataExportProbe

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Thread.CurrentThread.CurrentCulture = new CultureInfo("pl-PL"); // the user's real culture — the §1.5.1 trap

const string DbPath = @"C:\Temp\et_sqlexport_probe.fdb";
const string FbInstall = @"C:\Program Files\Firebird\Firebird_5_0";
Directory.CreateDirectory(@"C:\Temp");

var pwd = Environment.GetEnvironmentVariable("ET_LAB_PWD");
var embedded = string.IsNullOrWhiteSpace(pwd);

var csb = new FbConnectionStringBuilder
{
    Database = DbPath,
    UserID = "SYSDBA",
    Charset = "UTF8",
    Dialect = 3,
    Pooling = false,
};

if (embedded)
{
    csb.ServerType = FbServerType.Embedded;
    csb.ClientLibrary = Path.Combine(FbInstall, "fbclient.dll");
    csb.Password = "ignored-in-embedded";
}
else
{
    csb.ServerType = FbServerType.Default; // pure managed wire — what EmberTern ships
    csb.DataSource = "localhost";
    csb.Port = 3050;
    csb.Password = pwd;
}

var cs = csb.ToString();
Console.WriteLine(embedded
    ? "MODE: EMBEDDED (no ET_LAB_PWD set) — same engine in-process; set ET_LAB_PWD to run over the wire."
    : "MODE: SERVER localhost:3050 via the managed driver — the path EmberTern ships.");

int failures = 0;
void Pass(string what, string detail = "") => Console.WriteLine($"  PASS  {what}{(detail.Length > 0 ? "  — " + detail : "")}");
void Fail(string what, string detail)
{
    failures++;
    Console.WriteLine($"  FAIL  {what}  — {detail}");
}
void Info(string what) => Console.WriteLine($"  ....  {what}");
void Head(string t) => Console.WriteLine($"\n=== {t} ===");

try
{
    if (File.Exists(DbPath)) File.Delete(DbPath);
    FbConnection.CreateDatabase(cs, overwrite: true);

    using var cn = new FbConnection(cs);
    await cn.OpenAsync();

    string ServerVersion()
    {
        using var c = new FbCommand("select rdb$get_context('SYSTEM','ENGINE_VERSION') from rdb$database", cn);
        return (string)(c.ExecuteScalar() ?? "?");
    }
    Console.WriteLine($"Engine: {ServerVersion()}   Driver: FirebirdSql.Data.FirebirdClient 10.3.4");
    Console.WriteLine($"Culture: {CultureInfo.CurrentCulture.Name} (decimal separator "
                      + $"'{CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator}')");

    void Exec(string sql)
    {
        using var c = new FbCommand(sql, cn);
        c.ExecuteNonQuery();
    }

    object? Scalar(string sql)
    {
        using var c = new FbCommand(sql, cn);
        return c.ExecuteScalar();
    }

    // ── 1. Round-trip every kind ─────────────────────────────────────────────
    Head("1. Round-trip: writer literal → INSERT → read back → compare");

    Exec("""
        create table PROBE (
          ID         integer not null primary key,
          I_SMALL    smallint,
          I_INT      integer,
          I_BIG      bigint,
          N_NUM      numeric(18,4),
          F_FLOAT    float,
          F_DOUBLE   double precision,
          S_CHAR     char(12),
          S_VARCHAR  varchar(200),
          D_DATE     date,
          T_TIME     time,
          TS_STAMP   timestamp,
          B_BOOL     boolean,
          BL_BIN     blob sub_type 0,
          BL_TXT     blob sub_type text
        )
        """);

    var cells = new (string Col, SqlValueKind Kind, object? Value)[]
    {
        ("ID",        SqlValueKind.Integer,    1),
        ("I_SMALL",   SqlValueKind.Integer,    (short)-32768),
        ("I_INT",     SqlValueKind.Integer,    int.MaxValue),
        ("I_BIG",     SqlValueKind.Integer,    9223372036854775807L),
        ("N_NUM",     SqlValueKind.Decimal,    123456789.1234m),
        ("F_FLOAT",   SqlValueKind.Float,      3.14f),
        ("F_DOUBLE",  SqlValueKind.Float,      2.718281828459045d),
        ("S_CHAR",    SqlValueKind.Text,       "It's"),
        ("S_VARCHAR", SqlValueKind.Text,       "It's a \"test\" \\ n — Zażółć gęślą jaźń 日本語"),
        ("D_DATE",    SqlValueKind.Date,       new DateTime(2024, 3, 15)),
        ("T_TIME",    SqlValueKind.Time,       new TimeSpan(0, 13, 45, 59).Add(TimeSpan.FromTicks(1234000))),
        ("TS_STAMP",  SqlValueKind.Timestamp,  new DateTime(2024, 3, 15, 13, 45, 59).AddTicks(1234000)),
        ("B_BOOL",    SqlValueKind.Boolean,    true),
        ("BL_BIN",    SqlValueKind.BinaryBlob, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0xFF }),
        ("BL_TXT",    SqlValueKind.TextBlob,   "text blob with 'quote' and Zażółć"),
    };

    var cols = string.Join(", ", cells.Select(c => c.Col));
    var lits = new List<string>();
    foreach (var c in cells)
    {
        var r = SqlLiteralWriter.Write(c.Value, c.Kind);
        if (!r.IsWritten) { Fail(c.Col, $"writer refused: {r.Refusal}"); return 1; }
        lits.Add(r.Literal);
    }
    var insert = $"insert into PROBE ({cols}) values ({string.Join(", ", lits)})";
    Console.WriteLine("Generated INSERT:\n" + insert + "\n");

    try { Exec(insert); Pass("INSERT executes"); }
    catch (Exception ex) { Fail("INSERT executes", ex.Message); return 1; }

    using (var c = new FbCommand($"select {cols} from PROBE where ID = 1", cn))
    using (var rd = c.ExecuteReader())
    {
        rd.Read();
        for (int i = 0; i < cells.Length; i++)
        {
            var (col, kind, expected) = cells[i];
            var actual = rd.GetValue(i);
            var clr = actual.GetType().Name;

            bool ok = (expected, actual) switch
            {
                (byte[] e, byte[] a) => e.SequenceEqual(a),
                (string e, string a) => e == a.TrimEnd(' '), // CHAR is blank-padded by the engine
                _ => Equals(expected, actual),
            };

            if (ok) Pass($"{col,-10} [{kind}]", $"driver returns {clr}");
            else Fail($"{col,-10} [{kind}]", $"wrote {Show(expected)} → read back {Show(actual)} ({clr})");
        }
    }

    // NULL row
    var nullLits = cells.Select(c => c.Col == "ID"
        ? "2"
        : SqlLiteralWriter.Write(DBNull.Value, c.Kind).Literal!);
    try
    {
        Exec($"insert into PROBE ({cols}) values ({string.Join(", ", nullLits)})");
        var n = Convert.ToInt32(Scalar("select count(*) from PROBE where ID = 2 and I_INT is null and BL_BIN is null"), CultureInfo.InvariantCulture);
        if (n == 1) Pass("all-NULL row round-trips");
        else Fail("all-NULL row round-trips", $"count = {n}");
    }
    catch (Exception ex) { Fail("all-NULL row round-trips", ex.Message); }

    // ── 2. Edge values the writer currently ACCEPTS — does the engine? ────────
    Head("2. Edge literals the writer accepts — engine verdict");

    // long.MinValue: Firebird parses '-x' as negation of a positive literal, and 9223372036854775808
    // overflows BIGINT. If this fails, the writer must refuse it — a unit test would never have caught it.
    Check("bigint long.MinValue", SqlLiteralWriter.Write(long.MinValue, SqlValueKind.Integer), "bigint");
    Check("bigint long.MaxValue", SqlLiteralWriter.Write(long.MaxValue, SqlValueKind.Integer), "bigint");
    Check("smallint -32768", SqlLiteralWriter.Write((short)-32768, SqlValueKind.Integer), "smallint");
    Check("double 1e20", SqlLiteralWriter.Write(1e20d, SqlValueKind.Float), "double precision");
    Check("double 5e-324 (Epsilon)", SqlLiteralWriter.Write(double.Epsilon, SqlValueKind.Float), "double precision");
    Check("double -1.7976931348623157E+308", SqlLiteralWriter.Write(double.MinValue, SqlValueKind.Float), "double precision");
    Check("float 1.4E-45 (Epsilon)", SqlLiteralWriter.Write(float.Epsilon, SqlValueKind.Float), "double precision");
    Check("empty binary blob x''", SqlLiteralWriter.Write(Array.Empty<byte>(), SqlValueKind.BinaryBlob), "blob sub_type 0");
    Check("empty string ''", SqlLiteralWriter.Write("", SqlValueKind.Text), "varchar(10)");
    Check("decimal 1.10 (trailing zero)", SqlLiteralWriter.Write(1.10m, SqlValueKind.Decimal), "numeric(18,4)");
    Check("timestamp .0000 fraction", SqlLiteralWriter.Write(new DateTime(2024, 3, 15), SqlValueKind.Timestamp), "timestamp");
    Check("time 00:00:00.0000", SqlLiteralWriter.Write(TimeSpan.Zero, SqlValueKind.Time), "time");
    Check("date year 0001", SqlLiteralWriter.Write(new DateTime(1, 1, 1), SqlValueKind.Date), "date");
    Check("date year 9999", SqlLiteralWriter.Write(new DateTime(9999, 12, 31), SqlValueKind.Date), "date");

    // A REFUSAL is a valid outcome, not a crash: the writer deliberately declines values the engine
    // cannot carry faithfully (a subnormal double is the measured one). This section asks "of the
    // literals the writer is willing to emit, does the engine accept them?".
    void Check(string what, SqlLiteralResult r, string castTo)
    {
        if (!r.IsWritten)
        {
            Info($"{what}: writer REFUSES ({r.Refusal}) — by design, nothing to execute");
            return;
        }
        var literal = r.Literal!;
        try
        {
            Scalar($"select cast({literal} as {castTo}) from rdb$database");
            Pass(what, literal.Length > 40 ? literal[..40] + "…" : literal);
        }
        catch (Exception ex)
        {
            Fail(what, $"literal {(literal.Length > 30 ? literal[..30] + "…" : literal)} → {ex.Message.Split('\n')[0]}");
        }
    }

    // Exactness of the float round-trip through a literal (not just "it parses").
    foreach (var d in new[] { 0.1d, 2.718281828459045d, 1e20d, double.Epsilon, double.MaxValue })
    {
        var w = SqlLiteralWriter.Write(d, SqlValueKind.Float);
        if (!w.IsWritten)
        {
            // double.Epsilon lands here now — and that IS the fix: the engine accepts its literal and
            // silently returns 0, so refusing is the only faithful answer.
            Info($"double {d:R}: writer REFUSES ({w.Refusal}) — the engine would have zeroed it silently");
            continue;
        }
        var lit = w.Literal!;
        try
        {
            var back = Convert.ToDouble(Scalar($"select cast({lit} as double precision) from rdb$database"), CultureInfo.InvariantCulture);
            if (back == d) Pass($"double reparse-exact {lit}");
            else Fail($"double reparse-exact {lit}", $"came back {back:R}");
        }
        catch (Exception ex) { Fail($"double reparse-exact {lit}", ex.Message.Split('\n')[0]); }
    }

    // ── 3. Literal ceilings — the numbers SqlLiteralLimits is set from ───────
    Head("3. Literal ceilings (SqlLiteralLimits defaults must match these)");

    int MaxOk(Func<int, string> build, int lo, int hi)
    {
        // largest n that parses
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            bool ok;
            try { Scalar($"select {build(mid)} from rdb$database"); ok = true; }
            catch { ok = false; }
            if (ok) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    var maxAscii = MaxOk(n => "'" + new string('a', n) + "'", 1, 70000);
    Info($"max ASCII string literal (UTF8 connection): {maxAscii} chars");

    var maxPolish = MaxOk(n => "'" + new string('ą', n) + "'", 1, 70000);
    Info($"max 2-byte-char string literal (UTF8 connection): {maxPolish} chars ({maxPolish * 2} bytes)");

    var maxHexBytes = MaxOk(n => "x'" + new string('A', n * 2) + "'", 1, 70000);
    Info($"max hex literal: {maxHexBytes} bytes ({maxHexBytes * 2} hex digits)");

    Console.WriteLine($"\n  → SqlLiteralLimits.MaxBinaryBytes  is {SqlLiteralLimits.Default.MaxBinaryBytes}, engine allows {maxHexBytes}");
    Console.WriteLine($"  → SqlLiteralLimits.MaxTextBlobChars is {SqlLiteralLimits.Default.MaxTextBlobChars}, engine allows {maxAscii} ASCII / {maxPolish} 2-byte chars");
    if (SqlLiteralLimits.Default.MaxBinaryBytes <= maxHexBytes) Pass("MaxBinaryBytes is within the engine's limit");
    else Fail("MaxBinaryBytes is within the engine's limit", $"{SqlLiteralLimits.Default.MaxBinaryBytes} > {maxHexBytes}");
    if (SqlLiteralLimits.Default.MaxTextBlobChars <= Math.Min(maxAscii, maxPolish)) Pass("MaxTextBlobChars is within the engine's limit");
    else Fail("MaxTextBlobChars is within the engine's limit", $"{SqlLiteralLimits.Default.MaxTextBlobChars} > {Math.Min(maxAscii, maxPolish)}");

    // Does an over-ceiling literal fail LOUDLY (acceptable) or silently truncate (unacceptable)?
    try
    {
        var over = new string('a', maxAscii + 1);
        var back = (string?)Scalar($"select '{over}' from rdb$database");
        Fail("over-ceiling string literal fails loudly", $"it did NOT fail — returned {back?.Length} chars (TRUNCATION RISK)");
    }
    catch (Exception ex) { Pass("over-ceiling string literal fails loudly", ex.Message.Split('\n')[0]); }

    // A blob bigger than any literal: confirm there is no single-literal form (justifies TooLarge).
    try
    {
        Exec("create table BIGBLOB (ID integer, B blob sub_type 0)");
        var hex = new string('A', (maxHexBytes + 100) * 2);
        Exec($"insert into BIGBLOB values (1, x'{hex}')");
        Fail("a blob above the hex ceiling has no literal form", "an over-ceiling hex literal was ACCEPTED");
    }
    catch (Exception ex) { Pass("a blob above the hex ceiling has no literal form", ex.Message.Split('\n')[0]); }

    // ── 4. Multi-row VALUES (the user's post-E3 question) ────────────────────
    Head("4. Multi-row VALUES — is `values (1,'a'),(2,'b')` supported?");

    Exec("create table MULTI (ID integer, TXT varchar(20))");
    try
    {
        Exec("insert into MULTI (ID, TXT) values (1, 'John'), (2, 'Adam'), (3, 'Kate')");
        var n = Convert.ToInt32(Scalar("select count(*) from MULTI"), CultureInfo.InvariantCulture);
        Pass("multi-row VALUES", $"SUPPORTED — inserted {n} rows in one statement");
    }
    catch (Exception ex)
    {
        Info($"multi-row VALUES → NOT SUPPORTED: {ex.Message.Split('\n')[0]}");
    }

    // The portable multi-row alternative, if the above is unsupported.
    try
    {
        Exec("delete from MULTI");
        Exec("""
            insert into MULTI (ID, TXT)
            select 1, 'John' from rdb$database
            union all select 2, 'Adam' from rdb$database
            union all select 3, 'Kate' from rdb$database
            """);
        var n = Convert.ToInt32(Scalar("select count(*) from MULTI"), CultureInfo.InvariantCulture);
        Info($"INSERT…SELECT UNION ALL alternative: works — {n} rows");
    }
    catch (Exception ex) { Info($"INSERT…SELECT UNION ALL alternative: {ex.Message.Split('\n')[0]}"); }

    // ── 5. What the driver actually returns (pins the writer's type expectations) ──
    Head("5. Driver CLR types per Firebird type (pins UnexpectedValueType)");
    using (var c = new FbCommand("select I_SMALL, I_INT, I_BIG, N_NUM, F_FLOAT, F_DOUBLE, S_VARCHAR, D_DATE, T_TIME, TS_STAMP, B_BOOL, BL_BIN, BL_TXT from PROBE where ID = 1", cn))
    using (var rd = c.ExecuteReader())
    {
        rd.Read();
        for (int i = 0; i < rd.FieldCount; i++)
            Info($"{rd.GetName(i),-10} → {rd.GetValue(i).GetType().Name,-10} (GetFieldType: {rd.GetFieldType(i).Name})");
    }

    // ── 6. E2 — what does GetSchemaTable() actually expose? ─────────────────
    // The design measured BaseTableName/BaseColumnName/IsExpression/IsKey. What it did NOT establish is
    // how to read a column's DECLARED Firebird type from a reader — which is exactly what SqlValueKind
    // needs (DATE and TIMESTAMP are the same CLR type). Dump the whole schema table and find out.
    Head("6. E2 — GetSchemaTable() surface (which column carries the declared FbDbType?)");

    using (var c = new FbCommand("select I_INT, N_NUM, S_VARCHAR, D_DATE, T_TIME, TS_STAMP, B_BOOL, BL_BIN, BL_TXT from PROBE where ID = 1", cn))
    using (var rd = c.ExecuteReader(CommandBehavior.SchemaOnly))
    {
        var schema = rd.GetSchemaTable();
        if (schema is null) { Fail("GetSchemaTable", "returned null"); }
        else
        {
            Info("schema columns: " + string.Join(", ", schema.Columns.Cast<DataColumn>()
                .Select(x => $"{x.ColumnName}:{x.DataType.Name}")));
            Console.WriteLine();
            foreach (DataRow row in schema.Rows)
            {
                var parts = new List<string>();
                foreach (var name in new[] { "ColumnName", "BaseTableName", "BaseColumnName", "IsExpression", "IsKey", "ProviderType", "DataType", "ProviderSpecificDataType" })
                {
                    if (schema.Columns.Contains(name))
                    {
                        var v = row[name];
                        parts.Add($"{name}={(v is DBNull ? "<null>" : v)}");
                    }
                }
                Info(string.Join("  ", parts));
            }
        }

        // Is ProviderType castable to FbDbType? That is the whole question for the capture step.
        var st = rd.GetSchemaTable();
        if (st is not null && st.Columns.Contains("ProviderType"))
        {
            Console.WriteLine();
            for (int i = 0; i < st.Rows.Count; i++)
            {
                var raw = st.Rows[i]["ProviderType"];
                var name = st.Rows[i]["ColumnName"];
                string asFb;
                try { asFb = ((FbDbType)Convert.ToInt32(raw, CultureInfo.InvariantCulture)).ToString(); }
                catch (Exception ex) { asFb = "NOT AN FbDbType: " + ex.GetType().Name; }
                Info($"{name,-10} ProviderType raw={raw} ({raw.GetType().Name}) → as FbDbType: {asFb}");
            }
        }

        // The alternative source, if ProviderType is not usable.
        Console.WriteLine();
        for (int i = 0; i < rd.FieldCount; i++)
            Info($"{rd.GetName(i),-10} GetDataTypeName = {rd.GetDataTypeName(i)}");
    }

    // §1.1 / §1.2 / §1.3 re-confirmation — cheap, and these are the findings the whole design rests on.
    Head("6b. E2 — the provenance traps (re-confirm on this engine)");

    void Provenance(string label, string sql)
    {
        try
        {
            using var c = new FbCommand(sql, cn);
            using var rd = c.ExecuteReader(CommandBehavior.SchemaOnly);
            var st = rd.GetSchemaTable();
            if (st is null) { Info($"{label}: no schema table"); return; }
            var rows = st.Rows.Cast<DataRow>().Select(r =>
                $"{r["ColumnName"]}→{(r["BaseTableName"] is DBNull or "" ? "<none>" : r["BaseTableName"])}." +
                $"{(r["BaseColumnName"] is DBNull ? "<null>" : r["BaseColumnName"])}" +
                $"{(st.Columns.Contains("IsKey") && r["IsKey"] is bool k && k ? " [IsKey]" : "")}");
            Info($"{label}: {string.Join(" | ", rows)}");
        }
        catch (Exception ex) { Info($"{label}: ERROR {ex.Message.Split('\n')[0]}"); }
    }

    Exec("create table OI (ORDER_ID integer not null, LINE_NO integer not null, QTY integer, primary key (ORDER_ID, LINE_NO))");
    Exec("create table CUST (CUSTOMER_ID integer not null primary key, NAME varchar(30))");
    Exec("create table PROD (PRODUCT_ID integer not null primary key, NAME varchar(30))");
    Exec("create view V_CUST as select CUSTOMER_ID, NAME from CUST");

    Provenance("alias de-aliasing      ", "select c.CUSTOMER_ID as CID from CUST c");
    Provenance("derived expression     ", "select CUSTOMER_ID * 2 as DOUBLED, count(*) as CNT from CUST group by CUSTOMER_ID");
    Provenance("literal                ", "select 1 as LIT from rdb$database");
    Provenance("PARTIAL composite PK   ", "select ORDER_ID, QTY from OI");
    Provenance("UNION of two tables    ", "select CUSTOMER_ID, NAME from CUST union all select PRODUCT_ID, NAME from PROD");
    Provenance("self-join              ", "select a.CUSTOMER_ID, b.CUSTOMER_ID from CUST a join CUST b on a.CUSTOMER_ID = b.CUSTOMER_ID");
    Provenance("duplicate column       ", "select CUSTOMER_ID, CUSTOMER_ID as AGAIN from CUST");
    Provenance("view                   ", "select CUSTOMER_ID, NAME from V_CUST");
    Provenance("derived table          ", "select * from (select CUSTOMER_ID, NAME from CUST) x");

    // The §1.2 headline: a WHERE built from a partial composite PK hits more than one row.
    try
    {
        Exec("insert into OI values (1, 1, 10)");
        Exec("insert into OI values (1, 2, 20)");
        var hit = Convert.ToInt32(Scalar("select count(*) from OI where ORDER_ID = 1"), CultureInfo.InvariantCulture);
        if (hit > 1) Pass("partial-PK WHERE hits >1 row", $"WHERE ORDER_ID = 1 matches {hit} rows — the refusal is justified");
        else Fail("partial-PK WHERE hits >1 row", $"only {hit} — the premise would need re-checking");
    }
    catch (Exception ex) { Fail("partial-PK WHERE", ex.Message.Split('\n')[0]); }

    // ── 7. E3 prep — RDB$IDENTITY_TYPE encoding ─────────────────────────────
    // Needed for OVERRIDING SYSTEM VALUE. The codebase collapses this to a bool today, so the
    // ALWAYS/BY DEFAULT distinction does not exist anywhere yet.
    Head("7. E3 prep — RDB$IDENTITY_TYPE encoding + OVERRIDING SYSTEM VALUE");
    try
    {
        Exec("create table IDENT (ID_ALWAYS integer generated always as identity, A integer)");
        Exec("create table IDENT2 (ID_DEFAULT integer generated by default as identity, A integer)");
        using var c = new FbCommand(
            "select TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), rf.RDB$IDENTITY_TYPE " +
            "from RDB$RELATION_FIELDS rf where rf.RDB$RELATION_NAME in ('IDENT','IDENT2') " +
            "and rf.RDB$IDENTITY_TYPE is not null order by 1", cn);
        using var rd = c.ExecuteReader();
        while (rd.Read())
            Info($"{rd.GetString(0),-7}.{rd.GetString(1),-11} RDB$IDENTITY_TYPE = {rd.GetValue(2)}  ← (which is ALWAYS?)");
    }
    catch (Exception ex) { Info("identity type: " + ex.Message.Split('\n')[0]); }

    try { Exec("insert into IDENT (ID_ALWAYS, A) values (5, 1)"); Fail("GENERATED ALWAYS rejects a plain INSERT", "it was ACCEPTED"); }
    catch (Exception ex) { Pass("GENERATED ALWAYS rejects a plain INSERT", ex.Message.Split('\n')[0]); }

    try { Exec("insert into IDENT (ID_ALWAYS, A) overriding system value values (5, 1)"); Pass("OVERRIDING SYSTEM VALUE works"); }
    catch (Exception ex) { Fail("OVERRIDING SYSTEM VALUE works", ex.Message.Split('\n')[0]); }

    try { Exec("insert into IDENT2 (ID_DEFAULT, A) values (5, 1)"); Pass("GENERATED BY DEFAULT accepts a plain INSERT"); }
    catch (Exception ex) { Fail("GENERATED BY DEFAULT accepts a plain INSERT", ex.Message.Split('\n')[0]); }

    // ── 8. Boundary refinement — the two numbers E1 must encode ─────────────
    Head("8a. Smallest double that survives a literal round-trip (subnormals silently become 0)");

    // 5E-324 PARSES but comes back 0 — the literal is accepted and the value is destroyed, which is the
    // one failure mode §0 forbids. Find where it starts so WriteFloat can refuse below it.
    foreach (var d in new[]
    {
        double.Epsilon,             // 5E-324  subnormal
        1e-320,                     // subnormal
        1e-310,                     // subnormal
        2.2250738585072014e-308,    // smallest NORMAL double
        1e-300, 1e-200, 1e-100,
    })
    {
        // Deliberately bypasses the writer's refusal: this section measures the ENGINE, and needs to see
        // the loss the writer now exists to prevent. Rendering it here proves the guard is aimed right.
        var lit = d.ToString("R", CultureInfo.InvariantCulture);
        var refused = !SqlLiteralWriter.Write(d, SqlValueKind.Float).IsWritten;
        try
        {
            var back = Convert.ToDouble(Scalar($"select cast({lit} as double precision) from rdb$database"), CultureInfo.InvariantCulture);
            var exact = back == d;
            Info($"{lit,-26} → {back.ToString("R", CultureInfo.InvariantCulture),-26} {(exact ? "exact" : "*** LOST ***")}"
                 + $"  (subnormal={double.IsSubnormal(d)}, writer refuses={refused})");
            if (!exact && !refused) Fail($"writer must refuse {lit}", "the engine destroys it and we would emit it");
            if (exact && refused) Fail($"writer must NOT refuse {lit}", "it round-trips exactly — this is a false refusal");
        }
        catch (Exception ex) { Info($"{lit,-26} → rejected: {ex.Message.Split('\n')[0]}"); }
    }

    Head("8b. Is the literal ceiling a property of the LITERAL or of the STATEMENT?");

    // If the ceiling moves with the surrounding statement, no constant can be sound and the limit must
    // be far more conservative than the measured maximum.
    int MaxOk2(Func<int, string> build, int lo, int hi)
    {
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            bool ok;
            try { Exec(build(mid)); ok = true; } catch { ok = false; }
            if (ok) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    Exec("create table HEXT (ID integer, B blob sub_type 0)");
    var bare = MaxOk2(n => $"insert into HEXT values (1, x'{new string('A', n * 2)}')", 1, 40000);
    Info($"max hex bytes in a SHORT insert:  {bare}");

    var padded = MaxOk2(n =>
        $"insert into HEXT values (1 /*{new string('p', 4000)}*/, x'{new string('A', n * 2)}')", 1, 40000);
    Info($"max hex bytes with 4 KB of extra statement text: {padded}");

    // The ceiling MOVING is the established finding, now encoded in the design — so that is the PASS.
    // The limit is on the STATEMENT (~65,535 chars), not the literal, which is why a per-value ceiling is
    // necessary but never sufficient and SqlStatementBuilder checks the assembled length. If this ever
    // stops moving, the statement budget would be over-cautious and worth revisiting.
    if (bare != padded)
        Pass("hex ceiling moves with statement length (⇒ the limit is the STATEMENT)",
            $"{bare} → {padded} with 4 KB of extra text; SqlStatementBuilder.MaxStatementLength covers it");
    else
        Fail("hex ceiling moves with statement length", $"it did NOT move (stable at {bare}) — re-check the statement-budget rule");

    Head("8c. Is the string ceiling charset-dependent? (Core does not know the connection charset)");
    Info("this connection is UTF8; the user's real lab is WIN1250 — if these differ, the constant must be the worst case");

    var utf8Max = MaxOk(n => "'" + new string('a', n) + "'", 1, 70000);
    Info($"UTF8 connection: {utf8Max} chars  (= 32765/4 if Firebird reserves 4 bytes per UTF8 char)");

    // Same question over a WIN1250 database — one byte per char.
    const string Win1250Db = @"C:\Temp\et_sqlexport_probe_w1250.fdb";
    try
    {
        var w = new FbConnectionStringBuilder(cs) { Database = Win1250Db, Charset = "WIN1250" }.ToString();
        if (File.Exists(Win1250Db)) File.Delete(Win1250Db);
        FbConnection.CreateDatabase(w, overwrite: true);
        using var wcn = new FbConnection(w);
        wcn.Open();
        int WMax(int lo, int hi)
        {
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                bool ok;
                try
                {
                    using var c = new FbCommand("select '" + new string('a', mid) + "' from rdb$database", wcn);
                    c.ExecuteScalar();
                    ok = true;
                }
                catch { ok = false; }
                if (ok) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
        var w1250Max = WMax(1, 70000);
        Info($"WIN1250 connection: {w1250Max} chars");
        if (w1250Max != utf8Max)
            Info($"⇒ CHARSET-DEPENDENT ({utf8Max} vs {w1250Max}). Core cannot know the charset ⇒ the ceiling must be the "
                 + $"worst case, {Math.Min(utf8Max, w1250Max)} chars.");
        else
            Info("⇒ charset-independent — one constant is sound.");
        wcn.Close();
        FbConnection.ClearAllPools();
        try { File.Delete(Win1250Db); } catch { /* scratch */ }
    }
    catch (Exception ex) { Info("WIN1250 probe: " + ex.Message.Split('\n')[0]); }

    Head("8d. Does a FLOAT-column subnormal survive? And what IS the statement-length limit?");

    // 8a proved DOUBLE subnormals die. A float subnormal (~1.4E-45) is a NORMAL double, so it should
    // survive the literal parser — but "should" is what this probe exists to replace.
    Exec("create table FL (ID integer, F float, D double precision)");
    foreach (var f in new[] { float.Epsilon, 1.4e-45f, 1.17549435e-38f /* smallest normal float */ })
    {
        var lit = SqlLiteralWriter.Write(f, SqlValueKind.Float).Literal!;
        try
        {
            Exec("delete from FL");
            Exec($"insert into FL (ID, F) values (1, {lit})");
            var back = (float)Convert.ToSingle(Scalar("select F from FL where ID = 1"), CultureInfo.InvariantCulture);
            Info($"float {lit,-12} → {back:R,-14} {(back == f ? "exact" : "*** LOST ***")}  (float subnormal={float.IsSubnormal(f)}, as double subnormal={double.IsSubnormal(f)})");
        }
        catch (Exception ex) { Info($"float {lit,-12} → rejected: {ex.Message.Split('\n')[0]}"); }
    }

    // 8b showed the ceiling moves with statement length ⇒ the real limit is on the STATEMENT. Find it:
    // E3's statement builder needs this number, because a per-literal ceiling cannot see two big blobs.
    int MaxStmt(int lo, int hi)
    {
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            // pad inside a comment: parsed away, but it is still statement TEXT
            var sql = $"select 1 /*{new string('p', mid)}*/ from rdb$database";
            bool ok;
            try { Scalar(sql); ok = true; } catch { ok = false; }
            if (ok) lo = mid; else hi = mid - 1;
        }
        return lo;
    }
    var pad = MaxStmt(1, 200000);
    var total = pad + "select 1 /**/ from rdb$database".Length;
    Info($"max statement text: ~{total} chars (padding {pad} + {total - pad} of statement)");
    Info($"  → 65535 would mean a 64 KB DSQL limit; E3's builder must refuse an assembled statement above it");

    // ── 9. E3/E4 END-TO-END — the safety property, executed ─────────────────
    // The milestone's whole claim in one check: run the REAL chain (schema table → capture → AST shape →
    // resolver → builder), EXECUTE the generated statement, and count the rows it touched. A generated
    // UPDATE that matches more than one row is the failure this design exists to prevent, and it is not
    // provable by any amount of unit testing — the naive version SUCCEEDS.
    Head("9. E3/E4 end-to-end: generate → EXECUTE → count affected rows");

    var catalog = new ProbeCatalog(cn);

    ResultOrigin OriginFor(string sql)
    {
        using var c = new FbCommand(sql, cn);
        using var rd = c.ExecuteReader(CommandBehavior.SchemaOnly);
        return new ResultOrigin(
            FirebirdResultOriginReader.ReadColumnOrigins(rd.GetSchemaTable()!),
            StatementShapeReader.Read(sql));
    }

    object?[] FirstRow(string sql)
    {
        using var c = new FbCommand(sql, cn);
        using var rd = c.ExecuteReader();
        rd.Read();
        var row = new object?[rd.FieldCount];
        rd.GetValues(row!);
        return row;
    }

    int Affected(string sql)
    {
        using var c = new FbCommand(sql, cn);
        return c.ExecuteNonQuery();
    }

    // (a) The partial-PK shape: two ORDER_ITEMS rows share ORDER_ID = 1. The driver reports ORDER_ID
    //     IsKey=True, so the naive WHERE hits BOTH. We must refuse instead.
    var partial = ResultOriginResolver.Resolve(OriginFor("select ORDER_ID, QTY from OI"), catalog);
    var partialUpdate = SqlFormatAvailability.ForUpdate(partial);
    if (!partialUpdate.IsAvailable && partialUpdate.Reason!.Code == ExportUnavailableCode.IncompletePrimaryKey)
        Pass("partial PK ⇒ UPDATE refused", $"missing: {string.Join(",", partialUpdate.Reason.Names)}");
    else
        Fail("partial PK ⇒ UPDATE refused", $"available={partialUpdate.IsAvailable} reason={partialUpdate.Reason?.Code}");

    // (b) The complete-PK shape: the generated UPDATE must touch EXACTLY ONE row.
    var full = ResultOriginResolver.Resolve(OriginFor("select ORDER_ID, LINE_NO, QTY from OI"), catalog);
    if (full is TargetResolution.Resolved fullTarget)
    {
        var built = SqlStatementBuilder.BuildUpdate(fullTarget, FirstRow("select ORDER_ID, LINE_NO, QTY from OI order by LINE_NO"));
        if (!built.IsBuilt) Fail("complete PK ⇒ UPDATE builds", built.Reason!.Code.ToString());
        else
        {
            Console.WriteLine("  generated: " + built.Sql);
            var hit = Affected(built.Sql!);
            if (hit == 1) Pass("generated UPDATE affects EXACTLY 1 row", $"affected={hit}");
            else Fail("generated UPDATE affects EXACTLY 1 row", $"*** affected={hit} — THE FAILURE THIS DESIGN EXISTS TO PREVENT ***");
        }
    }
    else Fail("complete PK ⇒ resolves", ((TargetResolution.Unavailable)full).Reason.Code.ToString());

    // (c) The UNION: provenance says a clean CUST result. Only the AST saves us.
    var union = ResultOriginResolver.Resolve(
        OriginFor("select CUSTOMER_ID, NAME from CUST union all select PRODUCT_ID, NAME from PROD"), catalog);
    if (union is TargetResolution.Unavailable { Reason.Code: ExportUnavailableCode.SetOperation })
        Pass("UNION ⇒ refused", "signal A reported a clean CUST result; the AST vetoed it");
    else Fail("UNION ⇒ refused", union.ToString() ?? "?");

    // (d) A view is refused (this stage's decision).
    var view = ResultOriginResolver.Resolve(OriginFor("select CUSTOMER_ID, NAME from V_CUST"), catalog);
    if (view is TargetResolution.Unavailable { Reason.Code: ExportUnavailableCode.NotATable })
        Pass("view ⇒ refused as not-a-table");
    else Fail("view ⇒ refused as not-a-table", view.ToString() ?? "?");

    // (e) The generated INSERT actually RUNS — including OVERRIDING SYSTEM VALUE on a GENERATED ALWAYS
    //     identity, which is the case that fails on the user's own lab (PRODUCTS.PRODUCT_ID).
    Exec("delete from IDENT");
    var identOrigin = OriginFor("select ID_ALWAYS, A from IDENT");
    var identResolved = ResultOriginResolver.Resolve(identOrigin, catalog);
    if (identResolved is TargetResolution.Resolved identTarget)
    {
        var built = SqlStatementBuilder.BuildInsert(identTarget, new object?[] { 42, 7 });
        if (!built.IsBuilt) Fail("GENERATED ALWAYS INSERT builds", built.Reason!.Code.ToString());
        else
        {
            Console.WriteLine("  generated: " + built.Sql);
            if (!built.Sql!.Contains("OVERRIDING SYSTEM VALUE", StringComparison.Ordinal))
                Fail("GENERATED ALWAYS INSERT carries OVERRIDING", built.Sql);
            else
            {
                try
                {
                    Affected(built.Sql);
                    var back = Convert.ToInt32(Scalar("select ID_ALWAYS from IDENT where A = 7"), CultureInfo.InvariantCulture);
                    if (back == 42) Pass("generated INSERT runs and preserves the ALWAYS identity", "ID_ALWAYS = 42");
                    else Fail("generated INSERT preserves the ALWAYS identity", $"got {back}");
                }
                catch (Exception ex) { Fail("generated INSERT runs", ex.Message.Split('\n')[0]); }
            }
        }
    }
    else Fail("identity table resolves", ((TargetResolution.Unavailable)identResolved).Reason.Code.ToString());

    // (f) A round-trip through the REAL chain: copy a row as INSERT, run it, read it back, compare.
    Exec("delete from PROBE where ID = 3");
    var probeOrigin = OriginFor("select ID, S_VARCHAR, D_DATE, TS_STAMP, BL_BIN from PROBE where ID = 1");
    if (ResultOriginResolver.Resolve(probeOrigin, catalog) is TargetResolution.Resolved probeTarget)
    {
        var source = FirstRow("select ID, S_VARCHAR, D_DATE, TS_STAMP, BL_BIN from PROBE where ID = 1");
        source[0] = 3; // a new key
        var built = SqlStatementBuilder.BuildInsert(probeTarget, source);
        if (!built.IsBuilt) Fail("copy-a-row INSERT builds", built.Reason!.Code.ToString());
        else
        {
            Affected(built.Sql!);
            var copied = FirstRow("select ID, S_VARCHAR, D_DATE, TS_STAMP, BL_BIN from PROBE where ID = 3");
            bool same = Equals(copied[1], source[1]) && Equals(copied[2], source[2])
                        && Equals(copied[3], source[3]) && ((byte[])copied[4]!).SequenceEqual((byte[])source[4]!);
            if (same) Pass("copy-a-row INSERT round-trips every value", "text/date/timestamp/blob all identical");
            else Fail("copy-a-row INSERT round-trips every value", $"{Show(copied[1])} | {Show(copied[2])} | {Show(copied[3])}");
        }
    }

    // ── 10. E6 — Table Data via OriginShape.DirectTable (the adapter milestone) ──
    // E6 reuses the exact same chain, but the grid IS a table, so it declares DirectTable instead of
    // re-analysing a statement. The provenance comes from the SAME schema-table capture the reader's new
    // Data-lane seam does: SELECT * FROM "T" + SchemaOnly + GetSchemaTable → FirebirdResultOriginReader.
    // These checks prove that path is at least as safe as section 9's Statement path on the real engine.
    Head("10. E6 — DirectTable origin (Table Data grid): generate → EXECUTE → count");

    ResultOrigin DirectOriginFor(string table)
    {
        // EXACTLY what FirebirdTableDetailReader.CaptureDataSchemaTableAsync does, minus the lane/lock
        // plumbing (orthogonal to correctness): SELECT * FROM "T", schema only, read the origins.
        var quoted = table.Replace("\"", "\"\"");
        using var c = new FbCommand($"select * from \"{quoted}\"", cn);
        using var rd = c.ExecuteReader(CommandBehavior.SchemaOnly);
        return new ResultOrigin(
            FirebirdResultOriginReader.ReadColumnOrigins(rd.GetSchemaTable()!),
            new OriginShape.DirectTable(table));
    }

    // (a) single-column PK — the generated UPDATE must touch EXACTLY ONE row.
    var custDirect = ResultOriginResolver.Resolve(DirectOriginFor("CUST"), catalog);
    if (custDirect is TargetResolution.Resolved custTarget)
    {
        Exec("delete from CUST");
        Exec("insert into CUST (CUSTOMER_ID, NAME) values (1, 'Ann')");
        Exec("insert into CUST (CUSTOMER_ID, NAME) values (2, 'Bob')");
        var built = SqlStatementBuilder.BuildUpdate(custTarget, FirstRow("select * from CUST order by CUSTOMER_ID"));
        if (!built.IsBuilt) Fail("DirectTable single-PK UPDATE builds", built.Reason!.Code.ToString());
        else
        {
            Console.WriteLine("  generated: " + built.Sql);
            var hit = Affected(built.Sql!);
            if (hit == 1) Pass("DirectTable UPDATE affects EXACTLY 1 row", $"affected={hit}");
            else Fail("DirectTable UPDATE affects EXACTLY 1 row", $"*** affected={hit} ***");
        }
    }
    else Fail("DirectTable CUST resolves", ((TargetResolution.Unavailable)custDirect).Reason.Code.ToString());

    // (b) composite PK — SELECT * projects every key column, so the UPDATE is safe (one row).
    var oiDirect = ResultOriginResolver.Resolve(DirectOriginFor("OI"), catalog);
    if (oiDirect is TargetResolution.Resolved oiTarget)
    {
        var built = SqlStatementBuilder.BuildUpdate(oiTarget, FirstRow("select * from OI order by LINE_NO"));
        if (built.IsBuilt && Affected(built.Sql!) == 1)
            Pass("DirectTable composite-PK UPDATE affects EXACTLY 1 row");
        else
            Fail("DirectTable composite-PK UPDATE affects EXACTLY 1 row", built.IsBuilt ? "affected != 1" : built.Reason!.Code.ToString());
    }
    else Fail("DirectTable OI resolves", ((TargetResolution.Unavailable)oiDirect).Reason.Code.ToString());

    // (c) GENERATED ALWAYS identity — the INSERT must carry OVERRIDING SYSTEM VALUE and run.
    Exec("delete from IDENT");
    var identDirect = ResultOriginResolver.Resolve(DirectOriginFor("IDENT"), catalog);
    if (identDirect is TargetResolution.Resolved identDirTarget)
    {
        var built = SqlStatementBuilder.BuildInsert(identDirTarget, new object?[] { 99, 3 });
        if (built.IsBuilt && built.Sql!.Contains("OVERRIDING SYSTEM VALUE", StringComparison.Ordinal))
        {
            Affected(built.Sql);
            var back = Convert.ToInt32(Scalar("select ID_ALWAYS from IDENT where A = 3"), CultureInfo.InvariantCulture);
            if (back == 99) Pass("DirectTable INSERT preserves the ALWAYS identity", "ID_ALWAYS = 99");
            else Fail("DirectTable INSERT preserves the ALWAYS identity", $"got {back}");
        }
        else Fail("DirectTable GENERATED ALWAYS INSERT carries OVERRIDING", built.IsBuilt ? built.Sql! : built.Reason!.Code.ToString());
    }
    else Fail("DirectTable IDENT resolves", ((TargetResolution.Unavailable)identDirect).Reason.Code.ToString());

    // (d) a view's DirectTable origin is still refused — the catalog knows CUST-vs-view, so declaring
    //     DirectTable does not smuggle a non-updatable object past signal C.
    var viewDirect = ResultOriginResolver.Resolve(DirectOriginFor("V_CUST"), catalog);
    if (viewDirect is TargetResolution.Unavailable { Reason.Code: ExportUnavailableCode.NotATable })
        Pass("DirectTable on a view ⇒ still refused as not-a-table");
    else Fail("DirectTable on a view ⇒ still refused", viewDirect.ToString() ?? "?");

    // ── 11. QA — integer/numeric PK literals (the ID_NAGL refusal) ───────────
    // A real ERP PK (NUMERIC(18,0)) copied as INSERT was refused "value has no exact SQL literal". The
    // suspect is a kind/value-type mismatch: ProviderType may say one thing while the driver returns
    // another CLR type, and SqlLiteralWriter refuses the pairing. Measure it for every integer-ish PK
    // shape: what ProviderType/FbDbType is reported, what CLR type comes back, the mapped SqlValueKind,
    // and whether the REAL chain builds a literal or refuses.
    Head("11. QA — integer/numeric PK literal rendering");

    var pkTypes = new (string Name, string Ddl)[]
    {
        ("PK_SMALLINT", "smallint"),
        ("PK_INTEGER",  "integer"),
        ("PK_BIGINT",   "bigint"),
        ("PK_NUM40",    "numeric(4,0)"),
        ("PK_NUM90",    "numeric(9,0)"),
        ("PK_NUM180",   "numeric(18,0)"),
        ("PK_DEC180",   "decimal(18,0)"),
        ("PK_NUM182",   "numeric(18,2)"),
        // FB4+ INT128-backed — the unmapped-kind suspects for the ID_NAGL refusal.
        ("PK_NUM380",   "numeric(38,0)"),
        ("PK_DEC380",   "decimal(38,0)"),
        ("PK_INT128",   "int128"),
    };

    // Domain-typed PK (the Streamsoft-style shape): does a column declared over a DOMAIN report its
    // ProviderType the same way a bare type does, or does it come back null/odd → SqlValueKind.Unknown?
    try
    {
        Exec("create domain D_ID_NAGL as numeric(18,0)");
        Exec("create table T_PK_DOMAIN (ID D_ID_NAGL not null primary key, TXT varchar(10))");
        Exec("insert into T_PK_DOMAIN (ID, TXT) values (7, 'x')");
        DataTable ds;
        using (var c = new FbCommand("select * from T_PK_DOMAIN", cn))
        using (var rd = c.ExecuteReader(CommandBehavior.SchemaOnly))
            ds = rd.GetSchemaTable()!;
        var raw = ds.Rows[0]["ProviderType"];
        var origins = FirebirdResultOriginReader.ReadColumnOrigins(ds);
        var val = Scalar("select ID from T_PK_DOMAIN");
        var res = ResultOriginResolver.Resolve(
            new ResultOrigin(origins, new OriginShape.DirectTable("T_PK_DOMAIN")), new ProbeCatalog(cn));
        string build = res is TargetResolution.Resolved rt
            ? (SqlStatementBuilder.BuildInsert(rt, FirstRow("select * from T_PK_DOMAIN")) is { IsBuilt: true } b
                ? "BUILT: " + b.Sql : "REFUSED: " + SqlStatementBuilder.BuildInsert(rt, FirstRow("select * from T_PK_DOMAIN")).Reason!.Code)
            : "UNRESOLVED";
        Info($"DOMAIN numeric(18,0)  ProviderType={raw}  kind={origins[0].ValueKind}  CLR={val?.GetType().Name}  → {build}");
    }
    catch (Exception ex) { Info("domain PK: " + ex.Message.Split('\n')[0]); }

    foreach (var (name, ddl) in pkTypes)
    {
        var table = "T_" + name;
        try
        {
            Exec($"create table {table} (ID {ddl} not null primary key, TXT varchar(10))");
            Exec($"insert into {table} (ID, TXT) values (7, 'x')");

            // What the driver reports for the column (ProviderType) and returns for the value (CLR type).
            string providerType = "?", clrType = "?";
            SqlValueKind kind = SqlValueKind.Unknown;
            using (var c = new FbCommand($"select ID from {table}", cn))
            using (var rd = c.ExecuteReader(CommandBehavior.SchemaOnly))
            {
                var st = rd.GetSchemaTable()!;
                var raw = st.Rows[0]["ProviderType"];
                try
                {
                    var fb = (FbDbType)Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    providerType = $"{raw}→{fb}";
                    kind = FirebirdValueKindMap.ToValueKind(fb);
                }
                catch { providerType = $"{raw} (not FbDbType)"; }
            }
            var val = Scalar($"select ID from {table}");
            clrType = val?.GetType().Name ?? "null";

            // The REAL chain: DirectTable origin → resolve → build INSERT for this row.
            DataTable starSchema;
            using (var c = new FbCommand($"select * from {table}", cn))
            using (var rd = c.ExecuteReader(CommandBehavior.SchemaOnly))
                starSchema = rd.GetSchemaTable()!;
            var origin = new ResultOrigin(
                FirebirdResultOriginReader.ReadColumnOrigins(starSchema),
                new OriginShape.DirectTable(table));
            var res = ResultOriginResolver.Resolve(origin, new ProbeCatalog(cn));
            string build;
            if (res is TargetResolution.Resolved rt)
            {
                var b = SqlStatementBuilder.BuildInsert(rt, FirstRow($"select * from {table}"));
                build = b.IsBuilt ? "BUILT: " + b.Sql : "REFUSED: " + b.Reason!.Code;
            }
            else build = "UNRESOLVED: " + ((TargetResolution.Unavailable)res).Reason.Code;

            var ok = build.StartsWith("BUILT", StringComparison.Ordinal);
            var line = $"{ddl,-14} ProviderType={providerType,-18} CLR={clrType,-10} kind={kind,-9} → {build}";
            if (ok) Pass(name, line);
            else Fail(name, line);
        }
        catch (Exception ex) { Fail(name, $"{ddl}: {ex.Message.Split('\n')[0]}"); }
    }

    // ── 13. QA — a string PK value: which kind does the STATEMENT path resolve? ──
    // The debugger shows ID_NAGL arriving as a STRING "10019" with kind=Integer. GetValue only returns a
    // string for CHAR/VARCHAR/text — so either the column is text (and the kind is wrong → we'd wrongly
    // emit a bare integer) or it is numeric returned oddly. Reproduce the user's exact shape: an ALIASED
    // statement over a VARCHAR PK, through the SQL-editor capture path, and print the resolved kind + CLR.
    Head("13. QA — VARCHAR PK via aliased statement (the ID_NAGL shape)");

    foreach (var (name, ddl) in new[] { ("T_VC", "varchar(10)"), ("T_CH", "char(10)") })
    {
        try
        {
            Exec($"create table {name} (CODE {ddl} not null primary key, TXT varchar(10))");
            Exec($"insert into {name} (CODE, TXT) values ('10019', 'x')");
            var sql = $"select n.CODE, n.TXT from {name} n";  // aliased, like the user's query
            using var c = new FbCommand(sql, cn);
            using var rd = c.ExecuteReader(CommandBehavior.SchemaOnly);
            var st = rd.GetSchemaTable()!;
            var raw = st.Rows[0]["ProviderType"];
            var origins = FirebirdResultOriginReader.ReadColumnOrigins(st);
            var val = Scalar($"select CODE from {name}");
            Info($"{ddl,-12} ProviderType={raw}  resolvedKind={origins[0].ValueKind}  CLR={val?.GetType().Name}  "
                 + $"lit={(SqlLiteralWriter.Write(val, origins[0].ValueKind) is { IsWritten: true } w ? w.Literal : "REFUSED")}");
        }
        catch (Exception ex) { Info($"{name}: {ex.Message.Split('\n')[0]}"); }
    }

    Console.WriteLine($"\n{(failures == 0 ? "ALL CHECKS PASSED" : $"{failures} FAILURE(S)")}");
}
catch (Exception ex)
{
    Console.Error.WriteLine("PROBE ABORTED: " + ex);
    return 3;
}
finally
{
    FbConnection.ClearAllPools();
    try { if (File.Exists(DbPath)) File.Delete(DbPath); } catch { /* scratch file; leave it */ }
}

return failures == 0 ? 0 : 1;

static string Show(object? v) => v switch
{
    null or DBNull => "NULL",
    byte[] b => "x'" + Convert.ToHexString(b) + "'",
    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
    _ => v.ToString() ?? "",
};

// A REAL catalog, read from the live database — signal C. Deliberately not a mock: the point of section
// 9 is that the whole chain works against a real engine, and a fake catalog would quietly assume the one
// thing (PK completeness) the milestone turns on.
internal sealed class ProbeCatalog : ISqlMetadataProvider
{
    private readonly FbConnection _cn;
    private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);

    public ProbeCatalog(FbConnection cn)
    {
        _cn = cn;

        using (var c = new FbCommand(
            "select TRIM(RDB$RELATION_NAME), RDB$VIEW_BLR from RDB$RELATIONS where COALESCE(RDB$SYSTEM_FLAG,0)=0", cn))
        using (var rd = c.ExecuteReader())
        {
            while (rd.Read())
                _objects[rd.GetString(0)] = new ObjectMetadata(
                    rd.GetString(0), rd.IsDBNull(1) ? SymbolKind.Table : SymbolKind.View);
        }

        using (var c = new FbCommand(
            "select TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME), rf.RDB$IDENTITY_TYPE, " +
            "  (select count(*) from RDB$INDEX_SEGMENTS s join RDB$RELATION_CONSTRAINTS rc " +
            "     on rc.RDB$INDEX_NAME = s.RDB$INDEX_NAME " +
            "   where rc.RDB$RELATION_NAME = rf.RDB$RELATION_NAME and rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY' " +
            "     and s.RDB$FIELD_NAME = rf.RDB$FIELD_NAME), " +
            "  f.RDB$COMPUTED_SOURCE " +
            "from RDB$RELATION_FIELDS rf join RDB$FIELDS f on f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
            "where COALESCE(rf.RDB$SYSTEM_FLAG,0)=0", cn))
        using (var rd = c.ExecuteReader())
        {
            while (rd.Read())
            {
                var table = rd.GetString(0);
                if (!_cols.TryGetValue(table, out var list)) _cols[table] = list = new();
                list.Add(new ColumnMetadata(rd.GetString(1), "?")
                {
                    // The measured encoding: 0 = ALWAYS, 1 = BY DEFAULT, NULL = not an identity.
                    Identity = rd.IsDBNull(2)
                        ? IdentityKind.None
                        : rd.GetInt32(2) == 0 ? IdentityKind.Always : IdentityKind.ByDefault,
                    IsPrimaryKey = !rd.IsDBNull(3) && rd.GetInt32(3) > 0,
                    IsComputed = !rd.IsDBNull(4),
                });
            }
        }
    }

    public ObjectMetadata? FindObject(string name)
        => _objects.TryGetValue(name, out var o) ? o : null;

    public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
        => _cols.TryGetValue(tableOrView, out var c) ? c : Array.Empty<ColumnMetadata>();

    public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
        => Array.Empty<RoutineParameterMetadata>();

    public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToArray();
}

using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class DdlGeneratorTests
{
    // ─── Quoting + skeleton CREATE TABLE ──────────────────────────────────

    [Fact]
    public void Quote_DoublesInternalQuotes()
    {
        Assert.Equal("\"FOO\"", DdlGenerator.Quote("FOO"));
        Assert.Equal("\"He said \"\"hi\"\"\"", DdlGenerator.Quote("He said \"hi\""));
    }

    [Fact]
    public void BuildCreateTable_EmitsMinimalSkeleton()
    {
        var sql = DdlGenerator.BuildCreateTable("MY_TABLE");
        Assert.Contains("CREATE TABLE \"MY_TABLE\"", sql);
        Assert.Contains("ID INTEGER NOT NULL PRIMARY KEY", sql);
    }

    [Fact]
    public void BuildCreateTable_ThrowsOnEmptyName()
    {
        Assert.Throws<System.ArgumentException>(() => DdlGenerator.BuildCreateTable("   "));
    }

    // ─── Drop / Move ──────────────────────────────────────────────────────

    [Fact]
    public void BuildDropField_QuotesBothIdentifiers()
    {
        Assert.Equal(
            "ALTER TABLE \"NAGL\" DROP \"OBSOLETE_COL\"",
            DdlGenerator.BuildDropField("NAGL", "OBSOLETE_COL"));
    }

    [Fact]
    public void BuildMoveField_EmitsPositionStatement()
    {
        Assert.Equal(
            "ALTER TABLE \"NAGL\" ALTER \"NAZWA\" POSITION 3",
            DdlGenerator.BuildMoveField("NAGL", "NAZWA", 3));
    }

    [Fact]
    public void BuildMoveField_ThrowsOnZeroOrNegativePosition()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => DdlGenerator.BuildMoveField("T", "F", 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => DdlGenerator.BuildMoveField("T", "F", -1));
    }

    // ─── Add field — basic types ──────────────────────────────────────────

    [Fact]
    public void BuildAddField_Integer_PlainShape()
    {
        var def = new FieldDefinition { Name = "AGE", BasicType = "INTEGER" };
        var sql = DdlGenerator.BuildAddField("PERSON", def);
        Assert.Equal("ALTER TABLE \"PERSON\" ADD \"AGE\" INTEGER", sql);
    }

    [Fact]
    public void BuildAddField_Varchar_IncludesSize()
    {
        var def = new FieldDefinition { Name = "NAZWA", BasicType = "VARCHAR", Size = 80 };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.Contains("VARCHAR(80)", sql);
    }

    [Fact]
    public void BuildAddField_NumericWithScale()
    {
        var def = new FieldDefinition { Name = "CENA", BasicType = "NUMERIC", Precision = 15, Scale = 2 };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.Contains("NUMERIC(15,2)", sql);
    }

    [Fact]
    public void BuildAddField_Blob_IncludesSubType()
    {
        var binary = new FieldDefinition { Name = "DATA", BasicType = "BLOB", BlobSubType = BlobSubType.Binary };
        Assert.Contains("BLOB SUB_TYPE 0", DdlGenerator.BuildAddField("T", binary));

        var text = new FieldDefinition { Name = "OPIS", BasicType = "BLOB", BlobSubType = BlobSubType.Text };
        Assert.Contains("BLOB SUB_TYPE 1", DdlGenerator.BuildAddField("T", text));
    }

    [Fact]
    public void BuildAddField_Domain_WinsOverBasicType()
    {
        var def = new FieldDefinition { Name = "ID_KRAJ", Domain = "T_ID", BasicType = "VARCHAR", Size = 80 };
        var sql = DdlGenerator.BuildAddField("ADRES", def);
        // Generated-DDL identifier style: a regular domain name is UPPERCASE + bare (not quoted).
        Assert.Contains("T_ID", sql);
        Assert.DoesNotContain("\"T_ID\"", sql);
        Assert.DoesNotContain("VARCHAR", sql);
    }

    [Fact]
    public void BuildAddField_NotNullAndDefault_AreEmitted()
    {
        var def = new FieldDefinition
        {
            Name = "FLAGA",
            BasicType = "SMALLINT",
            NotNull = true,
            DefaultValue = "0",
        };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.Contains("DEFAULT 0", sql);
        Assert.Contains("NOT NULL", sql);
    }

    [Fact]
    public void BuildAddField_CheckExpression_WrappedInParens()
    {
        var def = new FieldDefinition
        {
            Name = "RABAT",
            BasicType = "NUMERIC", Precision = 5, Scale = 2,
            CheckExpression = "VALUE >= 0 AND VALUE <= 100",
        };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.Contains("CHECK (VALUE >= 0 AND VALUE <= 100)", sql);
    }

    [Fact]
    public void BuildAddField_Computed_OmitsTypeNullDefault()
    {
        var def = new FieldDefinition
        {
            Name = "WARTOSC",
            ComputedExpression = "ILOSC * CENA",
            NotNull = true,        // ignored on computed
            DefaultValue = "0",    // ignored on computed
            BasicType = "INTEGER", // ignored on computed
        };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.Contains("COMPUTED BY (ILOSC * CENA)", sql);
        Assert.DoesNotContain("NOT NULL", sql);
        Assert.DoesNotContain("DEFAULT", sql);
        Assert.DoesNotContain("INTEGER", sql);
    }

    [Fact]
    public void BuildAddField_PrimaryKey_AppendsPrimaryKeyClause()
    {
        var def = new FieldDefinition { Name = "ID", BasicType = "INTEGER", NotNull = true, PrimaryKey = true };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.EndsWith("PRIMARY KEY", sql);
    }

    // ─── Autoincrement ────────────────────────────────────────────────────

    [Fact]
    public void BuildAddField_Identity_EmitsGeneratedByDefault()
    {
        var def = new FieldDefinition
        {
            Name = "ID", BasicType = "INTEGER", NotNull = true,
            AutoIncrement = AutoIncrementMode.Identity,
        };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.Contains("GENERATED BY DEFAULT AS IDENTITY", sql);
        // No CREATE GENERATOR / CREATE TRIGGER on identity path.
        Assert.DoesNotContain("CREATE GENERATOR", sql);
        Assert.DoesNotContain("CREATE TRIGGER", sql);
    }

    [Fact]
    public void BuildAddField_NewGenerator_EmitsThreeStatements()
    {
        var def = new FieldDefinition
        {
            Name = "ID", BasicType = "INTEGER", NotNull = true,
            AutoIncrement = AutoIncrementMode.NewGenerator,
            GeneratorName = "GEN_T_ID",
        };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.Contains("ALTER TABLE \"T\" ADD \"ID\" INTEGER", sql);
        Assert.Contains("CREATE GENERATOR \"GEN_T_ID\"", sql);
        Assert.Contains("CREATE TRIGGER", sql);
        Assert.Contains("GEN_ID(\"GEN_T_ID\", 1)", sql);
        // Should split into 3 top-level statements.
        Assert.Equal(3, FirebirdDdlExecutor.SplitStatements(sql).Count);
    }

    [Fact]
    public void BuildAddField_NewGenerator_NoNameDerivesFromTableAndField()
    {
        var def = new FieldDefinition
        {
            Name = "id", BasicType = "INTEGER",
            AutoIncrement = AutoIncrementMode.NewGenerator,
            // GeneratorName left null on purpose
        };
        var sql = DdlGenerator.BuildAddField("nagl", def);
        Assert.Contains("CREATE GENERATOR \"GEN_NAGL_ID\"", sql);
    }

    [Fact]
    public void BuildAddField_ExistingGenerator_EmitsTriggerOnly()
    {
        var def = new FieldDefinition
        {
            Name = "ID", BasicType = "INTEGER",
            AutoIncrement = AutoIncrementMode.ExistingGenerator,
            GeneratorName = "GEN_SHARED",
        };
        var sql = DdlGenerator.BuildAddField("T", def);
        Assert.DoesNotContain("CREATE GENERATOR", sql);
        Assert.Contains("CREATE TRIGGER", sql);
        Assert.Contains("GEN_ID(\"GEN_SHARED\", 1)", sql);
    }

    [Fact]
    public void BuildAddField_TriggerName_AutoDerivedWhenBlank()
    {
        var def = new FieldDefinition
        {
            Name = "id", BasicType = "INTEGER",
            AutoIncrement = AutoIncrementMode.ExistingGenerator,
            GeneratorName = "GEN_X",
        };
        var trigger = DdlGenerator.BuildAutoIncTrigger("nagl", "id", "GEN_X", null);
        Assert.Contains("CREATE TRIGGER \"BI_NAGL_ID\"", trigger);
    }

    [Fact]
    public void BuildAddField_ThrowsOnEmptyName()
    {
        var def = new FieldDefinition { Name = "  " };
        Assert.Throws<System.ArgumentException>(() => DdlGenerator.BuildAddField("T", def));
    }

    // ─── FirebirdDdlExecutor.SplitStatements ──────────────────────────────

    [Fact]
    public void SplitStatements_SkipsEmptySegments()
    {
        var split = FirebirdDdlExecutor.SplitStatements("A;;B;;;C");
        Assert.Equal(new[] { "A", "B", "C" }, split);
    }

    [Fact]
    public void SplitStatements_TrimsWhitespace()
    {
        var split = FirebirdDdlExecutor.SplitStatements("  A  ;\n  B\n;C");
        Assert.Equal(new[] { "A", "B", "C" }, split);
    }

    [Fact]
    public void SplitStatements_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(FirebirdDdlExecutor.SplitStatements(""));
        Assert.Empty(FirebirdDdlExecutor.SplitStatements("   \n\t  "));
    }

    [Fact]
    public void SplitStatements_ProcedureWithCaseInBody_StaysOneStatement()
    {
        // Regression: a CASE … END expression in the body must NOT close the
        // enclosing BEGIN block. Before the fix, the CASE's END dropped the nesting
        // counter to 0, so the ';' after SUSPEND split the procedure mid-body —
        // sending a truncated statement → "Unexpected end of command".
        var sql = @"CREATE OR ALTER PROCEDURE P (AID INTEGER) RETURNS (V INTEGER) AS
begin
  for select sum(x)
      from t
      where (case when :aid > 0 then 1 else 0 end) = 1
      into :v
  do
    suspend;
end";
        var split = FirebirdDdlExecutor.SplitStatements(sql);
        Assert.Single(split);
        Assert.Contains("suspend", split[0]);
        Assert.EndsWith("end", split[0]);
    }

    [Fact]
    public void SplitStatements_NestedCaseAndBegin_BalancesNesting()
    {
        var sql = @"CREATE OR ALTER PROCEDURE P AS
begin
  if (1 = 1) then
  begin
    x = case when a then 1 else 2 end;
    y = 3;
  end
end";
        Assert.Single(FirebirdDdlExecutor.SplitStatements(sql));
    }

    [Fact]
    public void SplitStatements_SemicolonInsideStringLiteral_DoesNotSplit()
    {
        var split = FirebirdDdlExecutor.SplitStatements("EXECUTE PROCEDURE P('a;b');SELECT 1 FROM RDB$DATABASE");
        Assert.Equal(2, split.Count);
        Assert.Contains("'a;b'", split[0]);
    }

    [Fact]
    public void SplitStatements_KeywordsInsideStringOrComment_DoNotAffectNesting()
    {
        // 'BEGIN'/'END'/'CASE' inside a literal or comment must be ignored, so the
        // two real statements split normally.
        var sql = "INSERT INTO T VALUES ('begin case end'); -- end\nUPDATE T SET X = 1";
        Assert.Equal(2, FirebirdDdlExecutor.SplitStatements(sql).Count);
    }

    [Fact]
    public void SplitStatements_DoubledQuoteEscapeWithKeyword_StaysOpaque()
    {
        // The '' escape must NOT terminate the literal early; the 'begin' keyword
        // inside it must stay opaque (no nesting effect). Single trailing statement.
        var sql = "EXECUTE STATEMENT 'select ''begin'' from rdb$database';";
        var split = FirebirdDdlExecutor.SplitStatements(sql);
        Assert.Single(split);
        Assert.Contains("''begin''", split[0]);
    }

    [Fact]
    public void SplitStatements_DeeplyNestedCase_BalancesNesting()
    {
        var sql = @"CREATE OR ALTER PROCEDURE P AS
begin
  x = case
        when a then case when b then 1 else 2 end
        else 3
      end;
  y = 4;
end";
        Assert.Single(FirebirdDdlExecutor.SplitStatements(sql));
    }

    [Fact]
    public void SplitStatements_TriggerWithDeclareSection_StaysOneStatement()
    {
        // Regression for the blocker: the DECLARE VARIABLE ';' sits BEFORE the BEGIN
        // (block-depth 0). A plain top-level split cut the trigger there → the engine
        // got a truncated "… AS DECLARE VARIABLE ID_NAGL T_ID" → "Unexpected end of
        // command". The PSQL-aware scanner must keep the whole trigger as one statement.
        var sql = @"CREATE OR ALTER TRIGGER XXX_NAGL_BIU_99 FOR NAGL
ACTIVE BEFORE INSERT OR UPDATE POSITION 99
AS

DECLARE VARIABLE ID_NAGL T_ID;

begin
  id_nagl = new.id_nagl;
end";
        var split = FirebirdDdlExecutor.SplitStatements(sql);
        Assert.Single(split);
        Assert.Contains("DECLARE VARIABLE ID_NAGL T_ID;", split[0]);
        Assert.EndsWith("end", split[0]);
    }

    [Fact]
    public void SplitStatements_ProcedureWithDeclareSection_StaysOneStatement()
    {
        // Same defect class for procedures with a DECLARE section before BEGIN.
        var sql = @"CREATE OR ALTER PROCEDURE P RETURNS (R INTEGER) AS
DECLARE VARIABLE T INTEGER;
DECLARE VARIABLE S VARCHAR(10);
BEGIN
  T = 1;
  R = T;
  SUSPEND;
END";
        Assert.Single(FirebirdDdlExecutor.SplitStatements(sql));
    }

    [Fact]
    public void SplitStatements_TriggerInBatch_WithGeneratorAndAlter_SplitsThree()
    {
        // A trigger with a DECLARE section is one unit even amid plain statements.
        var sql = @"CREATE GENERATOR GEN_X;
CREATE OR ALTER TRIGGER T FOR NAGL ACTIVE BEFORE INSERT POSITION 0
AS
DECLARE VARIABLE V INTEGER;
BEGIN
  V = GEN_ID(GEN_X, 1);
  NEW.ID = V;
END;
ALTER TABLE NAGL ADD X INTEGER";
        var split = FirebirdDdlExecutor.SplitStatements(sql);
        Assert.Equal(3, split.Count);
        Assert.StartsWith("CREATE GENERATOR", split[0]);
        Assert.StartsWith("CREATE OR ALTER TRIGGER", split[1]);
        Assert.Contains("DECLARE VARIABLE V INTEGER;", split[1]);
        Assert.StartsWith("ALTER TABLE", split[2]);
    }

    [Fact]
    public void SplitStatements_ProcedureWithSubprogram_StaysOneStatement()
    {
        // A FB3 subprogram's BEGIN…END in the DECLARE section closes to depth 0 mid-body;
        // the scanner peeks past it (next token is the main BEGIN) and keeps scanning.
        var sql = @"CREATE OR ALTER PROCEDURE P AS
DECLARE PROCEDURE SUB (A INTEGER) AS BEGIN A = A + 1; END
DECLARE VARIABLE V INTEGER;
BEGIN
  V = 1;
  SUSPEND;
END";
        Assert.Single(FirebirdDdlExecutor.SplitStatements(sql));
    }

    [Fact]
    public void SplitStatements_AlterTable_IsNotTreatedAsPsql()
    {
        // ALTER TABLE must split on ';' (not be swallowed as a PSQL body).
        var sql = "ALTER TABLE T ADD A INTEGER; ALTER TABLE T ADD B INTEGER";
        Assert.Equal(2, FirebirdDdlExecutor.SplitStatements(sql).Count);
    }

    [Fact]
    public void SplitStatements_CreateViewAsSelect_SplitsNormally()
    {
        // CREATE VIEW … AS SELECT is NOT a PSQL body — it must terminate at its ';'.
        var sql = "CREATE VIEW V AS SELECT 1 AS X FROM RDB$DATABASE; ALTER TABLE T ADD A INTEGER";
        Assert.Equal(2, FirebirdDdlExecutor.SplitStatements(sql).Count);
    }

    // ─── TYPE OF COLUMN (Faza 4) ──────────────────────────────────────────

    [Fact]
    public void FormatTypeOrDomain_TypeOf_EmitsTypeOfClause()
    {
        var def = new FieldDefinition { TypeOf = "COLUMN ADRES.MIASTO" };
        Assert.Equal("TYPE OF COLUMN ADRES.MIASTO", DdlGenerator.FormatTypeOrDomain(def));
    }

    [Fact]
    public void FormatTypeOrDomain_DomainWinsOverTypeOf()
    {
        var r = DdlGenerator.FormatTypeOrDomain(new FieldDefinition { Domain = "T_X", TypeOf = "COLUMN A.B" });
        Assert.DoesNotContain("TYPE OF", r);
        Assert.Contains("T_X", r);
    }

    [Fact]
    public void FormatTypeOrDomain_NoDomainNoTypeOf_UsesBasicType()
    {
        var def = new FieldDefinition { BasicType = "VARCHAR", Size = 30 };
        Assert.Equal("VARCHAR(30)", DdlGenerator.FormatTypeOrDomain(def));
    }

    // ─── Generated-DDL identifier presentation (Easy-mode casing) ─────────────

    [Theory]
    [InlineData("my_domain", "MY_DOMAIN")] // regular lower → UPPER (the reported case)
    [InlineData("MY_DOMAIN", "MY_DOMAIN")] // regular upper → unchanged
    [InlineData("Mixed_Case1", "MIXED_CASE1")]
    [InlineData("  spaced  ", "SPACED")]   // trimmed then folded
    [InlineData("A$B_C", "A$B_C")]         // '$' / '_' are regular-identifier chars
    public void PresentIdentifier_RegularName_IsUppercasedBare(string input, string expected)
    {
        Assert.Equal(expected, DdlGenerator.PresentIdentifier(input));
    }

    [Theory]
    [InlineData("weird name")]  // space → case-sensitive quoted identifier
    [InlineData("2cool")]       // leading digit
    [InlineData("źródło")]      // non-ASCII
    public void PresentIdentifier_SpecialName_PreservedVerbatimAndQuoted(string input)
    {
        var r = DdlGenerator.PresentIdentifier(input);
        // Never uppercased (identity preserved), always quoted so it stays valid.
        Assert.StartsWith("\"", r);
        Assert.Contains(input.Trim(), r);
    }

    [Fact]
    public void PresentIdentifier_AlreadyQuoted_LeftVerbatim()
        => Assert.Equal("\"lower\"", DdlGenerator.PresentIdentifier("\"lower\""));

    [Fact]
    public void PresentIdentifier_Empty_ReturnsEmpty()
        => Assert.Equal(string.Empty, DdlGenerator.PresentIdentifier("   "));
}

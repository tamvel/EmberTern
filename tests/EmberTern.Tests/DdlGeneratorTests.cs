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
        Assert.Contains("\"T_ID\"", sql);
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
}

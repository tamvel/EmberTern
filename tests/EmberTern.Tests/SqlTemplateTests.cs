using System.Collections.Generic;
using System.Linq;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Templates;
using Xunit;

namespace EmberTern.Tests;

public class SqlTemplateTests
{
    private static readonly SqlTemplateRegistry Registry = SqlTemplateCatalog.CreateRegistry();

    private static FieldInfo Col(string name, bool pk = false, bool computed = false, bool identity = false)
        => new()
        {
            Name = name,
            Type = "INTEGER",
            IsPrimaryKey = pk,
            ComputedSource = computed ? "(1 + 1)" : null,
            IsAutoIncrement = identity,
        };

    private static SnippetContext Table(
        string name,
        IEnumerable<FieldInfo> columns,
        IEnumerable<string>? pk = null)
        => new()
        {
            Object = new MetadataObject(name, MetadataObjectKind.Table),
            Columns = columns.ToArray(),
            PrimaryKey = (pk ?? Enumerable.Empty<string>()).ToArray(),
        };

    private static SqlSnippet Gen(string id, SnippetContext ctx) => Registry.Generate(id, ctx);

    // ---- SELECT -------------------------------------------------------------

    [Fact]
    public void SelectAll_Table()
    {
        var ctx = Table("CUSTOMERS", new[] { Col("ID", pk: true), Col("NAME") });
        Assert.Equal("SELECT * FROM CUSTOMERS", Gen("table.select-all", ctx).Text);
    }

    [Fact]
    public void SelectAll_AppliesToViewToo()
    {
        var view = new SnippetContext
        {
            Object = new MetadataObject("V_ORDERS", MetadataObjectKind.View),
            Columns = new[] { Col("ID") },
        };
        Assert.Equal("SELECT * FROM V_ORDERS", Gen("table.select-all", view).Text);
    }

    [Fact]
    public void SelectColumns_OnePerLine()
    {
        var ctx = Table("CUSTOMERS", new[] { Col("ID", pk: true), Col("NAME"), Col("EMAIL") });
        Assert.Equal(
            "SELECT\n  ID,\n  NAME,\n  EMAIL\nFROM CUSTOMERS",
            Gen("table.select-columns", ctx).Text);
    }

    // ---- Fragments ----------------------------------------------------------

    [Fact]
    public void FieldList_CommaSeparated()
    {
        var ctx = Table("CUSTOMERS", new[] { Col("ID"), Col("NAME"), Col("EMAIL") });
        Assert.Equal("ID, NAME, EMAIL", Gen("table.field-list", ctx).Text);
    }

    [Fact]
    public void ParameterList_NamedParamsWithTabStops()
    {
        var ctx = Table("CUSTOMERS", new[] { Col("ID"), Col("NAME") });
        var snip = Gen("table.parameter-list", ctx);
        Assert.Equal(":ID, :NAME", snip.Text);
        Assert.Equal(2, snip.Placeholders.Count);
        // First tab-stop selects the ":ID" token exactly.
        Assert.Equal(":ID", snip.Text.Substring(snip.Placeholders[0].Start, snip.Placeholders[0].Length));
    }

    // ---- INSERT -------------------------------------------------------------

    [Fact]
    public void Insert_ExcludesComputedAndIdentity()
    {
        var ctx = Table("CUSTOMERS", new[]
        {
            Col("ID", pk: true, identity: true),
            Col("NAME"),
            Col("FULL", computed: true),
        });
        Assert.Equal(
            "INSERT INTO CUSTOMERS (NAME)\nVALUES (:NAME)",
            Gen("table.insert", ctx).Text);
    }

    [Fact]
    public void Insert_PlaceholdersCoverValues()
    {
        var ctx = Table("T", new[] { Col("A"), Col("B") });
        var snip = Gen("table.insert", ctx);
        Assert.Equal("INSERT INTO T (A, B)\nVALUES (:A, :B)", snip.Text);
        Assert.Equal(2, snip.Placeholders.Count);
        Assert.All(snip.Placeholders,
            p => Assert.StartsWith(":", snip.Text.Substring(p.Start, p.Length)));
    }

    // ---- UPDATE -------------------------------------------------------------

    [Fact]
    public void Update_SetExcludesPk_WhereUsesPk()
    {
        var ctx = Table("CUSTOMERS",
            new[] { Col("ID", pk: true), Col("NAME"), Col("EMAIL") },
            pk: new[] { "ID" });
        Assert.Equal(
            "UPDATE CUSTOMERS SET\n  NAME = :NAME,\n  EMAIL = :EMAIL\nWHERE ID = :ID",
            Gen("table.update", ctx).Text);
    }

    [Fact]
    public void Update_NoPrimaryKey_EmitsPlaceholderCondition()
    {
        var ctx = Table("LOG", new[] { Col("MSG") });
        var snip = Gen("table.update", ctx);
        Assert.Equal(
            "UPDATE LOG SET\n  MSG = :MSG\nWHERE /* no primary key — specify condition */",
            snip.Text);
        Assert.Contains(snip.Placeholders, p => p.Name == "condition");
    }

    [Fact]
    public void Update_CompositePrimaryKey_AndsConditions()
    {
        var ctx = Table("ORDER_LINE",
            new[] { Col("ORDER_ID", pk: true), Col("LINE_NO", pk: true), Col("QTY") },
            pk: new[] { "ORDER_ID", "LINE_NO" });
        Assert.Equal(
            "UPDATE ORDER_LINE SET\n  QTY = :QTY\nWHERE ORDER_ID = :ORDER_ID\n  AND LINE_NO = :LINE_NO",
            Gen("table.update", ctx).Text);
    }

    // ---- DELETE / UPSERT ----------------------------------------------------

    [Fact]
    public void Delete_UsesPrimaryKey()
    {
        var ctx = Table("CUSTOMERS", new[] { Col("ID", pk: true) }, pk: new[] { "ID" });
        Assert.Equal("DELETE FROM CUSTOMERS\nWHERE ID = :ID", Gen("table.delete", ctx).Text);
    }

    [Fact]
    public void Upsert_MatchingOnPrimaryKey()
    {
        var ctx = Table("CUSTOMERS",
            new[] { Col("ID", pk: true), Col("NAME") },
            pk: new[] { "ID" });
        Assert.Equal(
            "UPDATE OR INSERT INTO CUSTOMERS (ID, NAME)\nVALUES (:ID, :NAME)\nMATCHING (ID)",
            Gen("table.upsert", ctx).Text);
    }

    // ---- Quoting ------------------------------------------------------------

    [Fact]
    public void LowercaseIdentifiers_AreQuoted()
    {
        var ctx = Table("my table", new[] { Col("id"), Col("Name") }, pk: new[] { "id" });
        // QuoteLight quotes lowercase/mixed-case; SHOUTY stays bare.
        Assert.Equal("SELECT * FROM \"my table\"", Gen("table.select-all", ctx).Text);
        Assert.Equal("\"id\", \"Name\"", Gen("table.field-list", ctx).Text);
    }

    // ---- Procedures ---------------------------------------------------------

    [Fact]
    public void ExecuteProcedure_WithInputs()
    {
        var ctx = new SnippetContext
        {
            Object = new MetadataObject("ADD_ORDER", MetadataObjectKind.Procedure),
            Inputs = new[]
            {
                new ProcedureParameterInfo { Name = "CUST_ID" },
                new ProcedureParameterInfo { Name = "AMOUNT" },
            },
        };
        Assert.Equal("EXECUTE PROCEDURE ADD_ORDER(:CUST_ID, :AMOUNT)", Gen("procedure.execute", ctx).Text);
    }

    [Fact]
    public void ExecuteProcedure_NoInputs_NoParens()
    {
        var ctx = new SnippetContext
        {
            Object = new MetadataObject("RECALC", MetadataObjectKind.Procedure),
        };
        Assert.Equal("EXECUTE PROCEDURE RECALC", Gen("procedure.execute", ctx).Text);
    }

    [Fact]
    public void SelectFromProcedure_OnlyWhenSelectable()
    {
        var executable = new SnippetContext
        {
            Object = new MetadataObject("P", MetadataObjectKind.Procedure),
            ProcedureIsSelectable = false,
        };
        Assert.DoesNotContain(Registry.DescriptorsFor(executable), d => d.Id == "procedure.select-from");

        var selectable = new SnippetContext
        {
            Object = new MetadataObject("GET_ROWS", MetadataObjectKind.Procedure),
            ProcedureIsSelectable = true,
            Inputs = new[] { new ProcedureParameterInfo { Name = "SINCE" } },
            Outputs = new[]
            {
                new ProcedureParameterInfo { Name = "ID" },
                new ProcedureParameterInfo { Name = "TOTAL" },
            },
        };
        Assert.Contains(Registry.DescriptorsFor(selectable), d => d.Id == "procedure.select-from");
        Assert.Equal("SELECT ID, TOTAL FROM GET_ROWS(:SINCE)", Gen("procedure.select-from", selectable).Text);
    }

    [Fact]
    public void SelectFromProcedure_NoOutputs_UsesStar()
    {
        var ctx = new SnippetContext
        {
            Object = new MetadataObject("GET_ALL", MetadataObjectKind.Procedure),
            ProcedureIsSelectable = true,
        };
        Assert.Equal("SELECT * FROM GET_ALL", Gen("procedure.select-from", ctx).Text);
    }

    // ---- Functions / Generators --------------------------------------------

    [Fact]
    public void FunctionCall_WithArgs()
    {
        var ctx = new SnippetContext
        {
            Object = new MetadataObject("ADD_ONE", MetadataObjectKind.Function),
            Function = new FunctionSignatureInfo
            {
                Arguments = new[] { new ProcedureParameterInfo { Name = "N" } },
                ReturnType = "INTEGER",
            },
        };
        Assert.Equal("SELECT ADD_ONE(:N) FROM RDB$DATABASE", Gen("function.call", ctx).Text);
    }

    [Fact]
    public void FunctionCall_NoArgs()
    {
        var ctx = new SnippetContext
        {
            Object = new MetadataObject("PI", MetadataObjectKind.Function),
            Function = new FunctionSignatureInfo { ReturnType = "DOUBLE PRECISION" },
        };
        Assert.Equal("SELECT PI() FROM RDB$DATABASE", Gen("function.call", ctx).Text);
    }

    [Fact]
    public void Generator_NextValueAndGenId()
    {
        var ctx = new SnippetContext
        {
            Object = new MetadataObject("GEN_CUSTOMER", MetadataObjectKind.Generator),
        };
        Assert.Equal("NEXT VALUE FOR GEN_CUSTOMER", Gen("generator.next-value", ctx).Text);

        var genId = Gen("generator.gen-id", ctx);
        Assert.Equal("GEN_ID(GEN_CUSTOMER, 1)", genId.Text);
        Assert.Contains(genId.Placeholders, p => p.Name == "increment");
    }

    // ---- Registry applicability --------------------------------------------

    [Fact]
    public void Registry_TableExposesFullDmlSet()
    {
        var ctx = Table("T", new[] { Col("ID", pk: true) }, pk: new[] { "ID" });
        var ids = Registry.DescriptorsFor(ctx).Select(d => d.Id).ToArray();
        Assert.Equal(new[]
        {
            "table.select-all", "table.select-columns", "table.field-list", "table.parameter-list",
            "table.insert", "table.update", "table.delete", "table.upsert",
        }, ids);
    }

    [Fact]
    public void Registry_ViewOnlyExposesSelects()
    {
        var view = new SnippetContext
        {
            Object = new MetadataObject("V", MetadataObjectKind.View),
            Columns = new[] { Col("ID") },
        };
        var ids = Registry.DescriptorsFor(view).Select(d => d.Id).ToArray();
        Assert.Equal(new[] { "table.select-all", "table.select-columns" }, ids);
    }

    [Fact]
    public void Registry_ColumnDependentTemplatesHiddenWhenNoColumnsLoaded()
    {
        var ctx = Table("T", System.Array.Empty<FieldInfo>());
        var ids = Registry.DescriptorsFor(ctx).Select(d => d.Id).ToArray();
        // SELECT * and DELETE need no column metadata; the rest do.
        Assert.Equal(new[] { "table.select-all", "table.delete" }, ids);
    }

    [Fact]
    public void Registry_UnknownIdThrows()
    {
        var ctx = Table("T", new[] { Col("ID") });
        Assert.Throws<System.ArgumentException>(() => Registry.Generate("nope", ctx));
    }

    // ---- Instant (kind-only) menu — no metadata required --------------------

    [Fact]
    public void DescriptorsForKind_Table_PlainSql_ShowsDmlSetWithoutPsql()
    {
        var ids = Registry.DescriptorsForKind(MetadataObjectKind.Table, SnippetInsertionContext.PlainSql)
            .Select(d => d.Id).ToArray();
        Assert.Equal(new[]
        {
            "table.select-all", "table.select-columns", "table.field-list", "table.parameter-list",
            "table.insert", "table.update", "table.delete", "table.upsert",
        }, ids);
    }

    [Fact]
    public void DescriptorsForKind_Table_PsqlBody_AddsPsqlScaffolds()
    {
        var ids = Registry.DescriptorsForKind(MetadataObjectKind.Table, SnippetInsertionContext.PsqlBody)
            .Select(d => d.Id).ToArray();
        Assert.Equal(new[]
        {
            "table.select-all", "table.select-columns", "table.field-list", "table.parameter-list",
            "table.insert", "table.update", "table.delete", "table.upsert",
            "table.for-select", "table.declare-vars",
        }, ids);
    }

    [Fact]
    public void DescriptorsForKind_View_ShowsBothSelects()
    {
        var ids = Registry.DescriptorsForKind(MetadataObjectKind.View, SnippetInsertionContext.PsqlBody)
            .Select(d => d.Id).ToArray();
        Assert.Equal(new[] { "table.select-all", "table.select-columns" }, ids);
    }

    [Fact]
    public void DescriptorsForKind_Procedure_PsqlBody_AddsForSelectFrom()
    {
        var plain = Registry.DescriptorsForKind(MetadataObjectKind.Procedure, SnippetInsertionContext.PlainSql)
            .Select(d => d.Id).ToArray();
        Assert.Equal(new[] { "procedure.execute", "procedure.select-from" }, plain);

        var psql = Registry.DescriptorsForKind(MetadataObjectKind.Procedure, SnippetInsertionContext.PsqlBody)
            .Select(d => d.Id).ToArray();
        Assert.Equal(new[] { "procedure.execute", "procedure.select-from", "procedure.for-select-from" }, psql);
    }

    [Fact]
    public void DescriptorsForKind_Exception_OnlyInPsqlBody()
    {
        Assert.Empty(Registry.DescriptorsForKind(MetadataObjectKind.Exception, SnippetInsertionContext.PlainSql));
        var psql = Registry.DescriptorsForKind(MetadataObjectKind.Exception, SnippetInsertionContext.PsqlBody)
            .Select(d => d.Id).ToArray();
        Assert.Equal(new[] { "exception.raise", "exception.raise-message" }, psql);
    }

    [Fact]
    public void DescriptorsForKind_UnsupportedKind_Empty()
        => Assert.Empty(Registry.DescriptorsForKind(MetadataObjectKind.Trigger, SnippetInsertionContext.PsqlBody));

    [Fact]
    public void HasTemplatesForKind_DraggableKinds()
    {
        Assert.True(Registry.HasTemplatesForKind(MetadataObjectKind.Table));
        Assert.True(Registry.HasTemplatesForKind(MetadataObjectKind.View));
        Assert.True(Registry.HasTemplatesForKind(MetadataObjectKind.Procedure));
        Assert.True(Registry.HasTemplatesForKind(MetadataObjectKind.Function));
        Assert.True(Registry.HasTemplatesForKind(MetadataObjectKind.Generator));
        Assert.True(Registry.HasTemplatesForKind(MetadataObjectKind.Exception));
        Assert.False(Registry.HasTemplatesForKind(MetadataObjectKind.Trigger));
        Assert.False(Registry.HasTemplatesForKind(MetadataObjectKind.Domain));
    }

    // ---- PSQL scaffolds -----------------------------------------------------

    private static SnippetContext PsqlTable(string name, IEnumerable<FieldInfo> cols)
        => new()
        {
            Object = new MetadataObject(name, MetadataObjectKind.Table),
            Insertion = SnippetInsertionContext.PsqlBody,
            Columns = cols.ToArray(),
        };

    [Fact]
    public void ForSelect_Table_BuildsCursorLoop()
    {
        var ctx = PsqlTable("ALERT", new[] { Col("ID"), Col("NAME") });
        Assert.Equal(
            "FOR\n    SELECT\n        ID,\n        NAME\n    FROM ALERT\n    INTO\n        :ID,\n        :NAME\nDO\nBEGIN\n\nEND",
            Gen("table.for-select", ctx).Text);
    }

    [Fact]
    public void DeclareVariables_Table_TypeOfColumnPerColumn()
    {
        var ctx = PsqlTable("ALERT", new[] { Col("ID"), Col("NAME") });
        Assert.Equal(
            "DECLARE VARIABLE V_ID TYPE OF COLUMN ALERT.ID;\nDECLARE VARIABLE V_NAME TYPE OF COLUMN ALERT.NAME;",
            Gen("table.declare-vars", ctx).Text);
    }

    [Fact]
    public void ForSelectFromProcedure_BuildsCursorLoopWithParams()
    {
        var ctx = new SnippetContext
        {
            Object = new MetadataObject("GET_ROWS", MetadataObjectKind.Procedure),
            Insertion = SnippetInsertionContext.PsqlBody,
            ProcedureIsSelectable = true,
            Inputs = new[] { new ProcedureParameterInfo { Name = "SINCE" } },
            Outputs = new[] { new ProcedureParameterInfo { Name = "ID" }, new ProcedureParameterInfo { Name = "TOTAL" } },
        };
        Assert.Equal(
            "FOR\n    SELECT\n        ID,\n        TOTAL\n    FROM GET_ROWS(:SINCE)\n    INTO\n        :ID,\n        :TOTAL\nDO\nBEGIN\n\nEND",
            Gen("procedure.for-select-from", ctx).Text);
    }

    [Fact]
    public void Exception_RaiseAndRaiseWithMessage()
    {
        var ctx = new SnippetContext
        {
            Object = new MetadataObject("E_NOT_FOUND", MetadataObjectKind.Exception),
            Insertion = SnippetInsertionContext.PsqlBody,
        };
        Assert.Equal("EXCEPTION E_NOT_FOUND;", Gen("exception.raise", ctx).Text);

        var withMsg = Gen("exception.raise-message", ctx);
        Assert.Equal("EXCEPTION E_NOT_FOUND 'message';", withMsg.Text);
        Assert.Contains(withMsg.Placeholders, p => p.Name == "message");
    }

    [Fact]
    public void PsqlTemplates_HiddenInPlainSql()
    {
        // A table dropped into a plain SQL editor never offers the PSQL scaffolds.
        var plain = new SnippetContext
        {
            Object = new MetadataObject("ALERT", MetadataObjectKind.Table),
            Insertion = SnippetInsertionContext.PlainSql,
            Columns = new[] { Col("ID") },
        };
        var ids = Registry.DescriptorsFor(plain).Select(d => d.Id).ToArray();
        Assert.DoesNotContain("table.for-select", ids);
        Assert.DoesNotContain("table.declare-vars", ids);
    }
}

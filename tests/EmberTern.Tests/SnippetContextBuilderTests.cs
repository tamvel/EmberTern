using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Sql;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Templates;
using Xunit;

namespace EmberTern.Tests;

public class SnippetContextBuilderTests
{
    // Records which loaders were invoked so we can assert "load only what the kind needs".
    private sealed class Fakes
    {
        public bool ColumnsCalled;
        public bool ConstraintsCalled;
        public readonly List<int> ParameterCalls = new();
        public bool FunctionCalled;

        public IReadOnlyList<FieldInfo> Columns = Array.Empty<FieldInfo>();
        public IReadOnlyList<ConstraintInfo> Constraints = Array.Empty<ConstraintInfo>();
        public IReadOnlyList<ProcedureParameterInfo> Inputs = Array.Empty<ProcedureParameterInfo>();
        public IReadOnlyList<ProcedureParameterInfo> Outputs = Array.Empty<ProcedureParameterInfo>();
        public FunctionSignatureInfo? Function;

        public SnippetContextBuilder Build(SnippetOptions? options = null) => new(
            (name, ct) => { ColumnsCalled = true; return Task.FromResult(Columns); },
            (name, ct) => { ConstraintsCalled = true; return Task.FromResult(Constraints); },
            (name, type, ct) =>
            {
                ParameterCalls.Add(type);
                return Task.FromResult(type == 0 ? Inputs : Outputs);
            },
            (name, ct) => { FunctionCalled = true; return Task.FromResult(Function); },
            options);
    }

    private static FieldInfo Col(string name) => new() { Name = name, Type = "INTEGER" };

    private static ConstraintInfo Pk(string fields)
        => new() { Name = "PK_T", ConstraintType = "PRIMARY KEY", Fields = fields };

    // ---- Table --------------------------------------------------------------

    [Fact]
    public async Task Table_LoadsColumnsAndPrimaryKey()
    {
        var fakes = new Fakes
        {
            Columns = new[] { Col("ID"), Col("NAME") },
            Constraints = new[] { Pk("ID") },
        };

        var ctx = await fakes.Build().BuildAsync(new MetadataObject("CUSTOMERS", MetadataObjectKind.Table));

        Assert.True(fakes.ColumnsCalled);
        Assert.True(fakes.ConstraintsCalled);
        Assert.Empty(fakes.ParameterCalls);
        Assert.False(fakes.FunctionCalled);
        Assert.Equal(new[] { "ID", "NAME" }, ctx.Columns.Select(c => c.Name));
        Assert.Equal(new[] { "ID" }, ctx.PrimaryKey);
    }

    [Fact]
    public async Task View_LoadsColumnsOnly_NoConstraints()
    {
        var fakes = new Fakes { Columns = new[] { Col("ID") } };

        var ctx = await fakes.Build().BuildAsync(new MetadataObject("V_ORDERS", MetadataObjectKind.View));

        Assert.True(fakes.ColumnsCalled);
        Assert.False(fakes.ConstraintsCalled);
        Assert.Single(ctx.Columns);
        Assert.Empty(ctx.PrimaryKey);
    }

    // ---- Procedure ----------------------------------------------------------

    [Fact]
    public async Task Procedure_LoadsInputsAndOutputs_SelectableWhenHasOutputs()
    {
        var fakes = new Fakes
        {
            Inputs = new[] { new ProcedureParameterInfo { Name = "SINCE" } },
            Outputs = new[] { new ProcedureParameterInfo { Name = "TOTAL" } },
        };

        var ctx = await fakes.Build().BuildAsync(new MetadataObject("GET_ROWS", MetadataObjectKind.Procedure));

        Assert.Equal(new[] { 0, 1 }, fakes.ParameterCalls);   // input list, then output list
        Assert.False(fakes.ColumnsCalled);
        Assert.Single(ctx.Inputs);
        Assert.Single(ctx.Outputs);
        Assert.True(ctx.ProcedureIsSelectable);
    }

    [Fact]
    public async Task Procedure_NoOutputs_NotSelectable()
    {
        var fakes = new Fakes { Inputs = new[] { new ProcedureParameterInfo { Name = "P" } } };

        var ctx = await fakes.Build().BuildAsync(new MetadataObject("DO_WORK", MetadataObjectKind.Procedure));

        Assert.False(ctx.ProcedureIsSelectable);
    }

    // ---- Function -----------------------------------------------------------

    [Fact]
    public async Task Function_LoadsSignature()
    {
        var fakes = new Fakes
        {
            Function = new FunctionSignatureInfo
            {
                Arguments = new[] { new ProcedureParameterInfo { Name = "N" } },
                ReturnType = "INTEGER",
            },
        };

        var ctx = await fakes.Build().BuildAsync(new MetadataObject("ADD_ONE", MetadataObjectKind.Function));

        Assert.True(fakes.FunctionCalled);
        Assert.NotNull(ctx.Function);
        Assert.Single(ctx.Function!.Arguments);
    }

    // ---- Generator (no detailed metadata) ----------------------------------

    [Fact]
    public async Task Generator_LoadsNothing()
    {
        var fakes = new Fakes();

        var ctx = await fakes.Build().BuildAsync(new MetadataObject("GEN_X", MetadataObjectKind.Generator));

        Assert.False(fakes.ColumnsCalled);
        Assert.False(fakes.ConstraintsCalled);
        Assert.Empty(fakes.ParameterCalls);
        Assert.False(fakes.FunctionCalled);
        Assert.Equal("GEN_X", ctx.Object.Name);
    }

    // ---- Options / insertion pass-through -----------------------------------

    [Fact]
    public async Task Options_And_Insertion_ArePassedThrough()
    {
        var options = new SnippetOptions { ParamPrefix = "@" };
        var fakes = new Fakes { Columns = new[] { Col("ID") }, Constraints = new[] { Pk("ID") } };

        var ctx = await fakes.Build(options).BuildAsync(
            new MetadataObject("T", MetadataObjectKind.Table),
            SnippetInsertionContext.PsqlBody);

        Assert.Same(options, ctx.Options);
        Assert.Equal(SnippetInsertionContext.PsqlBody, ctx.Insertion);
    }

    // ---- PrimaryKeyFromConstraints ------------------------------------------

    [Fact]
    public void PrimaryKeyFromConstraints_CompositeSplitAndTrimmed()
    {
        var pk = SnippetContextBuilder.PrimaryKeyFromConstraints(new[] { Pk("ORDER_ID, LINE_NO") });
        Assert.Equal(new[] { "ORDER_ID", "LINE_NO" }, pk);
    }

    [Fact]
    public void PrimaryKeyFromConstraints_NoPk_Empty()
    {
        var constraints = new[]
        {
            new ConstraintInfo { Name = "UQ", ConstraintType = "UNIQUE", Fields = "EMAIL" },
        };
        Assert.Empty(SnippetContextBuilder.PrimaryKeyFromConstraints(constraints));
    }

    [Fact]
    public void PrimaryKeyFromConstraints_EmptyInput_Empty()
        => Assert.Empty(SnippetContextBuilder.PrimaryKeyFromConstraints(Array.Empty<ConstraintInfo>()));

    // ---- End-to-end: builder → registry → snippet ---------------------------

    [Fact]
    public async Task EndToEnd_TableInsert_FromBuiltContext()
    {
        var fakes = new Fakes
        {
            Columns = new[] { Col("ID"), Col("NAME") },
            Constraints = new[] { Pk("ID") },
        };
        var ctx = await fakes.Build().BuildAsync(new MetadataObject("CUSTOMERS", MetadataObjectKind.Table));

        var registry = SqlTemplateCatalog.CreateRegistry();
        Assert.Equal(
            "INSERT INTO CUSTOMERS (ID, NAME)\nVALUES (:ID, :NAME)",
            registry.Generate("table.insert", ctx).Text);
    }
}

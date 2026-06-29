using System;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

public class FunctionDetailTests
{
    private const string Src =
        "CREATE OR ALTER FUNCTION ADD_TAX (AMOUNT NUMERIC(15,2), RATE NUMERIC(15,2))\n" +
        "RETURNS NUMERIC(15,2) DETERMINISTIC\n" +
        "AS\n" +
        "DECLARE VARIABLE TMP NUMERIC(15,2);\n" +
        "BEGIN\n" +
        "  TMP = AMOUNT * RATE;\n" +
        "  RETURN AMOUNT + TMP;\nEND";

    [Fact]
    public void EasyMode_FromSource_PopulatesArgumentsResultAndBody()
    {
        var vm = new FunctionDetailTabViewModel("ADD_TAX") { SourceText = Src };
        vm.EasyMode = true;

        Assert.Equal("ADD_TAX", vm.EditableFunctionName);
        Assert.Equal(2, vm.Arguments.Count);
        Assert.Equal("AMOUNT", vm.Arguments[0].Name);
        Assert.Equal("NUMERIC(15,2)", vm.Arguments[1].TypeText);
        Assert.NotNull(vm.ResultRow);
        Assert.Equal("NUMERIC(15,2)", vm.ResultRow!.TypeText);
        Assert.True(vm.Deterministic);
        Assert.Single(vm.Variables);
        Assert.Equal("TMP", vm.Variables[0].Name);
        Assert.Contains("RETURN AMOUNT + TMP;", vm.ExecutableBody);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void EasyMode_ToSource_Regenerates()
    {
        var vm = new FunctionDetailTabViewModel("MY_FN");
        vm.EasyMode = true;            // parse of empty source fails → notice (ignored)
        vm.ErrorMessage = null;
        vm.AddArgumentCommand.Execute(null);
        vm.Arguments[^1].Name = "A";
        vm.Arguments[^1].TypeText = "INTEGER";
        vm.ExecutableBody = "BEGIN RETURN A; END";

        vm.EasyMode = false;

        Assert.Contains("CREATE OR ALTER FUNCTION MY_FN", vm.SourceText);
        Assert.Contains("A INTEGER", vm.SourceText);
        Assert.Contains("RETURNS", vm.SourceText);
    }

    [Fact]
    public void BuildCompileSql_Easy_RoundTripsName_Args_Return()
    {
        var vm = new FunctionDetailTabViewModel("ADD_TAX") { SourceText = Src };
        vm.EasyMode = true;
        var sql = vm.BuildCompileSql();

        Assert.Contains("CREATE OR ALTER FUNCTION ADD_TAX", sql);
        Assert.Contains("AMOUNT NUMERIC(15,2)", sql);
        Assert.Contains("RETURNS NUMERIC(15,2) DETERMINISTIC", sql);
        Assert.Contains("RETURN AMOUNT + TMP;", sql);
    }

    [Fact]
    public void BuildCompileSql_Source_UsesRawText()
    {
        var vm = new FunctionDetailTabViewModel("F")
        {
            SourceText = "CREATE OR ALTER FUNCTION F RETURNS INTEGER AS BEGIN RETURN 1; END",
        };
        Assert.Equal(vm.SourceText, vm.BuildCompileSql());
    }

    [Fact]
    public void BuildFullSource_DefensiveReturnType_WhenResultEmpty()
    {
        var vm = new FunctionDetailTabViewModel("F");
        vm.EasyMode = true;
        vm.ErrorMessage = null;
        // No return type set yet — must not throw; falls back to INTEGER.
        var sql = vm.BuildFullSource();
        Assert.Contains("CREATE OR ALTER FUNCTION F", sql);
        Assert.Contains("RETURNS INTEGER", sql);
    }

    [Fact]
    public void BuildExecuteStatement_WithArgs()
    {
        var vm = new FunctionDetailTabViewModel("ADD_TAX");
        var (sql, ps) = vm.BuildExecuteStatement(new object?[] { 100m, 0.23m });

        Assert.Equal("SELECT \"ADD_TAX\"(@p0, @p1) FROM RDB$DATABASE", sql);
        Assert.Equal(2, ps.Count);
        Assert.Equal("@p0", ps[0].Name);
        Assert.Equal(100m, ps[0].Value);
    }

    [Fact]
    public void BuildExecuteStatement_NoArgs_EmptyParens()
    {
        var vm = new FunctionDetailTabViewModel("PI");
        var (sql, ps) = vm.BuildExecuteStatement(Array.Empty<object?>());

        Assert.Equal("SELECT \"PI\"() FROM RDB$DATABASE", sql);
        Assert.Empty(ps);
    }

    [Theory]
    [InlineData("CREATE FUNCTION F RETURNS INTEGER AS BEGIN RETURN 1; END", "F")]
    [InlineData("create or alter function lower_fn returns integer as begin return 1; end", "LOWER_FN")]
    [InlineData("CREATE PROCEDURE P AS BEGIN END", null)]
    [InlineData(null, null)]
    public void TryParseFunctionName_Cases(string? sql, string? expected)
        => Assert.Equal(expected, FunctionDetailTabViewModel.TryParseFunctionName(sql));

    [Fact]
    public void IsEasyCollectionEditable_FalseOnResultSubTab()
    {
        var vm = new FunctionDetailTabViewModel("F");
        vm.ActiveEasyCollectionIndex = 0; // Arguments
        Assert.True(vm.IsEasyCollectionEditable);
        vm.ActiveEasyCollectionIndex = 1; // Result (single record)
        Assert.False(vm.IsEasyCollectionEditable);
        vm.ActiveEasyCollectionIndex = 2; // Variables
        Assert.True(vm.IsEasyCollectionEditable);
    }

    [Fact]
    public void ResultRow_AlwaysExactlyOneRow()
    {
        var vm = new FunctionDetailTabViewModel("F");
        Assert.Single(vm.ResultRows);
        Assert.NotNull(vm.ResultRow);
    }

    [Fact]
    public void Dirty_NewFunction_AfterEdit_IsNewObjectUnsavedWork()
    {
        var vm = new FunctionDetailTabViewModel("NEW_FUNCTION") { IsNew = true };
        vm.ClearDirty();
        Assert.False(vm.IsDirty);
        Assert.Null(vm.GetUnsavedWork());

        vm.SourceText = "CREATE OR ALTER FUNCTION F RETURNS INTEGER AS BEGIN RETURN 1; END";
        Assert.True(vm.IsDirty);
        Assert.Equal(UnsavedWorkKind.NewObject, vm.GetUnsavedWork()!.Kind);
    }

    [Fact]
    public void Dirty_ExistingFunction_AfterEdit_IsModifiedSource()
    {
        var vm = new FunctionDetailTabViewModel("F");
        vm.ClearDirty();
        Assert.Null(vm.GetUnsavedWork());

        vm.ExecutableBody = "BEGIN RETURN 2; END";
        Assert.True(vm.IsDirty);
        Assert.Equal(UnsavedWorkKind.ModifiedSource, vm.GetUnsavedWork()!.Kind);
    }

    [Fact]
    public void ModeToggle_IsNotAnEdit()
    {
        var vm = new FunctionDetailTabViewModel("F") { SourceText = Src };
        vm.ClearDirty();
        vm.EasyMode = true;
        vm.EasyMode = false;
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void CanRevert_OnlyExistingAndDirty()
    {
        var fresh = new FunctionDetailTabViewModel("F");
        fresh.ClearDirty();
        Assert.False(fresh.CanRevertChanges);          // clean

        var edited = new FunctionDetailTabViewModel("F");
        edited.SourceText = "x";
        Assert.True(edited.CanRevertChanges);           // existing + dirty

        var created = new FunctionDetailTabViewModel("F") { IsNew = true };
        created.SourceText = "x";
        Assert.False(created.CanRevertChanges);         // new → nothing to revert to
    }
}

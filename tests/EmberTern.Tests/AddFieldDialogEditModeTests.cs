using System;
using System.Collections.Generic;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Edit-mode pin for <see cref="AddFieldDialogViewModel"/>. Cover:
///   - IsEditMode flag is true when ctor receives a FieldInfo
///   - SeedFromField populates form properties from the original
///   - Domain match wires SelectedDomain correctly
///   - CanRename=false drives ShowRenameBlockedHint
///   - DialogTitle picks the edit format
///   - Add mode unchanged (regression — IsAddMode=true, no seed)
/// </summary>
public class AddFieldDialogEditModeTests
{
    private static readonly IReadOnlyList<DomainSpec> Domains = new[]
    {
        new DomainSpec("T_ID", "INTEGER"),
        new DomainSpec("T_OPIS", "VARCHAR(255)"),
    };
    private static readonly IReadOnlyList<string> Generators = Array.Empty<string>();

    [Fact]
    public void AddCtor_IsAddMode_NoSeed()
    {
        var vm = new AddFieldDialogViewModel("MY_T", Domains, Generators);
        Assert.True(vm.IsAddMode);
        Assert.False(vm.IsEditMode);
        Assert.Equal(string.Empty, vm.FieldName);
        Assert.True(vm.CanRename); // defaults to true when no original
        Assert.False(vm.ShowRenameBlockedHint);
    }

    [Fact]
    public void EditCtor_SetsIsEditMode_SeedsFormFromOriginal()
    {
        var original = new FieldInfo
        {
            Name = "NAZWA",
            Type = "VARCHAR(80)",
            Size = 80,
            NotNull = true,
            DefaultValue = "'?'",
            Description = "Field description",
        };
        var vm = new AddFieldDialogViewModel("MY_T", Domains, Generators, original, canRename: true);

        Assert.True(vm.IsEditMode);
        Assert.Equal("NAZWA", vm.FieldName);
        Assert.True(vm.NotNull);
        Assert.Equal("'?'", vm.DefaultValue);
        Assert.Equal("Field description", vm.Description);
        Assert.Equal("VARCHAR", vm.SelectedBasicType);
        Assert.Equal(80, vm.Size);
    }

    [Fact]
    public void EditCtor_WithDomain_SelectsMatchingDomainSpec()
    {
        var original = new FieldInfo
        {
            Name = "ID_KLIENT",
            Type = "INTEGER",
            Domain = "T_ID",
        };
        var vm = new AddFieldDialogViewModel("MY_T", Domains, Generators, original, canRename: true);
        Assert.NotNull(vm.SelectedDomain);
        Assert.Equal("T_ID", vm.SelectedDomain!.Name);
    }

    [Fact]
    public void EditCtor_NumericType_SeedsPrecisionAndScale()
    {
        var original = new FieldInfo
        {
            Name = "KWOTA",
            Type = "NUMERIC(15,2)",
            Size = 15,
            Scale = 2,
        };
        var vm = new AddFieldDialogViewModel("MY_T", Domains, Generators, original, canRename: true);
        Assert.Equal("NUMERIC", vm.SelectedBasicType);
        Assert.Equal(15, vm.Precision);
        Assert.Equal(2, vm.Scale);
    }

    [Fact]
    public void EditCtor_CanRenameFalse_GatesNameInputAndShowsHint()
    {
        var original = new FieldInfo { Name = "NAZWA", Type = "VARCHAR(50)", Size = 50 };
        var vm = new AddFieldDialogViewModel("MY_T", Domains, Generators, original, canRename: false);
        Assert.False(vm.CanRename);
        Assert.True(vm.ShowRenameBlockedHint);
    }

    [Fact]
    public void EditCtor_DialogTitle_UsesEditFormat()
    {
        var original = new FieldInfo { Name = "OPIS", Type = "VARCHAR(255)", Size = 255 };
        var vm = new AddFieldDialogViewModel("MY_T", Domains, Generators, original, canRename: true);
        Assert.Contains("OPIS", vm.DialogTitle);
        // Verify it's the edit-format, not the add-format
        Assert.NotEqual(UiStrings.AddFieldDialogTitle, vm.DialogTitle);
    }

    [Fact]
    public void EditCtor_IsAddOnlyTabEnabled_FalseInEditMode()
    {
        var original = new FieldInfo { Name = "OPIS", Type = "VARCHAR(50)", Size = 50 };
        var vm = new AddFieldDialogViewModel("MY_T", Domains, Generators, original, canRename: true);
        Assert.False(vm.IsAddOnlyTabEnabled);
    }

    [Fact]
    public void AddCtor_IsAddOnlyTabEnabled_TrueByDefault()
    {
        var vm = new AddFieldDialogViewModel("MY_T", Domains, Generators);
        Assert.True(vm.IsAddOnlyTabEnabled);
    }

    [Fact]
    public void EditCtor_BuildDefinition_ReturnsSeededValues()
    {
        // Round-trip: seed → BuildDefinition produces a FieldDefinition that
        // matches the original's properties. The downstream ExecuteEditFieldAsync
        // then diffs this against the original — if user touched nothing, the
        // result is "empty diff", confirming the no-op-when-no-change spec.
        var original = new FieldInfo
        {
            Name = "OPIS",
            Type = "VARCHAR(50)",
            Size = 50,
            NotNull = true,
            DefaultValue = "''",
            Description = "Hello",
        };
        var vm = new AddFieldDialogViewModel("MY_T", Domains, Generators, original, canRename: true);
        var def = vm.BuildDefinition();
        Assert.Equal("OPIS", def.Name);
        Assert.True(def.NotNull);
        Assert.Equal("''", def.DefaultValue);
        Assert.Equal("Hello", def.Description);
        Assert.Equal("VARCHAR", def.BasicType);
        Assert.Equal(50, def.Size);
    }
}

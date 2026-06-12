using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

public class NewTableTabVmTests
{
    [Fact]
    public void Ctor_SeedsIdField_AndPersistentKind()
    {
        var vm = new NewTableTabViewModel();
        Assert.Single(vm.Fields);
        Assert.Equal("ID", vm.Fields[0].Name);
        Assert.True(vm.Fields[0].PrimaryKey);
        Assert.NotNull(vm.SelectedKind);
        Assert.Equal(TableKind.Persistent, vm.SelectedKind!.Kind);
    }

    [Fact]
    public void DefaultDisplayTitle_IsNewTable_UntilNamed()
    {
        var vm = new NewTableTabViewModel();
        Assert.Equal("New Table", vm.DisplayTitle);
        vm.TableName = "FOO";
        Assert.Equal("FOO", vm.DisplayTitle);
    }

    [Fact]
    public void DdlPreview_UpdatesOnNameChange()
    {
        var vm = new NewTableTabViewModel();
        Assert.Contains("<table>", vm.DdlPreview);
        vm.TableName = "MY_TABLE";
        Assert.Contains("\"MY_TABLE\"", vm.DdlPreview);
        Assert.DoesNotContain("<table>", vm.DdlPreview);
    }

    [Fact]
    public void AddField_AppendsRow()
    {
        var vm = new NewTableTabViewModel();
        vm.AddFieldCommand.Execute(null);
        Assert.Equal(2, vm.Fields.Count);
    }

    [Fact]
    public void IsValid_FailsWithoutName()
    {
        var vm = new NewTableTabViewModel();
        Assert.False(vm.IsValid());
        Assert.NotEmpty(vm.ValidationMessage);
    }

    [Fact]
    public void IsValid_FailsWithEmptyFieldsList()
    {
        var vm = new NewTableTabViewModel();
        vm.TableName = "T";
        vm.Fields.Clear();
        Assert.False(vm.IsValid());
    }

    [Fact]
    public void ToFieldDefinition_MapsPkAndAi()
    {
        var row = new NewTableFieldRowViewModel { Name = "ID", PrimaryKey = true, AutoIncrement = true, NotNull = true };
        var def = row.ToFieldDefinition();
        Assert.True(def.PrimaryKey);
        Assert.Equal(AutoIncrementMode.NewGenerator, def.AutoIncrement);
        Assert.True(def.NotNull);
    }

    [Fact]
    public void TempKinds_EmitOnCommitClauses()
    {
        var vm = new NewTableTabViewModel();
        vm.TableName = "T";
        vm.SelectedKind = vm.TableKinds[1]; // TempDeleteRows
        Assert.Contains("ON COMMIT DELETE ROWS", vm.DdlPreview);

        vm.SelectedKind = vm.TableKinds[2]; // TempPreserveRows
        Assert.Contains("ON COMMIT PRESERVE ROWS", vm.DdlPreview);
    }

    [Fact]
    public void SetAvailableDomains_PopulatesObservable()
    {
        var vm = new NewTableTabViewModel();
        vm.SetAvailableDomains(new[]
        {
            new DomainSpec("T_ID", "INTEGER"),
            new DomainSpec("T_KWOTA", "NUMERIC(15,2)"),
        });
        Assert.Equal(2, vm.AvailableDomains.Count);
        Assert.Equal("T_KWOTA", vm.AvailableDomains[1].Name);
    }
}

public class TableDetailEditModeTests
{
    [Fact]
    public void IsFieldEditMode_DefaultsFalse_ReadOnlyTrue()
    {
        var vm = new TableDetailTabViewModel("T");
        Assert.False(vm.IsFieldEditMode);
        Assert.True(vm.IsFieldsReadOnly);
    }

    [Fact]
    public void Toggle_FlipsBothFlags()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.ToggleFieldEditModeCommand.Execute(null);
        Assert.True(vm.IsFieldEditMode);
        Assert.False(vm.IsFieldsReadOnly);
        vm.ToggleFieldEditModeCommand.Execute(null);
        Assert.False(vm.IsFieldEditMode);
    }

    [Fact]
    public void DomainChange_QueuesAlterColumnType()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "F", Type = "INTEGER", Domain = "OLD_DOMAIN" });
        var row = vm.EditableFields[0];
        row.DomainName = "NEW_DOMAIN";
        vm.EnqueueRowEdits(row);

        var last = vm.PendingChanges[^1];
        Assert.Contains("ALTER TABLE \"T\" ALTER \"F\" TYPE NEW_DOMAIN", last.Sql);
    }

    [Fact]
    public void TypeChange_BlockedByDeps_AndReverted()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "F", Type = "INTEGER" });
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "VW", ObjectType = "View", FieldName = "F" });

        var row = vm.EditableFields[0];
        row.TypeText = "BIGINT";
        vm.EnqueueRowEdits(row);

        Assert.Empty(vm.PendingChanges);
        Assert.Equal("INTEGER", row.TypeText); // reverted
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public void SelectedDomainSpec_RoundTripsWithDomainName()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "F", Type = "INTEGER" });
        // Owner-side domain list
        vm.AvailableDomains.Add(new DomainSpec("T_ID", "INTEGER"));
        vm.AvailableDomains.Add(new DomainSpec("T_KWOTA", "NUMERIC(15,2)"));

        var row = vm.EditableFields[0];
        row.SelectedDomainSpec = vm.AvailableDomains[1];
        Assert.Equal("T_KWOTA", row.DomainName);
        Assert.Same(vm.AvailableDomains[1], row.SelectedDomainSpec);
    }
}

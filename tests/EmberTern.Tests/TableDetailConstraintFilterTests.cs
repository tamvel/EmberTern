using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

public class TableDetailConstraintFilterTests
{
    private static TableDetailTabViewModel NewVm() => new("DUMMY_TABLE");

    [Fact]
    public void EmptyConstraintList_AllFiltersAreEmpty_AndCountsZero()
    {
        var vm = NewVm();
        Assert.Empty(vm.PrimaryKeyConstraints);
        Assert.Empty(vm.ForeignKeyConstraints);
        Assert.Empty(vm.CheckConstraints);
        Assert.Empty(vm.UniqueConstraints);
        Assert.Equal(0, vm.PrimaryKeyConstraintCount);
        Assert.Equal(0, vm.ForeignKeyConstraintCount);
        Assert.Equal(0, vm.CheckConstraintCount);
        Assert.Equal(0, vm.UniqueConstraintCount);
        Assert.False(vm.HasPrimaryKeyConstraints);
        Assert.False(vm.HasForeignKeyConstraints);
        Assert.False(vm.HasCheckConstraints);
        Assert.False(vm.HasUniqueConstraints);
    }

    [Fact]
    public void PrimaryKeyConstraints_OnlyReturnsPrimaryKeyRows()
    {
        var vm = NewVm();
        vm.Constraints.Add(new ConstraintInfo { Name = "PK1", ConstraintType = "PRIMARY KEY" });
        vm.Constraints.Add(new ConstraintInfo { Name = "FK1", ConstraintType = "FOREIGN KEY" });
        vm.Constraints.Add(new ConstraintInfo { Name = "PK2", ConstraintType = "PRIMARY KEY" });
        vm.Constraints.Add(new ConstraintInfo { Name = "CHK1", ConstraintType = "CHECK" });

        var pk = vm.PrimaryKeyConstraints;
        Assert.Equal(2, pk.Count);
        Assert.Equal(new[] { "PK1", "PK2" }, pk.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void ForeignKeyConstraints_OnlyReturnsForeignKeyRows()
    {
        var vm = NewVm();
        vm.Constraints.Add(new ConstraintInfo { Name = "PK", ConstraintType = "PRIMARY KEY" });
        vm.Constraints.Add(new ConstraintInfo { Name = "FK_A", ConstraintType = "FOREIGN KEY" });
        vm.Constraints.Add(new ConstraintInfo { Name = "UQ", ConstraintType = "UNIQUE" });
        vm.Constraints.Add(new ConstraintInfo { Name = "FK_B", ConstraintType = "FOREIGN KEY" });

        var fk = vm.ForeignKeyConstraints;
        Assert.Equal(2, fk.Count);
        Assert.Equal(new[] { "FK_A", "FK_B" }, fk.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void CheckConstraints_OnlyReturnsCheckRows()
    {
        var vm = NewVm();
        vm.Constraints.Add(new ConstraintInfo { Name = "CHK1", ConstraintType = "CHECK" });
        vm.Constraints.Add(new ConstraintInfo { Name = "FK1", ConstraintType = "FOREIGN KEY" });

        var chk = vm.CheckConstraints;
        Assert.Single(chk);
        Assert.Equal("CHK1", chk[0].Name);
    }

    [Fact]
    public void UniqueConstraints_OnlyReturnsUniqueRows()
    {
        var vm = NewVm();
        vm.Constraints.Add(new ConstraintInfo { Name = "UQ", ConstraintType = "UNIQUE" });
        vm.Constraints.Add(new ConstraintInfo { Name = "PK", ConstraintType = "PRIMARY KEY" });

        var uq = vm.UniqueConstraints;
        Assert.Single(uq);
        Assert.Equal("UQ", uq[0].Name);
    }

    [Fact]
    public void ConstraintTypeMatch_IsCaseInsensitive()
    {
        var vm = NewVm();
        vm.Constraints.Add(new ConstraintInfo { Name = "lower", ConstraintType = "primary key" });
        vm.Constraints.Add(new ConstraintInfo { Name = "mixed", ConstraintType = "Primary Key" });
        Assert.Equal(2, vm.PrimaryKeyConstraintCount);
    }

    [Fact]
    public void TabHeaders_IncludeCountAndLabel()
    {
        var vm = NewVm();
        vm.Constraints.Add(new ConstraintInfo { Name = "PK", ConstraintType = "PRIMARY KEY" });
        vm.Constraints.Add(new ConstraintInfo { Name = "FK1", ConstraintType = "FOREIGN KEY" });
        vm.Constraints.Add(new ConstraintInfo { Name = "FK2", ConstraintType = "FOREIGN KEY" });

        Assert.Contains("(1)", vm.PrimaryKeyTabHeader);
        Assert.Contains("(2)", vm.ForeignKeyTabHeader);
        Assert.Contains("(0)", vm.CheckTabHeader);
        Assert.Contains("(0)", vm.UniqueTabHeader);
    }

    [Fact]
    public void AddingConstraint_NotifiesFilteredPropertyAndCount()
    {
        var vm = NewVm();
        var changed = new System.Collections.Generic.List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Constraints.Add(new ConstraintInfo { Name = "PK", ConstraintType = "PRIMARY KEY" });

        Assert.Contains(nameof(TableDetailTabViewModel.PrimaryKeyConstraints), changed);
        Assert.Contains(nameof(TableDetailTabViewModel.PrimaryKeyConstraintCount), changed);
        Assert.Contains(nameof(TableDetailTabViewModel.HasPrimaryKeyConstraints), changed);
        Assert.Contains(nameof(TableDetailTabViewModel.PrimaryKeyTabHeader), changed);
    }

    [Fact]
    public void ClearingConstraints_NotifiesAllFilters()
    {
        var vm = NewVm();
        vm.Constraints.Add(new ConstraintInfo { Name = "PK", ConstraintType = "PRIMARY KEY" });

        var changed = new System.Collections.Generic.List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Constraints.Clear();

        Assert.Contains(nameof(TableDetailTabViewModel.PrimaryKeyConstraints), changed);
        Assert.Contains(nameof(TableDetailTabViewModel.ForeignKeyConstraints), changed);
        Assert.Contains(nameof(TableDetailTabViewModel.CheckConstraints), changed);
        Assert.Contains(nameof(TableDetailTabViewModel.UniqueConstraints), changed);
        Assert.Empty(vm.PrimaryKeyConstraints);
    }
}

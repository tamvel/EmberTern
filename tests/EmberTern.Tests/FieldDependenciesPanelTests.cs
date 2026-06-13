using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// VM-level pin for the field-dependencies panel (Pola sub-tab, Session 4).
/// Covers:
///   - filter by SelectedField.Name (case-insensitive)
///   - dedup by (ObjectType, ObjectName) — same trigger touching multiple
///     fields shows up once per selection
///   - CanNavigate flag derives from object kind mapping
///   - reactivity to SelectedField change
///   - reactivity to DependedOnBy.Add (sim of RefreshStructureAsync repopulation)
///   - no-selection state flags (ShowFieldDependenciesNoSelection)
///   - empty-state flags when selection has no matches
/// </summary>
public class FieldDependenciesPanelTests
{
    private static TableDetailTabViewModel BuildVm()
    {
        var vm = new TableDetailTabViewModel("MY_T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "ID", Type = "INTEGER" });
        vm.Fields.Add(new FieldInfo { Position = 1, Name = "NAZWA", Type = "VARCHAR(50)", Size = 50 });
        vm.Fields.Add(new FieldInfo { Position = 2, Name = "ID_KONTRAHENT", Type = "INTEGER" });
        return vm;
    }

    [Fact]
    public void NoSelection_PanelShowsNoSelectionState()
    {
        var vm = BuildVm();
        Assert.True(vm.ShowFieldDependenciesNoSelection);
        Assert.False(vm.ShowFieldDependenciesEmpty);
        Assert.False(vm.HasFieldDependencies);
        Assert.Empty(vm.FieldDependencies);
    }

    [Fact]
    public void SelectionWithoutMatchingDeps_ShowsEmptyState()
    {
        var vm = BuildVm();
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_OTHER", ObjectType = "Trigger", FieldName = "OTHER_FIELD" });
        vm.SelectedField = vm.Fields[0]; // ID

        Assert.False(vm.ShowFieldDependenciesNoSelection);
        Assert.True(vm.ShowFieldDependenciesEmpty);
        Assert.False(vm.HasFieldDependencies);
        Assert.Empty(vm.FieldDependencies);
    }

    [Fact]
    public void SelectingField_FiltersDependenciesByName()
    {
        var vm = BuildVm();
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_ID", ObjectType = "Trigger", FieldName = "ID" });
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_NAZWA", ObjectType = "Trigger", FieldName = "NAZWA" });
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "V_VIEW", ObjectType = "View", FieldName = "ID" });

        vm.SelectedField = vm.Fields[0]; // ID

        Assert.Equal(2, vm.FieldDependencies.Count);
        Assert.Contains(vm.FieldDependencies, d => d.ObjectName == "TR_ID");
        Assert.Contains(vm.FieldDependencies, d => d.ObjectName == "V_VIEW");
        Assert.DoesNotContain(vm.FieldDependencies, d => d.ObjectName == "TR_NAZWA");
    }

    [Fact]
    public void Filter_IsCaseInsensitive()
    {
        var vm = BuildVm();
        // Reader normally uppercases; this test simulates a hypothetical
        // mixed-case row to confirm the comparator tolerates it.
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_X", ObjectType = "Trigger", FieldName = "id" });
        vm.SelectedField = vm.Fields[0]; // "ID"
        Assert.Single(vm.FieldDependencies);
    }

    [Fact]
    public void Dedup_SameObjectAcrossMultipleFieldsCollapsesPerSelection()
    {
        // Same trigger registered against 3 different fields. When user
        // picks one of those fields we want the trigger to appear once,
        // not three times. The catalog representation typically has one
        // (object, field) row per field touched.
        var vm = BuildVm();
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_AUDIT", ObjectType = "Trigger", FieldName = "ID" });
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_AUDIT", ObjectType = "Trigger", FieldName = "ID" });
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_AUDIT", ObjectType = "Trigger", FieldName = "ID" });

        vm.SelectedField = vm.Fields[0]; // ID
        Assert.Single(vm.FieldDependencies);
        Assert.Equal("TR_AUDIT", vm.FieldDependencies[0].ObjectName);
    }

    [Fact]
    public void Dedup_KeyIsObjectTypePlusName_NotJustName()
    {
        // Two distinct dependencies that happen to share a name but have
        // different ObjectType (extremely unlikely IRL but defensive
        // pinning) — both should survive the dedup.
        var vm = BuildVm();
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "X", ObjectType = "Trigger", FieldName = "ID" });
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "X", ObjectType = "View", FieldName = "ID" });
        vm.SelectedField = vm.Fields[0];
        Assert.Equal(2, vm.FieldDependencies.Count);
    }

    [Fact]
    public void CanNavigate_TrueForKnownKinds()
    {
        var item = new FieldDependencyItem(new DependencyInfo
        {
            ObjectName = "TR_X",
            ObjectType = "Trigger",
            FieldName = "ID",
        });
        Assert.True(item.CanNavigate);
        Assert.True(item.NavigateCommand.CanExecute(null));
    }

    [Fact]
    public void CanNavigate_FalseForUnknownKind()
    {
        // "Field" / "Object (N)" / random strings — not openable as their
        // own tab; the future double-click affordance must skip them.
        var item = new FieldDependencyItem(new DependencyInfo
        {
            ObjectName = "?",
            ObjectType = "Object (99)",
            FieldName = "ID",
        });
        Assert.False(item.CanNavigate);
        Assert.False(item.NavigateCommand.CanExecute(null));
    }

    [Fact]
    public void ChangingSelectedField_RebuildsDependencies()
    {
        var vm = BuildVm();
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_ID", ObjectType = "Trigger", FieldName = "ID" });
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_NAZWA", ObjectType = "Trigger", FieldName = "NAZWA" });

        vm.SelectedField = vm.Fields[0]; // ID
        Assert.Single(vm.FieldDependencies);
        Assert.Equal("TR_ID", vm.FieldDependencies[0].ObjectName);

        vm.SelectedField = vm.Fields[1]; // NAZWA
        Assert.Single(vm.FieldDependencies);
        Assert.Equal("TR_NAZWA", vm.FieldDependencies[0].ObjectName);

        vm.SelectedField = null;
        Assert.Empty(vm.FieldDependencies);
        Assert.True(vm.ShowFieldDependenciesNoSelection);
    }

    [Fact]
    public void DependedOnByCollectionChanged_TriggersRebuild()
    {
        // Simulates the RefreshStructureAsync flow: DependedOnBy clears and
        // repopulates after a structural change. SelectedField stays
        // selected; FieldDependencies must follow.
        var vm = BuildVm();
        vm.SelectedField = vm.Fields[0]; // ID
        Assert.Empty(vm.FieldDependencies);

        // Sim: dependency arrives during refresh
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_NEW", ObjectType = "Trigger", FieldName = "ID" });
        Assert.Single(vm.FieldDependencies);
        Assert.Equal("TR_NEW", vm.FieldDependencies[0].ObjectName);

        // Sim: structure refreshes again; collection cleared then repopulated
        vm.DependedOnBy.Clear();
        Assert.Empty(vm.FieldDependencies);
        vm.DependedOnBy.Add(new DependencyInfo { ObjectName = "TR_X", ObjectType = "Trigger", FieldName = "ID" });
        Assert.Single(vm.FieldDependencies);
        Assert.Equal("TR_X", vm.FieldDependencies[0].ObjectName);
    }

    [Fact]
    public void FieldDependencyItem_ExposesObjectMetadata()
    {
        var dep = new DependencyInfo { ObjectName = "MY_VIEW", ObjectType = "View", FieldName = "ID" };
        var item = new FieldDependencyItem(dep);
        Assert.Equal("MY_VIEW", item.ObjectName);
        Assert.Equal("View", item.ObjectType);
        Assert.Same(dep, item.Info);
    }
}

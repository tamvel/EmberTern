using System.Linq;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

// Faza 3: after picking a domain the field's Type/Size/Scale (+ SubType/Charset/NotNull
// for Procedure/Trigger) show the domain's resolved definition, and changing to a type
// without Size/Scale clears the stale cells. The DDL still uses the domain name when set.
public class DomainSyncFaza3Tests
{
    // ─── Procedure/Trigger (ProcedureFieldRowBase) ────────────────────────

    [Fact]
    public void Proc_PickDomain_MirrorsResolvedTypeSizeScaleCharsetNotNull()
    {
        var owner = new ProcedureDetailTabViewModel("P");
        owner.SetAvailableDomains(new[]
        {
            new DomainSpec("T_KWOTA", "NUMERIC(15,2)", NotNull: true, Charset: null),
            new DomainSpec("T_KOD", "VARCHAR(20)", NotNull: false, Charset: "WIN1250"),
        });

        var row = new ProcedureVariableRowViewModel(owner) { Name = "V" };
        row.DomainName = "T_KWOTA";
        Assert.Equal("NUMERIC", row.BaseType);
        Assert.Equal(15, row.Size);
        Assert.Equal(2, row.Scale);
        Assert.True(row.NotNull);
        // TypeText (canonical/DDL) stays the domain name — display sync is informational.
        Assert.Equal("T_KWOTA", row.TypeText);

        row.DomainName = "T_KOD";
        Assert.Equal("VARCHAR", row.BaseType);
        Assert.Equal(20, row.Size);
        Assert.Equal("WIN1250", row.Charset);
        Assert.Equal("T_KOD", row.TypeText);
    }

    [Fact]
    public void Proc_ChangeTypeAwayFromSize_ClearsStaleSizeScale()
    {
        var row = new ProcedureVariableRowViewModel { BaseType = "NUMERIC", Size = 15, Scale = 2 };
        row.BaseType = "SMALLINT";
        Assert.Null(row.Size);
        Assert.Null(row.Scale);
    }

    // ─── Table Detail (FieldRowViewModel) ─────────────────────────────────

    [Fact]
    public void TableDetail_PickDomain_MirrorsResolvedTypeForDisplay()
    {
        var vm = new TableDetailTabViewModel("T");
        vm.Fields.Add(new FieldInfo { Position = 0, Name = "F", Type = "INTEGER" });
        vm.AvailableDomains.Add(new DomainSpec("T_KWOTA", "NUMERIC(15,2)"));

        var row = vm.EditableFields[0];
        row.DomainName = "T_KWOTA";

        Assert.Equal("NUMERIC", row.SelectedTypeItem); // disabled Type combo shows the base
        Assert.Equal(15, row.Size);
        Assert.Equal(2, row.Scale);
        Assert.Equal("T_KWOTA", row.DomainName);       // domain drives the DDL
    }

    [Fact]
    public void TableDetail_ChangeTypeAwayFromSize_ClearsStaleSize()
    {
        var row = new FieldRowViewModel(new FieldInfo { Name = "F", Type = "VARCHAR(50)" });
        Assert.Equal(50, row.Size);
        row.SelectedTypeItem = "SMALLINT";
        Assert.Null(row.Size);
    }

    // ─── New Table (NewTableFieldRowViewModel) ────────────────────────────

    [Fact]
    public void NewTable_ChangeTypeAwayFromSize_ClearsStaleSizeScale()
    {
        var row = new NewTableFieldRowViewModel { Type = "NUMERIC", Size = 15, Scale = 2 };
        row.Type = "SMALLINT";
        Assert.Null(row.Size);
        Assert.Null(row.Scale);
    }

    [Fact]
    public void NewTable_PickDomain_ShowsResolvedTypeInTypeColumn()
    {
        var vm = new NewTableTabViewModel();
        vm.SetAvailableDomains(new[] { new DomainSpec("T_KWOTA", "NUMERIC(15,2)") });
        var row = new NewTableFieldRowViewModel(vm) { DomainName = "T_KWOTA" };

        Assert.True(row.HasDomain);
        Assert.Equal("NUMERIC(15,2)", row.EffectiveTypeDisplay); // resolved type shown
    }
}

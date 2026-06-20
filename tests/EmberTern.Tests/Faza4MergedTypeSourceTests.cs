using System.Collections.Generic;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

// Faza 4 / Krok 1: the Procedure/Trigger field grids merge the separate Domain + TYPE OF
// columns into one "Domain / Column" picker. The row VM exposes a unified SelectedTypeSource
// (DomainSpec → DomainName | ColumnRef → TYPE OF COLUMN | null → clear) plus a TypeSourceDisplay
// for the closed-box text, and forwards AvailableTables/ColumnsLoader from the owner.
public class Faza4MergedTypeSourceTests
{
    private static ProcedureDetailTabViewModel OwnerWithDomains(params DomainSpec[] domains)
    {
        var owner = new ProcedureDetailTabViewModel("P");
        owner.SetAvailableDomains(domains);
        return owner;
    }

    [Fact]
    public void PickDomain_SetsDomainName_DisplayAndCanonicalType()
    {
        var owner = OwnerWithDomains(new DomainSpec("T_KOD", "VARCHAR(20)"));
        var row = new ProcedureVariableRowViewModel(owner) { Name = "V" };

        row.SelectedTypeSource = owner.AvailableDomains[0];

        Assert.Equal("T_KOD", row.DomainName);
        Assert.True(string.IsNullOrEmpty(row.TypeOf));
        Assert.Equal("T_KOD", row.TypeSourceDisplay);
        Assert.Equal("T_KOD", row.TypeText);                 // domain drives the DDL
        Assert.Same(owner.AvailableDomains[0], row.SelectedTypeSource); // resolved instance
    }

    [Fact]
    public void PickColumn_SetsTypeOfColumn_DisplayAndCanonicalType()
    {
        var row = new ProcedureVariableRowViewModel { Name = "V" };

        row.SelectedTypeSource = new ColumnRef("ADRES", "ID_ADRES");

        Assert.Equal("COLUMN ADRES.ID_ADRES", row.TypeOf);
        Assert.True(string.IsNullOrEmpty(row.DomainName));
        Assert.Equal("ADRES.ID_ADRES", row.TypeSourceDisplay);   // "COLUMN " stripped for display
        Assert.Equal("TYPE OF COLUMN ADRES.ID_ADRES", row.TypeText);
        var cr = Assert.IsType<ColumnRef>(row.SelectedTypeSource);
        Assert.Equal("ADRES", cr.Table);
        Assert.Equal("ID_ADRES", cr.Column);
    }

    [Fact]
    public void Clear_DropsBothSources()
    {
        var owner = OwnerWithDomains(new DomainSpec("T_KOD", "VARCHAR(20)"));
        var row = new ProcedureVariableRowViewModel(owner) { Name = "V" };
        row.SelectedTypeSource = owner.AvailableDomains[0];

        row.SelectedTypeSource = null;

        Assert.True(string.IsNullOrEmpty(row.DomainName));
        Assert.True(string.IsNullOrEmpty(row.TypeOf));
        Assert.Equal(string.Empty, row.TypeSourceDisplay);
        Assert.Null(row.SelectedTypeSource);
    }

    [Fact]
    public void SwitchDomainToColumn_ClearsDomain()
    {
        var owner = OwnerWithDomains(new DomainSpec("T_KOD", "VARCHAR(20)"));
        var row = new ProcedureVariableRowViewModel(owner) { Name = "V" };
        row.SelectedTypeSource = owner.AvailableDomains[0];

        row.SelectedTypeSource = new ColumnRef("ADRES", "ID_ADRES");

        Assert.True(string.IsNullOrEmpty(row.DomainName));
        Assert.Equal("COLUMN ADRES.ID_ADRES", row.TypeOf);
    }

    [Fact]
    public void SwitchColumnToDomain_ClearsTypeOf()
    {
        var owner = OwnerWithDomains(new DomainSpec("T_KOD", "VARCHAR(20)"));
        var row = new ProcedureVariableRowViewModel(owner) { Name = "V" };
        row.SelectedTypeSource = new ColumnRef("ADRES", "ID_ADRES");

        row.SelectedTypeSource = owner.AvailableDomains[0];

        Assert.True(string.IsNullOrEmpty(row.TypeOf));
        Assert.Equal("T_KOD", row.DomainName);
    }

    [Fact]
    public void LoadTypeOfColumn_RoundTripsThroughDisplayAndCanonical()
    {
        var row = ProcedureVariableRowViewModel.From(
            new EmberTern.Core.Sql.ProcedureVariable { Name = "V", TypeText = "TYPE OF COLUMN ADRES.ID_ADRES" });

        Assert.Equal("ADRES.ID_ADRES", row.TypeSourceDisplay);
        var cr = Assert.IsType<ColumnRef>(row.SelectedTypeSource);
        Assert.Equal("ADRES", cr.Table);
        Assert.Equal("ID_ADRES", cr.Column);
        // TypeText preserved verbatim — no information loss.
        Assert.Equal("TYPE OF COLUMN ADRES.ID_ADRES", row.TypeText);
    }

    [Fact]
    public void PlainBaseType_HasNoTypeSource()
    {
        var row = new ProcedureVariableRowViewModel { Name = "V", BaseType = "INTEGER" };
        Assert.Null(row.SelectedTypeSource);
        Assert.Equal(string.Empty, row.TypeSourceDisplay);
    }

    [Fact]
    public void TablesAndColumnsLoader_ForwardedFromOwner()
    {
        var owner = new ProcedureDetailTabViewModel("P");
        owner.SetAvailableTables(new[] { "ADRES", "NAGL" });
        owner.ColumnsLoader = new DelegateColumnsLoader(_ =>
            Task.FromResult<IReadOnlyList<ColumnSpec>>(new[] { new ColumnSpec("ID", "INTEGER") }));

        var row = new ProcedureVariableRowViewModel(owner);

        Assert.Same(owner.AvailableTables, row.AvailableTables);
        Assert.Same(owner.ColumnsLoader, row.ColumnsLoader);
    }
}

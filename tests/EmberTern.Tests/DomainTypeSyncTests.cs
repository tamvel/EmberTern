using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

// After picking a domain the field's Type/Size/Scale (+ SubType/Charset/NotNull
// for Procedure/Trigger) show the domain's resolved definition, and changing to a type
// without Size/Scale clears the stale cells. The DDL still uses the domain name when set.
public class DomainTypeSyncTests
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
    public void Proc_PickLowercaseDomain_GeneratedTypeAndDisplayAreUppercase()
    {
        // The reported case: a domain the catalog happens to expose lower-case must show UPPERCASE in
        // the generated parameter/variable type (Easy-mode DDL presentation), not verbatim lower-case.
        var owner = new ProcedureDetailTabViewModel("P");
        owner.SetAvailableDomains(new[] { new DomainSpec("my_domain", "INTEGER") });

        var row = new ProcedureVariableRowViewModel(owner) { Name = "V" };
        row.DomainName = "my_domain";

        Assert.Equal("MY_DOMAIN", row.TypeText);          // generated DDL type
        Assert.Equal("MY_DOMAIN", row.TypeSourceDisplay); // the picker's closed-box display
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

    // Parity for the "Add Field" dialog path (not just inline grid edit): a field added
    // with a domain shows the domain's resolved Type/Size in the new grid row.
    [Fact]
    public async Task TableDetail_AddFieldWithDomain_FillsResolvedTypeInGrid()
    {
        using var service = new FirebirdConnectionService();
        var executor = new FirebirdDdlExecutor(service, null);
        var vm = new TableDetailTabViewModel("T", null, null, null, executor, null);
        vm.AvailableDomains.Add(new DomainSpec("T_ADRES", "VARCHAR(50)"));

        await vm.ExecuteAddFieldAsync(new FieldDefinition { Name = "ADRES", Domain = "T_ADRES" });

        var row = vm.EditableFields.Last();
        Assert.Equal("T_ADRES", row.DomainName);
        Assert.Equal("VARCHAR", row.SelectedTypeItem);
        Assert.Equal(50, row.Size);
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

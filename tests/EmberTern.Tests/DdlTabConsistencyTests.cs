using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap 4 — DDL tab == Export. The form-based DDL tabs (Domain / Exception) render the
/// complete portable DDL (structure + COMMENT ON) via the SAME PortableDdl composition the
/// MetadataExportService uses, so the DDL tab and the exported .sql agree. (The
/// DB-reconstruction DDL tabs — View/Procedure/Trigger/Function/Package/Generator/Table —
/// route directly through MetadataExportService; that live path is DB-smoke.)
/// </summary>
public class DdlTabConsistencyTests
{
    [Fact]
    public void DomainDdlTab_IncludesCommentOnDomain_WhenDescribed()
    {
        var vm = new DomainDetailTabViewModel("D_TEST") { DataType = "VARCHAR", Length = 50m };
        vm.EditableDescription = "opis domeny — żółć";

        Assert.Contains("CREATE DOMAIN \"D_TEST\"", vm.DdlText);
        Assert.Contains("COMMENT ON DOMAIN \"D_TEST\" IS 'opis domeny — żółć'", vm.DdlText);
    }

    [Fact]
    public void DomainDdlTab_NoComment_WhenNoDescription()
    {
        // Setting DataType fires RefreshDdl while the description is empty.
        var vm = new DomainDetailTabViewModel("D_TEST") { DataType = "INTEGER" };

        Assert.Contains("CREATE DOMAIN", vm.DdlText);
        Assert.DoesNotContain("COMMENT ON", vm.DdlText); // no IS NULL noise
    }

    [Fact]
    public void ExceptionDdlTab_IncludesMessageAndCommentOnException_WhenDescribed()
    {
        var vm = new ExceptionDetailTabViewModel("E_TEST") { Message = "boom" };
        vm.EditableDescription = "opis wyjątku";

        Assert.Contains("CREATE EXCEPTION \"E_TEST\" 'boom'", vm.DdlText);
        Assert.Contains("COMMENT ON EXCEPTION \"E_TEST\" IS 'opis wyjątku'", vm.DdlText);
    }

    [Fact]
    public void ExceptionDdlTab_NoComment_WhenNoDescription()
    {
        var vm = new ExceptionDetailTabViewModel("E_TEST") { Message = "boom" };

        Assert.Contains("CREATE EXCEPTION \"E_TEST\" 'boom'", vm.DdlText);
        Assert.DoesNotContain("COMMENT ON", vm.DdlText);
    }

    [Fact]
    public void ExceptionDdlTab_LiveUpdatesComment_OnDescriptionEdit()
    {
        var vm = new ExceptionDetailTabViewModel("E_TEST") { Message = "boom" };

        vm.EditableDescription = "first";
        Assert.Contains("IS 'first'", vm.DdlText);

        vm.EditableDescription = "second";
        Assert.Contains("IS 'second'", vm.DdlText);
        Assert.DoesNotContain("'first'", vm.DdlText);
    }
}

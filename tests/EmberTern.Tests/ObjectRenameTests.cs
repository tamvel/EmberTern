using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Renaming an object in a metadata editor (2026-08-07).
///
/// <para>⭐⭐ THE DEFECT THIS PINS WAS A DATA-LOSS PATH, not the reported symptom. The report was cosmetic
/// ("the tab stays on the old object and the DDL snaps back, as if Compile did nothing"). Measured, the cause
/// was that <c>ObjectDisplayName</c> answered two different questions — "what do we call this to the user"
/// (it follows the editable field) and "which object is this tab" (the change-safety gate used it) — and those
/// stop being the same answer the moment the name becomes editable. So on a rename the gate was handed the NEW
/// name as a label and the OLD object's definition as evidence, found it unchanged, and answered <b>Safe</b>
/// about an object the statement does not touch. Renaming onto an EXISTING name therefore overwrote it
/// silently: the gate was not bypassed, it was asked about the wrong object.</para>
///
/// <para>⚠ Firebird has no rename for these kinds — measured on FB 5.0: <c>ALTER PROCEDURE P1 TO P2</c>,
/// <c>ALTER TRIGGER TR1 TO TR2</c> and <c>ALTER TABLE T1 TO T2</c> all fail with <c>-104 Token unknown - TO</c>,
/// while <c>ALTER DOMAIN D1 TO D2</c> succeeds. That is why a rename here creates a second object and the user
/// must be told; the domain editor, whose kind the engine CAN rename, already emits the native statement.</para>
///
/// <para>⚠ The end-to-end compile cannot be driven headlessly — <c>FirebirdDdlExecutor</c> is a sealed class
/// with no interface, so there is nothing to substitute (which is why the neighbouring change-safety tests
/// assert against the gate directly). What is unit-testable is the DECISION, and it is the decision that was
/// wrong. The live half belongs to <c>tools/probes/ChangeSafetyProbe</c>.</para>
/// </summary>
public class ObjectRenameTests
{
    private static ProcedureDetailTabViewModel Loaded(string name) => new(name);

    [Fact]
    public void CompilingTheSameName_IsNotARename()
    {
        var vm = Loaded("SP_X");
        Assert.Null(vm.ResolveRenameTarget("CREATE OR ALTER PROCEDURE SP_X AS BEGIN END"));
    }

    [Fact]
    public void CompilingADifferentName_IsARename_AndReportsTheTarget()
    {
        var vm = Loaded("SP_X");
        Assert.Equal("SP_Y", vm.ResolveRenameTarget("CREATE OR ALTER PROCEDURE SP_Y AS BEGIN END"));
    }

    /// <summary>
    /// ⚠ Firebird folds an unquoted identifier to upper case, so a tab opened on <c>SP_X</c> and a statement
    /// naming <c>sp_x</c> address the same object. Treating that as a rename would create nothing, refuse for
    /// no reason, and close the user's tab.
    /// </summary>
    [Theory]
    [InlineData("sp_x")]
    [InlineData("Sp_X")]
    public void CaseAloneIsNotARename(string typed)
    {
        var vm = Loaded("SP_X");
        Assert.Null(vm.ResolveRenameTarget($"CREATE OR ALTER PROCEDURE {typed} AS BEGIN END"));
    }

    /// <summary>
    /// ⭐ THE HALF THAT MADE THE OLD BEHAVIOUR WRONG RATHER THAN MERELY ODD: the decision must read the name the
    /// tab was OPENED with, never the editable field. Here the user has typed a new name, so the editable field
    /// — and therefore <c>ObjectDisplayName</c> — already says "SP_Y"; a decision taken from it would compare
    /// SP_Y with SP_Y and conclude "not a rename", which is exactly how the old code reached the wrong gate.
    /// </summary>
    [Fact]
    public void TheDecisionReadsTheLoadedName_NotTheEditedOne()
    {
        var vm = Loaded("SP_X");
        vm.EditableProcedureName = "SP_Y";

        Assert.Equal("SP_Y", vm.ResolveRenameTarget("CREATE OR ALTER PROCEDURE SP_Y AS BEGIN END"));
    }

    /// <summary>A brand-new object has nothing to be renamed FROM — the New flow owns that case and asks the
    /// create gate about the parsed name already.</summary>
    [Fact]
    public void ANewObject_IsNeverARename()
    {
        var vm = new ProcedureDetailTabViewModel("NEW_PROCEDURE") { IsNew = true };
        Assert.Null(vm.ResolveRenameTarget("CREATE OR ALTER PROCEDURE SP_ANYTHING AS BEGIN END"));
    }

    /// <summary>
    /// ⚠ An unparseable statement is NOT treated as a rename. The safe direction here is the ordinary compile
    /// path: refusing on "we could not read a name" would block legitimate work on our own parsing limits,
    /// and the server still rejects genuinely malformed DDL.
    /// </summary>
    [Fact]
    public void AnUnparseableStatement_IsNotTreatedAsARename()
    {
        var vm = Loaded("SP_X");
        Assert.Null(vm.ResolveRenameTarget("this is not ddl at all"));
    }

    /// <summary>
    /// ⭐⭐ THE DATA-LOSS FIX, asserted where it can be: a rename asks the CREATE gate — "is this name free?" —
    /// because <c>CREATE OR ALTER</c> would otherwise overwrite whatever already holds it. The overwrite gate
    /// ("is the definition I loaded still there?") cannot answer that question at all: it re-reads the ORIGINAL
    /// object, which the statement does not touch.
    /// </summary>
    [Fact]
    public async Task RenamingOntoAnExistingName_IsRefusedByTheCreateGate()
    {
        var vm = Loaded("SP_X");

        var check = await vm.ChangeGate.CheckCreateAsync("SP_TAKEN", _ => Task.FromResult(true));

        Assert.False(check.MayProceed);
        Assert.Contains("SP_TAKEN", check.RefusalMessage!, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenamingOntoAFreeName_IsAllowed()
    {
        var vm = Loaded("SP_X");

        var check = await vm.ChangeGate.CheckCreateAsync("SP_FREE", _ => Task.FromResult(false));

        Assert.True(check.MayProceed);
    }

    /// <summary>
    /// The outcome payload is what lets ONE owner-side handler serve a create and a rename while wording them
    /// differently — and <c>IsRename</c> is the only thing it branches on.
    /// </summary>
    [Fact]
    public void TheOutcome_DistinguishesACreateFromARename()
    {
        var created = new SourceObjectDetailTabViewModel.ObjectCompileOutcome("SP_NEW");
        var renamed = new SourceObjectDetailTabViewModel.ObjectCompileOutcome("SP_Y", "SP_X");

        Assert.False(created.IsRename);
        Assert.True(renamed.IsRename);
        Assert.Equal("SP_X", renamed.PreviousName);
    }

    /// <summary>
    /// ⚠ The disclosure must state all THREE facts the user needs — Firebird cannot rename this kind, a new
    /// object exists, and the original was NOT removed. Pinned because the third is the one that costs them
    /// something if it is dropped for brevity: an object they did not ask for is now in their database.
    /// </summary>
    [Fact]
    public void TheDisclosure_NamesBothObjects_AndSaysTheOriginalSurvives()
    {
        var text = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            UiStrings.ObjectRenameNotSupportedFormat, "SP_Y", "SP_X");

        Assert.Contains("SP_Y", text, System.StringComparison.Ordinal);
        Assert.Contains("SP_X", text, System.StringComparison.Ordinal);
        Assert.Contains("cannot rename", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NOT removed", text, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐ The domain editor is the ONE kind Firebird can really rename, and it already emits the native
    /// statement. Pinned so the source-object rename flow added here is never "unified" onto it — they are
    /// different because the engine is different, not because nobody got round to it.
    /// </summary>
    [Fact]
    public void ADomainRename_UsesTheNativeAlterStatement()
    {
        var sql = DdlGenerator.BuildAlterDomainRename("D_OLD", "D_NEW");

        Assert.Contains("ALTER DOMAIN", sql, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("D_OLD", sql, System.StringComparison.Ordinal);
        Assert.Contains("D_NEW", sql, System.StringComparison.Ordinal);
    }
}

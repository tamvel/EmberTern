using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The DDL change-safety gate (audit A-01): EmberTern must never overwrite an object definition it cannot
/// prove is the one the editor loaded.
/// <para>Two layers, tested at both. <see cref="ObjectChangeSafety"/> is the pure decision table — every
/// branch asserted with no server. <see cref="ObjectChangeGate"/> is the orchestration: baseline lifetime,
/// what a failed read means, and the fact that a refusal carries a message.</para>
/// </summary>
public class ObjectChangeSafetyTests
{
    private const string Definition = "CREATE OR ALTER PROCEDURE SP_X AS BEGIN END";

    // ─── Fingerprint ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Fingerprint_IsStable_ForTheSameText()
        => Assert.Equal(ObjectChangeSafety.Fingerprint(Definition), ObjectChangeSafety.Fingerprint(Definition));

    [Fact]
    public void Fingerprint_Differs_ForDifferentText()
        => Assert.NotEqual(
            ObjectChangeSafety.Fingerprint(Definition),
            ObjectChangeSafety.Fingerprint(Definition + " /* colleague's edit */"));

    [Fact]
    public void Fingerprint_IsWhitespaceSensitive()
    {
        // Deliberate: a whitespace-only change to a routine body IS a change to the user's code. Normalising
        // it away would silently permit an overwrite of somebody's reformat.
        Assert.NotEqual(
            ObjectChangeSafety.Fingerprint("BEGIN END"),
            ObjectChangeSafety.Fingerprint("BEGIN  END"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fingerprint_IsNull_WhenNothingWasRead(string? definition)
        => Assert.Null(ObjectChangeSafety.Fingerprint(definition));

    [Fact]
    public void Fingerprint_IsNotTheSourceText()
    {
        // The witness must be structurally incapable of being mistaken for content (rule #11): a later change
        // must not be able to fall back to it as though it were source code.
        var fingerprint = ObjectChangeSafety.Fingerprint(Definition);
        Assert.NotNull(fingerprint);
        Assert.DoesNotContain("PROCEDURE", fingerprint, System.StringComparison.OrdinalIgnoreCase);
    }

    // ─── EvaluateOverwrite (existing object) ────────────────────────────────────────────────

    [Fact]
    public void Overwrite_Safe_WhenDatabaseStillHoldsWhatWeLoaded()
        => Assert.Equal(
            ObjectChangeVerdict.Safe,
            ObjectChangeSafety.EvaluateOverwrite(ObjectChangeSafety.Fingerprint(Definition), Definition));

    [Fact]
    public void Overwrite_Conflict_WhenAnotherSessionChangedIt()
        => Assert.Equal(
            ObjectChangeVerdict.ChangedInDatabase,
            ObjectChangeSafety.EvaluateOverwrite(
                ObjectChangeSafety.Fingerprint(Definition), Definition + " /* theirs */"));

    [Fact]
    public void Overwrite_Unverifiable_WithNoBaseline()
    {
        // The load failed. Reporting Safe here would disable the whole mechanism on exactly the path where
        // something already went wrong.
        Assert.Equal(
            ObjectChangeVerdict.Unverifiable,
            ObjectChangeSafety.EvaluateOverwrite(baselineFingerprint: null, Definition));
    }

    [Fact]
    public void Overwrite_Unverifiable_WhenTheReadProducedNothing()
        => Assert.Equal(
            ObjectChangeVerdict.Unverifiable,
            ObjectChangeSafety.EvaluateOverwrite(ObjectChangeSafety.Fingerprint(Definition), currentDefinition: null));

    // ─── EvaluateCreate (new object) ────────────────────────────────────────────────────────

    [Fact]
    public void Create_Safe_WhenTheNameIsFree()
        => Assert.Equal(ObjectChangeVerdict.Safe, ObjectChangeSafety.EvaluateCreate(nameIsTaken: false));

    [Fact]
    public void Create_Refused_WhenTheNameIsTaken()
    {
        // The New flow generates CREATE OR ALTER, so a name collision overwrites instead of failing. No
        // concurrency needed — one user and a typo are enough.
        Assert.Equal(ObjectChangeVerdict.AlreadyExists, ObjectChangeSafety.EvaluateCreate(nameIsTaken: true));
    }

    [Fact]
    public void Create_Unverifiable_WhenWeCouldNotFindOut()
        => Assert.Equal(ObjectChangeVerdict.Unverifiable, ObjectChangeSafety.EvaluateCreate(nameIsTaken: null));

    // ─── ObjectChangeGate (orchestration) ───────────────────────────────────────────────────

    [Fact]
    public async Task Gate_Allows_WhenTheDefinitionIsUnchanged()
    {
        var gate = new ObjectChangeGate();
        gate.CaptureBaseline(Definition);

        var check = await gate.CheckOverwriteAsync("SP_X", _ => Task.FromResult<string?>(Definition));

        Assert.True(check.MayProceed);
        Assert.Null(check.RefusalMessage);
    }

    [Fact]
    public async Task Gate_Refuses_WithAMessageNamingTheObject_WhenItChanged()
    {
        var gate = new ObjectChangeGate();
        gate.CaptureBaseline(Definition);

        var check = await gate.CheckOverwriteAsync("SP_X", _ => Task.FromResult<string?>("something else"));

        Assert.False(check.MayProceed);
        Assert.Equal(ObjectChangeVerdict.ChangedInDatabase, check.Verdict);
        // A refusal that does not say which object, and that nothing was written, is not usable.
        Assert.Contains("SP_X", check.RefusalMessage!, System.StringComparison.Ordinal);
        Assert.Contains("Nothing was written", check.RefusalMessage!, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gate_Refuses_WhenTheReadThrows()
    {
        // A read failure is not permission to write. The delegate reaches the world, so its exception type is
        // not the gate's to enumerate — any failure means the same thing.
        var gate = new ObjectChangeGate();
        gate.CaptureBaseline(Definition);

        var check = await gate.CheckOverwriteAsync(
            "SP_X", _ => Task.FromException<string?>(new System.InvalidOperationException("lane closed")));

        Assert.False(check.MayProceed);
        Assert.Equal(ObjectChangeVerdict.Unverifiable, check.Verdict);
    }

    [Fact]
    public async Task Gate_LetsCancellationOut_RatherThanCallingItAConflict()
    {
        // gotcha #253: work the user cancelled must never be reported as a fault of the work — and a cancelled
        // save is certainly not "somebody else changed your procedure".
        var gate = new ObjectChangeGate();
        gate.CaptureBaseline(Definition);

        await Assert.ThrowsAnyAsync<System.OperationCanceledException>(() =>
            gate.CheckOverwriteAsync("SP_X", _ => Task.FromCanceled<string?>(new CancellationToken(true))));
    }

    [Fact]
    public async Task Gate_Refuses_BeforeAnyBaselineWasCaptured()
    {
        var gate = new ObjectChangeGate();

        var check = await gate.CheckOverwriteAsync("SP_X", _ => Task.FromResult<string?>(Definition));

        Assert.Equal(ObjectChangeVerdict.Unverifiable, check.Verdict);
    }

    [Fact]
    public async Task Gate_Refuses_AfterForget_SoAFailedReloadCannotAuthoriseAWrite()
    {
        // The load path drops the baseline BEFORE re-reading. This pins the consequence: a reload that fails
        // leaves the gate disarmed rather than holding a fingerprint nobody has re-verified.
        var gate = new ObjectChangeGate();
        gate.CaptureBaseline(Definition);
        gate.Forget();

        var check = await gate.CheckOverwriteAsync("SP_X", _ => Task.FromResult<string?>(Definition));

        Assert.Equal(ObjectChangeVerdict.Unverifiable, check.Verdict);
    }

    [Fact]
    public void Gate_CapturesNothing_FromABlankDefinition()
    {
        var gate = new ObjectChangeGate();
        gate.CaptureBaseline("   ");
        Assert.Null(gate.BaselineFingerprint);
    }

    [Fact]
    public async Task Gate_Create_Refuses_WhenTheNameIsTaken()
    {
        var gate = new ObjectChangeGate();

        var check = await gate.CheckCreateAsync("SP_EXISTING", _ => Task.FromResult(true));

        Assert.False(check.MayProceed);
        Assert.Equal(ObjectChangeVerdict.AlreadyExists, check.Verdict);
        Assert.Contains("SP_EXISTING", check.RefusalMessage!, System.StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gate_Create_Allows_WhenTheNameIsFree()
    {
        var gate = new ObjectChangeGate();

        var check = await gate.CheckCreateAsync("SP_NEW", _ => Task.FromResult(false));

        Assert.True(check.MayProceed);
    }

    [Fact]
    public async Task Gate_Create_Refuses_WithNoProbe()
    {
        // An unwired probe must not read as "the name is free" — that is the very overwrite this exists to stop.
        var gate = new ObjectChangeGate();

        var check = await gate.CheckCreateAsync("SP_NEW", existsAsync: null);

        Assert.Equal(ObjectChangeVerdict.Unverifiable, check.Verdict);
    }
}

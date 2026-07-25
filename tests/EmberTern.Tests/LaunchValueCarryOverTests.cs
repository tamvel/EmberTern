using System.Collections.Generic;
using EmberTern.App.Debugging;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

// Carrying entered values across a rebuilt launch panel: keep what can be proven, hand back what cannot,
// never guess in between. The primitives are pure functions over parameter rows, which is what every launch
// surface (procedure, function, package member, trigger NEW/OLD) is built from — so these tests are the
// behaviour, not a sample of it.
//
// The one thing not pinned here is the VM's call site, because reaching it needs a compile that SUCCEEDS —
// i.e. a live server — exactly as the launch-signature tests in DebuggerTabVmTests already document. What is
// pinned there is the decision that gates the rebuild; what is pinned here is what the rebuild then does.
public class LaunchValueCarryOverTests
{
    private static ExecuteProcedureParamRowViewModel Row(string name, string type, decimal? number = null, string? text = null)
    {
        var row = new ExecuteProcedureParamRowViewModel(name, type);
        if (number is { } n) { row.IsNull = false; row.NumericValue = n; }
        if (text is { } t) { row.IsNull = false; row.TextValue = t; }
        return row;
    }

    private static List<ExecuteProcedureParamRowViewModel> Rows(params ExecuteProcedureParamRowViewModel[] rows)
        => new(rows);

    // ─── ByName — a parameter is the same parameter when it is called the same thing ──────────

    [Fact]
    public void ByName_CarriesTheValueOfTheSameParameter()
    {
        var previous = Rows(Row("A", "INTEGER", number: 10m));
        var current = Rows(Row("A", "INTEGER"));

        LaunchValueCarryOver.ByName(previous, current);

        Assert.False(current[0].IsNull);
        Assert.Equal(10m, current[0].NumericValue);
        Assert.Equal(ValueOrigin.Restored, current[0].Origin); // kept, not inferred
    }

    [Fact]
    public void ByName_CarriesAcrossAWideningOfTheSameTypeFamily()
    {
        // INTEGER → BIGINT is the same input kind, so the value means exactly what it did.
        var previous = Rows(Row("A", "INTEGER", number: 10m));
        var current = Rows(Row("A", "BIGINT"));

        LaunchValueCarryOver.ByName(previous, current);

        Assert.Equal(10m, current[0].NumericValue);
    }

    [Fact]
    public void ByName_DoesNotCarryAcrossAChangeOfTypeFamily()
    {
        // INTEGER → VARCHAR would mean converting 10 into "10". The field is handed back instead.
        var previous = Rows(Row("A", "INTEGER", number: 10m));
        var current = Rows(Row("A", "VARCHAR(10)"));

        LaunchValueCarryOver.ByName(previous, current);

        Assert.True(current[0].IsNull);
        Assert.Equal(string.Empty, current[0].TextValue);
        Assert.Equal(ValueOrigin.Entered, current[0].Origin); // nothing was filled in, so nothing is marked
    }

    [Fact]
    public void ByName_FollowsTheNameRatherThanThePosition()
    {
        // Reordered parameters: the name is the stronger evidence, so each value follows its own parameter
        // rather than the slot it used to sit in.
        var previous = Rows(Row("A", "INTEGER", number: 1m), Row("B", "INTEGER", number: 2m));
        var current = Rows(Row("B", "INTEGER"), Row("A", "INTEGER"));

        LaunchValueCarryOver.ByName(previous, current);

        Assert.Equal(2m, current[0].NumericValue); // B
        Assert.Equal(1m, current[1].NumericValue); // A
    }

    [Fact]
    public void ByName_LeavesAnAddedParameterForTheUser()
    {
        var previous = Rows(Row("A", "INTEGER", number: 10m));
        var current = Rows(Row("A", "INTEGER"), Row("B", "VARCHAR(10)"));

        var matches = LaunchValueCarryOver.ByName(previous, current);
        LaunchValueCarryOver.SoleRemainingPair(previous, current, matches);

        Assert.Equal(10m, current[0].NumericValue);
        Assert.True(current[1].IsNull);                          // never asked for before
        Assert.Equal(ValueOrigin.Entered, current[1].Origin);
    }

    // ─── SoleRemainingPair — the one inference, and its limit ─────────────────────────────────

    [Fact]
    public void SoleRemainingPair_CarriesARenamedParameter()
    {
        var previous = Rows(Row("A", "INTEGER", number: 10m));
        var current = Rows(Row("CustomerId", "INTEGER"));

        var matches = LaunchValueCarryOver.ByName(previous, current);
        LaunchValueCarryOver.SoleRemainingPair(previous, current, matches);

        Assert.Equal(10m, current[0].NumericValue);
        Assert.Equal(ValueOrigin.Assumed, current[0].Origin); // the user is told an assumption was made
    }

    [Fact]
    public void SoleRemainingPair_CarriesNothing_WhenSeveralRemainOnEitherSide()
    {
        // Both names changed: any pairing would be a guess, so none is made.
        var previous = Rows(Row("A", "INTEGER", number: 1m), Row("B", "INTEGER", number: 2m));
        var current = Rows(Row("X", "INTEGER"), Row("Y", "INTEGER"));

        var matches = LaunchValueCarryOver.ByName(previous, current);
        LaunchValueCarryOver.SoleRemainingPair(previous, current, matches);

        Assert.True(current[0].IsNull);
        Assert.True(current[1].IsNull);
    }

    [Fact]
    public void SoleRemainingPair_DoesNotCarry_WhenTheTypeFamilyDiffers()
    {
        var previous = Rows(Row("A", "INTEGER", number: 10m));
        var current = Rows(Row("Label", "VARCHAR(10)"));

        var matches = LaunchValueCarryOver.ByName(previous, current);
        LaunchValueCarryOver.SoleRemainingPair(previous, current, matches);

        Assert.True(current[0].IsNull);
        Assert.Equal(ValueOrigin.Entered, current[0].Origin);
    }

    [Fact]
    public void ByName_ConsumesARetypedParameter_SoItIsNeverPairedWithAnother()
    {
        // A is the same parameter under both signatures — its type changed, so its value is dropped, but it is
        // still A. Leaving it unclaimed would let the pair rule offer A's old value to C, which is a guess
        // about two parameters we can already tell apart by name.
        var previous = Rows(Row("A", "INTEGER", number: 1m), Row("B", "INTEGER", number: 2m));
        var current = Rows(Row("A", "VARCHAR(10)"), Row("C", "INTEGER"));

        var matches = LaunchValueCarryOver.ByName(previous, current);
        LaunchValueCarryOver.SoleRemainingPair(previous, current, matches);

        Assert.True(current[0].IsNull);                          // A: retyped, so its value goes
        Assert.Equal(2m, current[1].NumericValue);               // C: paired with the only row left, B
        Assert.Equal(ValueOrigin.Assumed, current[1].Origin);
    }

    [Fact]
    public void SoleRemainingPair_CarriesNothing_WhenNothingIsLeftOver()
    {
        var previous = Rows(Row("A", "INTEGER", number: 1m));
        var current = Rows(Row("A", "INTEGER"));

        var matches = LaunchValueCarryOver.ByName(previous, current);
        LaunchValueCarryOver.SoleRemainingPair(previous, current, matches);

        Assert.Equal(ValueOrigin.Restored, current[0].Origin); // still the name match, not an assumption
    }

    // ─── The marker tells the truth about the value that is there NOW ─────────────────────────

    [Fact]
    public void EditingACarriedValue_ClearsTheMarker()
    {
        var previous = Rows(Row("A", "INTEGER", number: 10m));
        var current = Rows(Row("A", "INTEGER"));
        LaunchValueCarryOver.ByName(previous, current);
        Assert.True(current[0].IsAutoFilled);

        current[0].NumericValue = 99m;

        Assert.Equal(ValueOrigin.Entered, current[0].Origin);
        Assert.False(current[0].IsAutoFilled); // it is the user's value now, and says so
    }
}

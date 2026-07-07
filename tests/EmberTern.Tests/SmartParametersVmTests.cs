using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Pins the Part 3 App-side helpers (pure) + the "Unknown type → generic input, no guess"
/// behaviour. The dialog wiring + catalog typing are view/DB-side (manual smoke).</summary>
public class SmartParametersVmTests
{
    [Fact]
    public void HistoryKey_IsDeterministic_AndTrims()
    {
        var a = MainWindowViewModel.SmartParamsHistoryKey("select * from t where id = :id");
        var b = MainWindowViewModel.SmartParamsHistoryKey("   select * from t where id = :id  ");
        Assert.Equal(a, b); // trimmed → same key
        Assert.NotEqual(a, MainWindowViewModel.SmartParamsHistoryKey("select * from u where id = :id"));
        Assert.Equal(16, a.Length); // FNV-1a 64-bit hex
    }

    [Fact]
    public void BuildQueryParameters_PrependsAtMarker_AndPairsByIndex()
    {
        var p = MainWindowViewModel.BuildQueryParameters(new[] { "id", "code" }, new object?[] { 10, "x" });
        Assert.Equal(2, p.Count);
        Assert.Equal("@id", p[0].Name);
        Assert.Equal(10, p[0].Value);
        Assert.Equal("@code", p[1].Name);
        Assert.Equal("x", p[1].Value);
    }

    [Fact]
    public void BuildQueryParameters_NullValueBindsNull()
    {
        var p = MainWindowViewModel.BuildQueryParameters(new[] { "x" }, new object?[] { null });
        Assert.Null(p[0].Value);
    }

    [Fact]
    public void UnknownType_ShowsUnknown_UsesGenericTextInput_NoGuess()
    {
        var row = new ExecuteProcedureParamRowViewModel("p", UiStrings.SmartParamUnknownType);
        // Type column shows "Unknown"; the control is the generic single-line text input — we do
        // NOT guess a numeric/date/etc. type.
        Assert.Equal("Unknown", row.TypeText);
        Assert.Equal(ExecuteParamKind.Text, row.Kind);
        Assert.True(row.IsSingleLineTextKind);

        row.IsNull = false;
        row.TextValue = "hello";
        Assert.Equal("hello", row.Resolve());
    }
}

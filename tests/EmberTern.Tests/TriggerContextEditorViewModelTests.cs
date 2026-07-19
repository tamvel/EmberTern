using System.Collections.Generic;
using EmberTern.App.ViewModels;
using EmberTern.Core.Sql.Debugging;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, D10 Seam C: the launch-panel editor for a debugged trigger. It is a <b>dumb
/// VM</b> — it holds no BEFORE/AFTER × INSERT/UPDATE/DELETE availability rules of its own, it reads them from the
/// Core <see cref="TriggerContext"/> — and it maps the entered NEW/OLD values onto their synthetic frame
/// variables (the launch's root-frame seed, spec §8.1). Pure VM (no server, no UI).
/// </summary>
public class TriggerContextEditorViewModelTests
{
    private static readonly Dictionary<string, string> Types = new()
    {
        ["TOTAL"] = "NUMERIC(15,2)",
        ["STATUS"] = "VARCHAR(20)",
    };

    [Fact]
    public void BeforeUpdate_BothRecordsAvailable_MapsEnteredValuesToSynthetics()
    {
        var header = new TriggerHeader("ORDERS", TriggerTiming.Before, new[] { TriggerEvent.Update });
        var columns = new[]
        {
            new ContextColumn(TriggerRecord.New, "TOTAL", "ET_CTX_0"),
            new ContextColumn(TriggerRecord.Old, "TOTAL", "ET_CTX_1"),
        };
        var editor = new TriggerContextEditorViewModel(header, columns, Types);

        // Availability is READ from Core, never decided here: BEFORE UPDATE ⇒ both NEW and OLD.
        Assert.True(editor.NewAvailable);
        Assert.True(editor.OldAvailable);
        Assert.False(editor.HasMultipleActions);
        // Only the referenced column is a row (not the whole table).
        Assert.Equal("TOTAL", Assert.Single(editor.NewParameters.Params).Name);
        Assert.Equal("TOTAL", Assert.Single(editor.OldParameters.Params).Name);

        editor.NewParameters.Params[0].IsNull = false;
        editor.NewParameters.Params[0].NumericValue = 100m;
        editor.OldParameters.Params[0].IsNull = false;
        editor.OldParameters.Params[0].NumericValue = 50m;

        Assert.True(editor.Accept());
        var root = editor.CollectRootValues(editor.BuildTriggerContext());
        Assert.Equal(100m, root["ET_CTX_0"]); // NEW.TOTAL → its synthetic
        Assert.Equal(50m, root["ET_CTX_1"]);  // OLD.TOTAL → its synthetic
    }

    [Fact]
    public void MultiAction_SwitchingToInsert_HidesOld_ReadFromCore()
    {
        var header = new TriggerHeader("ORDERS", TriggerTiming.Before,
            new[] { TriggerEvent.Insert, TriggerEvent.Update });
        var columns = new[] { new ContextColumn(TriggerRecord.Old, "STATUS", "ET_CTX_0") };
        var editor = new TriggerContextEditorViewModel(header, columns, Types);

        Assert.True(editor.HasMultipleActions);
        // Index 0 = INSERT ⇒ OLD unavailable, NEW available.
        Assert.False(editor.OldAvailable);
        Assert.True(editor.NewAvailable);

        editor.SelectedActionIndex = 1; // UPDATE ⇒ OLD becomes available
        Assert.True(editor.OldAvailable);
        Assert.Equal(TriggerEvent.Update, editor.SelectedEvent);
    }

    [Fact]
    public void AfterUpdate_NewNotWritable_OldStillCollected()
    {
        // AFTER UPDATE: NEW is available but not writable; both records still contribute their entered values.
        var header = new TriggerHeader("ORDERS", TriggerTiming.After, new[] { TriggerEvent.Update });
        var columns = new[] { new ContextColumn(TriggerRecord.Old, "STATUS", "ET_CTX_0") };
        var editor = new TriggerContextEditorViewModel(header, columns, Types);

        Assert.True(editor.OldAvailable);
        editor.OldParameters.Params[0].IsNull = false;
        editor.OldParameters.Params[0].TextValue = "ACTIVE";

        Assert.True(editor.Accept());
        var root = editor.CollectRootValues(editor.BuildTriggerContext());
        Assert.Equal("ACTIVE", root["ET_CTX_0"]);
    }
}

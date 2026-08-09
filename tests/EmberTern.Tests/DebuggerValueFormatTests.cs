using System;
using System.Globalization;
using EmberTern.App.ViewModels;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐ <b>How the debugger spells a date (QA of the 2026-08-07 sprint).</b> The Variables / Context panel
/// rendered every value through the invariant culture, so a <c>TIMESTAMP</c> read as <c>08/07/2026 00:00:02</c>
/// — an American date on a Polish machine, and a spelling Firebird itself never prints.
/// <para>
/// ⭐ The rule these tests pin: <b>in the debugger the ENGINE's form is the readable one</b>
/// (<c>yyyy-MM-dd</c> / <c>yyyy-MM-dd HH:mm:ss</c>), because the reader is comparing what they see against
/// <c>isql</c>, against the source they are stepping through, and against literals they are about to type into
/// a Watch. ⛔ Numbers are NOT part of that change — they stay invariant, which is the harness's own literal
/// convention rather than a presentation choice.
/// </para>
/// <para>
/// ⚠ The last test is the one that matters most: the inline-edit box is seeded with this same text, so it must
/// still parse — through the REAL commit parser, not a re-implementation of it.
/// </para>
/// </summary>
public sealed class DebuggerValueFormatTests
{
    private static DebugVariableRowViewModel Row(string type)
        => new("V", DebugVariableKind.Local, type);

    [Fact]
    public void ATimestamp_IsShownTheWayFirebirdPrintsIt()
    {
        var row = Row("TIMESTAMP");
        row.Update(hasValue: true, new DateTime(2026, 8, 7, 0, 0, 2), changed: false);

        Assert.Equal("2026-08-07 00:00:02", row.ValueText);
    }

    // ⚠ The declared type is what separates the two — the driver hands a DATE and a TIMESTAMP back as the same
    // CLR type, so without it a timestamp standing at midnight would silently shed its 00:00:00.
    [Fact]
    public void TheDeclaredType_DecidesDateVersusTimestamp()
    {
        var midnight = new DateTime(2026, 8, 7);

        var date = Row("DATE");
        date.Update(hasValue: true, midnight, changed: false);
        Assert.Equal("2026-08-07", date.ValueText);

        var stamp = Row("TIMESTAMP");
        stamp.Update(hasValue: true, midnight, changed: false);
        Assert.Equal("2026-08-07 00:00:00", stamp.ValueText);
    }

    // ⚠ Written with a FRACTION on purpose, so the case is discriminating: a whole-second TimeSpan formats
    // identically under the invariant culture, so `14:05:09` alone would pass with the fix removed — a test
    // that looks right and pins nothing (the shape of probe case 39 / gotcha #322).
    [Fact]
    public void ATime_IsShownAsAClock()
    {
        var row = Row("TIME");
        row.Update(hasValue: true, new TimeSpan(0, 14, 5, 9, 500), changed: false);

        Assert.Equal("14:05:09.5", row.ValueText);       // …and not TimeSpan's own "14:05:09.5000000"
    }

    // ⭐ The engine form is a property of the DEBUGGER, not of the machine it runs on — a reader comparing the
    // panel with isql gets the same string in Warsaw and in Chicago.
    [Fact]
    public void TheEngineForm_DoesNotFollowTheMachinesCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "pl-PL" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name, useUserOverride: false);
                var row = Row("TIMESTAMP");
                row.Update(hasValue: true, new DateTime(2026, 8, 7, 14, 5, 9), changed: false);
                Assert.Equal("2026-08-07 14:05:09", row.ValueText);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ⛔ The boundary of the change: only date/time kinds moved. A number keeps the invariant spelling the
    // harness writes its literals in — reformatting those would be a different, unratified change.
    [Fact]
    public void NonDateValues_KeepTheirInvariantSpelling()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL", useUserOverride: false);
            var row = Row("NUMERIC(15,2)");
            row.Update(hasValue: true, 1234.56m, changed: false);

            Assert.Equal("1234.56", row.ValueText); // not the Polish "1234,56"
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ⚠⚠ The round trip, against the REAL parser. The edit box is seeded from the same formatter as the label,
    // so if the commit path ever stopped accepting that spelling, editing a date in the debugger would fail
    // with a red box and no explanation — and every observable state would still look correct.
    [Theory]
    [InlineData("TIMESTAMP", "2026-08-07 14:05:09")]
    [InlineData("DATE", "2026-08-07")]
    public void TheEditSeed_StillParsesThroughTheCommitPath(string type, string expectedSeed)
    {
        var value = type == "DATE" ? new DateTime(2026, 8, 7) : new DateTime(2026, 8, 7, 14, 5, 9);
        var row = Row(type);
        row.Update(hasValue: true, value, changed: false);
        row.BeginEdit();

        Assert.Equal(expectedSeed, row.EditText);

        Assert.True(DebuggerTabViewModel.TryParseEditedValue(row.EditText, value, type, out var parsed));
        Assert.Equal(value, parsed);
    }

    // ⚠ A sub-second value must survive the same trip: the seed carries the fraction precisely so that opening
    // a cell and pressing Enter cannot quietly truncate it.
    [Fact]
    public void ASubSecondTimestamp_IsNotTruncatedByTheEditSeed()
    {
        var value = new DateTime(2026, 8, 7, 14, 5, 9).AddTicks(TimeSpan.TicksPerMillisecond * 500);
        var row = Row("TIMESTAMP");
        row.Update(hasValue: true, value, changed: false);
        row.BeginEdit();

        Assert.Equal("2026-08-07 14:05:09.5", row.EditText);
        Assert.True(DebuggerTabViewModel.TryParseEditedValue(row.EditText, value, "TIMESTAMP", out var parsed));
        Assert.Equal(value, parsed);
    }

    // A watch has no declared type — the value itself is the only evidence, and that is stated rather than
    // hidden: a midnight value reads as a date.
    [Fact]
    public void AWatch_FormatsTheSameWay_WithoutADeclaredType()
    {
        var watch = new WatchRowViewModel("new.czas", hasSideEffect: false);
        watch.Apply(new EmberTern.Core.Sql.Debugging.EvaluationResult(
            Sql: "execute block returns (et_dbg_result varchar(8191)) as begin end",
            Success: true,
            Value: new DateTime(2026, 8, 7, 14, 5, 9),
            Error: null,
            Writes: null));

        Assert.Equal("2026-08-07 14:05:09", watch.ValueText);
    }
}

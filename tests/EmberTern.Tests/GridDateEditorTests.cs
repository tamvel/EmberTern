using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Threading;
using EmberTern.App.Views;
using EmberTern.Core.Formatting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐ The date editor in the Table Data grid (P3, 2026-08-07) — reported as "the control is clipped".
/// <para>
/// ⚠ <b>Measured before it was fixed, because the report does not name a dimension</b> and the two candidates
/// have different causes. The measurement below is the fix's whole justification, so it is kept as the guard:
/// the row is a FIXED <c>Height="32"</c> (gotcha #322 — it cannot grow from its content) and the cell padding
/// is <c>6,2</c>, so an editor has exactly <b>28 px</b>. Fluent's <see cref="CalendarDatePicker"/> asks for
/// more than that on its own, and a control that asks for more than its container gives is clipped, silently.
/// </para>
/// <para>
/// ⛔ Asserting the picker's DESIRED height against the space the cell actually leaves — not against a
/// number — is deliberate. A literal would pass the day someone changes the row height and the editor starts
/// being clipped again, which is exactly the failure this is written to prevent (the premise, not the policy —
/// gotcha #322).
/// </para>
/// <para>⚠ Joins <see cref="HeadlessCollection"/>; never its own class fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class GridDateEditorTests
{
    // The geometry TableDetailTabView pins for an editable data row, transcribed from its XAML. ⚠ If either
    // moves, DateEditor_FitsTheDataRow starts failing — which is the point: the editor's size is a
    // consequence of the row's, and the two must be decided together.
    private const double DataRowHeight = 32;
    private const double CellVerticalPadding = 2 + 2;
    private const double Available = DataRowHeight - CellVerticalPadding;

    private readonly HeadlessUnitTestSession _session;

    public GridDateEditorTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    [Fact]
    public async Task DateEditor_FitsTheDataRow()
    {
        await _session.Dispatch(() =>
        {
            var picker = new CalendarDatePicker();
            var window = new Window { Content = new DataGridCell { Content = picker } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            picker.Measure(new Size(400, 400));
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                picker.DesiredSize.Height <= Available,
                $"the date editor asks for {picker.DesiredSize.Height:0.##} px in a row that leaves " +
                $"{Available:0.##} px — anything above that is clipped, with no error and no layout shift " +
                "to make it visible (the row height is fixed, so it cannot grow to accommodate the editor).");
        }, default);
    }

    // ⭐ The other half of "looks consistent with the other editors": the same height ROLE as the TextBox
    // beside it. Read from the catalog rather than as a literal — role before value.
    [Fact]
    public async Task DateEditor_CarriesTheSameHeightRoleAsATextEditor()
    {
        await _session.Dispatch(() =>
        {
            var picker = new CalendarDatePicker();
            var loose = new CalendarDatePicker();
            var window = new Window
            {
                Content = new StackPanel { Children = { new DataGridCell { Content = picker }, loose } },
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(
                Application.Current!.TryFindResource("Size.Control", window.ActualThemeVariant, out var role)
                && role is double,
                "Size.Control must resolve — it is the height role every in-cell editor reads.");

            Assert.Equal((double)role!, picker.MinHeight);

            // ⛔ …and the same control OUTSIDE a grid cell keeps Fluent's form height. The narrowing is the
            // decision (a picker in the Execute dialog is a form field, not a grid editor), so a rule that
            // reached it too would be a different — unratified — change wearing this fix's clothes.
            Assert.NotEqual((double)role!, loose.MinHeight);
        }, default);
    }

    // ⚠ A guard on the PREMISE, not on the policy: the fix above is only correct while the data row really
    // is fixed at 32 with 2 px of vertical cell padding. Reading both out of the view means a future density
    // change fails here instead of silently re-clipping the editor.
    [Fact]
    public void TheDataRowGeometryThisFixRestsOn_IsStillWhatTheViewDeclares()
    {
        var xaml = System.IO.File.ReadAllText(RepoFile("src/EmberTern.App/Views/TableDetailTabView.axaml"));

        int rowStyle = xaml.IndexOf("\"DataGrid.data-edit DataGridRow\"", StringComparison.Ordinal);
        Assert.True(rowStyle > 0, "the data grid's own row style must still exist");
        Assert.Contains(
            $"Value=\"{DataRowHeight.ToString(CultureInfo.InvariantCulture)}\"",
            xaml.Substring(rowStyle, Math.Min(160, xaml.Length - rowStyle)));

        Assert.Contains("<Setter Property=\"Padding\" Value=\"6 2\" />", xaml);
    }

    // ⛔ The horizontal half. The data grid PERSISTS pixel column widths, so an editor with a MinWidth of
    // its own overflows any column the user narrowed below it — and the overflow survives a restart,
    // because the width does. The size belongs to the column; a guard on the source keeps it that way,
    // since a re-added MinWidth would look perfectly reasonable in review.
    [Fact]
    public void TheDateEditor_DeclaresNoWidthOfItsOwn()
    {
        var code = System.IO.File.ReadAllText(RepoFile("src/EmberTern.App/Views/TableDetailTabView.axaml.cs"));

        int template = code.IndexOf("BuildDateEditingTemplate", StringComparison.Ordinal);
        Assert.True(template > 0);
        int body = code.IndexOf("new CalendarDatePicker", template, StringComparison.Ordinal);
        Assert.True(body > 0, "the date editing template must still build a CalendarDatePicker");

        // ⚠ Only the object initializer, up to its closing `};` — the explanation of WHY there is no
        // MinWidth follows it and naturally contains the word.
        int end = code.IndexOf("};", body, StringComparison.Ordinal);
        Assert.True(end > body);
        var initializer = code.Substring(body, end - body);

        Assert.DoesNotContain("MinWidth", initializer);
        Assert.DoesNotContain("MinHeight", initializer);
    }

    // ── TIMESTAMP is not DATE (QA of this sprint) ─────────────────────────────────────────────
    //
    // ⭐⭐ The fix above made the picker fit, and fitting is what surfaced the second defect: the same picker
    // was serving TIMESTAMP columns, where it offers a DAY and nothing else. The visible half was "I cannot
    // edit the time"; the dangerous half was that committing a picked date wrote MIDNIGHT over the time the
    // row already had — a silent write of a value the user never chose (rule #11).
    //
    // ⚠ Avalonia 12.1.1 was checked before choosing the replacement: it ships CalendarDatePicker, DatePicker,
    // TimePicker and Calendar — and NO combined date+time control. Pairing two inside a 24 px grid cell would
    // be a bespoke composite, out of scope. So a TIMESTAMP is edited as text, which is the one editor that can
    // express the whole value.

    // ⚠ The expected kind travels as its NAME, not as the enum: the enum is internal (it is a view's private
    // vocabulary, exposed only so these two decisions are pinnable) and an xUnit theory method must be public.
    [Theory]
    [InlineData("DATE", "Date")]
    [InlineData("TIMESTAMP", "Timestamp")]
    // ⚠ WITH TIME ZONE is neither: its value is not a DateTime, so the typed parse would drop the zone —
    // the literal goes to Firebird, which owns that grammar.
    [InlineData("TIMESTAMP WITH TIME ZONE", "Text")]
    [InlineData("TIME", "Text")]
    [InlineData("VARCHAR(40)", "Text")]
    public void EachColumnType_GetsAnEditorThatCanExpressItsWholeValue(string baseTypeName, string expected)
        => Assert.Equal(expected, TableDetailTabView.EditorKindForType(baseTypeName, domain: null).ToString());

    // ⭐⭐ The reported defect itself: a DATE column showed `00:00:00`, an invented time the column cannot even
    // store. Asserted on the TEXT the cell actually renders — the column type reaching the template is the
    // thing that broke, and a test on the formatter alone would pass with that wiring cut (#315).
    [Fact]
    public async Task ADateColumn_ShowsNoTimeAtAll()
    {
        await _session.Dispatch(() =>
        {
            var midnight = new DateTime(2026, 8, 7);
            var row = new object?[] { midnight };

            var date = (TextBlock)TableDetailTabView.BuildTextCellTemplate(0, "DATE").Build(row)!;
            var stamp = (TextBlock)TableDetailTabView.BuildTextCellTemplate(0, "TIMESTAMP").Build(row)!;

            Assert.DoesNotContain("00:00", date.Text ?? string.Empty);
            Assert.Equal(DateTimeDisplay.Date(midnight), date.Text);
            // ⚠ …and the same CLR value on a TIMESTAMP column keeps its 00:00:00: the fix must not become
            // "hide midnight", which would lose a real time the column does store.
            Assert.Contains("00:00:00", stamp.Text ?? string.Empty);
        }, default);
    }

    // ⭐ The seed works to the SECOND (ratified 2026-08-08): sub-second digits are needed rarely and make the
    // value tedious to retype. ⚠⚠ Which is precisely why the untouched-edit check below has to exist — a seed
    // that shows less than the value holds would otherwise write the rounded value back on a tab-through.
    [Fact]
    public void TheTimestampEditor_IsSeededToTheSecond()
    {
        var withFraction = new DateTime(2026, 8, 7, 12, 34, 56).AddMilliseconds(789);

        Assert.Equal("2026-08-07 12:34:56",
            TableDetailTabView.EditorSeedText(withFraction, TableDetailTabView.CellEditorKind.Timestamp, "TIMESTAMP"));
    }

    [Fact]
    public void TabbingThroughACell_NeverRewritesIt()
    {
        var withFraction = new DateTime(2026, 8, 7, 12, 34, 56).AddMilliseconds(789);
        const TableDetailTabView.CellEditorKind stamp = TableDetailTabView.CellEditorKind.Timestamp;

        // Untouched: the box still holds its seed, so the rounded value must NOT reach the UPDATE.
        Assert.True(TableDetailTabView.IsUntouchedEdit("2026-08-07 12:34:56", withFraction, stamp, "TIMESTAMP"));
        // Actually edited — including deliberately typing a fraction back in, which stays allowed.
        Assert.False(TableDetailTabView.IsUntouchedEdit("2026-08-07 12:34:57", withFraction, stamp, "TIMESTAMP"));
        Assert.False(TableDetailTabView.IsUntouchedEdit("2026-08-07 12:34:56.789", withFraction, stamp, "TIMESTAMP"));
        Assert.Equal(withFraction, TableDetailTabView.ParseTimestampText("2026-08-07 12:34:56.789"));
    }

    // ⭐ The engine's own form is accepted exactly — it is what the editor is seeded with, so an untouched
    // commit must round-trip rather than quietly re-writing a rounded value.
    [Theory]
    [InlineData("2026-08-07 14:05:09", "2026-08-07T14:05:09.0000000")]
    [InlineData("2026-08-07 14:05:09.5", "2026-08-07T14:05:09.5000000")]
    [InlineData("2026-08-07 14:05", "2026-08-07T14:05:00.0000000")]
    [InlineData("2026-08-07", "2026-08-07T00:00:00.0000000")]
    public void TheTimestampEditor_ReadsTheEngineFormExactly(string text, string expected)
        => Assert.Equal(
            DateTime.ParseExact(expected, "O", CultureInfo.InvariantCulture),
            TableDetailTabView.ParseTimestampText(text));

    // ⛔⛔ The ambiguity this parse exists to remove, and the reason the reader's culture is tried before the
    // invariant one: `07/08/2026` is 7 August to a Pole and 8 July to Firebird, which reads a literal by its
    // SEPARATOR. Handing the server the string would let the engine decide against the person looking at a
    // Polish grid; a typed DateTime settles it where the user can see what they meant.
    [Fact]
    public void AnAmbiguousDate_IsReadTheWayTheGridDisplaysDates()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL", useUserOverride: false);
            Assert.Equal(new DateTime(2026, 8, 7), TableDetailTabView.ParseTimestampText("07.08.2026"));

            CultureInfo.CurrentCulture = new CultureInfo("en-US", useUserOverride: false);
            Assert.Equal(new DateTime(2026, 7, 8), TableDetailTabView.ParseTimestampText("07/08/2026"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ⚠ An empty box clears the cell; anything that is not a date at all goes to Firebird verbatim — the
    // VARCHAR behaviour — so its refusal reaches the edit-status banner. ⛔ Never silently dropped: a commit
    // that does nothing is indistinguishable from a broken grid.
    [Fact]
    public void UnparseableText_IsNotSwallowed()
    {
        Assert.Null(TableDetailTabView.ParseTimestampText("   "));
        Assert.Null(TableDetailTabView.ParseTimestampText(null));
        Assert.Equal("nie-data", TableDetailTabView.ParseTimestampText("nie-data"));
    }

    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "EmberTern.slnx")))
        {
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return System.IO.Path.Combine(dir!, relative);
    }
}

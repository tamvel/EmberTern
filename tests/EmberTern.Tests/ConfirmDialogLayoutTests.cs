using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The shared <see cref="ConfirmDialog"/>'s footer — specifically, <b>that the optional "do not ask again"
/// checkbox is fully readable in every shipped language</b>.
///
/// <para>⚠⚠ This file exists for a defect manual QA found and the tests could not: the checkbox originally
/// shared a row with the buttons, so at the shared <c>Width="420"</c> the buttons took what they needed and the
/// label got the remainder. A <c>CheckBox</c> neither wraps nor ellipsizes, so it simply CUT — the user saw
/// <i>"Nie pokazuj tego ostrzeż…"</i>. Every behavioural test stayed green throughout, because the option
/// worked perfectly; only its label was unreadable. Measured here: the label wants 358 px in English and
/// <b>435 px in Polish</b>, against ~380 px of content width — so a row of its own is necessary and still not
/// sufficient, which is why the label wraps.</para>
///
/// <para>⭐ It measures with the engine the product lays out with (#333/#336) rather than transcribing a rule,
/// and it states the PROPERTY ("nothing is cut") rather than the current fix: a label that fits on one line is
/// fine, a label that wraps is fine, a label that does neither is the defect. ⛔ Do not weaken this into "the
/// checkbox is on row 0" — that would pin today's layout, and a longer translation is exactly what would break
/// the property while satisfying the shape.</para>
///
/// <para>⚠⚠ <b>It reads the two labels straight out of the shipped .resx files instead of switching the app's
/// language.</b> That is not convenience: <c>Loc.Apply</c> mutates PROCESS-GLOBAL state and broadcasts to every
/// live subscriber, and collections outside this one run in parallel — the first version of this test used it
/// and intermittently killed an unrelated test with a <c>NullReferenceException</c> inside a half-built
/// <c>MainWindowViewModel</c> that had subscribed mid-flight. The text is what is being measured, so taking the
/// text and leaving the global state alone is both safer and more direct.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ConfirmDialogLayoutTests
{
    private readonly HeadlessUnitTestSession _session;

    public ConfirmDialogLayoutTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    [Theory]
    [InlineData("Strings.resx", "en")]
    [InlineData("Strings.pl.resx", "pl")]
    public async Task TheSuppressCheckbox_IsFullyReadable_InEveryShippedLanguage(string resx, string language)
    {
        var label = ShippedLabel(resx, "DebuggerIrreversibleDoNotAskAgain");
        Assert.False(string.IsNullOrWhiteSpace(label));

        await _session.Dispatch(() =>
        {
            var request = new ConfirmRequest
            {
                Title = "T",
                Message = "M",
                ConfirmLabel = "OK",
                CancelLabel = "Cancel",
                SuppressLabel = label,
            };

            var window = new ConfirmDialog { DataContext = new ConfirmDialogViewModel(request) };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(window.Width, double.PositiveInfinity));
            window.Arrange(new Rect(0, 0, window.Width, window.DesiredSize.Height));
            Dispatcher.UIThread.RunJobs();

            var checkbox = window.GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.True(checkbox.IsVisible);

            // ⚠⚠ The width it was GIVEN comes from the arranged bounds; the width it WANTS has to come from a
            // fresh UNCONSTRAINED measure. Measuring inside the layout pass is useless here, because
            // DesiredSize is already clamped to the space offered — so `DesiredSize <= Bounds` always holds and
            // the assertion can never fail. Measured: the first version of this guard did exactly that and
            // passed happily against the clipped layout, which is why it is spelled out.
            var given = checkbox.Bounds.Width;
            checkbox.InvalidateMeasure();
            checkbox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var oneLine = checkbox.DesiredSize;

            checkbox.InvalidateMeasure();
            checkbox.Measure(new Size(given, double.PositiveInfinity));
            var atGivenWidth = checkbox.DesiredSize;

            if (oneLine.Width > given + 0.5)
            {
                Assert.True(
                    atGivenWidth.Height > oneLine.Height + 0.5,
                    $"[{language}] the suppress checkbox is CLIPPED: its label needs {oneLine.Width:F1} px, it "
                    + $"was given {given:F1} px, and it did not wrap. Give it room or let it wrap — do NOT "
                    + "shrink the font, widen this shared dialog, or shorten the sentence to fit.");
            }

            // ⚠ DesiredSize INCLUDES the margin, Bounds does not — comparing them raw fails by exactly the
            // margin and reports "the row did not grow" about a row that grew perfectly.
            var arranged = checkbox.Bounds.Height + checkbox.Margin.Top + checkbox.Margin.Bottom;

            Assert.True(
                arranged >= atGivenWidth.Height - 0.5,
                $"[{language}] the suppress checkbox wrapped but its row did not grow: it needs "
                + $"{atGivenWidth.Height:F1} px of height and was given {arranged:F1} px.");

            window.Close();
        }, CancellationToken.None);
    }

    /// <summary>The value a shipped resource file carries for a key — the text the user will actually see.</summary>
    private static string ShippedLabel(string resxFileName, string key)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Localization", resxFileName);
        return XDocument.Load(path).Root!
            .Elements("data")
            .Single(d => (string?)d.Attribute("name") == key)
            .Element("value")!.Value;
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

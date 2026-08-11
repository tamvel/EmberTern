using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The SQL editor's <b>Messages</b> log — that the timestamp and the message it belongs to sit on ONE baseline.
///
/// <para>⚠⚠ <b>The reported symptom was "the rows look uneven", and the obvious reading of it was wrong.</b>
/// The panel is already a grid with a fixed 80 px monospace timestamp column, so the message always starts at
/// the same x and the length of the time cannot move it — every horizontal requirement was met before this
/// round. What was actually uneven is VERTICAL: the timestamp renders at <c>Text.Compact</c> and the message at
/// <c>Text.Application</c> with an explicit 18 px line, so the two texts were laid out in independently sized
/// line boxes and the smaller one's baseline sat higher.</para>
///
/// <para>⭐ Measured with the engine that lays the product out, on the two roles the template actually uses —
/// not on transcribed pixel sizes (#336/#333). ⛔ Do not "fix" a failure here by nudging a Margin: a margin
/// that happens to cancel the difference at today's two font sizes silently stops working the moment either
/// role moves.</para>
///
/// <para>⚠ Joins the headless collection (it resolves application resources and lays out controls) and never
/// takes its own class fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class MessagesLogAlignmentTests
{
    private readonly HeadlessUnitTestSession _session;

    public MessagesLogAlignmentTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    private static double Role(string key)
    {
        Assert.True(Application.Current!.TryFindResource(key, out var value), key + " does not resolve.");
        return Assert.IsType<double>(value);
    }

    /// <summary>The first line's baseline, measured the way the control will draw it.</summary>
    private static double BaselineOf(TextBlock block)
    {
        block.Measure(new Size(400, 100));
        block.Arrange(new Rect(0, 0, 400, 100));
        return block.TextLayout.Baseline;
    }

    [Fact]
    public async Task TheTimestampAndItsMessage_ShareOneBaseline()
    {
        await _session.Dispatch(() =>
        {
            // ⚠⚠ Each control takes its OWN line height as the template declares it — not one value shared by
            // the test. Reading a single number and applying it to both would make this a test of its own
            // arrangement: deleting the timestamp's LineHeight from the markup would leave the measurement
            // perfectly aligned and green, which is precisely the defect it exists to catch.
            var timestamp = new TextBlock
            {
                Text = "11:08:04",
                FontFamily = new FontFamily("Cascadia Code,Consolas,Menlo,monospace"),
                FontSize = Role("Text.Compact.Size"),
                TextWrapping = TextWrapping.NoWrap,
            };
            if (LineHeightOf("TimestampLabel") is { } timeLine) timestamp.LineHeight = timeLine;

            var message = new TextBlock
            {
                Text = "Rozpoczęto transakcję Data.",
                FontSize = Role("Text.Application.Size"),
                TextWrapping = TextWrapping.Wrap,
            };
            if (LineHeightOf("Binding Text") is { } messageLine) message.LineHeight = messageLine;

            var timeBaseline = BaselineOf(timestamp);
            var messageBaseline = BaselineOf(message);

            Assert.True(
                Math.Abs(timeBaseline - messageBaseline) <= 0.5,
                $"The timestamp and the message do not sit on one baseline ({timeBaseline:0.##} vs "
                + $"{messageBaseline:0.##}), so the log rows read as uneven.");
        }, default);
    }

    /// <summary>
    /// ⚠ The premise: the template really does give both texts the same line height. Without this the test
    /// above measures a pair of controls it built itself and says nothing about the panel — and it is the
    /// timestamp's <c>LineHeight</c> that was missing, so this is the half that would have caught the defect.
    /// </summary>
    [Fact]
    public void TheTemplate_GivesBothTextsTheSameLineHeight()
    {
        var markup = MessagesTemplate();

        var lineHeights = Regex.Matches(markup, @"LineHeight=""(\d+)""");
        Assert.Equal(2, lineHeights.Count);
        Assert.Equal(lineHeights[0].Groups[1].Value, lineHeights[1].Groups[1].Value);
    }

    /// <summary>
    /// The <c>LineHeight</c> the template gives the element containing <paramref name="marker"/>, or
    /// <c>null</c> when it sets none — which is exactly the state that produced the defect, so "absent" has to
    /// be representable rather than defaulted away.
    /// </summary>
    private static double? LineHeightOf(string marker)
    {
        var template = MessagesTemplate();
        var at = template.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(at >= 0, $"'{marker}' is no longer in the Messages template — this guard lost its subject.");

        // The element runs from the '<' before the marker to its closing '>' or '/>'.
        var start = template.LastIndexOf('<', at);
        var end = template.IndexOf('>', at);
        var element = template[start..end];

        var match = Regex.Match(element, @"LineHeight=""(\d+(?:\.\d+)?)""");
        return match.Success
            ? double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    /// <summary>The <c>QueryMessageViewModel</c> DataTemplate, sliced out of MainWindow's markup.</summary>
    private static string MessagesTemplate()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Views", "MainWindow.axaml");
        var markup = File.ReadAllText(path);

        var start = markup.IndexOf("<DataTemplate DataType=\"vm:QueryMessageViewModel\">", StringComparison.Ordinal);
        Assert.True(start >= 0, "The Messages DataTemplate was not found — this guard has lost its subject.");

        var end = markup.IndexOf("</DataTemplate>", start, StringComparison.Ordinal);
        return markup[start..end];
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

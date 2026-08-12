using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.App.ViewModels;
using EmberTern.App.Views;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The Execution Summary's per-table activity cards — the expanded body of the exec-info panel on the
/// Procedure and Function editors.
///
/// <para>⚠⚠ <b>This class is written because its ABSENCE shipped a visible defect.</b> Both views carried a
/// comment claiming "<c>ExecActivityCardTests</c> reads the realized text back", and no such class existed.
/// Meanwhile C6 replaced the bound items with <see cref="ExecActivityLineViewModel"/> but left the outer
/// <c>DataTemplate</c> declaring Core's <see cref="TableActivityLine"/>. On a <c>DataTemplate</c>
/// <c>x:DataType</c> is also the MATCHING type, so the template silently stopped matching, the
/// <see cref="ItemsControl"/> fell back to the default presenter, and the card area rendered the literal
/// text <c>"EmberTern.App.ViewModels.ExecActivityLineViewModel"</c> (user report 2026-08-12).</para>
///
/// <para>⭐ The guard is deliberately on the REALIZED output rather than on the declared type: a mismatch
/// between the template's type and the collection's element type is one way to break this, and a dropped
/// binding or a future template rewrite are others. Asking "does the card show the table name" fails for
/// all of them; asking "does the XAML spell the right type" fails only for today's.</para>
///
/// <para>⚠ Joins <see cref="HeadlessCollection"/>; never its own class fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ExecActivityCardTests
{
    private readonly HeadlessUnitTestSession _session;

    public ExecActivityCardTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    [Theory]
    [InlineData("procedure")]
    [InlineData("function")]
    public async Task TheCardTemplate_MatchesTheRowsTheViewModelActuallyProduces(string editor)
    {
        await _session.Dispatch(() =>
        {
            var template = CardTemplate(editor);
            var row = new ExecActivityLineViewModel(
                new TableActivityLine("ORDERS", new TableChange[] { new InsertChange(14) }));

            // ⭐ THE defect, stated as its cause: a DataTemplate that does not match its item is not an
            // error — the host quietly falls back to ToString().
            Assert.True(
                template.Match(row),
                $"the {editor} editor's activity-card template does not match "
                + $"{nameof(ExecActivityLineViewModel)}, so the ItemsControl will fall back to the default "
                + "presenter and render the view model's TYPE NAME instead of the card.");
        }, default);
    }

    [Theory]
    [InlineData("procedure")]
    [InlineData("function")]
    public async Task TheCard_RendersTheTableNameAndTheChangeSentence_NotTheViewModelsTypeName(string editor)
    {
        await _session.Dispatch(() =>
        {
            var host = new ItemsControl
            {
                ItemTemplate = CardTemplate(editor),
                ItemsSource = new[]
                {
                    new ExecActivityLineViewModel(
                        new TableActivityLine("ORDERS", new TableChange[] { new InsertChange(14) })),
                },
            };
            var window = new Window { Content = host, Width = 600, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var texts = host.GetVisualDescendants().OfType<TextBlock>().Select(TextOf).ToList();
            var all = string.Join(" | ", texts);

            Assert.DoesNotContain(nameof(ExecActivityLineViewModel), all, StringComparison.Ordinal);
            Assert.Contains("ORDERS", all, StringComparison.Ordinal);

            // The localized sentence is split around its count so the count can keep its kind colour, so the
            // realized runs carry the number on its own — that is the part a wrong DataType loses entirely.
            Assert.Contains(texts, t => t.Contains("14", StringComparison.Ordinal));
        }, default);
    }

    // ⚠ The change sentence is built from three <c>&lt;Run&gt;</c> inlines (so the count can keep its kind
    // colour wherever the language puts it), and a TextBlock carrying Inlines reports a NULL Text — reading
    // only Text would have made this guard silently blind to the very line it is here to check.
    private static string TextOf(TextBlock tb)
    {
        if (tb.Text is { } direct) return direct;
        if (tb.Inlines is null) return string.Empty;
        return string.Concat(tb.Inlines.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text ?? string.Empty));
    }

    // The activity-card ItemTemplate, read out of the real view rather than restated here — the view is the
    // only place that decides it, and a copy would keep passing after the view drifted (gotcha #284).
    private static IDataTemplate CardTemplate(string editor)
    {
        Control view = editor == "procedure" ? new ProcedureDetailTabView() : new FunctionDetailTabView();
        var window = new Window { Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var cards = view.GetVisualDescendants().OfType<ItemsControl>()
            .Concat(Find(view))
            .FirstOrDefault(c => c.Name == "ExecActivityCards");
        Assert.NotNull(cards);
        Assert.NotNull(cards!.ItemTemplate);
        return cards.ItemTemplate!;
    }

    // The exec-info panel lives inside a collapsed/unrealised branch on a freshly constructed view, so the
    // visual tree alone may not reach it — fall back to the logical tree, which the XAML always builds.
    private static IEnumerable<ItemsControl> Find(Control root)
    {
        if (root.FindNameScope()?.Find("ExecActivityCards") is ItemsControl named) yield return named;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Localization;
using EmberTern.Core.Performance;
using EmberTern.Core.Query;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap <b>C6</b> — <see cref="ExecutionSummary"/> / <see cref="ExecutionActivity"/> on decision D‑3, and the
/// plural mechanism that made them migratable at all.
///
/// <para>⭐⭐ <b>The zero-content-change proof is elsewhere, and that is the point.</b>
/// <c>ExecutionSummaryTests</c> and <c>ExecutionActivityTests</c> pin the English wording literally and were
/// NOT touched by this etap — they still call the no-resolver overloads, which render through
/// <c>ExecutionEnglish</c>. What this class adds is the other half a dual form needs: that the CATALOG
/// reproduces those same sentences, for every shape and every count. Two independent bodies of data
/// (literals in Core, entries in the resx) compared through one composer.</para>
///
/// <para>⚠ Joins the headless collection because it swaps <c>Loc</c>'s catalog — process-global state that a
/// concurrent reader of <c>UiStrings</c> would see (localization.md §5.4). Every swap is undone in a
/// <c>finally</c>.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ExecutionSummaryLocalizationTests
{
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    // Counts chosen to cross every boundary the mechanism has: the singular, the Slavic "few" band, the
    // teens the band excludes, zero, and a value big enough to expose a grouping specifier if one appeared.
    private static readonly long[] Counts = { 0, 1, 2, 4, 5, 12, 21, 22, 112, 12_345 };

    private static ExecutionSummary Summary(
        long ins = 0, long upd = 0, long del = 0, long read = 0,
        bool changesMeasured = true, bool readsMeasured = true, int? affected = null, long ms = 93)
        => new()
        {
            Inserts = ins,
            Updates = upd,
            Deletes = del,
            RowsRead = read,
            ChangesMeasured = changesMeasured,
            ReadsMeasured = readsMeasured,
            RecordsAffected = affected,
            Elapsed = TimeSpan.FromMilliseconds(ms),
        };

    /// <summary>Every rendering shape this module can produce, driven across every interesting count.</summary>
    private static IEnumerable<ExecutionSummary> EveryShape()
    {
        foreach (var n in Counts)
        {
            yield return Summary(ins: n, upd: n, del: n, read: n);                       // all terms
            yield return Summary(upd: n);                                                // one term
            yield return Summary(read: n);                                               // reads only
            yield return Summary(ins: n, readsMeasured: false);                          // changes, no reads
            yield return Summary(changesMeasured: false, readsMeasured: false,
                affected: (int)Math.Min(n, int.MaxValue));                               // driver fallback
        }

        yield return Summary(changesMeasured: false, readsMeasured: false, affected: null);   // null affected
        yield return Summary();                                                               // measured, no work
    }

    private static PerTableReadRow Row(string t, long ins = 0, long upd = 0, long del = 0)
        => new(t, 0, 0, ins, upd, del);

    // ── The dual form agrees ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The English literals and the catalog say exactly the same thing, for every shape and every
    /// count.</b> This is C6's proof of zero content change, and it is stronger than a text diff: the two
    /// halves are independent DATA (a table in <c>ExecutionEnglish</c>, entries in <c>Strings.resx</c>) run
    /// through one shared composer, so nothing but a wording difference can make them disagree.
    ///
    /// <para>⚠ It also covers the number side. If a translator ever adds a grouping specifier
    /// (<c>{0:N0}</c>) to one of these entries, the halves diverge on a five-digit count and this goes red —
    /// which is exactly the hazard #357 measured, caught here without a test that has to restate it.</para>
    /// </summary>
    [Fact]
    public void TheEnglishAndLocalizedForms_Agree_ForEveryShapeAndEveryCount()
    {
        foreach (var s in EveryShape())
        {
            Assert.Equal(s.BuildMessage(), s.BuildMessage(Loc.Format));
            Assert.Equal(s.BuildDetailedMessage(), s.BuildDetailedMessage(Loc.Format));
            Assert.Equal(s.BuildCompactLine(), s.BuildCompactLine(Loc.Format));
        }
    }

    /// <inheritdoc cref="TheEnglishAndLocalizedForms_Agree_ForEveryShapeAndEveryCount"/>
    [Fact]
    public void TheEnglishAndLocalizedLogLines_Agree_ForEveryCount()
    {
        foreach (var n in Counts)
        {
            var reads = new[] { Row("ORDERS", ins: n, upd: n, del: n), Row("ITEMS", upd: n) };
            Assert.Equal(
                ExecutionActivity.BuildLogLines(reads),
                ExecutionActivity.BuildLogLines(reads, Loc.Format));
        }
    }

    /// <summary>
    /// The singular English wording the migration introduced, stated so it is a DECISION on record rather
    /// than a drift somebody notices later.
    ///
    /// <para>⚠ Before C6 the driver-total fallback had no singular form at all and rendered
    /// <c>"1 rows affected"</c> — reachable whenever the driver reported exactly one row. Giving the key a
    /// plural family is what makes it translatable, and correcting the English is the consequence. It is the
    /// ONLY English value this etap changed.</para>
    /// </summary>
    [Fact]
    public void TheOneRowFallback_IsTheOnlyEnglishWordingThisEtapChanged()
    {
        var one = Summary(changesMeasured: false, readsMeasured: false, affected: 1, ms: 5);
        Assert.Equal("1 row affected in 5 ms", one.BuildMessage(Loc.Format));
        Assert.Equal("Executed in 5 ms · 1 row affected", one.BuildCompactLine(Loc.Format));

        // …and the plural forms around it are untouched.
        var many = Summary(changesMeasured: false, readsMeasured: false, affected: 42, ms: 5);
        Assert.Equal("42 rows affected in 5 ms", many.BuildMessage(Loc.Format));
        var none = Summary(changesMeasured: false, readsMeasured: false, affected: 0, ms: 4);
        Assert.Equal("0 rows affected in 4 ms", none.BuildMessage(Loc.Format));
    }

    // ── Core stays language-unaware ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// <b>Core produces the same keys and the same arguments whatever the language is.</b> The producer is
    /// handed a resolver, so the only way it could vary is by consulting one — which is the thing D‑3 forbids.
    /// </summary>
    [Fact]
    public void Core_YieldsTheSameKeysAndArguments_WhateverTheLanguage()
    {
        var s = Summary(ins: 1, upd: 2, del: 5, read: 12);
        var english = Captured(r => s.BuildDetailedMessage(r));

        try
        {
            Loc.UseCatalogForVerification(new PluralCatalog(), Pseudo);
            Assert.Equal(english, Captured(r => s.BuildDetailedMessage(r)));
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⭐ <b>The premise the whole mechanism rests on (ratified R3): a plural key's first argument is the
    /// COUNT.</b> The App reads argument {0} to pick a category without knowing what the sentence says, so a
    /// producer that put a table name or an id there would silently choose a grammatical form from unrelated
    /// data. Pinned as a premise, not as a policy (#322).
    /// </summary>
    [Fact]
    public void EveryProducerOfAPluralKey_PassesACountFirst()
    {
        var families = PluralKeysInTheCatalog();
        Assert.NotEmpty(families);

        var produced = new List<LocalizableMessage>();
        var s = Summary(ins: 3, upd: 3, del: 3, read: 3);
        s.BuildDetailedMessage(Capture(produced));
        s.BuildCompactLine(Capture(produced));
        s.BuildMessage(Capture(produced));
        Summary(changesMeasured: false, affected: 3).BuildMessage(Capture(produced));
        ExecutionActivity.BuildLogLines(new[] { Row("T", ins: 3, upd: 3, del: 3) }, Capture(produced));

        var seen = produced.Where(m => families.Contains(m.Key.Value)).ToList();
        Assert.NotEmpty(seen);
        Assert.All(seen, m => Assert.True(
            m.TryGetCount(out _),
            $"{m.Key.Value} has a plural family but its first argument is not a count."));
    }

    // ── The plural mechanism, under a three-form language ────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The case the mechanism exists for: a language with three forms gets three forms, and the
    /// producer said nothing about grammar.</b> The very same <see cref="ExecutionSummary"/> renders two
    /// forms in a <c>one-other</c> language and three in a <c>one-few-many</c> one — chosen from the count in
    /// argument {0} and the rule set the CULTURE declares.
    /// </summary>
    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "few")]
    [InlineData(4, "few")]
    [InlineData(5, "many")]
    [InlineData(12, "many")]    // the teen band the "few" rule excludes …
    [InlineData(22, "few")]     // … and the band it does not
    // ⚠ Zero is deliberately absent: a change term is OMITTED when its count is zero, so `RowsInserted`
    // cannot be reached with 0. The zero case belongs to the driver-total fallback and is covered by
    // AZeroCount_TakesTheGrammarsZeroForm.
    public void AThreeFormLanguage_PicksTheFormItsGrammarRequires(long count, string expected)
    {
        try
        {
            Loc.UseCatalogForVerification(new PluralCatalog(), Pseudo);

            var rendered = Summary(ins: count).BuildDetailedMessage(Loc.Format);

            Assert.Contains("[" + expected + "]", rendered, StringComparison.Ordinal);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// The same key, the same producer, a two-form language: two forms and nothing else.
    /// </summary>
    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "other")]
    [InlineData(22, "other")]
    public void ATwoFormLanguage_PicksFromTwo(long count, string expected)
    {
        try
        {
            Loc.UseCatalogForVerification(new PluralCatalog(), CultureInfo.InvariantCulture);

            Assert.Contains(
                "[" + expected + "]", Summary(ins: count).BuildDetailedMessage(Loc.Format), StringComparison.Ordinal);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⭐ <b>Zero has a grammatical form too, and it is not the singular.</b> Reachable only through the
    /// driver-total fallback ("0 rows affected"), because a change term is omitted when its count is zero —
    /// a measured limit of the shape, not of the mechanism.
    /// </summary>
    [Fact]
    public void AZeroCount_TakesTheGrammarsZeroForm()
    {
        Assert.Equal("many", PluralRules.SuffixFor(PluralRules.CategoryFor(PluralRules.OneFewMany, 0)));
        Assert.Equal("other", PluralRules.SuffixFor(PluralRules.CategoryFor(PluralRules.OneOther, 0)));

        // And the product reaches it: the English catalog's `other` form is what renders.
        Assert.Equal(
            "0 rows affected in 4 ms",
            Summary(changesMeasured: false, affected: 0, ms: 4).BuildMessage(Loc.Format));
    }

    /// <summary>
    /// <b>A category a translation has not covered falls back to <c>other</c>, never to the raw key.</b>
    /// ⚠ The build-time answer is <c>EveryPluralFamily_IsCompleteInEveryShippedCulture</c>; this is the
    /// runtime one, and it renders a readable sentence rather than an identifier.
    /// </summary>
    [Fact]
    public void AMissingCategory_FallsBackToOther_NotToTheKey()
    {
        try
        {
            Loc.UseCatalogForVerification(new GappyCatalog(), Pseudo);

            var rendered = Summary(ins: 5).BuildDetailedMessage(Loc.Format);

            Assert.Contains("[other]", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("Query.Exec.RowsInserted", rendered, StringComparison.Ordinal);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// <b>A flat key is untouched by the probe.</b> Most messages in the app carry an integer first argument
    /// that is an id, a version or an elapsed time; they must resolve exactly as before.
    /// </summary>
    [Fact]
    public void AFlatKeyWithAnIntegerArgument_ResolvesFlat()
    {
        // "Executed in {0} ms" carries a count-shaped argument and has no family — it must not change.
        Assert.Equal("Executed in 1 ms", Summary(ms: 1).BuildCompactLine(Loc.Format));
        Assert.Equal("Executed in 93 ms", Summary().BuildCompactLine(Loc.Format));
    }

    // ── Splitting a sentence around its number (the per-table card) ──────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The card colours the NUMBER wherever the language puts it.</b> Under a language whose sentence
    /// starts with a verb, the split still isolates the count — which is the whole reason the card stopped
    /// binding <c>Count</c> and <c>Verb</c> side by side.
    /// </summary>
    [Fact]
    public void TheActivityCard_SplitsTheSentenceAroundItsCount()
    {
        var change = new ExecActivityChangeViewModel(new InsertChange(14));

        // English: the number leads.
        Assert.Equal(string.Empty, change.Before);
        Assert.Equal("14", change.Value);
        Assert.Equal(" inserted", change.After);

        try
        {
            Loc.UseCatalogForVerification(new PluralCatalog(), Pseudo);

            // A language that puts the verb first: the number is still isolated, now in the middle.
            Assert.Equal("wstawiono ", change.Before);
            Assert.Equal("14", change.Value);
            Assert.Equal(" wierszy", change.After);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// <b>A sentence with no placeholder still renders — it just loses the accent.</b> ⚠ Degrading is the
    /// right trade on a status surface: a translation that dropped <c>{0}</c> must not blank the line.
    /// </summary>
    [Fact]
    public void TheActivityCard_KeepsTheWholeSentence_WhenThereIsNoPlaceholder()
    {
        try
        {
            Loc.UseCatalogForVerification(new NoPlaceholderCatalog(), CultureInfo.InvariantCulture);

            var change = new ExecActivityChangeViewModel(new UpdateChange(7));

            Assert.Equal("nothing to substitute", change.Before);
            Assert.Equal(string.Empty, change.Value);
            Assert.Equal(string.Empty, change.After);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>Each change kind keeps its own glyph and its own colour key — unchanged from the template
    /// this row model replaced.</summary>
    [Fact]
    public void EachChangeKind_KeepsItsIconAndItsColourKey()
    {
        Assert.Equal("Icon.Plus", new ExecActivityChangeViewModel(new InsertChange(1)).IconGeometryKey);
        Assert.Equal("SuccessIconBrush", new ExecActivityChangeViewModel(new InsertChange(1)).IconResourceKey);
        Assert.Equal("Icon.Pencil", new ExecActivityChangeViewModel(new UpdateChange(1)).IconGeometryKey);
        Assert.Equal("WarningIconBrush", new ExecActivityChangeViewModel(new UpdateChange(1)).IconResourceKey);
        Assert.Equal("Icon.Trash", new ExecActivityChangeViewModel(new DeleteChange(1)).IconGeometryKey);
        Assert.Equal("DangerIconBrush", new ExecActivityChangeViewModel(new DeleteChange(1)).IconResourceKey);
    }

    // ── The SHIPPED markup actually renders the split sentence ───────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b><c>Run.Text</c> is bindable in this Avalonia build, and three bound runs compose one
    /// sentence.</b> The framework half of the card's rendering, measured rather than assumed — "the code is
    /// right" is not "the feature works" (#338), and nothing in this repository had relied on binding an
    /// inline before.
    ///
    /// <para>⚠ <b>Stated limit:</b> this exercises the SHAPE, not the shipped XAML file; the markup half is
    /// <see cref="TheShippedActivityMarkup_HasNoWhitespaceBetweenItsRuns"/>. Loading the real template at run
    /// time would need <c>Avalonia.Markup.Xaml</c> as a test dependency, which is not reachable from this
    /// project — so the two halves are asserted separately and both are named here so neither reads as
    /// complete on its own.</para>
    /// </summary>
    [Fact]
    public void ThreeBoundRuns_ComposeTheSentence()
    {
        var block = new Avalonia.Controls.TextBlock();
        foreach (var path in new[] { "Before", "Value", "After" })
        {
            var run = new Avalonia.Controls.Documents.Run();
            run.Bind(Avalonia.Controls.Documents.Run.TextProperty, new Avalonia.Data.Binding(path));
            block.Inlines!.Add(run);
        }

        block.DataContext = new ExecActivityChangeViewModel(new InsertChange(14));

        var rendered = string.Concat(block.Inlines!
            .Cast<Avalonia.Controls.Documents.Run>()
            .Select(r => r.Text));
        Assert.Equal("14 inserted", rendered);
    }

    /// <summary>
    /// ⭐⭐ <b>No whitespace between the runs in the shipped markup.</b>
    ///
    /// <para>Whitespace between inline elements is CONTENT: a newline for readability puts a stray space in
    /// the middle of every activity line — a defect that survives review because the markup looks tidier with
    /// it than without. Read from the real <c>.axaml</c>, never transcribed (#333).</para>
    /// </summary>
    [Theory]
    [InlineData("ProcedureDetailTabView")]
    [InlineData("FunctionDetailTabView")]
    public void TheShippedActivityMarkup_HasNoWhitespaceBetweenItsRuns(string view)
    {
        var path = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Views", view + ".axaml");
        var source = File.ReadAllText(path);

        var start = source.IndexOf("<Run Text=\"{Binding Before}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{view}.axaml no longer declares the activity sentence runs.");

        var end = source.IndexOf("</TextBlock>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"{view}.axaml: the activity runs are not inside a TextBlock.");

        var inlines = source[start..end];
        // ⚠ `<Run\s`, not `<Run\b` — the latter also counts the `<Run.Foreground>` property element and
        // reports four inlines where there are three.
        Assert.Equal(3, Regex.Matches(inlines, @"<Run\s").Count);
        Assert.Contains("{Binding Value}", inlines, StringComparison.Ordinal);
        Assert.Contains("{Binding After}", inlines, StringComparison.Ordinal);

        // Between one inline element and the next there must be nothing at all.
        Assert.DoesNotMatch(@">\s+<Run\s", inlines);
        Assert.DoesNotMatch(@"/>\s+</TextBlock>", source[start..(end + "</TextBlock>".Length)]);
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

    // ── Live switching (ratified R7) ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The exec-info panels follow a language change.</b> Both texts are stored
    /// <c>[ObservableProperty]</c> values built by joining several resolved sentences, so a
    /// <c>PropertyChanged</c> alone would re-read the same finished English — gotcha #353, and the reason
    /// <c>RefreshLocalizedText</c> had to become the fifth member of the per-kind family.
    ///
    /// <para>⚠ The run happens BEFORE the switch on purpose: a panel populated afterwards would render
    /// correctly even if nothing recomposed.</para>
    /// </summary>
    [Fact]
    public async Task SwitchingLanguage_RecomposesTheProcedureExecPanels()
    {
        var vm = new ProcedureDetailTabViewModel("P")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(new ProcedureExecOutcome(
                new QueryResult { RecordsAffected = null, Elapsed = TimeSpan.FromMilliseconds(93) },
                null,
                Summary(ins: 8, upd: 16, del: 8, read: 20_552),
                new[] { Row("ORDERS", ins: 8, upd: 16, del: 8) })),
        };

        await vm.ExecuteProcedureCommand.ExecuteAsync(null);
        Assert.Contains("16 rows updated", vm.ExecInfo, StringComparison.Ordinal);

        try
        {
            Loc.UseCatalogForVerification(new PluralCatalog(), Pseudo);
            vm.RefreshLocalizedText();

            Assert.DoesNotContain("rows updated", vm.ExecInfo, StringComparison.Ordinal);
            Assert.Contains("[", vm.ExecInfo, StringComparison.Ordinal);
            Assert.Contains("[", vm.ExecInfoCompact, StringComparison.Ordinal);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⭐ <b>The per-table cards are REBUILT, because their bindings live on the row objects.</b> Replacing
    /// the objects is what reaches them — the same answer the Session Manager's warning cards reached, and it
    /// is why those rows carry no subscription of their own.
    /// </summary>
    [Fact]
    public async Task SwitchingLanguage_RebuildsTheActivityCards()
    {
        var vm = new FunctionDetailTabViewModel("F")
        {
            RunExecuteRequested = (_, _) => Task.FromResult(new ProcedureExecOutcome(
                null,
                null,
                Summary(upd: 5),
                new[] { Row("ORDERS", upd: 5) })),
        };

        await vm.ExecuteFunctionCommand.ExecuteAsync(null);
        var beforeLine = Assert.Single(vm.ExecTableActivity);
        var beforeRow = Assert.Single(beforeLine.Changes);
        Assert.Equal(" updated", beforeRow.After);

        try
        {
            Loc.UseCatalogForVerification(new PluralCatalog(), Pseudo);

            // (a) NO CACHE — the row object captured before the switch already answers in the new language,
            // because it resolves on read. ⚠ Asserted on the ORIGINAL instance on purpose: a rebuilt row
            // would answer correctly even if it cached, so checking the collection would prove nothing here.
            Assert.Equal(" wierszy", beforeRow.After);

            vm.RefreshLocalizedText();

            // (b) REBUILT — and this half is what actually reaches the screen. The row has no
            // INotifyPropertyChanged, so nothing would ever ask it again; replacing the bound objects is the
            // only thing that re-evaluates the bindings (the Session Manager's answer, for the same reason).
            var afterLine = Assert.Single(vm.ExecTableActivity);
            Assert.NotSame(beforeLine, afterLine);
            Assert.NotSame(beforeRow, Assert.Single(afterLine.Changes));
            Assert.Equal(" wierszy", Assert.Single(afterLine.Changes).After);
            Assert.Equal("ORDERS", afterLine.Table);   // the DATA is untouched by the language
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────

    private static Func<LocalizableMessage, string> Capture(List<LocalizableMessage> into)
        => m => { into.Add(m); return m.Key.Value; };

    /// <remarks>
    /// ⚠⚠ Flattened to STRINGS on purpose. The first version returned <c>(string, object?[])</c> tuples and
    /// failed with "Collections differ" while printing two identical lines — because an array member of a
    /// tuple compares by REFERENCE. That is gotcha #358's exact shape, committed inside the guard written to
    /// watch the seam #358 protects; the lesson is that the trap is not specific to records.
    /// </remarks>
    private static IReadOnlyList<string> Captured(Action<Func<LocalizableMessage, string>> run)
    {
        var seen = new List<LocalizableMessage>();
        run(Capture(seen));
        return seen
            .Select(m => m.Key.Value
                + "(" + string.Join(", ", m.Arguments.Select(a => a?.ToString() ?? "<null>")) + ")")
            .ToList();
    }

    /// <summary>Keys the SHIPPED catalog serves with plural variants rather than a flat entry.</summary>
    private static HashSet<string> PluralKeysInTheCatalog()
    {
        var resources = new ResourceManager("EmberTern.App.Localization.Strings", typeof(UiStrings).Assembly);
        var set = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
        var keys = set.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToList();

        return keys
            .Where(k => k.EndsWith(".other", StringComparison.Ordinal))
            .Select(k => k[..^".other".Length])
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// A catalog with real plural families: a two-form neutral culture and a three-form pseudo one. ⚠ Returns
    /// null for anything it does not declare, exactly as a <see cref="ResourceManager"/> does — a catalog that
    /// answered every key would make the fallback path untestable.
    /// </summary>
    private sealed class PluralCatalog : ResourceManager
    {
        private static readonly Dictionary<string, string> Neutral = new(StringComparer.Ordinal)
        {
            [PluralRules.RuleSetKey] = PluralRules.OneOther,
            ["Query.Exec.RowsInserted.one"] = "EN [one] {0}",
            ["Query.Exec.RowsInserted.other"] = "EN [other] {0}",
            ["Query.Exec.Term.Inserted"] = "{0} inserted",
            ["Query.Exec.Term.Updated"] = "{0} updated",
        };

        private static readonly Dictionary<string, string> Ploc = new(StringComparer.Ordinal)
        {
            [PluralRules.RuleSetKey] = PluralRules.OneFewMany,
            ["Query.Exec.RowsInserted.one"] = "PL [one] {0}",
            ["Query.Exec.RowsInserted.few"] = "PL [few] {0}",
            ["Query.Exec.RowsInserted.many"] = "PL [many] {0}",
            ["Query.Exec.Term.Inserted"] = "wstawiono {0} wierszy",
            ["Query.Exec.Term.Updated"] = "zmieniono {0} wierszy",
        };

        public override string? GetString(string name, CultureInfo? culture)
        {
            var table = Equals(culture, Pseudo) ? Ploc : Neutral;
            if (table.TryGetValue(name, out var value)) return value;

            // Anything else: a plausible sentence carrying every argument, marked so a test can tell the
            // language apart, and NEVER a plural variant — those must come from the tables above.
            return name.Contains(".one", StringComparison.Ordinal)
                || name.Contains(".few", StringComparison.Ordinal)
                || name.Contains(".many", StringComparison.Ordinal)
                || name.Contains(".other", StringComparison.Ordinal)
                    ? null
                    : (Equals(culture, Pseudo) ? "[" : "EN ") + name + " {0}";
        }
    }

    /// <summary>A translation that declares <c>other</c> but forgot <c>many</c>.</summary>
    private sealed class GappyCatalog : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture) => name switch
        {
            PluralRules.RuleSetKey => PluralRules.OneFewMany,
            "Query.Exec.RowsInserted.one" => "[one] {0}",
            "Query.Exec.RowsInserted.other" => "[other] {0}",
            _ when name.Contains(".one", StringComparison.Ordinal)
                || name.Contains(".few", StringComparison.Ordinal)
                || name.Contains(".many", StringComparison.Ordinal)
                || name.Contains(".other", StringComparison.Ordinal) => null,
            _ => name + " {0}",
        };
    }

    /// <summary>A translation whose sentence lost its <c>{0}</c>.</summary>
    private sealed class NoPlaceholderCatalog : ResourceManager
    {
        public override string? GetString(string name, CultureInfo? culture)
            => name.StartsWith("Query.Exec.Term.", StringComparison.Ordinal) ? "nothing to substitute" : null;
    }
}

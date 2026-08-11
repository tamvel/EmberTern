using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Resources;
using EmberTern.App.Localization;
using EmberTern.Core.Localization;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Language.QuickInfo;
using EmberTern.Core.Sql.Language.Semantics;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The second Core producer on decision <b>D‑3</b>: a Quick Info fact's <b>label</b> is EmberTern speaking and
/// carries a <see cref="MessageKey"/>; its <b>value</b> is Firebird speaking and stays verbatim.
///
/// <para>⚠ That split is the whole subject of these guards, and it is the one thing a reader is most likely to
/// get wrong in either direction — translating <c>NOT NULL</c>, or leaving <c>Nullability</c> in English. So
/// the tests pin the SPLIT, not a list of sentences.</para>
///
/// <para>⚠ Joins the headless collection: it swaps <c>Loc</c>'s catalog, which is process-global state
/// (localization.md §5.4).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class QuickInfoLocalizationTests
{
    // A metadata snapshot rich enough that one sweep reaches every fact builder in the engine: a column with
    // domain / nullability / default / key / generated, a table with counts, a function with a return type,
    // a trigger header and a generator.
    private sealed class Meta : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _cols = new(System.StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<RoutineParameterMetadata>> _params = new(System.StringComparer.OrdinalIgnoreCase);

        public Meta Add(ObjectMetadata m) { _objects[m.Name] = m; return this; }
        public Meta Col(string t, ColumnMetadata c)
        {
            if (!_objects.ContainsKey(t)) _objects[t] = new ObjectMetadata(t, SymbolKind.Table, null, "SYSDBA");
            if (!_cols.TryGetValue(t, out var l)) _cols[t] = l = new();
            l.Add(c);
            return this;
        }
        public Meta Param(string r, RoutineParameterMetadata p)
        {
            if (!_params.TryGetValue(r, out var l)) _params[r] = l = new();
            l.Add(p);
            return this;
        }

        public ObjectMetadata? FindObject(string name)
            => _objects.TryGetValue(name, out var o) ? o : null;
        public IReadOnlyList<ColumnMetadata> GetColumns(string table)
            => _cols.TryGetValue(table, out var l) ? l : System.Array.Empty<ColumnMetadata>();
        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
            => _params.TryGetValue(routine, out var l) ? l : System.Array.Empty<RoutineParameterMetadata>();
        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    private static Meta Snapshot() => new Meta()
        .Col("KONTRAHENT", new ColumnMetadata("ID", "INTEGER")
        {
            Domain = "T_ID",
            Nullable = false,
            DefaultValue = "0",
            IsPrimaryKey = true,
            Identity = EmberTern.Core.Metadata.IdentityKind.Always,
        })
        .Col("KONTRAHENT", new ColumnMetadata("NAZWA", "VARCHAR(50)")
        {
            IsForeignKey = true,
            ForeignKeyTable = "MIASTO",
        });

    // Every fact the engine produces for the snapshot's symbols, across the builders one sweep can reach.
    private static IReadOnlyList<QuickInfoFact> AllFacts()
    {
        var meta = Snapshot();
        var facts = new List<QuickInfoFact>();
        foreach (var sql in new[]
                 {
                     "select k.id from kontrahent k",
                     "select k.nazwa from kontrahent k",
                     "select * from kontrahent",
                 })
        {
            var model = SemanticModel.Build(sql, meta);
            for (var i = 0; i < sql.Length; i++)
            {
                var qi = QuickInfoEngine.GetQuickInfo(model, i);
                if (qi is not null) facts.AddRange(qi.Facts);
            }
        }

        Assert.NotEmpty(facts);
        return facts;
    }

    /// <summary>
    /// ⭐ <b>Core produces no label prose.</b> The type already forbids it — <see cref="QuickInfoFact.Label"/>
    /// is a <see cref="MessageKey"/> — so what this adds is the reflection half: a NEW string-typed text member
    /// on the fact would reopen the hole, and would be covered the day it is added.
    /// </summary>
    [Fact]
    public void AFactCarriesAKeyForItsLabel_AndNoOtherProse()
    {
        var stringMembers = typeof(QuickInfoFact)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToList();

        // `Value` is the ONE string, and deliberately so: it is Firebird's vocabulary, not ours.
        Assert.Equal(new[] { nameof(QuickInfoFact.Value) }, stringMembers);

        foreach (var fact in AllFacts())
        {
            Assert.DoesNotContain(' ', fact.Label.Value);
        }
    }

    /// <summary>Every label the engine can actually utter resolves to real English — the missing-entry symptom
    /// is a raw key on the hover card.</summary>
    [Fact]
    public void EveryFactLabelTheEngineProduces_RendersEnglish()
    {
        foreach (var fact in AllFacts())
        {
            var text = Loc.Text(fact.Label);
            Assert.NotEqual(fact.Label.Value, text);
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    // ── The split: labels are ours, values are Firebird's ────────────────────────────────────────────────

    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    private sealed class TwoLanguageCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture)
            => Equals(culture, Pseudo) ? "[[" + name + "]]" : "EN " + name;
    }

    /// <summary>
    /// ⭐⭐ <b>The measurement C2 exists for, in both directions at once.</b> After a language change the
    /// LABEL must follow, and the VALUE must not: <c>NOT NULL</c>, <c>PRIMARY KEY</c> and a domain name are
    /// what the user reads in every other Firebird tool, and a card that renamed them would disagree with the
    /// DDL it describes.
    ///
    /// <para>⚠ The facts are produced ONCE, before the switch, and re-rendered afterwards — which is what
    /// makes this a live-switching test rather than a lookup test.</para>
    /// </summary>
    [Fact]
    public void ALabelFollowsTheLanguage_AndAValueNeverDoes()
    {
        var facts = AllFacts();
        var values = facts.Select(f => f.Value).ToList();

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);
            Assert.All(facts, f => Assert.StartsWith("EN ", Loc.Text(f.Label)));

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
            Assert.All(facts, f => Assert.StartsWith("[[", Loc.Text(f.Label)));

            // The values are untouched by any of it — same objects, same text.
            Assert.Equal(values, facts.Select(f => f.Value).ToList());
            Assert.Contains("NOT NULL", values);
            Assert.Contains("PRIMARY KEY", values);
            Assert.Contains("T_ID", values); // a domain name — data, never a word we own
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⭐⭐ <b>The card RENDERS the resolved label, not the key — and this guard exists because the opposite
    /// compiled silently.</b> When <c>QuickInfoFact.Label</c> changed from <c>string</c> to
    /// <see cref="MessageKey"/>, the view's <c>new Run(fact.Label + "  ")</c> kept building: a record struct
    /// in a string concatenation resolves through <c>ToString()</c>, so the product would have shown
    /// <c>QuickInfo.Fact.Table</c> on the hover card with a green build and every other test passing.
    ///
    /// <para>⚠ None of the guards above can see it: they call <c>Loc.Text</c> themselves and therefore test
    /// the catalog, not the surface. Only reading the realized control does — so this asserts the TEXT of the
    /// rendered runs, which is the thing a user actually looks at.</para>
    /// </summary>
    [Fact]
    public void TheRenderedCard_ShowsResolvedLabels_NeverRawKeys()
    {
        var model = SemanticModel.Build("select k.id from kontrahent k", Snapshot());
        var qi = QuickInfoEngine.GetQuickInfo(model, "select k.".Length + 1);
        Assert.NotNull(qi);
        Assert.NotEmpty(qi!.Facts);

        var card = App.Completion.QuickInfoView.BuildContent(qi, Avalonia.Styling.ThemeVariant.Dark);

        var rendered = card.GetVisualDescendants()
            .OfType<Avalonia.Controls.TextBlock>()
            .SelectMany(t => t.Inlines?.OfType<Avalonia.Controls.Documents.Run>() ?? Enumerable.Empty<Avalonia.Controls.Documents.Run>())
            .Select(r => r.Text ?? string.Empty)
            .ToList();

        // Logical tree, not visual: the card is not attached to a window here, so nothing is realized.
        if (rendered.Count == 0)
        {
            rendered = card.GetLogicalDescendants()
                .OfType<Avalonia.Controls.TextBlock>()
                .SelectMany(t => t.Inlines?.OfType<Avalonia.Controls.Documents.Run>() ?? Enumerable.Empty<Avalonia.Controls.Documents.Run>())
                .Select(r => r.Text ?? string.Empty)
                .ToList();
        }

        Assert.NotEmpty(rendered);

        // No run may contain a raw key, and the human label must be there.
        Assert.DoesNotContain(rendered, r => r.Contains("QuickInfo.Fact.", System.StringComparison.Ordinal));
        Assert.Contains(rendered, r => r.StartsWith("Nullability", System.StringComparison.Ordinal));
    }

    /// <summary>
    /// The producer is language-unaware: the same input yields the same keys and the same values whatever
    /// language is current. ⭐ This is the structural claim behind D‑3 — if Core ever resolved a word itself,
    /// this is the test that would catch it, and it needs no list of expected strings to do so.
    /// </summary>
    [Fact]
    public void TheEngineProducesTheSameFacts_WhateverTheLanguage()
    {
        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);
            var before = AllFacts().Select(f => f.Label.Value + "=" + f.Value).ToList();

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
            var after = AllFacts().Select(f => f.Label.Value + "=" + f.Value).ToList();

            Assert.Equal(before, after);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Collections.Specialized;
using System.Linq;
using System.Reflection;
using System.Resources;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Localization;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The editor's semantic diagnostics on decision <b>D‑3</b> (etap C5): <c>Diagnostic.Message</c> is a
/// <see cref="LocalizableMessage"/>, the App resolves it at display time, and — the reason this etap needed a
/// ratified contract before any code — <b><see cref="Diagnostic"/>'s value equality still holds</b>.
///
/// <para>⭐⭐ <b>The equality guards are the point of the file.</b> <c>DiagnosticsPanelViewModel.Update</c> skips
/// rebuilding its <c>ObservableCollection</c> — and so keeps the user's selection — by comparing findings. The
/// carrier's synthesized equality compared its argument list by REFERENCE, so embedding it naively would have
/// churned the panel on every debounce tick with a green build and no failing test. The fix was structural
/// equality on <see cref="LocalizableMessage"/>; these tests are what make that fix un-undoable.</para>
///
/// <para>⚠ Joins the headless collection: it swaps <c>Loc</c>'s catalog, which is process-global state.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DiagnosticsLocalizationTests
{
    /// <summary>
    /// ⛔ <b>The one key no scenario can reach, with its reason and a pinned premise.</b>
    /// <c>ET0004</c> (<see cref="DiagnosticCategory.UnresolvedParameter"/>) needs an <i>unresolved</i>
    /// <c>SymbolReference</c> whose role is <c>Parameter</c> — and measured in C5, <b>no binder path produces
    /// one</b>: every arm that records an unresolved local records it as <c>Variable</c>
    /// (<c>BindParameterToken</c>'s <c>_ =&gt; Variable</c> fallback, <c>BindBareLocal</c>'s default arm, and the
    /// explicit <c>AddReference(tok, null, Variable)</c>), while the <c>Parameter</c> role is only ever attached
    /// to a symbol already matched as a <c>ParameterSymbol</c> — i.e. always resolved.
    ///
    /// <para>⚠ Corroborated independently: there is <b>no ET0004 test anywhere in the suite</b>, which is what an
    /// unreachable category looks like from outside. The key is still declared and produced by live code, so the
    /// sentence exists in the product. <see cref="TheOnlyUnreachableCategory_IsStillUnreachable"/> pins the
    /// premise, so a binder change that starts emitting one turns this exemption red instead of quietly excusing
    /// an unchecked message (#322 — guard the premise, not the policy).</para>
    /// </summary>
    private static readonly string[] UnreachableKeys = ["Sql.Diagnostics.UnresolvedParameter"];

    // ⭐ A minimal metadata snapshot for the two categories that need one. Deliberately NOT a copy of
    // DiagnosticsEngineTests' richer builder — that one is private to its class and these scenarios need only
    // "this table exists and has these columns".
    private sealed class Snapshot : ISqlMetadataProvider
    {
        private readonly Dictionary<string, ObjectMetadata> _objects = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ColumnMetadata>> _columns = new(StringComparer.OrdinalIgnoreCase);

        public Snapshot Col(string table, string column, string type)
        {
            _objects[table] = new ObjectMetadata(table, SymbolKind.Table);
            if (!_columns.TryGetValue(table, out var list)) _columns[table] = list = new List<ColumnMetadata>();
            list.Add(new ColumnMetadata(column, type));
            return this;
        }

        public bool KnowsColumns(string tableOrView) => true;

        public ObjectMetadata? FindObject(string name)
            => _objects.TryGetValue(name, out var o) ? o : null;

        public IReadOnlyList<ColumnMetadata> GetColumns(string tableOrView)
            => _columns.TryGetValue(tableOrView, out var c) ? c : Array.Empty<ColumnMetadata>();

        public IReadOnlyList<RoutineParameterMetadata> GetRoutineParameters(string routine)
            => Array.Empty<RoutineParameterMetadata>();

        public IReadOnlyList<ObjectMetadata> AllObjects() => _objects.Values.ToList();
    }

    // ── Scenarios: every category the engine can emit, from the real engine ──────────────────────────

    private static IReadOnlyList<Diagnostic> Analyze(string sql, ISqlMetadataProvider? metadata = null)
        => DiagnosticsEngine.Analyze(SemanticModel.Build(sql, metadata));

    /// <summary>
    /// Every diagnostic the engine can produce, collected from REAL scenarios. ⚠ Never written down as a table
    /// of expected sentences: a table would be a second copy of the catalog, red on a typo fix and green if a
    /// producer stopped setting the key (gotcha #333).
    /// </summary>
    private static List<Diagnostic> AllEmitted()
    {
        var withColumns = new Snapshot().Col("ORDERS", "ID", "INTEGER").Col("ITEMS", "ID", "INTEGER");

        var found = new List<Diagnostic>();
        found.AddRange(Analyze("execute procedure sp_missing", new Snapshot().Col("ORDERS", "ID", "INTEGER")));
        found.AddRange(Analyze("select o.nosuch from orders o", withColumns));
        found.AddRange(Analyze("select id from orders o join items i on i.id = o.id", withColumns));
        found.AddRange(Analyze("create procedure p as begin v_a = v_missing + 1; end"));
        found.AddRange(Analyze("create procedure p as begin open c_missing; end"));
        found.AddRange(Analyze("insert into orders (a, b) values (1)"));
        found.AddRange(Analyze("create trigger t for orders before insert as begin suspend; end"));
        found.AddRange(Analyze("create function f returns integer as begin suspend; return 1; end"));

        Assert.NotEmpty(found);
        return found;
    }

    // ── The equality contract ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Two independently built messages carrying the same key and the same data are equal — by value.</b>
    /// The whole C5 contract rests on this one sentence; without it every downstream guard here is vacuous.
    /// </summary>
    [Fact]
    public void TwoIndependentlyBuiltMessages_AreValueEqual()
    {
        var a = LocalizableMessage.Of(DiagnosticsMessages.UnknownObject, "SP_MISSING");
        var b = LocalizableMessage.Of(DiagnosticsMessages.UnknownObject, "SP_MISSING");

        Assert.False(ReferenceEquals(a, b));                       // genuinely two objects
        Assert.False(ReferenceEquals(a.Arguments, b.Arguments));    // and genuinely two argument lists
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());

        // …and it still discriminates: a different key, a different argument, and a different arity are unequal.
        Assert.NotEqual(a, LocalizableMessage.Of(DiagnosticsMessages.UnknownColumn, "SP_MISSING"));
        Assert.NotEqual(a, LocalizableMessage.Of(DiagnosticsMessages.UnknownObject, "SP_OTHER"));
        Assert.NotEqual(a, LocalizableMessage.Of(DiagnosticsMessages.UnknownObject, "SP_MISSING", 1));
        Assert.NotEqual(a, LocalizableMessage.Of(DiagnosticsMessages.UnknownObject));
    }

    /// <summary>⭐ The same, for the two-argument message — boxed integers must compare by value too.</summary>
    [Fact]
    public void MessagesWithNumericArguments_AreValueEqual()
    {
        var a = LocalizableMessage.Of(DiagnosticsMessages.InsertCountMismatch, 2, 1);
        var b = LocalizableMessage.Of(DiagnosticsMessages.InsertCountMismatch, 2, 1);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, LocalizableMessage.Of(DiagnosticsMessages.InsertCountMismatch, 1, 2));
    }

    /// <summary>
    /// ⭐⭐ The same property one level up, and this is the one the panel actually depends on: two
    /// <see cref="Diagnostic"/> records built separately from the same facts are equal.
    /// </summary>
    [Fact]
    public void TwoIndependentlyBuiltDiagnostics_AreValueEqual()
    {
        static Diagnostic Make() => new(
            7, 10, DiagnosticSeverity.Warning,
            LocalizableMessage.Of(DiagnosticsMessages.UnknownObject, "SP_MISSING"),
            "ET0001", DiagnosticCategory.UnknownObject);

        Assert.Equal(Make(), Make());
        Assert.Equal(Make().GetHashCode(), Make().GetHashCode());
    }

    /// <summary>
    /// ⭐⭐ And through the REAL engine, which is a different claim: analysing the same model twice must yield
    /// element-wise equal lists. A struct can be value-equal while an engine still produces differing findings.
    /// </summary>
    [Fact]
    public void TwoAnalysesOfTheSameModel_ProduceAnEqualList()
    {
        const string sql = "create procedure p as begin v_a = v_missing + 1; insert into t (a, b) values (1); end";

        var first = DiagnosticsEngine.Analyze(SemanticModel.Build(sql));
        var second = DiagnosticsEngine.Analyze(SemanticModel.Build(sql));

        Assert.NotEmpty(first);
        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i], second[i]);
        }
    }

    /// <summary>
    /// ⭐⭐ <b>The behaviour the equality exists for, driven by the real engine rather than by fixtures.</b>
    /// Republishing an unchanged analysis must not touch the collection and must not drop the selection —
    /// that is what a keystroke does every debounce tick.
    /// </summary>
    [Fact]
    public void RepublishingAnUnchangedAnalysis_DoesNotChurnTheCollection_NorLoseTheSelection()
    {
        const string sql = "create procedure p as begin v_a = v_missing + 1; end";
        var panel = new DiagnosticsPanelViewModel();

        panel.Update(Rows(DiagnosticsEngine.Analyze(SemanticModel.Build(sql))));
        Assert.NotEmpty(panel.Diagnostics);
        panel.SelectedIndex = 0;

        var events = 0;
        ((INotifyCollectionChanged)panel.Diagnostics).CollectionChanged += (_, _) => events++;

        panel.Update(Rows(DiagnosticsEngine.Analyze(SemanticModel.Build(sql))));

        Assert.Equal(0, events);
        Assert.Equal(0, panel.SelectedIndex);
    }

    private static List<DiagnosticRowViewModel> Rows(IReadOnlyList<Diagnostic> diagnostics)
        => diagnostics.Select(d => new DiagnosticRowViewModel(d, line: 1, column: d.Start + 1)).ToList();

    // ── Live switching (ratified W3) ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>W3 as built: a language change refreshes the rows' text without touching the collection or the
    /// selection.</b>
    ///
    /// <para>⛔ The trap this pins: the obvious repair — rebuild the rows and republish — is swallowed by
    /// <c>Update</c>'s unchanged-check, because after a mere language change the findings ARE the same. So the
    /// hook must not go through <c>Update</c> at all.</para>
    ///
    /// <para>⚠⚠ <b>What changed in the audit follow-up, and why this is not a weaker test.</b> The panel used to
    /// take its OWN <c>Loc.LanguageChanged</c> subscription, so this test could swap the catalog and watch a
    /// bare panel react. That subscription was a leak: a panel exists per <c>MainWindowViewModel</c> AND per
    /// Package / View / routine editor tab, while the static event is a GC root — so every tab ever opened
    /// stayed alive for the session and answered every later language change. The panel is now an ordinary
    /// child of the app's single long-lived subscriber, which is the pattern every other refreshable view model
    /// here already follows.</para>
    ///
    /// <para>⭐ The claim is therefore split, and the two halves together are STRONGER than the one they
    /// replace: this test keeps the whole behavioural half (notified, not rebuilt, selection kept, new catalog
    /// read), and the wiring half — that every owner actually calls it — is carried by the three self-arming
    /// guards in <c>LocalizationMechanismTests</c>, which began covering this type automatically the moment it
    /// declared <c>RefreshLocalizedText</c>. ⛔ Do not "restore" the subscription to make this test shorter.</para>
    /// </summary>
    [Fact]
    public void ALanguageChange_RefreshesTheRowsText_WithoutRebuildingOrLosingTheSelection()
    {
        var panel = new DiagnosticsPanelViewModel();
        panel.Update(Rows(DiagnosticsEngine.Analyze(
            SemanticModel.Build("create procedure p as begin v_a = v_missing + 1; end"))));

        Assert.NotEmpty(panel.Diagnostics);
        var row = panel.Diagnostics[0];
        panel.SelectedIndex = 0;

        var collectionEvents = 0;
        ((INotifyCollectionChanged)panel.Diagnostics).CollectionChanged += (_, _) => collectionEvents++;
        var messageNotifications = 0;
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DiagnosticRowViewModel.Message)) messageNotifications++;
        };

        var english = row.Message;

        try
        {
            // The same switch the Settings Center Language row performs, followed by the same forward the
            // owner performs — MainWindowViewModel.OnLanguageChanged for the SQL Editor's panel, and each
            // editor tab's RefreshLocalizedText for its own.
            Loc.UseCatalogForVerification(new PassThroughCatalog(), CultureInfo.InvariantCulture);
            panel.RefreshLocalizedText();

            Assert.True(messageNotifications > 0, "the row was never told its text changed");
            Assert.Equal(0, collectionEvents);           // no rebuild
            Assert.Equal(0, panel.SelectedIndex);        // selection kept
            Assert.Same(row, panel.Diagnostics[0]);      // the very same row object
            Assert.NotEqual(english, row.Message);       // and it now reads the new catalog
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }

        Assert.Equal(english, row.Message);
    }

    // ── The message contract ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Every key the module declares is produced by a real scenario, and every produced key resolves to a
    /// real catalog entry. Catches both a declared-but-dead key and a key whose resource entry is missing.
    /// </summary>
    [Fact]
    public void EveryDeclaredKey_IsProducedByAScenario_AndResolves()
    {
        var produced = AllEmitted().Select(d => d.Message.Key.Value).ToHashSet(StringComparer.Ordinal);

        var declared = typeof(DiagnosticsMessages)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MessageKey))
            .Select(f => ((MessageKey)f.GetValue(null)!).Value)
            .ToList();

        Assert.Equal(9, declared.Count);

        var unexercised = declared
            .Where(k => !produced.Contains(k) && !UnreachableKeys.Contains(k, StringComparer.Ordinal))
            .ToList();
        Assert.True(unexercised.Count == 0,
            "Declared but never produced by a scenario: " + string.Join(", ", unexercised));

        // …and in the other direction: an exemption that has become reachable is a stale exemption.
        var stale = UnreachableKeys.Where(produced.Contains).ToList();
        Assert.True(stale.Count == 0, "Named unreachable yet produced: " + string.Join(", ", stale));

        foreach (var diagnostic in AllEmitted())
        {
            var rendered = Loc.Format(diagnostic.Message);
            Assert.NotEqual(diagnostic.Message.Key.Value, rendered);   // the entry exists
            Assert.DoesNotContain(' ', diagnostic.Message.Key.Value);   // a key, never prose
            Assert.False(string.IsNullOrWhiteSpace(rendered));
        }
    }

    /// <summary>
    /// ⭐ <b>The premise behind the single exemption, asserted rather than trusted.</b> ET0004 requires an
    /// UNRESOLVED reference whose role is <c>Parameter</c>; the binder never records one. Driven over the shapes
    /// that would produce it if any did — an undeclared <c>:name</c>, an undeclared bare name, a
    /// <c>RETURNING_VALUES</c> target, an undeclared name in an embedded query.
    ///
    /// <para>⚠ Reach stated honestly: this is a negative over the shapes we know, not a proof over the whole
    /// binder. It is enough to make the exemption falsifiable, which is its job — the day a binder change emits
    /// one of these, this fails and asks for a real scenario.</para>
    /// </summary>
    [Fact]
    public void TheOnlyUnreachableCategory_IsStillUnreachable()
    {
        string[] shapes =
        [
            "create procedure p (a integer) as begin a = :p_missing; end",
            "create procedure p (a integer) as begin a = p_missing; end",
            "create procedure p as begin execute procedure q returning_values :p_missing; end",
            "create procedure p as begin select 1 from t where c = :p_missing into :p_other; end",
            "execute block (a integer = ?) as begin a = :p_missing; end",
            "create trigger t for orders before insert as begin new.id = :p_missing; end",
        ];

        foreach (var sql in shapes)
        {
            var model = SemanticModel.Build(sql);

            Assert.DoesNotContain(model.References,
                r => r.Role == ReferenceRole.Parameter && !r.IsResolved);
            Assert.DoesNotContain(DiagnosticsEngine.Analyze(model),
                d => d.Category == DiagnosticCategory.UnresolvedParameter);
        }

        Assert.Single(UnreachableKeys);
    }

    /// <summary>
    /// ⛔⛔ <b>ET0008 must not carry EmberTern's own noun as an argument.</b> The producer used to interpolate
    /// "trigger"/"function" into one sentence; substituting a noun works in English and breaks in a language
    /// that inflects. So the context picks between two keys and the message carries no arguments at all.
    /// </summary>
    [Fact]
    public void TheSuspendDiagnostic_UsesTwoKeys_AndCarriesNoNounArgument()
    {
        var inTrigger = Assert.Single(Analyze(
            "create trigger t for orders before insert as begin suspend; end"));
        var inFunction = Assert.Single(Analyze(
            "create function f returns integer as begin suspend; return 1; end"));

        Assert.Equal("ET0008", inTrigger.Code);
        Assert.Equal("ET0008", inFunction.Code);
        Assert.Equal(DiagnosticCategory.SuspendOutsideSelectable, inTrigger.Category);
        Assert.Equal(DiagnosticCategory.SuspendOutsideSelectable, inFunction.Category);

        // One category, TWO distinct keys — which is why the key cannot be derived from the category.
        Assert.NotEqual(inTrigger.Message.Key, inFunction.Message.Key);

        // And neither carries a word as data.
        Assert.Empty(inTrigger.Message.Arguments);
        Assert.Empty(inFunction.Message.Arguments);
    }

    /// <summary>
    /// ⭐ <b>The precondition behind the structural equality, asserted rather than trusted:</b> no producer
    /// hands over an argument whose type does not implement value equality. A <c>byte[]</c> or a mutable holder
    /// would silently restore reference comparison and re-open the panel-churn defect.
    /// </summary>
    [Fact]
    public void NoProducerPassesAnArgumentWithoutValueEquality()
    {
        foreach (var diagnostic in AllEmitted())
        {
            foreach (var argument in diagnostic.Message.Arguments)
            {
                Assert.NotNull(argument);
                var type = argument!.GetType();

                // Declares its own Equals(object) — i.e. does not inherit object's reference comparison.
                var declared = type.GetMethod(
                    nameof(Equals), BindingFlags.Public | BindingFlags.Instance, new[] { typeof(object) });
                Assert.NotNull(declared);
                Assert.NotEqual(typeof(object), declared!.DeclaringType);

                // And behaves that way: an independently produced equal value compares equal when boxed.
                Assert.True(argument.Equals(Clone(argument)), type.Name + " did not compare by value");
            }
        }

        // Rebuilt through a round trip so it cannot be the same reference.
        static object Clone(object value) => value switch
        {
            string s => new string(s.ToCharArray()),
            int i => int.Parse(i.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
            _ => value,
        };
    }

    /// <summary>
    /// Core never resolves words: the same model yields the same keys and the same arguments whatever language
    /// is current. ⭐ Catches a producer that started reading a catalog, without naming a single sentence.
    /// </summary>
    [Fact]
    public void TheEngineProducesTheSameKeys_WhateverTheLanguage()
    {
        try
        {
            Loc.UseCatalogForVerification(new PassThroughCatalog(), CultureInfo.InvariantCulture);
            var before = AllEmitted().Select(d => d.Message.Key.Value).ToList();

            Loc.UseCatalogForVerification(new PassThroughCatalog(), CultureInfo.GetCultureInfo("qps-ploc"));
            var after = AllEmitted().Select(d => d.Message.Key.Value).ToList();

            Assert.Equal(before, after);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    private sealed class PassThroughCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture) => name;
    }
}

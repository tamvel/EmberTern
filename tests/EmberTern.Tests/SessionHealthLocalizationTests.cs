using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using EmberTern.App;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Diagnostics;
using EmberTern.Core.Localization;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The first Core producer on decision <b>D‑3</b>: <see cref="SessionHealthAnalyzer"/> names its messages and
/// the App resolves them.
///
/// <para>⚠ These guards deliberately assert <b>structure and meaning</b>, not a list of English sentences. A
/// transcribed sentence list would be a second copy of the catalog — it would go red when someone corrects a
/// typo and, worse, it would go green while the words were frozen in the wrong language (gotcha #333: a guard
/// that transcribes a premise breaks when the premise moves). What must hold is that Core produces no prose,
/// that every key it produces resolves, and that the resolution follows a language change.</para>
///
/// <para>⚠ Joins the headless collection because it swaps <c>Loc</c>'s catalog, which is process-global state
/// — any test reading <c>UiStrings</c> concurrently would see the substitute (localization.md §5.4).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SessionHealthLocalizationTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    // A report that exercises BOTH finding builders at once: a long-lived snapshot holding the OAT (the GC
    // card, with its three evidence rows) and a second long transaction that is not the gatekeeper.
    private static SessionHealthReport Report()
    {
        var sessions = new[] { Session(23), Session(24) };
        var txs = new[]
        {
            Tx(79195, 23, isolation: 1, started: Now.AddHours(-2)),
            Tx(79240, 24, isolation: 1, started: Now.AddMinutes(-30)),
        };
        var db = new DatabaseTransactionState
        {
            OldestTransaction = 79190, OldestActive = 79195, OldestSnapshot = 79195, NextTransaction = 127_297,
        };
        return SessionHealthAnalyzer.Analyze(sessions, txs, db, Now);
    }

    // Same fixture shape as SessionHealthAnalyzerTests — IsActive / IsSnapshot are DERIVED from the codes,
    // so they are set the way the reader sets them, never assigned.
    private static SessionInfo Session(long id) => new() { AttachmentId = id, User = "USER" + id, StateCode = 1 };

    private static TransactionInfo Tx(long id, long attachment, int isolation, DateTime started) => new()
    {
        TransactionId = id,
        AttachmentId = attachment,
        StateCode = 1,
        StartedAt = started,
        IsolationModeCode = isolation,
        IsolationMode = isolation is 0 or 1 ? "SNAPSHOT" : "READ COMMITTED",
    };

    private static IEnumerable<LocalizableMessage> AllMessages(SessionHealthReport report)
        => report.Findings.SelectMany(f => new[] { f.Title, f.Explanation }
            .Concat(f.Impact is { } i ? new[] { i } : Array.Empty<LocalizableMessage>())
            .Concat(f.Evidence)
            .Concat(f.WhatToCheck));

    /// <summary>
    /// ⭐ <b>Core produces no prose.</b> Enforced by construction — <see cref="MessageKey"/> refuses anything
    /// that is not identifier-shaped — so this asserts the thing that construction cannot: that the analyzer
    /// actually routes every text member through the seam, rather than a member having quietly stayed a
    /// <c>string</c>. Reflection over the record means a NEW text member is covered the day it is added.
    /// </summary>
    [Fact]
    public void TheAnalyzer_ProducesNoProse()
    {
        var textMembers = typeof(SessionHealthFinding)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string)
                        || p.PropertyType == typeof(IReadOnlyList<string>))
            .Select(p => p.Name)
            .ToList();

        Assert.True(textMembers.Count == 0,
            "SessionHealthFinding still carries raw text in: " + string.Join(", ", textMembers)
            + ". A finding's words belong to the App (D‑3); Core names the message and supplies the data.");

        var report = Report();
        Assert.NotEmpty(report.Findings);
        foreach (var message in AllMessages(report))
        {
            // Identifier-shaped, i.e. not a sentence. MessageKey's constructor already refuses prose; this
            // states the consequence at the producer, where a reader looks for it.
            Assert.DoesNotContain(' ', message.Key.Value);
        }
    }

    /// <summary>
    /// Every key the analyzer can actually utter resolves to real English. ⚠ Distinct from
    /// <c>EveryCoreMessageKey_HasAnEnglishEntry</c>, which walks DECLARED fields: this walks the keys a real
    /// analysis PRODUCES, so a key that is declared and spelled correctly but never wired up is not what is
    /// being measured here — the rendered card is.
    /// </summary>
    [Fact]
    public void EveryMessageTheAnalyzerProduces_RendersEnglish()
    {
        foreach (var message in AllMessages(Report()))
        {
            var text = Loc.Format(message);

            Assert.NotEqual(message.Key.Value, text); // a key on screen is the missing-entry symptom
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }

    /// <summary>
    /// The data survives the round trip: the GC card still names the transaction it is about and the lag it
    /// measured. ⭐ This is what stops the migration from silently dropping an argument — a key with a missing
    /// argument still renders, it just renders a sentence with a hole in it.
    /// </summary>
    [Fact]
    public void TheGcCard_CarriesItsMeasuredData()
    {
        var gc = Assert.Single(Report().Findings, f => f.Kind == SessionHealthKind.GarbageCollectionRisk);

        Assert.Equal(127_297L - 79_195L, Assert.Single(gc.Impact!.Arguments));
        // ⚠ Rendered with the READER's grouping, not Invariant — Loc.Format formats under CurrentCulture by
        // design (words follow the language, numbers follow the machine). On a pl-PL machine this is
        // "48 102", on en-US "48,102"; asserting the culture-formatted value is what keeps the test true on
        // both, and asserting a literal here is what would make it a machine-dependent flake.
        Assert.Contains((127_297L - 79_195L).ToString("N0", CultureInfo.CurrentCulture), Loc.Format(gc.Impact));

        var evidence = gc.Evidence.Select(Loc.Format).ToList();
        Assert.Contains(evidence, e => e.Contains("79195", StringComparison.Ordinal));
        // The isolation label travels as DATA, so the engine's own vocabulary reaches the row unchanged.
        Assert.Contains(evidence, e => e.Contains("SNAPSHOT", StringComparison.Ordinal));
    }

    // ── Live switching ───────────────────────────────────────────────────────────────────────────────────

    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    private sealed class TwoLanguageCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture)
            => Equals(culture, Pseudo) ? "[[" + name + "]]" : "EN " + name;
    }

    /// <summary>
    /// ⭐⭐ <b>The measurement the migration exists for.</b> A finding is produced ONCE, while one language is
    /// current, and its card must still follow a later switch. That only holds because the view model resolves
    /// on read instead of storing the text — so this fails if anyone "optimises" <c>SessionWarningViewModel</c>
    /// by caching a resolved string in a field, which is the natural-looking change that would silently freeze
    /// the language.
    ///
    /// <para>⚠ The finding is deliberately built BEFORE the first switch, because a view model constructed
    /// after a change would render correctly even if it did cache.</para>
    /// </summary>
    [Fact]
    public void AWarningCard_FollowsALanguageChange()
    {
        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);

            var finding = Report().Findings.First(f => f.Kind == SessionHealthKind.GarbageCollectionRisk);
            var card = new SessionWarningViewModel(finding);

            Assert.StartsWith("EN ", card.Title);
            Assert.StartsWith("EN ", card.Explanation);
            Assert.All(card.WhatToCheck, w => Assert.StartsWith("EN ", w));

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);

            Assert.StartsWith("[[", card.Title);
            Assert.StartsWith("[[", card.Explanation);
            Assert.All(card.WhatToCheck, w => Assert.StartsWith("[[", w));
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    // ── The verdict headline (the C1 deferral, closed) ───────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The headline is a message, not a sentence Core wrote.</b> It was the last hardcoded English text
    /// in this module — the PL QA round found "All sessions healthy." on a fully Polish screen — because C1
    /// deliberately left it a <c>string</c> "until the plural mechanism is decided". C6 decided it.
    ///
    /// <para>⚠ All three shapes, because they fail differently: two are counted (so they must resolve through
    /// a plural family) and the healthy one is flat (so it must NOT grow one).</para>
    /// </summary>
    [Fact]
    public void TheVerdictHeadline_IsAKeyWithItsCountAsAnArgument()
    {
        var db = new DatabaseTransactionState { OldestTransaction = 1, NextTransaction = 2 };

        var gc = Report().Verdict.Headline;
        Assert.Equal(SessionHealthMessages.VerdictGcBlocked, gc.Key);
        Assert.Equal(1, Assert.Single(gc.Arguments));

        var healthy = SessionHealthAnalyzer.Analyze(
            Array.Empty<SessionInfo>(), Array.Empty<TransactionInfo>(), db, Now).Verdict.Headline;
        Assert.Equal(SessionHealthMessages.VerdictHealthy, healthy.Key);
        Assert.Empty(healthy.Arguments);
    }

    /// <summary>
    /// ⭐ The headline resolves through the shipped catalogs and moves with the language — measured on the
    /// real analyzer output, with the expected values READ from the two resource sets. ⛔ Never transcribe
    /// "Wszystkie sesje są zdrowe." here: that would make this a test of today's wording (#333).
    ///
    /// <para>⚠ It stops at the composition. <see cref="SessionManagerTabViewModel"/> cannot be constructed
    /// without a live <c>FirebirdSessionReader</c> (its constructor starts a poll timer), so the view model's
    /// own half — that a STORED property is re-composed rather than merely notified — is pinned by the source
    /// guard below instead of by driving it.</para>
    /// </summary>
    [Fact]
    public void TheHeadline_ResolvesInBothShippedLanguages()
    {
        var catalog = new ResourceManager("EmberTern.App.Localization.Strings", typeof(UiStrings).Assembly);
        const string Key = "SessionHealth.Verdict.Healthy";
        var english = catalog.GetString(Key, CultureInfo.InvariantCulture);
        var polish = catalog.GetString(Key, CultureInfo.GetCultureInfo("pl"));

        Assert.False(string.IsNullOrEmpty(polish), Key + " has no Polish entry.");
        Assert.NotEqual(english, polish);

        var db = new DatabaseTransactionState { OldestTransaction = 1, NextTransaction = 2 };
        var headline = SessionHealthAnalyzer.Analyze(
            Array.Empty<SessionInfo>(), Array.Empty<TransactionInfo>(), db, Now).Verdict.Headline;

        var previous = Loc.Culture;
        try
        {
            Loc.Apply(Core.Settings.PreferenceOptions.LanguageEnglish);
            Assert.Equal(english, Loc.Format(headline));

            Loc.Apply(Core.Settings.PreferenceOptions.LanguagePolish);
            Assert.Equal(polish, Loc.Format(headline));

            Loc.Apply(Core.Settings.PreferenceOptions.LanguageEnglish);
            Assert.Equal(english, Loc.Format(headline));
        }
        finally
        {
            Loc.Apply(previous.Name.Length == 0
                ? Core.Settings.PreferenceOptions.LanguageEnglish
                : previous.Name);
        }
    }

    /// <summary>
    /// ⚠ <c>Headline</c> is a stored <c>[ObservableProperty]</c>, so the blanket notification in
    /// <c>RefreshLocalizedText</c> cannot fix it — the text has to be re-composed from the kept report
    /// (#353). This is the half the test above cannot reach, and its failure mode is precisely the reported
    /// defect: a headline frozen in the language the tab was opened in.
    /// </summary>
    [Fact]
    public void TheSessionManagerRefresh_RecomposesTheHeadline_RatherThanOnlyNotifying()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "ViewModels", "SessionManagerTabViewModel.cs"));

        var start = source.IndexOf("internal void RefreshLocalizedText()", StringComparison.Ordinal);
        Assert.True(start >= 0, "RefreshLocalizedText no longer exists — this guard has lost its subject.");

        var body = source[start..];
        body = body[..body.IndexOf("\n    private ", StringComparison.Ordinal)];

        Assert.Contains("Headline = Loc.Format(_report.Verdict.Headline)", body, StringComparison.Ordinal);
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

    /// <summary>
    /// ⭐ The counted shapes really do go through a plural family — including the Polish teen band, which is
    /// the case a two-form language has no reason to have and a translator cannot check by eye.
    /// </summary>
    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "few")]
    [InlineData(5, "many")]
    [InlineData(12, "many")]
    [InlineData(22, "few")]
    public void ThePolishHeadline_PicksTheBandTheCountBelongsTo(long count, string suffix)
    {
        var catalog = new ResourceManager("EmberTern.App.Localization.Strings", typeof(UiStrings).Assembly);
        var expected = string.Format(
            CultureInfo.GetCultureInfo("pl"),
            catalog.GetString("SessionHealth.Verdict.GcBlocked." + suffix, CultureInfo.GetCultureInfo("pl"))!,
            count);

        var previous = Loc.Culture;
        try
        {
            Loc.Apply(Core.Settings.PreferenceOptions.LanguagePolish);
            var message = LocalizableMessage.Of(SessionHealthMessages.VerdictGcBlocked, count);
            Assert.Equal(expected, Loc.Format(message));
        }
        finally
        {
            Loc.Apply(previous.Name.Length == 0
                ? Core.Settings.PreferenceOptions.LanguageEnglish
                : previous.Name);
        }
    }
}

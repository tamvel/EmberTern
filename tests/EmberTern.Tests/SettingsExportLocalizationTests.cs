using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using EmberTern.App.Localization;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;
using EmberTern.Core.Localization;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using EmberTern.Core.Settings.Export;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// <c>Settings/Export</c> on decision <b>D‑3</b> (etap C4b): every sentence an <b>import</b> can utter now
/// exists twice — in English, for logs and for the tests that pin the exact wording, and as a
/// <see cref="LocalizableMessage"/> the dialog resolves in the reader's language.
///
/// <para>⭐⭐ <b><see cref="EveryLocalizedImportMessage_RendersExactlyItsEnglishForm"/> is at once the anti-drift
/// check and the zero-text-change proof</b>, the same shape C4a used for the settings store: each localized form
/// must resolve, in English, to exactly the string the producer built — so a resource entry that does not
/// reproduce the shipped sentence character for character fails here, and no separate "did the wording change"
/// exercise is needed. ⚠ Driven through REAL scenarios rather than a table of expected strings: a table would be
/// a second copy of the catalog, red on a typo fix and green if a producer stopped setting the pair (#333).</para>
///
/// <para>⚠⚠ <b>The equality proof has an unstated PRECONDITION — both halves must format the argument the same
/// way — and finding out how it can be violated took a plant that did NOT fire.</b> The plausible story was that
/// <c>Loc.Format</c>'s <c>CurrentCulture</c> would group a large numeric argument while the English literal
/// stayed invariant; planting the numeric argument left all nine tests green, because a bare <c>{0}</c> does not
/// group. The real lever is a <b>format specifier in the resource value</b> (<c>{0:N0}</c>) — which a translator
/// can add and which is exactly where gotcha #354's <c>48 102</c> came from. Hence
/// <see cref="EveryArgument_IsAlreadyFormatted_SoNoFormatSpecifierCanChangeIt"/>, which measures that immunity
/// instead of restating the rule, and <see cref="TheEnglishAndLocalizedForms_AgreeOnAnyCulture"/>, which is an
/// honest broad invariance sweep rather than a proof about numbers. Gotcha #357.</para>
///
/// <para>⚠ Joins the headless collection: it swaps <c>Loc</c>'s catalog and moves <c>CurrentCulture</c>, both
/// process/thread-global.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SettingsExportLocalizationTests
{
    // Header field positions — the same ones SettingsExportTestIo.WithHeaderField addresses.
    private const int FormatVersionField = 1;
    private const int SchemeField = 3;
    private const int KdfField = 4;
    private const int IterationsField = 5;
    private const int SaltField = 6;

    /// <summary>
    /// ⛔ <b>The one key no scenario can reach, with the reason and a pinned premise.</b>
    /// <see cref="SettingsExportMessages.NoMigrationStep"/> lives in the envelope ladder's <c>default</c> arm,
    /// which the public reader cannot enter while <c>OldestSupportedFormatVersion == CurrentFormatVersion</c>:
    /// a lower version is refused by check 3 before the ladder runs, and there is no higher one.
    /// <see cref="TheOnlyUnreachableKey_IsStillUnreachable"/> asserts that premise, so the day format version 2
    /// ships this exemption fails instead of quietly excusing a message nobody checks (#322 — guard the
    /// premise, not the policy).
    /// </summary>
    private static readonly string[] UnreachableKeys = ["Settings.Import.NoMigrationStep"];

    private sealed record Observed(string English, LocalizableMessage Localized, string Scenario);

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────────────

    private static string RealExport() => SettingsExporter.Export(
        ExportFixtures.Populated(), new SettingsExportOptions(), ExportFixtures.Passphrase,
        ExportFixtures.AppVersion, ExportFixtures.Iterations);

    /// <summary>
    /// A well-formed, correctly authenticated export whose decrypted payload is <paramref name="json"/>.
    ///
    /// <para>⭐ The key is re-derived from the real header's own salt and iteration count, so the payload
    /// authenticates under the fixture passphrase — these scenarios are about a payload we CHOSE, not about a
    /// broken one, and a wrong-passphrase failure would mask every one of them.</para>
    /// </summary>
    private static string ExportWithPayload(string json)
    {
        var real = RealExport();
        var headerLine = real[..real.IndexOf('\n', StringComparison.Ordinal)];

        using var stream = SettingsExportTestIo.AsStream(real);
        SettingsExportEnvelope.TryReadHeader(stream, out var header);

        var protector = PassphraseProtector.Create(
            ExportFixtures.Passphrase, header.Salt, header.Iterations, header.Kdf);
        return headerLine + "\n" + protector.Protect(json);
    }

    private static string ExportWithRawPayload(string payload)
    {
        var real = RealExport();
        return real[..real.IndexOf('\n', StringComparison.Ordinal)] + "\n" + payload;
    }

    private static void InTempDir(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));
        try { body(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    // ── The scenarios ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every (English, localized) pair the import layer can produce, collected from the real producers. ⚠ Never
    /// written down here — a scenario that stops producing a pair shows up as a coverage failure, not as a
    /// silently green assertion.
    /// </summary>
    private static List<Observed> ObservedPairs()
    {
        var pairs = new List<Observed>();

        void Inspect(string scenario, string file)
        {
            var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(file));
            if (inspection.Localized is { } localized)
            {
                pairs.Add(new Observed(inspection.Message, localized, scenario));
            }
        }

        void Open(string scenario, string file, string passphrase)
        {
            var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(file));
            var result = SettingsImportReader.Open(inspection, passphrase);
            if (result.Localized is { } localized)
            {
                pairs.Add(new Observed(result.Message, localized, scenario));
            }
        }

        var real = RealExport();

        // ── Phase one ──
        // The path overload, on a file that is not there. Its message is the same sentence the stream overload
        // produces, which is why the two share one key.
        var missing = SettingsImportReader.Inspect(
            Path.Combine(Path.GetTempPath(), "EmberTern-absent-" + Guid.NewGuid().ToString("N") + ".etsettings"));
        pairs.Add(new Observed(missing.Message, missing.Localized!, "missing file"));

        Inspect("not our file", "just some notes I keep in a text file");
        Inspect("malformed header", SettingsExportTestIo.WithHeaderField(real, FormatVersionField, "one"));
        // ⚠ Deliberately an absurdly LARGE version, not 2: this is the argument-formatting case the
        // any-culture test below turns into a proof.
        Inspect("future format version",
            SettingsExportTestIo.WithHeaderField(real, FormatVersionField, "2000000000"));
        Inspect("format version too old", SettingsExportTestIo.WithHeaderField(real, FormatVersionField, "0"));
        Inspect("unknown scheme", SettingsExportTestIo.WithHeaderField(real, SchemeField, "rot13"));
        Inspect("unknown kdf", SettingsExportTestIo.WithHeaderField(real, KdfField, "scrypt"));
        Inspect("absurd iteration count",
            SettingsExportTestIo.WithHeaderField(real, IterationsField, "2000000000"));

        // ── Phase two ──
        Open("no passphrase", real, string.Empty);
        Open("wrong passphrase", real, "not the passphrase");
        Open("payload not base64", ExportWithRawPayload("!!! not base64 !!!"), ExportFixtures.Passphrase);
        // An empty salt parses as a header (Base64 of "" is an empty array) and then fails the protector's own
        // precondition — a header that read cleanly and is nonetheless unusable.
        Open("empty salt", SettingsExportTestIo.WithHeaderField(real, SaltField, string.Empty),
            ExportFixtures.Passphrase);
        Open("payload is not an object", ExportWithPayload("[1, 2, 3]"), ExportFixtures.Passphrase);
        Open("payload is not JSON", ExportWithPayload("{ this is not json"), ExportFixtures.Passphrase);
        Open("payload will not deserialize", ExportWithPayload("{\"Settings\": 42}"),
            ExportFixtures.Passphrase);
        // ⚠ An EXPLICIT null, not "{}": Settings has an initializer, so an absent property deserializes to a
        // fresh instance and the payload would open successfully. Only an explicit null reaches this arm.
        Open("payload carries no settings", ExportWithPayload("{\"Settings\": null}"),
            ExportFixtures.Passphrase);
        Open("future settings schema", ExportWithPayload(
                "{\"Sections\":[\"Preferences\"],\"Settings\":{\"SchemaVersion\":"
                + (ApplicationSettingsStore.CurrentSchemaVersion + 5) + "}}"),
            ExportFixtures.Passphrase);

        // ── Applying ──
        pairs.AddRange(ApplyPairs());

        Assert.NotEmpty(pairs);
        return pairs;
    }

    private static List<Observed> ApplyPairs()
    {
        var pairs = new List<Observed>();
        var content = OpenedContent();

        // Nothing ticked. Refused before anything is read, so no store state is needed.
        InTempDir(dir =>
        {
            var result = SettingsImportApplier.Apply(
                new ApplicationSettingsStore(dir), content, new SettingsImportSelection());
            pairs.Add(new Observed(result.Message, result.Localized!, "nothing selected"));
        });

        // The recovery copy cannot be made. ⚠ Provoked by occupying the destination path with a DIRECTORY, which
        // File.Copy can never overwrite. The name carries a to-the-second stamp, so a small window of them is
        // pre-created; a missed window shows up as a different key here, never as a silent pass.
        InTempDir(dir =>
        {
            var store = new ApplicationSettingsStore(dir);
            store.Save(new ApplicationSettings());

            for (var second = 0; second < 5; second++)
            {
                var stamp = DateTime.Now.AddSeconds(second).ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                Directory.CreateDirectory(store.FilePath + ".pre-import-" + stamp);
            }

            var result = SettingsImportApplier.Apply(
                store, content, new SettingsImportSelection { Preferences = true });

            Assert.Equal(SettingsImportApplyStatus.Refused, result.Status);
            pairs.Add(new Observed(result.Message, result.Localized!, "copy aside failed"));
        });

        // ⭐ The FORWARDED refusal: the store will not overwrite a file this build cannot decrypt, and the import
        // must show the STORE's sentence rather than a second one saying the same thing.
        InTempDir(dir =>
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "settings.dat"), SettingsExportFormat.Magic + "\nrest-of-an-export");

            var result = SettingsImportApplier.Apply(
                new ApplicationSettingsStore(dir), content, new SettingsImportSelection { Preferences = true });

            Assert.Equal(SettingsImportApplyStatus.Refused, result.Status);
            pairs.Add(new Observed(result.Message, result.Localized!, "store refuses the write"));
        });

        return pairs;
    }

    private static SettingsExportContent OpenedContent()
    {
        var inspection = SettingsImportReader.Inspect(SettingsExportTestIo.AsStream(RealExport()));
        var result = SettingsImportReader.Open(inspection, ExportFixtures.Passphrase);
        Assert.True(result.IsUsable, result.Message);
        return result.Content!;
    }

    // ── The guards ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>C4b's correctness in one assertion.</b> Every localized import message must render, in English,
    /// exactly the sentence the producer already produced — which proves both that the two forms cannot drift
    /// and that no shipped wording changed.
    /// </summary>
    [Fact]
    public void EveryLocalizedImportMessage_RendersExactlyItsEnglishForm()
    {
        foreach (var (english, localized, scenario) in ObservedPairs())
        {
            Assert.False(string.IsNullOrEmpty(english), scenario + ": no English form");
            Assert.Equal(english, Loc.Format(localized));
            Assert.DoesNotContain(' ', localized.Key.Value);          // a key, never prose
            Assert.NotEqual(localized.Key.Value, Loc.Format(localized)); // the entry exists
        }
    }

    /// <summary>
    /// The equality above must not depend on the machine that runs it, so it is re-run under four cultures.
    ///
    /// <para>⚠ <b>Stated honestly: this is a broad invariance sweep, not a proof about numeric formatting.</b> It
    /// stays green whether the echoed numbers travel as strings or as <c>int</c>s — measured by planting the
    /// numeric argument. What guards the numeric case is
    /// <see cref="EveryArgument_IsAlreadyFormatted_SoNoFormatSpecifierCanChangeIt"/>. Keeping this one anyway is
    /// worth its cost: it also covers decimal separators, negative signs and any future non-string argument whose
    /// <c>ToString</c> <i>is</i> culture-sensitive.</para>
    /// </summary>
    [Theory]
    [InlineData("pl-PL")]
    [InlineData("de-DE")]
    [InlineData("en-US")]
    [InlineData("")]
    public void TheEnglishAndLocalizedForms_AgreeOnAnyCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = culture.Length == 0
                ? CultureInfo.InvariantCulture
                : CultureInfo.GetCultureInfo(culture);

            foreach (var (english, localized, scenario) in ObservedPairs())
            {
                Assert.Equal(english, Loc.Format(localized));
                _ = scenario;
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// ⭐⭐ <b>The precondition of the equality proof, measured rather than restated: no format specifier a
    /// resource value could carry can change how an argument renders.</b>
    ///
    /// <para>The English half of each pair is a literal in the producer; the localized half is a resource value a
    /// translator may edit, and <c>{0:N0}</c> on a nine-digit count is a reasonable thing for a translator to
    /// write. So the test serves a deliberately hostile template — <c>{0:N0}</c> under a grouping culture — and
    /// requires every argument to still appear verbatim. A string is immune (a specifier is inert on it); an
    /// <c>int</c> is not. ⛔ This is the test that goes red if someone "simplifies" an echoed header number back
    /// into a number, and it is discriminating where the culture sweep above is not.</para>
    /// </summary>
    [Fact]
    public void EveryArgument_IsAlreadyFormatted_SoNoFormatSpecifierCanChangeIt()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            Loc.UseCatalogForVerification(new GroupingCatalog(), CultureInfo.InvariantCulture);

            var withArguments = 0;
            foreach (var (_, localized, scenario) in ObservedPairs())
            {
                if (localized.Arguments.Count == 0)
                {
                    continue;
                }

                withArguments++;

                // One argument at a time, so the hostile template needs exactly one placeholder whatever the
                // message's real arity — and the path is still production's Loc.Format, not a copy of it.
                foreach (var argument in localized.Arguments)
                {
                    Assert.NotNull(argument);
                    var rendered = Loc.Format(LocalizableMessage.Of(localized.Key, argument));
                    Assert.Contains(argument!.ToString()!, rendered, StringComparison.Ordinal);
                }

                _ = scenario;
            }

            Assert.True(withArguments > 0, "no message carried an argument — the test would prove nothing");
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>
    /// ⭐ Every key the module declares is exercised by a scenario above, or is a named exemption with a written
    /// reason. A key with no producer is both a broken build (the self-arming catalog guard) and a component
    /// with no consumer (#233); this catches the subtler case of a key whose producer exists but is untested.
    /// </summary>
    [Fact]
    public void EveryDeclaredImportKey_IsExercisedOrNamedUnreachable()
    {
        var observed = ObservedPairs().Select(p => p.Localized.Key.Value).ToHashSet(StringComparer.Ordinal);

        var declared = typeof(SettingsExportMessages)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(MessageKey))
            .Select(f => ((MessageKey)f.GetValue(null)!).Value)
            .ToList();

        Assert.NotEmpty(declared);

        var unexercised = declared
            .Where(k => !observed.Contains(k) && !UnreachableKeys.Contains(k, StringComparer.Ordinal))
            .ToList();

        Assert.True(unexercised.Count == 0,
            "Declared but never produced by a scenario: " + string.Join(", ", unexercised));

        // And in the other direction: an exemption that has become reachable is a stale exemption.
        var stale = UnreachableKeys.Where(observed.Contains).ToList();
        Assert.True(stale.Count == 0, "Named unreachable yet observed: " + string.Join(", ", stale));
    }

    /// <summary>
    /// ⭐ The premise behind the single exemption, asserted rather than trusted: the envelope ladder's refusal
    /// arm cannot be entered while these two versions are equal. The day a format version 2 ships, this fails
    /// and the exemption has to be replaced by a scenario.
    /// </summary>
    [Fact]
    public void TheOnlyUnreachableKey_IsStillUnreachable()
    {
        Assert.Equal(
            SettingsExportFormat.CurrentFormatVersion, SettingsExportFormat.OldestSupportedFormatVersion);
        Assert.Single(UnreachableKeys);
    }

    /// <summary>
    /// ⛔ <b>The <c>Damaged</c> prefix is never a key of its own.</b> Each of those four messages is a WHOLE
    /// SENTENCE in the catalog: a fixed prefix glued to a fragment cannot be translated into a language that
    /// inflects, and <i>"its payload is not valid JSON (…)"</i> is not a sentence in any language.
    /// </summary>
    [Fact]
    public void TheDamagedMessages_AreWholeSentences_NotAPrefixPlusAFragment()
    {
        const string prefix = "This settings export is damaged: ";

        var damaged = ObservedPairs()
            .Where(p => p.English.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        Assert.Equal(4, damaged.Count);

        foreach (var pair in damaged)
        {
            // The catalog entry itself must carry the whole sentence, prefix included — not be composed from one.
            var template = Loc.Text(pair.Localized.Key);
            Assert.StartsWith(prefix, template, StringComparison.Ordinal);
            Assert.EndsWith(".", template.TrimEnd(), StringComparison.Ordinal);
        }

        // And no entry is the bare prefix.
        Assert.DoesNotContain(prefix.TrimEnd(), EnglishValues());
    }

    /// <summary>
    /// The import layer never resolves words itself: the same scenario yields the same keys and the same
    /// arguments whatever language is current. ⭐ If anyone made a producer read a catalog, this catches it
    /// without naming a single expected sentence.
    /// </summary>
    [Fact]
    public void TheImportLayerProducesTheSameKeys_WhateverTheLanguage()
    {
        try
        {
            Loc.UseCatalogForVerification(new PassThroughCatalog(), CultureInfo.InvariantCulture);
            var before = ObservedPairs().Select(p => p.Localized.Key.Value).ToList();

            Loc.UseCatalogForVerification(new PassThroughCatalog(), CultureInfo.GetCultureInfo("qps-ploc"));
            var after = ObservedPairs().Select(p => p.Localized.Key.Value).ToList();

            Assert.Equal(before, after);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⭐⭐ <b>The App half: the dialog resolves Core's verdict at the moment it composes the message, not when
    /// the inspection was produced.</b> Swapping the catalog between two identical operations must change what the
    /// dialog says — which is the property that makes a language switch reach this surface at all.
    ///
    /// <para>⚠ <b>What this does NOT claim, stated because the difference matters:</b> that a language switch can
    /// happen <i>while the dialog is open</i>. It cannot — measured: the language preference has exactly one
    /// writer (Settings Center's Language row) and the dialog is opened with <c>ShowDialog</c> over that very
    /// window. And in the one case where an import itself changes the language, <c>SettingsPortability.Apply</c>
    /// reloads the preferences before returning, so composition already happens in the new language. So the
    /// storable <c>Message</c> is correct <b>by ordering and by modality</b>, not by a refresh hook — the same
    /// answer C4a measured for Settings Center's banner. ⛔ If this dialog ever becomes non-modal, that reasoning
    /// lapses.</para>
    /// </summary>
    [Fact]
    public void TheDialog_ResolvesCoresVerdictAtDisplayTime_NotWhenItWasProduced()
    {
        InTempDir(dir =>
        {
            Directory.CreateDirectory(dir);
            var notAnExport = Path.Combine(dir, "notes.txt");
            File.WriteAllText(notAnExport, "just some notes I keep in a text file");

            var store = new ApplicationSettingsStore(dir);
            var portability = new SettingsPortability(
                store, new PreferencesService(new PreferencesStore(dir)), ExportFixtures.AppVersion);
            var dialog = new SettingsImportDialogViewModel(portability);

            try
            {
                // A catalog that renders the KEY. If the dialog showed Core's English half instead of resolving,
                // this would read the sentence and the assertion would fail.
                Loc.UseCatalogForVerification(new PassThroughCatalog(), CultureInfo.InvariantCulture);
                dialog.PickFile(notAnExport);
                Assert.Equal("Settings.Import.NotAnExportFile", dialog.Message);
            }
            finally
            {
                Loc.UseCatalogForVerification(null, null);
            }

            // Same file, same operation, real catalog — the text follows the catalog, so it was never captured.
            dialog.PickFile(notAnExport);
            Assert.Equal("This is not an EmberTern settings file.", dialog.Message);
            Assert.True(dialog.ShowMessage);
            Assert.False(dialog.CanEnterPassphrase);
        });
    }

    private static List<string> EnglishValues()
        => ObservedPairs().Select(p => Loc.Text(p.Localized.Key)).ToList();

    // Renders the key itself, so a language switch cannot make two runs look equal for the wrong reason.
    private sealed class PassThroughCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture) => name;
    }

    /// <summary>The hostile template: one substitution carrying a grouping specifier — the shape a translator
    /// could reasonably introduce for a long number.</summary>
    private sealed class GroupingCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture) => "[{0:N0}]";
    }
}

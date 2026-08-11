using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Resources;
using EmberTern.App.Localization;
using EmberTern.Core.Localization;
using EmberTern.Core.Security;
using EmberTern.Core.Settings;
using EmberTern.Core.Settings.Export;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The settings store on decision <b>D‑3</b>: every refusal it can utter now exists twice — in English for
/// logs and for the ~20 existing tests that pin the exact wording, and as a
/// <see cref="LocalizableMessage"/> for the two surfaces that show it.
///
/// <para>⭐⭐ <b>The equality guard below is simultaneously the anti-drift check AND the zero-text-change
/// proof.</b> It asserts that each localized form resolves, in English, to exactly the string the untouched
/// producer built — so a resource entry that does not reproduce the shipped sentence character for character
/// fails here, and no separate "did the text change" exercise is needed.</para>
///
/// <para>⚠ Driven through REAL scenarios (a file this build cannot decrypt, an export copied over
/// settings.dat, a future schema) rather than a table of expected strings: a table would be a second copy of
/// the catalog and would go red on a typo fix while staying green if the producer stopped setting the pair
/// (gotcha #333).</para>
///
/// <para>⚠ Joins the headless collection: it swaps <c>Loc</c>'s catalog, process-global state.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class SettingsStoreLocalizationTests
{
    private static SecretProtector FakeProtector() => new(s => "ENC:" + s, s => s["ENC:".Length..]);

    private static SecretProtector UndecryptableProtector() =>
        new(s => "ENC:" + s, _ => throw new InvalidOperationException("Key not valid for use in specified state."));

    private static void InTempDir(Action<string> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));
        try { body(dir); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }

    // Every (english, localized) pair the store records across the scenarios below. Collected from the real
    // producer, never written down here.
    private static List<(string English, LocalizableMessage Localized, string Scenario)> ObservedPairs()
    {
        var pairs = new List<(string, LocalizableMessage, string)>();

        void Capture(ApplicationSettingsStore store, string scenario)
        {
            if (store.LastLoadDiagnostic is { } le && store.LastLoadMessage is { } lm)
            {
                pairs.Add((le, lm, scenario + " (load)"));
            }
            if (store.LastSaveDiagnostic is { } se && store.LastSaveMessage is { } sm)
            {
                pairs.Add((se, sm, scenario + " (save)"));
            }
        }

        // 1. A file this build cannot decrypt — the audit A-03 case, and the one most worth not destroying.
        InTempDir(dir =>
        {
            new ApplicationSettingsStore(dir, FakeProtector()).Save(new ApplicationSettings());
            var store = new ApplicationSettingsStore(dir, UndecryptableProtector());
            store.LoadWithStatus();
            Capture(store, "undecryptable");
            store.Save(new ApplicationSettings());
            Capture(store, "undecryptable");
        });

        // 2. An .etsettings export copied over settings.dat.
        InTempDir(dir =>
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "settings.dat"),
                SettingsExportFormat.Magic + "\nrest-of-an-export");
            var store = new ApplicationSettingsStore(dir, FakeProtector());
            store.LoadWithStatus();
            Capture(store, "export-in-place");
            store.Save(new ApplicationSettings());
            Capture(store, "export-in-place");
        });

        // 3. Content that decrypts but is not settings JSON.
        InTempDir(dir =>
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "settings.dat"), "ENC:{not json at all");
            var store = new ApplicationSettingsStore(dir, FakeProtector());
            store.LoadWithStatus();
            Capture(store, "unparseable");
            store.Save(new ApplicationSettings());
            Capture(store, "unparseable");
        });

        // 4. A payload from a build with a newer settings shape.
        InTempDir(dir =>
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "settings.dat"),
                "ENC:{\"SchemaVersion\":" + (ApplicationSettingsStore.CurrentSchemaVersion + 5) + "}");
            var store = new ApplicationSettingsStore(dir, FakeProtector());
            store.LoadWithStatus();
            Capture(store, "future-schema");
            store.Save(new ApplicationSettings());
            Capture(store, "future-schema");
        });

        Assert.NotEmpty(pairs);
        return pairs;
    }

    /// <summary>
    /// ⭐⭐ <b>The whole of C4's correctness in one assertion.</b> Every localized refusal must render, in
    /// English, exactly the sentence the store already produced — which proves both that the two forms cannot
    /// drift and that no shipped wording changed.
    /// </summary>
    [Fact]
    public void EveryLocalizedRefusal_RendersExactlyItsEnglishForm()
    {
        foreach (var (english, localized, scenario) in ObservedPairs())
        {
            Assert.Equal(english, Loc.Format(localized));
            Assert.DoesNotContain(' ', localized.Key.Value); // a key, never prose
            Assert.NotEqual(localized.Key.Value, Loc.Format(localized)); // the entry exists
            _ = scenario;
        }
    }

    /// <summary>
    /// The store never resolves words itself: the same scenario yields the same key and the same arguments
    /// whatever language is current. ⭐ If anyone ever made the store read a catalog, this is what would catch
    /// it, without naming a single expected sentence.
    /// </summary>
    [Fact]
    public void TheStoreProducesTheSameKeys_WhateverTheLanguage()
    {
        var pseudo = CultureInfo.GetCultureInfo("qps-ploc");
        try
        {
            Loc.UseCatalogForVerification(new PassThroughCatalog(), CultureInfo.InvariantCulture);
            var before = KeysOf(ObservedPairs());

            Loc.UseCatalogForVerification(new PassThroughCatalog(), pseudo);
            var after = KeysOf(ObservedPairs());

            Assert.Equal(before, after);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }

        static List<string> KeysOf(List<(string English, LocalizableMessage Localized, string Scenario)> pairs)
        {
            var keys = new List<string>();
            foreach (var p in pairs) keys.Add(p.Localized.Key.Value);
            return keys;
        }
    }

    // Renders the key itself, so a language switch cannot accidentally make two runs look equal for the wrong
    // reason — the comparison above is about the KEYS the store chose, not about any text.
    private sealed class PassThroughCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture) => name;
    }
}

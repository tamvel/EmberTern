using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using EmberTern.App;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Localization;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Guards for the localization mechanism delivered in stage <b>L1</b>.
///
/// <para>⚠ Every test here runs against the SHIPPED artefacts — the embedded resource set, the real
/// <c>UiStrings</c> members, the real preference catalog. None of them transcribes a list of its own; where a
/// test needs to know "which languages exist" or "which keys exist" it asks the catalog or the resource set.
/// That is deliberate: a guard that copies its premise breaks when the premise moves and then reports
/// something its own name does not describe (gotcha #333), and this stage's whole purpose is that a second
/// language can be added <i>without editing code</i> — including without editing these tests.</para>
///
/// <para>⚠⚠ <b>Joined to the headless collection even though it constructs no control — and the reason is a
/// real race, not tidiness.</b> <c>Loc</c> is process-global mutable state, and the liveness tests swap its
/// catalog to measure that a language change re-reads. xunit runs different collections in PARALLEL, so a
/// mechanism test could read <c>UiStrings.X</c> while the fake catalog was installed and see another key's
/// value. Measured, not theorised: running both filters in one invocation produced exactly that —
/// <c>AboutAuthorFormat</c> rendering the sidebar's empty-state text.</para>
///
/// <para>⚠ The documented partition scheme never ran them together, so the race was latent rather than
/// observed. Sharing a collection serialises them and removes it; the cost is one more name in the
/// headless-partition filter, which is the cheaper of the two.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class LocalizationMechanismTests
{
    private static readonly ResourceManager Resources =
        new("EmberTern.App.Localization.Strings", typeof(UiStrings).Assembly);

    /// <summary>Every key the English base declares, read from the shipped resource set — never a hand-written list.</summary>
    private static IReadOnlyList<string> EnglishKeys()
    {
        // ⚠ NOT `using`. GetResourceSet hands back the ResourceManager's OWN cached set, so disposing it
        // closes the manager for every later call — the second test then fails with ObjectDisposedException
        // for a reason that has nothing to do with what it asserts. (Measured, not assumed: that is exactly
        // how the first version of this class failed.)
        var set = Resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)
            ?? throw new InvalidOperationException("The English resource set did not load.");
        return set.Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
    }

    // ── Guard 1 + the mechanism's own liveness ───────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The English resource set actually loads.</b> Not ceremony: the manifest name in <c>Loc</c> is a
    /// STRING (<c>"EmberTern.App.Localization.Strings"</c>), so a moved, renamed or wrongly-built <c>.resx</c>
    /// compiles perfectly and fails only when the first lookup runs — in the user's hands, as a
    /// <see cref="MissingManifestResourceException"/>. It is the same failure shape as gotcha #348: a missing
    /// registration is silent, one layer further out than a missing value.
    /// </summary>
    [Fact]
    public void TheEnglishResourceSet_Loads()
    {
        var keys = EnglishKeys();
        Assert.NotEmpty(keys);
    }

    /// <summary>
    /// Guard 1 — <b>English is a complete base language.</b> Complete means: every key the catalog declares
    /// resolves to a non-empty value under the neutral culture, so nothing can fall through to
    /// <c>Loc.Text</c>'s key-echo path.
    /// </summary>
    [Fact]
    public void EnglishBase_ResolvesEveryKeyItDeclares()
    {
        var missing = EnglishKeys()
            .Where(k => string.IsNullOrEmpty(Resources.GetString(k, CultureInfo.InvariantCulture)))
            .ToList();

        Assert.True(missing.Count == 0,
            "These keys exist in the English base but resolve to nothing: " + string.Join(", ", missing));
    }

    /// <summary>
    /// Guard 1, second half — <b>a translation may translate keys, never introduce them.</b> Vacuous today
    /// (English is the only culture) and it arms itself the moment L5 adds <c>Strings.pl.resx</c>: a key
    /// present in a satellite but absent from English would silently render in one language only.
    /// </summary>
    [Fact]
    public void NoShippedCulture_IntroducesAKeyEnglishLacks()
    {
        var english = EnglishKeys().ToHashSet(StringComparer.Ordinal);

        foreach (var key in PreferenceOptions.Language.Values)
        {
            var culture = LanguagePreference.CultureFor(key);
            if (Equals(culture, CultureInfo.InvariantCulture)) continue;

            var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
            if (set is null) continue; // no satellite for this culture yet — English is used, which is correct

            var extra = set.Cast<System.Collections.DictionaryEntry>()
                .Select(e => (string)e.Key)
                .Where(k => !english.Contains(k))
                // ⭐ Etap C6 WIDENED this too, and this is the case it was widened FOR: a language may need
                // more plural forms than English has. Polish declares `X.few` where English declares only
                // `X.one`/`X.other`, or even where English has a single flat `X` — a translation adding a
                // grammatical category is not "introducing a key", it is answering the same key in its own
                // grammar. ⛔ Still a real fence: the BASE key must exist in English, so a typo in a
                // translated key is caught exactly as before.
                .Where(k => BaseKeyOf(k) is not { } b
                    || !(english.Contains(b) || HasPluralFamily(english, b)))
                .ToList();

            Assert.True(extra.Count == 0,
                $"Culture '{culture.Name}' declares keys the English base does not: {string.Join(", ", extra)}");
        }
    }

    // ── Guard 6 — the mechanism did not change any English value ─────────────────────────────────────────

    /// <summary>
    /// Guard 6 — <b>every migrated <c>UiStrings</c> member still renders exactly its English text</b>, and the
    /// binding runs in both directions: a resource key with no matching member is an orphan (nothing reads
    /// it), and a member whose value drifted from the resource is a text change nobody asked for.
    ///
    /// <para>⭐ This is the guard that makes L3 safe to do mechanically: it grows by itself as members are
    /// migrated, because it enumerates the RESOURCE SET rather than a list of migrated names.</para>
    /// </summary>
    [Fact]
    public void EveryLocalizedMember_MatchesItsEnglishEntry()
    {
        // ⚠⚠ The catalog has TWO owners since the first Core producer migrated (D‑3). An App key is the name
        // of a UiStrings property; a Core key is a MessageKey declared in Core/Firebird and resolved through
        // Loc.Format, and it CANNOT have a member of that name — it contains dots, so it is not a legal C#
        // identifier. Skipping those here is a partition, not a relaxation: their own orphan check is
        // EveryCoreShapedEntry_IsDeclaredByCore, so both halves stay guarded in both directions.
        var coreKeys = DeclaredCoreMessageKeys().ToHashSet(StringComparer.Ordinal);

        foreach (var key in EnglishKeys()
            .Where(k => !coreKeys.Contains(k))
            // ⭐ C6: a plural VARIANT belongs to its base key, and the mechanism's own metadata belongs to
            // nobody's sentence — neither can have a UiStrings property. Both partitions keep their orphan
            // protection through EveryCoreShapedEntry_IsDeclaredByCore, which checks the same two shapes from
            // the other side, so there is still no gap between the halves.
            .Where(k => BaseKeyOf(k) is not { } b || !coreKeys.Contains(b))
            .Where(k => !MechanismMetadataKeys.Contains(k)))
        {
            var member = typeof(UiStrings).GetProperty(key, BindingFlags.Public | BindingFlags.Static);
            Assert.True(member is not null,
                $"Resource key '{key}' has no UiStrings property of that name. The member name IS the key — an " +
                "entry nothing reads is an orphan, and orphaned strings are how a defect survives (gotcha #346).");

            var expected = Resources.GetString(key, CultureInfo.InvariantCulture);
            var actual = (string?)member!.GetValue(null);

            // ⚠ A composed member renders MORE than its resource entry, because CommandTip adds a keyboard
            // gesture that is deliberately never stored — so this is not an equality check, and equality
            // would force the gesture into the catalog (exactly what UiStringsShortcutSourceTests forbids).
            //
            // ⚠⚠ There are TWO composition shapes and only one of them appends. `CommandTip.For` puts the
            // gesture at the END, so StartsWith holds; `CommandTip.Sentence` substitutes it into a `{0}`
            // placeholder MID-SENTENCE, and a StartsWith assertion fails on a perfectly correct member. The
            // first version of this guard had exactly that bug and reported DebuggerHarnessLogEmpty as
            // broken.
            var rendered = actual ?? string.Empty;
            var holds = expected!.Contains("{0}", StringComparison.Ordinal)
                ? Regex.IsMatch(rendered,
                    "^" + string.Join(".+", expected.Split("{0}").Select(Regex.Escape)) + "$",
                    RegexOptions.Singleline)
                : rendered.StartsWith(expected, StringComparison.Ordinal);

            Assert.True(holds,
                $"UiStrings.{key} does not render its English entry.\n  entry : {expected}\n  member: {rendered}");
        }
    }

    /// <summary>
    /// Guard 6, the part a value comparison cannot make: <b>a migrated member must not be a <c>const</c></b>.
    /// A <c>const</c> compares equal to its resource text and is nevertheless inlined by the compiler, so it
    /// would pass the test above while being permanently untranslatable — green, and wrong.
    /// </summary>
    [Fact]
    public void NoLocalizedMember_IsInlinedByTheCompiler()
    {
        var frozen = EnglishKeys()
            .Select(k => typeof(UiStrings).GetField(k, BindingFlags.Public | BindingFlags.Static))
            .Where(f => f is not null)
            .Select(f => f!.Name + (f.IsLiteral ? " (const)" : " (static readonly)"))
            .ToList();

        Assert.True(frozen.Count == 0,
            "These members have a resource entry but are still FIELDS. A `const` is inlined by the compiler, "
            + "so the resource can never be read at all; a `static readonly` resolves once at type "
            + "initialization, so it renders correctly and then freezes in whatever language was current "
            + "first — green build, correct-looking screen, dead switch. A localized member must be a "
            + "PROPERTY: " + string.Join(", ", frozen));
    }

    // ── Guards 3 + 4 — the language comes from the preference, and falls back to English ──────────────────

    /// <summary>
    /// Guard 3 — <b>absent, empty or unknown language ⇒ English.</b> Driven through the same
    /// <see cref="LanguagePreference"/> the app uses, so it tests the shipped path rather than a re-derivation.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("zz")]
    [InlineData("pl-PL-nonsense")]
    public void AnUnusableLanguage_FallsBackToEnglish(string? stored)
    {
        var culture = LanguagePreference.CultureFor(stored);
        Assert.Equal(PreferenceOptions.Language.Default, PreferenceOptions.Language.Normalize(stored));
        Assert.Equal(Resources.GetString(nameof(UiStrings.MessageBannerDismissTooltip), CultureInfo.InvariantCulture),
            Resources.GetString(nameof(UiStrings.MessageBannerDismissTooltip), culture));
    }

    /// <summary>
    /// Guard 4 — <b><see cref="Preferences.Language"/> is the only source of the chosen language.</b> Asserted
    /// on the source: the localization code reads the preference catalog and nothing else — no
    /// <c>CurrentUICulture</c> sniffing, no environment variable, no second store.
    ///
    /// <para>⚠ A behavioural assertion cannot cover this. Reading the OS culture as a fallback would still
    /// produce English on an English machine, so the test would be green on a developer's box and wrong on a
    /// Polish one — the exact shape of gotcha #328, where a display defect was invisible under the tester's
    /// own regional settings.</para>
    /// </summary>
    [Fact]
    public void TheLanguage_ComesOnlyFromThePreference()
    {
        var sources = LocalizationSources().ToList();

        foreach (var (path, text) in sources)
        {
            Assert.DoesNotContain("CurrentUICulture", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Environment.GetEnvironmentVariable", text, StringComparison.Ordinal);
            Assert.DoesNotContain("InstalledUICulture", text, StringComparison.Ordinal);
            Assert.False(path.EndsWith("Loc.cs", StringComparison.Ordinal) && text.Contains("CultureInfo.CurrentCulture", StringComparison.Ordinal)
                    && !text.Contains("string.Format(CultureInfo.CurrentCulture", StringComparison.Ordinal),
                "Loc may use CurrentCulture only to FORMAT data (numbers, dates) — never to choose the language.");
        }

        Assert.Contains(sources, s => s.Text.Contains("PreferenceOptions.Language", StringComparison.Ordinal));
    }

    // ── Guards 2 + 5 — no per-language branching; a new language needs no code change ────────────────────

    /// <summary>
    /// Guard 2 — <b>no <c>language == "pl"</c> anywhere in the localization or UI-string code.</b> The audit
    /// measured zero such branches today; this is what keeps that true, because the shortest route to a
    /// working Polish build is exactly such a branch.
    ///
    /// <para>⚠ The pattern is deliberately broad — any comparison of an identifier to a two-letter language
    /// literal — because the defect has many spellings (<c>==</c>, <c>Equals</c>, <c>switch</c>, a ternary)
    /// and keying on one of them would be a counter that cannot see the same thing built another way
    /// (gotcha #337).</para>
    /// </summary>
    [Fact]
    public void NoCode_BranchesOnAParticularLanguage()
    {
        var languageLiteral = new Regex(
            @"(==|!=|Equals\s*\(|case\s+)\s*""(pl|de|fr|es|it|cs|uk|ru|en)(-[A-Za-z]{2,4})?""",
            RegexOptions.IgnoreCase);

        foreach (var (path, text) in LocalizationSources())
        {
            // ⚠ CODE only, not comments — and this is not a convenience. The first version scanned the whole
            // file and fired on Loc.cs's own doc comment, which quotes `language == "pl"` in order to FORBID
            // it. A guard that cannot tell a rule from its statement punishes writing the rule down; the same
            // shape cost an iteration in M4.3c, where a comment quoting attribute syntax raised a counter.
            // ⚠ Limitation, stated rather than hidden: a trailing comment on a line of code is still scanned.
            var hit = languageLiteral.Match(CodeOnly(text));
            Assert.False(hit.Success,
                $"{Path.GetFileName(path)} branches on a specific language ({hit.Value}). A language is DATA: " +
                "it is a row in PreferenceOptions.Language plus a satellite .resx, never a branch.");
        }
    }

    /// <summary>
    /// Guard 5 — <b>adding a language needs no change to a ViewModel or to XAML.</b> Proven structurally: the
    /// mapping from a stored key to a culture is driven from the catalog, so every key the catalog offers —
    /// today one, tomorrow two — becomes the culture of that name with no code listing it.
    ///
    /// <para>⭐ Written as a loop over <see cref="PreferenceOptions.Language.Values"/> precisely so that
    /// adding <c>"pl"</c> to that catalog extends this test automatically. If the mapping ever grows a branch,
    /// a language will appear in the catalog that this test cannot resolve, and it fails.</para>
    /// </summary>
    [Fact]
    public void EveryLanguageInTheCatalog_ResolvesToItsOwnCultureWithNoCodeChange()
    {
        foreach (var key in PreferenceOptions.Language.Values)
        {
            var culture = LanguagePreference.CultureFor(key);
            Assert.Equal(key, culture.Name);
        }
    }

    /// <summary>
    /// Guard 5, the half that names the real risk: <b>no view, view model or converter reads the language.</b>
    /// If one did, a second language would mean editing UI code — the outcome D‑2 exists to prevent.
    /// </summary>
    [Fact]
    public void NoViewOrViewModel_ReadsTheLanguagePreference()
    {
        var offenders = new List<string>();
        var root = RepositoryRoot();

        foreach (var dir in new[] { "Views", "ViewModels", "Converters", "Controls", "Behaviors" })
        {
            var full = Path.Combine(root, "src", "EmberTern.App", dir);
            if (!Directory.Exists(full)) continue;

            foreach (var file in Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                if (text.Contains(".Language", StringComparison.Ordinal) &&
                    !text.Contains("Sql.Language", StringComparison.Ordinal))
                {
                    offenders.Add(Path.GetFileName(file));
                }
            }
        }

        // SettingsCenterViewModel legitimately reads it to DISPLAY the current value in the Settings window.
        offenders.Remove("SettingsCenterViewModel.cs");

        Assert.True(offenders.Count == 0,
            "These UI files read the language preference; a language must reach the UI only as resolved text: "
            + string.Join(", ", offenders));
    }

    // ── Guards 7 + 8 — the Core seam ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Guard 7 — <b>Core gained no dependency on App or Avalonia.</b> Asserted on the loaded assembly's real
    /// reference list, not on the project file, so a transitive reference cannot slip past.
    /// </summary>
    [Fact]
    public void Core_ReferencesNeitherAppNorAvalonia()
    {
        var referenced = typeof(MessageKey).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        Assert.DoesNotContain(referenced, n => n.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.Equals("EmberTern", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Guard 8 — <b>a <see cref="MessageKey"/> cannot carry finished prose</b>, and that is enforced by the
    /// constructor rather than by review.
    ///
    /// <para>⭐ This is why the seam is trustworthy without a "does this look English?" heuristic — the audit
    /// showed such heuristics have blind spots in both directions. A sentence needs a space or punctuation;
    /// a key may not have either; therefore no sentence is a valid key.</para>
    /// </summary>
    [Theory]
    [InlineData("Your settings file could not be read.")]
    [InlineData("Unknown object")]
    [InlineData("Could not connect to {0}")]
    [InlineData("")]
    [InlineData(".LeadingDot")]
    [InlineData("TrailingDot.")]
    public void AMessageKey_RefusesProse(string candidate)
        => Assert.Throws<ArgumentException>(() => new MessageKey(candidate));

    /// <summary>Guard 8, the positive half — an identifier-shaped key is accepted.</summary>
    [Theory]
    [InlineData("SettingsFileUnreadable")]
    [InlineData("Settings.FileUnreadable")]
    [InlineData("Diagnostics_ET0001")]
    public void AMessageKey_AcceptsAnIdentifier(string candidate)
        => Assert.Equal(candidate, new MessageKey(candidate).Value);

    /// <summary>
    /// Guard 8 — <b>the seam works end to end: a message built in Core, with no English in it, comes out of
    /// App as English text with its data substituted.</b>
    ///
    /// <para>⚠ The message is constructed here with a key that IS in the catalog, so this exercises the real
    /// resolver and the real resource set. It does not pretend a Core producer exists — there is none until
    /// stage L4, by decision.</para>
    /// </summary>
    [Fact]
    public void ACoreMessage_ResolvesToEnglishTextInTheAppLayer()
    {
        var message = LocalizableMessage.Of(
            new MessageKey(nameof(UiStrings.SettingsUnreadableWarningFormat)),
            @"C:\settings.dat", "decrypt failed");

        // The contract itself carries no prose — only a key and data.
        Assert.DoesNotContain(" ", message.Key.Value, StringComparison.Ordinal);

        var text = Loc.Format(message);

        Assert.Contains(@"C:\settings.dat", text, StringComparison.Ordinal);
        Assert.Contains("decrypt failed", text, StringComparison.Ordinal);
        Assert.Contains("could not be read", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐ <b>A guard that arms itself.</b> It was vacuous while Core declared no keys, and went live with the
    /// first producer (<c>SessionHealthMessages</c>): a key with no English entry resolves to itself and puts
    /// a raw identifier on screen.
    ///
    /// <para>⚠ Justification for adding it before it had a subject: the failure it prevents is silent, and the
    /// convention it encodes — Core owns the KEY, App's resource file owns the WORDS — is exactly the thing a
    /// future session would otherwise have to rediscover from prose.</para>
    /// </summary>
    [Fact]
    public void EveryCoreMessageKey_HasAnEnglishEntry()
    {
        var english = EnglishKeys().ToHashSet(StringComparer.Ordinal);

        // ⭐ Etap C6 WIDENED this, it did not weaken it. A key now resolves either to a flat entry or to a
        // complete family of plural variants (`key.one`, `key.other`, …) — the second shape is what lets a
        // language declare more forms than English needs. The premise "a declared key resolves to a sentence"
        // is unchanged; only the number of ways to satisfy it grew. ⛔ Do not turn this into "flat OR
        // anything with a dot after it": a family with no variants at all would then pass.
        var missing = DeclaredCoreMessageKeys()
            .Where(k => !english.Contains(k) && !HasPluralFamily(english, k))
            .ToList();

        Assert.True(missing.Count == 0,
            "Core declares these message keys with no English entry in Localization/Strings.resx (neither a "
            + "flat entry nor a plural family): " + string.Join(", ", missing));
    }

    /// <summary>Whether <paramref name="key"/> is served by category variants rather than a flat entry.</summary>
    private static bool HasPluralFamily(IReadOnlySet<string> entries, string key)
        => PluralRules.KnownRuleSets
            .SelectMany(PluralRules.CategoriesOf)
            .Distinct()
            .Any(c => entries.Contains(key + "." + PluralRules.SuffixFor(c)));

    /// <summary>Every category variant of <paramref name="key"/> present in <paramref name="entries"/>.</summary>
    private static IEnumerable<string> PluralVariantsOf(IReadOnlySet<string> entries, string key)
        => AllCategories()
            .Select(c => key + "." + PluralRules.SuffixFor(c))
            .Where(entries.Contains);

    private static IEnumerable<PluralCategory> AllCategories()
        => Enum.GetValues<PluralCategory>();

    /// <summary>
    /// The other direction, and the reason the catalog can be split by owner at all: <b>a Core-shaped entry
    /// nobody declares is an orphan</b>, exactly as an App entry with no <c>UiStrings</c> member is.
    ///
    /// <para>⚠ This exists because <see cref="EveryLocalizedMember_MatchesItsEnglishEntry"/> had to stop
    /// demanding a <c>UiStrings</c> property for <i>every</i> key: a Core key contains dots and therefore
    /// cannot be a C# identifier, so no member of that name can exist. ⛔ That is a PARTITION, not a
    /// relaxation — orphan protection had to keep holding on both sides of it, and this is the half that
    /// covers Core. Without it, a typo in a resource name would simply be a key nothing reads.</para>
    /// </summary>
    [Fact]
    public void EveryCoreShapedEntry_IsDeclaredByCore()
    {
        var declared = DeclaredCoreMessageKeys().ToHashSet(StringComparer.Ordinal);

        // The App partition is "has a UiStrings property"; anything else must be a declared Core key. The
        // discriminator is reflection over the real declarations, never a naming convention — a convention
        // would be a second source of truth about which layer owns a key.
        var orphans = EnglishKeys()
            .Where(k => typeof(UiStrings).GetProperty(k, BindingFlags.Public | BindingFlags.Static) is null)
            .Where(k => !declared.Contains(k))
            // ⭐ C6: a plural VARIANT is declared by its base key, not by a field of its own — Core says
            // `Query.Exec.RowsInserted` and the catalog answers with `.one` / `.other`. Stripping the
            // category suffix is the only way to ask who owns it, and it is exact rather than a
            // "contains a dot" heuristic: the suffix must be one this build's rule sets can actually
            // produce, so `Settings.Import.PayloadDamaged` is not mistaken for a variant of
            // `Settings.Import`.
            .Where(k => BaseKeyOf(k) is not { } b || !declared.Contains(b))
            .Where(k => !MechanismMetadataKeys.Contains(k))
            .ToList();

        Assert.True(orphans.Count == 0,
            "These resource entries belong to neither partition — no UiStrings property and no MessageKey "
            + "declared in Core/Firebird: " + string.Join(", ", orphans));
    }

    /// <summary>
    /// Entries that are the MECHANISM's own data rather than anybody's message.
    ///
    /// <para>⚠ A named exception with its reason written down, the shape C4b's <c>NoMigrationStep</c>
    /// established. <c>Localization.PluralRuleSet</c> holds a rule-set name, not a sentence, so it can have
    /// neither a <c>UiStrings</c> property (it is not user-visible text) nor a <c>MessageKey</c> (Core must
    /// not know that plural rules exist). It lives in the catalog because that is the one file a translator
    /// edits, and its correctness is pinned by
    /// <see cref="EveryShippedCulture_NamesAKnownRuleSet"/>.</para>
    /// </summary>
    private static readonly HashSet<string> MechanismMetadataKeys =
        new(StringComparer.Ordinal) { PluralRules.RuleSetKey };

    /// <summary>The key a category variant belongs to, or null when the entry is not a variant.</summary>
    private static string? BaseKeyOf(string entry)
    {
        foreach (var category in AllCategories())
        {
            var suffix = "." + PluralRules.SuffixFor(category);
            if (entry.EndsWith(suffix, StringComparison.Ordinal))
            {
                return entry[..^suffix.Length];
            }
        }

        return null;
    }

    // ── Etap C6 — the plural mechanism ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Each rule set puts every count in the category its grammar says.</b> The Slavic band is the
    /// interesting one and the numbers are chosen to hit the part that is easy to get wrong: 12, 13 and 14
    /// end in 2/3/4 yet are <c>many</c>, while 22, 23 and 24 are <c>few</c>. Dropping that exclusion renders
    /// "12 wiersze" instead of "12 wierszy" — grammatically wrong and completely invisible to anyone reading
    /// the English build.
    /// </summary>
    /// <remarks>⚠ The expectation is the catalog SUFFIX rather than the enum, because the suffix is what a
    /// translator writes in the resource — and because <c>PluralCategory</c> is internal to the App, which a
    /// public test signature cannot name.</remarks>
    [Theory]
    // one-other: singular at exactly one, everything else plural — including zero.
    [InlineData(PluralRules.OneOther, 0, "other")]
    [InlineData(PluralRules.OneOther, 1, "one")]
    [InlineData(PluralRules.OneOther, 2, "other")]
    [InlineData(PluralRules.OneOther, 5, "other")]
    [InlineData(PluralRules.OneOther, 21, "other")]
    [InlineData(PluralRules.OneOther, 101, "other")]
    // one-few-many.
    [InlineData(PluralRules.OneFewMany, 1, "one")]
    [InlineData(PluralRules.OneFewMany, 2, "few")]
    [InlineData(PluralRules.OneFewMany, 3, "few")]
    [InlineData(PluralRules.OneFewMany, 4, "few")]
    [InlineData(PluralRules.OneFewMany, 5, "many")]
    [InlineData(PluralRules.OneFewMany, 0, "many")]
    [InlineData(PluralRules.OneFewMany, 11, "many")]
    [InlineData(PluralRules.OneFewMany, 12, "many")]   // the teen exclusion …
    [InlineData(PluralRules.OneFewMany, 13, "many")]
    [InlineData(PluralRules.OneFewMany, 14, "many")]
    [InlineData(PluralRules.OneFewMany, 21, "many")]
    [InlineData(PluralRules.OneFewMany, 22, "few")]    // … and the band it does not touch
    [InlineData(PluralRules.OneFewMany, 24, "few")]
    [InlineData(PluralRules.OneFewMany, 25, "many")]
    [InlineData(PluralRules.OneFewMany, 112, "many")]
    [InlineData(PluralRules.OneFewMany, 122, "few")]
    public void EveryRuleSet_AssignsACategoryToEveryCount(string ruleSet, long count, string expected)
        => Assert.Equal(expected, PluralRules.SuffixFor(PluralRules.CategoryFor(ruleSet, count)));

    /// <summary>
    /// <b>Every language the catalog offers declares a plural rule set this build implements.</b>
    ///
    /// <para>⭐ Arms itself off <c>PreferenceOptions.Language</c>, so adding a language extends this test
    /// automatically — the same construction as
    /// <see cref="EveryLanguageInTheCatalog_ResolvesToItsOwnCultureWithNoCodeChange"/>. Without it, a
    /// translation that forgot the declaration (or misspelt it) would silently fall back to the two-form
    /// rule and render every Slavic sentence in the wrong grammatical form, with every test green.</para>
    /// </summary>
    [Fact]
    public void EveryShippedCulture_NamesAKnownRuleSet()
    {
        foreach (var key in PreferenceOptions.Language.Values)
        {
            var culture = LanguagePreference.CultureFor(key);
            var declared = Resources.GetString(PluralRules.RuleSetKey, culture);

            Assert.True(declared is not null,
                $"Language '{key}' declares no {PluralRules.RuleSetKey}. Every culture must name its plural "
                + "grammar; the neutral set carries the default and a satellite overrides it.");
            Assert.True(PluralRules.IsKnown(declared),
                $"Language '{key}' names plural rule set '{declared}', which this build does not implement. "
                + $"Known: {string.Join(", ", PluralRules.KnownRuleSets)}.");
        }
    }

    /// <summary>
    /// <b>An unknown or missing rule set degrades; it never throws.</b>
    ///
    /// <para>⚠ Same reasoning as <c>Loc.Text</c> returning the key rather than throwing: failing to choose a
    /// grammatical form must not be the thing that ends a session. The build-time answer is
    /// <see cref="EveryShippedCulture_NamesAKnownRuleSet"/>; this is the runtime one.</para>
    /// </summary>
    [Fact]
    public void AnUnknownRuleSet_FallsBackToOther_AndNeverThrows()
    {
        Assert.Equal(PluralCategory.Other, PluralRules.CategoryFor("no-such-rule-set", 5));
        Assert.Equal(PluralCategory.Other, PluralRules.CategoryFor(null, 5));
        Assert.Equal(PluralCategory.One, PluralRules.CategoryFor(string.Empty, 1));
    }

    /// <summary>
    /// ⭐⭐ <b>The premise behind R2, pinned: no rule set is named after a language.</b>
    ///
    /// <para>A rule set names the GRAMMAR it implements, because several languages share one — French and
    /// Spanish are both <c>one-other</c>, Russian and Czech are both <c>one-few-many</c>. A set called
    /// "polish" would be false at its second consumer, and it would re-create the per-language branch
    /// <see cref="NoCode_BranchesOnAParticularLanguage"/> forbids, one layer out where that guard cannot see
    /// it: its regex looks for two-letter codes in comparisons, not for a dictionary key.</para>
    /// </summary>
    [Fact]
    public void NoRuleSet_IsNamedAfterALanguage()
    {
        var languageWord = new Regex(
            @"(?i)\b(polish|english|german|french|spanish|italian|czech|ukrainian|russian|slavic|"
            + @"pl|en|de|fr|es|it|cs|uk|ru)\b");

        foreach (var name in PluralRules.KnownRuleSets)
        {
            var hit = languageWord.Match(name);
            Assert.False(hit.Success,
                $"Plural rule set '{name}' is named after a language ('{hit.Value}'). Name it after the "
                + "grammar it implements — several languages share one rule set, so a language name is "
                + "false the moment a second one uses it.");
        }
    }

    /// <summary>
    /// ⭐ <b>A plural family is complete in every culture that ships it</b> — every category that culture's
    /// own rule set can produce has an entry.
    ///
    /// <para>⚠ This is the guard that turns "the translator forgot <c>few</c>" from a quietly wrong sentence
    /// into a red build. At run time such a gap falls back to <c>other</c>, which renders readable text in
    /// the wrong grammatical form — the failure mode nobody reading the English build can see.</para>
    /// </summary>
    [Fact]
    public void EveryPluralFamily_IsCompleteInEveryShippedCulture()
    {
        foreach (var key in PreferenceOptions.Language.Values)
        {
            var culture = LanguagePreference.CultureFor(key);
            var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: true);
            if (set is null) continue;

            var entries = set.Cast<System.Collections.DictionaryEntry>()
                .Select(e => (string)e.Key)
                .ToHashSet(StringComparer.Ordinal);

            var ruleSet = Resources.GetString(PluralRules.RuleSetKey, culture) ?? PluralRules.Fallback;
            var required = PluralRules.CategoriesOf(ruleSet)
                .Select(PluralRules.SuffixFor)
                .ToList();

            var families = entries
                .Select(BaseKeyOf)
                .Where(b => b is not null)
                .Select(b => b!)
                .Distinct(StringComparer.Ordinal)
                .Where(b => !entries.Contains(b));

            foreach (var family in families)
            {
                var missing = required.Where(c => !entries.Contains(family + "." + c)).ToList();
                Assert.True(missing.Count == 0,
                    $"Culture '{culture.Name}' declares plural family '{family}' but is missing "
                    + $"{string.Join(", ", missing)} — its rule set '{ruleSet}' can produce those categories, "
                    + "so a count landing in one would silently render the wrong form.");
            }
        }
    }

    /// <summary>
    /// ⛔ <b>A key is EITHER flat OR a family, never both.</b>
    ///
    /// <para>Two representations of one sentence are two things to keep in step, and the flat one would win
    /// only when the count is missing — so a divergence would show up for some counts and not others. That is
    /// the shape §B.11 rejected for duplicate values, applied to the same key rather than to two keys.</para>
    /// </summary>
    [Fact]
    public void NoPluralFamily_AlsoDeclaresAFlatEntry()
    {
        var english = EnglishKeys().ToHashSet(StringComparer.Ordinal);

        var both = english
            .Select(BaseKeyOf)
            .Where(b => b is not null && english.Contains(b))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(both.Count == 0,
            "These keys have BOTH a flat entry and plural variants: " + string.Join(", ", both!));
    }

    /// <summary>
    /// ⭐⭐ <b>Every view model that can re-render itself is actually ASKED to.</b>
    ///
    /// <para>A tab hangs its real content off a child view model, and the language broadcast reaches the tab
    /// only — so a child that resolved text once keeps showing the old language unless
    /// <c>WorkspaceTabViewModel.RaiseAllPropertiesChanged</c> forwards to it. That is gotcha #353, and it has
    /// now cost two etaps (C1's Session Manager, C6's exec-info panels), which is exactly the shape that
    /// deserves a guard rather than a third discovery.</para>
    ///
    /// <para>⭐ Self-arming, and that is the point: it enumerates the TYPES that declare
    /// <c>RefreshLocalizedText</c>, so the next Core module to migrate a child view model fails this test the
    /// moment it adds the method and forgets the one line. ⚠ It reads the SOURCE because the forwarding is a
    /// call, not a property — there is nothing on the object to reflect over. The C5 lesson stands: "the rule
    /// is correct" and "the wiring exists" are two different claims, and this is the second one.</para>
    /// </summary>
    [Fact]
    public void EveryViewModelThatCanRefreshItsText_IsForwardedFromTheTab()
    {
        var refreshable = typeof(UiStrings).Assembly.GetTypes()
            .Where(t => t.GetMethod(
                "RefreshLocalizedText",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                Type.EmptyTypes) is not null)
            .ToList();

        Assert.NotEmpty(refreshable);

        var tab = typeof(WorkspaceTabViewModel);
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "ViewModels", "WorkspaceTabViewModel.cs"));
        var body = source[source.IndexOf("RaiseAllPropertiesChanged()", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n    }", StringComparison.Ordinal)];

        foreach (var type in refreshable)
        {
            // Only children a tab actually owns; a refreshable view model reached some other way (a row in a
            // collection, a dialog) is not this method's business.
            var held = tab.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.PropertyType == type)
                .ToList();

            foreach (var property in held)
            {
                Assert.True(
                    body.Contains(property.Name + "?.RefreshLocalizedText()", StringComparison.Ordinal),
                    $"WorkspaceTabViewModel.RaiseAllPropertiesChanged does not forward to {property.Name} "
                    + $"({type.Name}), so its text stays in the previous language after a switch (#353).");
            }
        }
    }

    /// <summary>
    /// ⭐⭐ <b>G9 — the same rule ONE LEVEL DOWN, and etap C7 is what proved the level was missing.</b>
    ///
    /// <para>The guard above walks types held as properties of <c>WorkspaceTabViewModel</c>. It is
    /// self-arming for CHILDREN and blind to GRANDCHILDREN — and <c>PerformancePanelViewModel</c> is one:
    /// the tab owns a Procedure / Function detail view model, which owns the panel. So declaring
    /// <c>RefreshLocalizedText</c> on the panel would have armed nothing, and forgetting to forward to it
    /// would have been silent, which is exactly the failure mode #353 keeps producing.</para>
    ///
    /// <para>⛔ This is a WIDENING, not a relaxation: the premise ("a refreshable view model must actually be
    /// called") is unchanged; only the search depth grew. ⚠ Its reach is stated honestly — one level, over
    /// types a refreshable type exposes as properties. A great-grandchild would need the next widening, and
    /// this comment is where that would be noticed.</para>
    /// </summary>
    [Fact]
    public void EveryRefreshableGrandchild_IsForwarded()
    {
        var assembly = typeof(UiStrings).Assembly;
        var refreshable = assembly.GetTypes()
            .Where(t => Refreshes(t))
            .ToList();

        Assert.NotEmpty(refreshable);

        var missing = new List<string>();
        foreach (var parent in refreshable)
        {
            var file = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "ViewModels", parent.Name + ".cs");
            if (!File.Exists(file))
            {
                continue;
            }

            var source = File.ReadAllText(file);
            var marker = "RefreshLocalizedText()";
            var start = source.IndexOf("void " + marker, StringComparison.Ordinal);
            if (start < 0)
            {
                continue;
            }
            var body = source[start..];
            var end = body.IndexOf("\n    }", StringComparison.Ordinal);
            body = end > 0 ? body[..end] : body;

            foreach (var property in parent.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                         .Where(p => Refreshes(p.PropertyType) && p.PropertyType != parent))
            {
                if (!body.Contains(property.Name + "?.RefreshLocalizedText()", StringComparison.Ordinal)
                    && !body.Contains(property.Name + ".RefreshLocalizedText()", StringComparison.Ordinal))
                {
                    missing.Add($"{parent.Name}.RefreshLocalizedText does not forward to {property.Name} "
                        + $"({property.PropertyType.Name})");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "A refreshable view model owns another one and never asks it to re-read its text, so the "
            + "grandchild stays in the previous language (#353):" + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    private static bool Refreshes(Type type) => type.GetMethod(
        "RefreshLocalizedText",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        Type.EmptyTypes) is not null;

    /// <summary>
    /// Every <c>MessageKey</c> declared as a static readonly field in any assembly that produces one.
    ///
    /// <para>⚠⚠ <b>The set is the load-bearing part, and it has been wrong once already.</b> The C0 audit found
    /// this scanning only Core, so a key declared in <c>EmberTern.Firebird</c> would never have been checked;
    /// C1 added Firebird before there was a producer there. Etap C8 adds <c>EmberTern.Office</c> for the same
    /// reason and it was again latent rather than theoretical — measured before the migration, an Office key
    /// with no English entry would have sailed past <see cref="EveryCoreMessageKey_HasAnEnglishEntry"/> and put
    /// a raw identifier on screen.</para>
    ///
    /// <para>⭐ Note the ASYMMETRY that made it worth closing rather than discovering later: of the three guards
    /// that read this, two (<see cref="EveryCoreShapedEntry_IsDeclaredByCore"/> and
    /// <see cref="EveryLocalizedMember_MatchesItsEnglishEntry"/>) go RED when an assembly is missing, because
    /// its resource entries become orphans in both partitions — but the one whose failure is silent is the one
    /// that simply stops looking.</para>
    ///
    /// <para>⛔ Add the assembly in the same change that adds the producer. A type reference is used rather than
    /// a name so the list cannot point at an assembly that no longer exists.</para>
    /// </summary>
    private static IEnumerable<string> DeclaredCoreMessageKeys()
        => new[]
            {
                typeof(MessageKey).Assembly,
                typeof(Firebird.FirebirdConnectionService).Assembly,
                typeof(Office.ImportSourceMessages).Assembly,
            }
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
            .Where(f => f.FieldType == typeof(MessageKey) && f.IsInitOnly)
            .Select(f => ((MessageKey)f.GetValue(null)!).Value);

    // ── Guard 6b — new hardcoded user text cannot come back ──────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The ratchet.</b> No view may carry a literal in a user-visible attribute; everything goes through
    /// <c>{app:Loc Key}</c>. Measured over the same attribute set the readiness audit used, so the number this
    /// asserts is the number that audit reported — it went 17 → 0 and may not go back up.
    ///
    /// <para>⚠ The exemptions are values that are not language: a blank spacer column header and an example
    /// file path. Each is named, so adding a third is a decision someone has to write down.</para>
    /// </summary>
    [Fact]
    public void NoViewCarriesAHardcodedUserVisibleString()
    {
        var visible = new[]
        {
            "Text", "Header", "Content", "Title", "Watermark", "Description", "ToolTip.Tip",
            "PlaceholderText", "AutomationProperties.Name", "AutomationProperties.HelpText",
        };
        var attribute = new Regex(
            @"(?<![\w.])(" + string.Join("|", visible.Select(Regex.Escape)) + @")\s*=\s*""([^""]*)""");

        var offenders = new List<string>();
        var viewRoot = Path.Combine(RepositoryRoot(), "src", "EmberTern.App");

        foreach (var file in Directory.EnumerateFiles(viewRoot, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in attribute.Matches(lines[i]))
                {
                    var value = m.Groups[2].Value.Trim();
                    if (value.Length == 0 || value.StartsWith("{", StringComparison.Ordinal)) continue;
                    // Not language: punctuation, digits, symbols, single glyphs.
                    if (!value.Any(char.IsLetter)) continue;
                    if (value is @"C:\data\example.fdb") continue;   // an example PATH, not a sentence
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {m.Groups[1].Value}=\"{value}\"");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These views carry user-visible text as a literal instead of {app:Loc Key}, so it can never be "
            + "translated and no guard would notice:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    // ── Guard — text built in CODE follows the language too ─────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>A code-built grid column must BIND its header, never assign it.</b> An assignment captures the
    /// text once, so the column renders correctly and then keeps the language it was built in — silent, and
    /// invisible to every other guard here because the resource entry and the member are both perfectly fine.
    ///
    /// <para>⚠ The exemption is headers that come from DATA — a result grid's column names, an imported
    /// file's fields. Those are the user's own identifiers and must NOT be localized (rule #11), so the guard
    /// looks only for assignments FROM the catalog.</para>
    /// </summary>
    [Fact]
    public void NoCodeBuiltColumn_AssignsALocalizedHeader()
    {
        var offenders = new List<string>();
        var root = Path.Combine(RepositoryRoot(), "src", "EmberTern.App");

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(line, @"^Header\s*=\s*UiStrings\."))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {line}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These assign a localized header instead of binding it (use LocalizedColumn.Header or "
            + "{app:Loc} in XAML), so the header freezes in the language the control was built in:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// ⭐ <b><c>UiStrings</c> must not be captured into a field.</b> A field initialiser runs once; the whole
    /// point of the property form is that a read happens after the language change. Catches the shape
    /// <c>private readonly string _x = UiStrings.Y;</c>, which compiles, renders correctly, and is dead.
    /// </summary>
    [Fact]
    public void NoField_CapturesALocalizedString()
    {
        var offenders = new List<string>();
        var root = Path.Combine(RepositoryRoot(), "src", "EmberTern.App");

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.EndsWith("UiStrings.cs", StringComparison.Ordinal)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(line, @"(readonly|const)\s+string\s+\w+\s*=\s*UiStrings\."))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}  {line}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These capture a localized string into a field, which resolves once and then never again:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The file's lines with whole-line comments removed, so a guard measures what the compiler
    /// sees rather than what the author wrote about it.</summary>
    private static string CodeOnly(string text)
        => string.Join('\n', text.Split('\n').Where(line =>
        {
            var t = line.TrimStart();
            return !t.StartsWith("//", StringComparison.Ordinal)
                && !t.StartsWith("*", StringComparison.Ordinal)
                && !t.StartsWith("/*", StringComparison.Ordinal);
        }));

    private static IEnumerable<(string Path, string Text)> LocalizationSources()
    {
        var dir = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Localization");
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            yield return (file, File.ReadAllText(file));
        }

        var uiStrings = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "UiStrings.cs");
        yield return (uiStrings, File.ReadAllText(uiStrings));
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

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
        foreach (var key in EnglishKeys())
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
    /// ⭐ <b>A guard that arms itself.</b> Vacuous today because Core declares no keys yet (stage L4 does), and
    /// live the moment the first one appears: a key with no English entry would resolve to itself and put a
    /// raw identifier on screen.
    ///
    /// <para>⚠ Justification for adding it now rather than with L4: the failure it prevents is silent, and the
    /// convention it encodes — Core owns the KEY, App's resource file owns the WORDS — is exactly the thing a
    /// future session would otherwise have to rediscover from prose.</para>
    /// </summary>
    [Fact]
    public void EveryCoreMessageKey_HasAnEnglishEntry()
    {
        var english = EnglishKeys().ToHashSet(StringComparer.Ordinal);

        var declared = typeof(MessageKey).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic))
            .Where(f => f.FieldType == typeof(MessageKey) && f.IsInitOnly)
            .Select(f => ((MessageKey)f.GetValue(null)!).Value)
            .ToList();

        var missing = declared.Where(k => !english.Contains(k)).ToList();

        Assert.True(missing.Count == 0,
            "Core declares these message keys with no English entry in Localization/Strings.resx: "
            + string.Join(", ", missing));
    }

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

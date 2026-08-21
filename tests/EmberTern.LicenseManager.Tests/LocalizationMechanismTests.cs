using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Settings;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The localization mechanism's own guards — everything that can be decided without a window.
///
/// <para>⭐ Mirrored from EmberTern's <c>LocalizationMechanismTests</c> and narrowed to what this
/// application actually has. ⛔ The claim these CANNOT make is the one that matters most — "a bound string
/// re-reads when the language changes" — because with one shipped language a live binding and a frozen one
/// render identical text. That is measured on a real control in
/// <see cref="LocalizationLivenessTests"/>.</para>
/// </summary>
public sealed class LocalizationMechanismTests
{
    private static readonly Assembly Product = typeof(ManagerSettingsCatalog).Assembly;

    private const string ManifestName = "EmberTern.LicenseManager.Localization.Strings";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string EnglishResxPath = Path.Combine(
        RepositoryRoot, "src", "EmberTern.LicenseManager", "Localization", "Strings.resx");

    // ── The catalog loads at all ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The English resource set actually loads.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The manifest name is a STRING passed to <c>ResourceManager</c>, so a typo — or a resource routed
    /// somewhere unexpected by MSBuild — is completely silent until the first read, and then surfaces as
    /// keys on screen rather than as an error. This project has met the MSBuild half of that already
    /// (gotcha #388: `Name.pl.ext` became a satellite assembly while every build surface reported success),
    /// which is why the embedding is ASSERTED rather than trusted.
    /// </remarks>
    [Fact]
    public void TheEnglishResourceSet_Loads()
    {
        Assert.Contains(
            ManifestName + ".resources",
            Product.GetManifestResourceNames());

        var manager = new ResourceManager(ManifestName, Product);
        Assert.NotNull(manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true));

        // ⭐⭐ And the RESOLVER reaches that same catalog. Without this line the constant above is an
        //    independent second copy of the manifest name (#284): the resource could be embedded exactly
        //    as asserted while `Loc` asked for a different name and served keys forever.
        //    ⚠ Measured — injecting a typo into Loc's name leaves the two assertions above green.
        Assert.Equal("Settings", Loc.Text("Settings.WindowTitle"));
    }

    /// <summary>Every key the English base declares actually resolves through it.</summary>
    [Fact]
    public void EnglishBase_ResolvesEveryKeyItDeclares()
    {
        var manager = new ResourceManager(ManifestName, Product);

        foreach (var (key, value) in DeclaredEntries())
        {
            Assert.Equal(value, manager.GetString(key, CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The base set is not empty — an empty catalog would make every guard here vacuous.</summary>
    [Fact]
    public void TheEnglishBase_IsNotEmpty() => Assert.NotEmpty(DeclaredEntries());

    // ── The catalogs ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Every word a catalog offers actually RESOLVES — measured by reading it, not by reading the file.
    /// </summary>
    /// <remarks>
    /// <para>⚠ It asserts the REALISED value rather than what the declaration spells (#370). A member whose
    /// key is missing returns the key itself — a silent, plausible-looking string — so the assertion is
    /// that the answer is not the key, which is the only thing that distinguishes "resolved" from
    /// "fell through".</para>
    /// <para>⭐ The catalogs are DISCOVERED through <c>StringCatalogAttribute</c>, so a new one is swept
    /// without editing this test — a hand-written list is a second copy of a fact and goes stale silently.</para>
    /// </remarks>
    [Fact]
    public void EveryCatalogMember_ResolvesToRealText()
    {
        var unresolved = new List<string>();

        foreach (var catalog in Catalogs())
        {
            var prefix = catalog.GetCustomAttribute<StringCatalogAttribute>()!.KeyPrefix;

            foreach (var member in WordProperties(catalog))
            {
                var key = prefix + member.Name;
                var value = (string?)member.GetValue(null);

                if (string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal))
                {
                    unresolved.Add($"{catalog.Name}.{member.Name} → '{key}'");
                }
            }
        }

        Assert.True(
            unresolved.Count == 0,
            "These catalog members did not resolve — the key is missing from Strings.resx, so the "
            + "application would render the key itself:\n  " + string.Join("\n  ", unresolved));
    }

    /// <summary>⭐ The discovery is asserted, or an empty sweep would pass for the wrong reason.</summary>
    [Fact]
    public void TheCatalogs_AreActuallyFound()
    {
        var found = Catalogs().Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        // ⚠ Updated deliberately by L8.2, which added the two catalogs the message strip and the
        //   confirmation dialog resolve through. ⛔ This list is a TRIPWIRE, not bookkeeping: a new catalog
        //   must fail here and be added on purpose, because a catalog nobody swept is a catalog nobody
        //   guards.
        Assert.Equal(
            [nameof(ConfirmCatalog), nameof(ManagerSettingsCatalog), nameof(StatusCatalog)],
            found);

        Assert.NotEmpty(WordProperties(typeof(ManagerSettingsCatalog)));
        Assert.NotEmpty(KeyProperties(typeof(StatusCatalog)));
        Assert.NotEmpty(KeyProperties(typeof(ConfirmCatalog)));
    }

    /// <summary>
    /// ⭐⭐ Every <see cref="MessageKey"/> a catalog offers names an entry that ACTUALLY EXISTS.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>This is the guard L8.2 could not have shipped without, and the reason is a trap the
    /// design walks straight past.</b> <see cref="Loc.Text"/> answers a missing key with THE KEY ITSELF.
    /// Had the migration left a sentence sitting where a key belongs — or had a key been mistyped — the
    /// catalog would have returned that text and the window would have rendered it perfectly. Every
    /// existing assertion would have stayed green while nothing was localized at all.</para>
    /// <para>⭐ So the question asked here is not "does it resolve to something", it is "is there an ENTRY":
    /// <see cref="Loc.Find"/> answers <see langword="null"/> for a missing key, which is the only signal
    /// that distinguishes a real translation from the fallback.</para>
    /// <para>⚠ It sweeps the catalogs by reflection, so a key added tomorrow is covered without editing
    /// this test — and <see cref="TheCatalogs_AreActuallyFound"/> stops the sweep from silently going
    /// empty.</para>
    /// </remarks>
    [Fact]
    public void EveryMessageKey_NamesARealCatalogEntry()
    {
        var missing = new List<string>();
        var swept = 0;

        foreach (var catalog in Catalogs())
        {
            var prefix = catalog.GetCustomAttribute<StringCatalogAttribute>()!.KeyPrefix;

            foreach (var member in KeyProperties(catalog))
            {
                swept++;
                var key = (MessageKey)member.GetValue(null)!;

                // ⭐ The member name IS the key's tail — a mismatch means the two are being kept in step
                //    by hand, which is the fact that goes stale (#284).
                Assert.Equal(prefix + member.Name, key.Value);

                if (Loc.Find(key.Value) is null)
                {
                    missing.Add($"{catalog.Name}.{member.Name} → '{key.Value}'");
                }
            }
        }

        Assert.True(swept > 0, "No MessageKey members were found — the sweep is measuring nothing.");

        Assert.True(
            missing.Count == 0,
            "These keys have no entry in Strings.resx. Loc.Text answers a missing key with the key "
            + "itself, so the application would render the key and no other test would notice:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>⛔ No catalog key is a field either — same reasoning as <see cref="NoCatalogWord_IsAField"/>.</summary>
    /// <remarks>
    /// ⚠ A <c>static readonly MessageKey</c> would be harmless TODAY, because a key does not move with the
    /// language. ⭐ It is still forbidden: the value of one rule for both catalogs is that no reader has to
    /// work out which kind of member they are looking at, and a <c>MessageKey</c> field is one refactor
    /// away from becoming a resolved string.
    /// </remarks>
    [Fact]
    public void NoCatalogKey_IsAField()
    {
        var fields = Catalogs()
            .SelectMany(c => c.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.FieldType == typeof(MessageKey))
                .Select(f => $"{c.Name}.{f.Name}"))
            .ToArray();

        Assert.Empty(fields);
    }

    /// <summary>⛔ Two catalogs must never share a prefix — the split exists to keep their keys apart.</summary>
    [Fact]
    public void NoTwoCatalogs_ShareAKeyPrefix()
    {
        var prefixes = Catalogs()
            .Select(c => c.GetCustomAttribute<StringCatalogAttribute>()!.KeyPrefix)
            .ToArray();

        Assert.Equal(prefixes.Length, prefixes.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// ⛔ No catalog member is a field — a <c>const</c> is inlined and a <c>static readonly</c> freezes.
    /// </summary>
    /// <remarks>
    /// ⚠ This is the guard against the failure that renders CORRECTLY: a <c>static readonly</c> resolves
    /// once, so it shows the right words in the first language and never changes again. ⭐ Only the
    /// DECLARATION distinguishes it from a correct member — the value looks identical (#284).
    /// </remarks>
    [Fact]
    public void NoCatalogWord_IsAField()
    {
        var fields = new List<string>();

        foreach (var catalog in Catalogs())
        {
            foreach (var field in catalog.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string))
                {
                    continue;
                }

                // ⚠ An IDENTIFIER is legitimately a const — a category id and an icon key are not words and
                //   do not move with the language. The rule is about members that carry TEXT, and the two
                //   are told apart by whether the catalog resolves them.
                if (field.Name.StartsWith("Category", StringComparison.Ordinal))
                {
                    continue;
                }

                fields.Add($"{catalog.Name}.{field.Name}");
            }
        }

        Assert.True(
            fields.Count == 0,
            "A localized member must be a PROPERTY. A const is inlined by the compiler and a static "
            + "readonly is resolved once and then frozen in the first language:\n  "
            + string.Join("\n  ", fields));
    }

    // ── The language comes from one place ────────────────────────────────────────────────────────────

    /// <summary>An unusable stored language falls back to the default rather than throwing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("de")]
    [InlineData("not-a-culture")]
    public void AnUnusableLanguage_FallsBackToTheDefault(string? stored)
    {
        var culture = LanguagePreference.CultureFor(stored);
        Assert.Equal(ApplicationLanguages.Default, culture.TwoLetterISOLanguageName);
    }

    /// <summary>
    /// ⭐ Every language in the catalog resolves to its OWN culture, with no code change.
    /// </summary>
    /// <remarks>
    /// ⚠ It walks the catalog rather than naming the two languages, so it extends itself the day a third
    /// row is added — which is the claim "a new language needs a row and a .resx" actually rests on.
    /// </remarks>
    [Fact]
    public void EveryLanguageInTheCatalog_ResolvesToItsOwnCulture()
    {
        foreach (var code in ApplicationLanguages.All)
        {
            Assert.Equal(code, LanguagePreference.CultureFor(code).TwoLetterISOLanguageName);
        }
    }

    /// <summary>
    /// ⛔ No code branches on a particular language.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Scanned over the SOURCE, because the shape being forbidden is a comparison the compiler
    /// happily accepts. ⭐ The catalogs and the message-language resolver legitimately mention the codes —
    /// they are the two places whose job is to know them — so the sweep excludes exactly those files and
    /// nothing else.</para>
    /// <para>⭐ The one place a language name may appear beside a word is the language PICKER's own label
    /// ("English" / "Polski"): a language is named in itself there, and that is not a branch on behaviour.</para>
    /// </remarks>
    [Fact]
    public void NoCode_BranchesOnAParticularLanguage()
    {
        var owners = new[]
        {
            "ApplicationLanguages.cs",
            "MessageLanguages.cs",
            "ManagerSettingsCatalog.cs",
            "LicenseEmailTemplates.cs",
        };

        var pattern = new Regex(
            @"(==|!=|Equals)\s*\(?\s*""(pl|en)""|""(pl|en)""\s*(==|!=)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            if (owners.Contains(Path.GetFileName(file), StringComparer.Ordinal))
            {
                continue;
            }

            if (pattern.IsMatch(CodeOf(file)))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A language must never be branched on — a code names a culture and a resource file, and "
            + "nothing else:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// ⛔ No view or view model reads the language preference.
    /// </summary>
    /// <remarks>
    /// ⭐ The language reaches the UI only as finished TEXT. A view model that read the preference would be
    /// a second place deciding what a language means, and the two would diverge — which is why
    /// <c>App.OnFrameworkInitializationCompleted</c> is the one apply site.
    /// </remarks>
    [Fact]
    public void NoViewOrViewModel_ReadsTheLanguagePreference()
    {
        var offenders = SourceFiles()
            .Where(f => f.Contains($"{Path.DirectorySeparatorChar}ViewModels{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                     || f.Contains($"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal))
            .Where(f => CodeOf(f).Contains("ManagerPreferences", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f) ?? string.Empty)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "A view or view model must never read the language preference — it receives resolved words:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>⭐ The application applies the language in exactly ONE place.</summary>
    [Fact]
    public void TheLanguage_IsAppliedInExactlyOnePlace()
    {
        var appliers = SourceFiles()
            .Where(f => CodeOf(f).Contains("Loc.Apply(", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f) ?? string.Empty)
            .ToArray();

        Assert.Equal(["App.axaml.cs"], appliers);
    }

    /// <summary>
    /// ⛔ Nothing reads the operating system's language.
    /// </summary>
    /// <remarks>
    /// ⚠ `CurrentUICulture` is the tempting default and it is wrong here: the operator's stored choice is
    /// the only source, so the application must render the same way on a Polish and an English Windows
    /// until the preference says otherwise.
    /// </remarks>
    [Fact]
    public void TheLanguage_ComesOnlyFromThePreference()
    {
        var offenders = SourceFiles()
            .Where(f => CodeOf(f).Contains("CurrentUICulture", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f) ?? string.Empty)
            .ToArray();

        Assert.Empty(offenders);
    }

    // ── Plural rules ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Every shipped culture declares a rule set this build models.</summary>
    /// <remarks>⚠ A culture that declared an unknown grammar would silently fall back to English's.</remarks>
    [Fact]
    public void EveryShippedCulture_NamesAKnownRuleSet()
    {
        var declared = DeclaredEntries()
            .Where(e => e.Key == PluralRules.RuleSetKey)
            .Select(e => e.Value)
            .ToArray();

        Assert.NotEmpty(declared);
        Assert.All(declared, set => Assert.True(PluralRules.IsKnown(set), $"Unknown rule set '{set}'."));
    }

    /// <summary>⛔ A rule set names a GRAMMAR, never a language.</summary>
    [Fact]
    public void NoRuleSet_IsNamedAfterALanguage()
    {
        foreach (var set in PluralRules.KnownRuleSets)
        {
            foreach (var code in ApplicationLanguages.All.Concat(MessageLanguages.All))
            {
                Assert.DoesNotContain(code, set, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>Every rule set assigns a category to every count it can meet.</summary>
    [Theory]
    // English: one / other.
    [InlineData(PluralRules.OneOther, 0, "other")]
    [InlineData(PluralRules.OneOther, 1, "one")]
    [InlineData(PluralRules.OneOther, 2, "other")]
    [InlineData(PluralRules.OneOther, 22, "other")]
    // Polish: one / few / many — including the teen exclusion, which is the part that is easy to drop.
    [InlineData(PluralRules.OneFewMany, 0, "many")]
    [InlineData(PluralRules.OneFewMany, 1, "one")]
    [InlineData(PluralRules.OneFewMany, 2, "few")]
    [InlineData(PluralRules.OneFewMany, 4, "few")]
    [InlineData(PluralRules.OneFewMany, 5, "many")]
    [InlineData(PluralRules.OneFewMany, 12, "many")]
    [InlineData(PluralRules.OneFewMany, 13, "many")]
    [InlineData(PluralRules.OneFewMany, 14, "many")]
    [InlineData(PluralRules.OneFewMany, 22, "few")]
    [InlineData(PluralRules.OneFewMany, 25, "many")]
    [InlineData(PluralRules.OneFewMany, 112, "many")]
    [InlineData(PluralRules.OneFewMany, 122, "few")]
    public void EveryRuleSet_AssignsTheRightCategory(string ruleSet, long count, string expected) =>
        Assert.Equal(expected, PluralRules.SuffixFor(PluralRules.CategoryFor(ruleSet, count)));

    /// <summary>An unknown rule set degrades to the fallback rather than throwing.</summary>
    [Fact]
    public void AnUnknownRuleSet_UsesTheFallback() =>
        Assert.Equal(
            PluralRules.CategoryFor(PluralRules.Fallback, 5),
            PluralRules.CategoryFor("no-such-grammar", 5));

    /// <summary>
    /// ⭐ Every plural family the catalog declares is COMPLETE for every culture that ships it.
    /// </summary>
    /// <remarks>
    /// ⚠ Self-arming: no family exists yet (the counted sentences are L8.4's), so this sweeps nothing today
    /// and starts working the moment the first family is declared. ⛔ It must not be deleted for being
    /// quiet — that is what it is for.
    /// </remarks>
    [Fact]
    public void EveryPluralFamily_IsCompleteInEveryShippedCulture()
    {
        var entries = DeclaredEntries();
        var ruleSet = entries.FirstOrDefault(e => e.Key == PluralRules.RuleSetKey).Value
            ?? PluralRules.Fallback;

        var required = PluralRules.CategoriesOf(ruleSet)
            .Select(PluralRules.SuffixFor)
            .ToArray();

        var families = entries
            .Select(e => e.Key)
            .Where(k => required.Any(s => k.EndsWith("." + s, StringComparison.Ordinal)))
            .Select(k => k[..k.LastIndexOf('.')])
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var declared = entries.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (var family in families)
        {
            foreach (var suffix in required)
            {
                if (!declared.Contains(family + "." + suffix))
                {
                    missing.Add(family + "." + suffix);
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "A counted key must declare every category its culture's grammar produces:\n  "
            + string.Join("\n  ", missing));
    }

    // ── Discovery helpers ────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<Type> Catalogs() =>
        Product.GetTypes().Where(t => t.GetCustomAttribute<StringCatalogAttribute>() is not null);

    /// <summary>Every public static string property on a catalog — i.e. its words.</summary>
    private static IReadOnlyList<PropertyInfo> WordProperties(Type catalog) =>
        catalog.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string) && p.GetMethod is not null)
            .ToList();

    /// <summary>
    /// Every public static <see cref="MessageKey"/> property on a catalog — i.e. its deferred sentences.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ A separate sweep from <see cref="WordProperties"/> ON PURPOSE, and the reason is a near miss:
    /// that helper filters on <c>typeof(string)</c>, so when L8.2 added catalogs whose members are
    /// <see cref="MessageKey"/>, every existing guard skipped them IN SILENCE — 143 keys that looked
    /// guarded and were not. ⭐ A filter that quietly matches nothing is the shape to distrust.
    /// </remarks>
    private static IReadOnlyList<PropertyInfo> KeyProperties(Type catalog) =>
        catalog.GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(MessageKey) && p.GetMethod is not null)
            .ToList();

    /// <summary>The English base's entries, read from the .resx itself rather than through the catalog.</summary>
    private static IReadOnlyList<(string Key, string Value)> DeclaredEntries() =>
        XDocument.Load(EnglishResxPath).Root!
            .Elements("data")
            .Select(d => (Key: d.Attribute("name")!.Value, Value: d.Element("value")!.Value))
            .ToList();

    /// <summary>
    /// A source file with its whole-line comments removed, so a guard about CODE is not answered by PROSE.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ Written because these guards found their own documentation: the comment on
    /// <c>App.OnFrameworkInitializationCompleted</c> says the language never comes from
    /// <c>CurrentUICulture</c>, and a raw text scan read that as a violation. ⭐ It is the same shape the
    /// repository already carries in `CharsetGuardSeamTests`, which matches a COMMENT in a csproj (§49.9)
    /// — a rule stated in prose is not the rule being broken.</para>
    /// <para>⭐ It strips ONLY lines that are entirely a comment, and deliberately nothing else. A trailing
    /// comment on a code line still gets scanned, so the error this can make is a FALSE POSITIVE — loud,
    /// and fixed by moving the comment. ⛔ A cleverer stripper that walked string literals could hide a
    /// real match, and a silent guard is worth less than none.</para>
    /// </remarks>
    private static string CodeOf(string file) =>
        string.Join(
            Environment.NewLine,
            File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "EmberTern.LicenseManager"), "*.cs",
                SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal));

    /// <summary>⚠ Walks up from the test binary rather than assuming a depth that changes with the TFM.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "EmberTern.LicenseManager.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}

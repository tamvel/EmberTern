using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Settings;
using EmberTern.LicenseManager.ViewModels;
using EmberTern.LicenseManager.Views;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The C# half of the localization contract, and the half that had no guard at all until L8.4:
/// every sentence a view model or a code-behind puts in front of the operator comes from the catalog.</b>
///
/// <para>⚠⚠ <b>These sweep the SOURCE of the user-facing files, and the scope is deliberately narrow.</b>
/// A project-wide literal ban is not possible here and pretending otherwise would produce a guard nobody
/// can keep green: L8.4's recon measured 204 sentence-shaped literals across the application, of which the
/// large majority are legitimate — SQL, JSON field names, persisted values, audit notes (which stay English
/// and invariant by `terminology.md` §4.4), e-mail templates in the CUSTOMER's language (D‑9), and the
/// English diagnostic halves L8.2 deliberately left beside a <c>MessageKey</c>. So the sweep names the
/// files whose job is to talk to the operator, and nothing else.</para>
///
/// <para>⛔ A file added to <see cref="OperatorFacingFiles"/> narrows the blind spot; a file left out of it
/// widens it silently. That is why <see cref="EveryOperatorFacingFile_Exists"/> exists — and why the
/// exemptions below are themselves guarded, the lesson `MenuStyleDriftTests` and L8.3's phantom
/// <c>NoLocKeyInXaml_IsMissingFromTheCatalog</c> both taught this repository.</para>
/// </summary>
public sealed class CSharpLocalizationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly Assembly Product = typeof(ManagerSettingsCatalog).Assembly;

    /// <summary>
    /// The files whose literals are read by the OPERATOR — view models and window code-behind.
    /// </summary>
    /// <remarks>
    /// ⭐ Named one at a time, exactly like <c>DatePresentationTests</c>'s allowlists, and for the same
    /// reason: an author has to SAY which side of the line a file is on. ⛔ Not a folder wildcard —
    /// <c>ViewModels</c> also holds the catalogs themselves, whose literals ARE keys.
    /// </remarks>
    private static readonly string[] OperatorFacingFiles =
    [
        "ViewModels/ArtifactHistoryViewModel.cs",
        "ViewModels/BatchRenewalViewModel.cs",
        "ViewModels/LicenseBrowserViewModel.cs",
        "ViewModels/SendLicenceViewModel.cs",
        "ViewModels/SettingsViewModel.cs",
        "ViewModels/ShellViewModel.cs",
        "ViewModels/StorageViewModel.cs",
        "ViewModels/UnlockViewModel.cs",
        "Views/MainWindow.axaml.cs",
        "Views/SendLicenceWindow.axaml.cs",
        "Views/SettingsWindow.axaml.cs",
        "Views/StorageWindow.axaml.cs",
        "Views/UnlockWindow.axaml.cs",
    ];

    /// <summary>
    /// ⛔ The literals that stay literals in an operator-facing file, each with the reason it is not a word.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>The exemption list is itself guarded</b> — see <see cref="EveryExemption_IsActuallyPresent"/>.
    /// A stale exemption reads as coverage while covering nothing.
    /// </remarks>
    private static readonly Dictionary<string, string> TechnicalLiterals = new(StringComparer.Ordinal)
    {
        ["{0}, payload v{1}"] =
            "A product name and a payload VERSION. 'payload v' names a wire format, not a thing to read.",
        ["yyyy-MM-dd HH:mm:ss"] =
            "A date/time FORMAT. ISO, invariant, and a technical contract (terminology.md §4.4) — pinned "
            + "by DatePresentationTests, which L8.4 was told explicitly not to disturb.",
        ["yyyy-MM-dd HH:mm"] =
            "The same format to the minute, for a backup header. Same contract.",
        ["EmberTern licence"] =
            "⚠ A default FILE NAME, not a label — it is concatenated with an extension and handed to the "
            + "save dialog as a suggestion. File names and extensions are technical contracts "
            + "(terminology.md §4.4). ⭐ It reads identically to FileType.Licence and is deliberately NOT "
            + "the same fact: one names a file, the other names a type in a picker (§56.3).",
    };

    // Matches a string literal, normal or interpolated, on one line. Whether its CONTENT is a sentence is
    // decided after the interpolations are removed — see WithoutHoles.
    private static readonly Regex SentenceLiteral =
        new(@"\$?""((?:[^""\\\r\n]|\\.)*)""", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Hole =
        new(@"\{(?!\{)(?:[^{}]|\{[^{}]*\})*\}", RegexOptions.Compiled);

    // ── The sweep ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ No sentence-shaped literal is left in a file whose job is to talk to the operator.
    /// </summary>
    [Fact]
    public void NoUserFacingCSharpLiteral_IsUnaddressed()
    {
        var offenders = new List<string>();
        var judged = 0;

        foreach (var relative in OperatorFacingFiles)
        {
            var file = Path.Combine(RepositoryRoot, "src", "EmberTern.LicenseManager", relative);

            foreach (var (line, literal) in SentenceLiterals(file))
            {
                judged++;

                if (TechnicalLiterals.ContainsKey(Normalise(literal)))
                {
                    continue;
                }

                offenders.Add($"{relative}:{line} \"{Shorten(literal)}\"");
            }
        }

        Assert.True(
            judged > 0,
            "The sweep judged nothing at all. Either the files moved or the literal pattern stopped "
            + "matching — and a guard that examines nothing passes for the wrong reason.");

        Assert.True(
            offenders.Count == 0,
            "A sentence is sitting in code where a catalog key belongs. Move the words into a "
            + "[StringCatalog] class and resolve them through Loc at the moment of display; if the literal "
            + "is a technical contract rather than language, add it to TechnicalLiterals WITH ITS REASON.\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>⛔ Every named operator-facing file still exists — a stale path is a hole in the sweep.</summary>
    [Fact]
    public void EveryOperatorFacingFile_Exists()
    {
        foreach (var relative in OperatorFacingFiles)
        {
            var file = Path.Combine(RepositoryRoot, "src", "EmberTern.LicenseManager", relative);
            Assert.True(File.Exists(file), relative + " is swept for literals but no longer exists.");
        }
    }

    /// <summary>⛔ Every exemption still matches something — a stale one reads as coverage.</summary>
    [Fact]
    public void EveryExemption_IsActuallyPresent()
    {
        var present = OperatorFacingFiles
            .SelectMany(r => SentenceLiterals(
                Path.Combine(RepositoryRoot, "src", "EmberTern.LicenseManager", r)))
            .Select(l => Normalise(l.Literal))
            .ToHashSet(StringComparer.Ordinal);

        var stale = TechnicalLiterals.Keys.Where(k => !present.Contains(k)).ToArray();

        Assert.True(
            stale.Length == 0,
            "These literals are exempted from the C# sweep but no operator-facing file contains them any "
            + "more. An exemption nobody is checking is worse than none:\n  " + string.Join("\n  ", stale));

        foreach (var (literal, reason) in TechnicalLiterals)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), literal + " is exempted without a reason.");
        }
    }

    // ── The catalogs answer for every key they name ───────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Every PUBLIC member of a catalog names a real entry — properties <b>and methods</b>.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>The gap this closes was already live before L8.4.</b>
    /// <c>EveryCatalogMember_ResolvesToRealText</c> filters on <c>typeof(string)</c> PROPERTIES, so a
    /// catalog member that takes an argument — every counted or interpolated sentence — was swept by
    /// nothing. Measured: <c>ManagerSettingsCatalog.SecurityLabel</c> resolved <c>Word("SecurityNone")</c>
    /// from a typed-out string, so neither of those two keys was guarded while both looked guarded. ⭐ A
    /// filter that quietly matches nothing is the shape to distrust — the same lesson
    /// <c>KeyProperties</c> records one layer up.</para>
    ///
    /// <para>⭐ The convention it rests on, stated positively: <b>a PUBLIC member of a catalog names a
    /// key.</b> A member that merely dispatches to other members (<c>Describe</c>, <c>SecurityLabel</c>) is
    /// <c>internal</c>, so it needs no exemption list — the accessibility IS the declaration.</para>
    ///
    /// <para>⚠ A counted key has no flat entry of its own: its family lives under <c>.one</c> / <c>.other</c>
    /// and friends, so either form counts as present here. <c>EveryPluralFamily_IsCompleteInEveryShippedCulture</c>
    /// is what checks the family is whole.</para>
    /// </remarks>
    [Fact]
    public void EveryPublicCatalogMember_NamesARealEntry()
    {
        var missing = new List<string>();
        var swept = 0;

        foreach (var catalog in Catalogs())
        {
            var prefix = catalog.GetCustomAttribute<StringCatalogAttribute>()!.KeyPrefix;

            foreach (var name in PublicKeyBearingMembers(catalog))
            {
                swept++;
                var key = prefix + name;

                if (Loc.Find(key) is null && !HasPluralFamily(key))
                {
                    missing.Add($"{catalog.Name}.{name} → '{key}'");
                }
            }
        }

        Assert.True(swept > 0, "No catalog members were found — the sweep is measuring nothing.");

        Assert.True(
            missing.Count == 0,
            "These catalog members name keys with no entry in Strings.resx. Loc.Text answers a missing "
            + "key with the key itself, so the application would render the key:\n  "
            + string.Join("\n  ", missing));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static IEnumerable<(int Line, string Literal)> SentenceLiterals(string file)
    {
        var line = 0;

        foreach (var raw in File.ReadLines(file))
        {
            line++;

            // ⚠ Whole-line comments only, exactly like LocalizationMechanismTests.CodeOf: a rule stated in
            //   prose is not the rule being broken, and a cleverer stripper could hide a real match.
            if (raw.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in SentenceLiteral.Matches(raw))
            {
                var literal = match.Groups[1].Value;

                // ⭐⭐ The words are what SURVIVES removing the interpolations. Judging the raw literal
                //    instead reported `$"{a.ToString(Format, Culture)} → "` as English, because the
                //    EXPRESSION inside the hole has spaces and letters — three false positives, all of
                //    them shapes that contain no word at all. ⛔ A structural fix, not three exemptions.
                var words = WithoutHoles(literal);

                if (words.Contains(' ', StringComparison.Ordinal) && words.Count(char.IsLetter) >= 2)
                {
                    yield return (line, literal);
                }
            }
        }
    }

    // Interpolation holes carry expressions, which are not words — normalising them lets one exemption
    // stand for one SHAPE rather than for one call site.
    private static string Normalise(string literal)
    {
        var index = 0;
        return Hole.Replace(literal, _ => "{" + index++ + "}");
    }

    // What is left when every interpolation is taken out — i.e. the part a translator would be handed.
    private static string WithoutHoles(string literal) => Hole.Replace(literal, string.Empty);

    private static bool HasPluralFamily(string key) =>
        Enum.GetValues<PluralCategory>()
            .Select(PluralRules.SuffixFor)
            .Any(suffix => Loc.Find(key + "." + suffix) is not null);

    private static IEnumerable<string> PublicKeyBearingMembers(Type catalog)
    {
        foreach (var property in catalog.GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.PropertyType == typeof(string))
            {
                yield return property.Name;
            }
        }

        foreach (var method in catalog.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType == typeof(string) && method.GetParameters().Length > 0)
            {
                yield return method.Name;
            }
        }
    }

    private static IEnumerable<Type> Catalogs() =>
        Product.GetTypes().Where(t => t.GetCustomAttribute<StringCatalogAttribute>() is not null);

    private static string Shorten(string value) =>
        value.Length <= 70 ? value : value[..70] + "…";

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

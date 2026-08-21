using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The XAML half of the localization contract: every word the markup shows comes from the catalog,
/// and every key the markup names exists.</b>
///
/// <para>⚠⚠ <b>These guards scan the SOURCE, not the rendered window, and that is deliberate.</b> A test
/// that reads text off a realised control answers <i>"does this say the right thing today"</i>; these
/// answer <i>"can this ever say the wrong thing"</i>. A literal left behind in XAML renders perfectly in
/// English and is invisible until somebody switches language — exactly the failure the whole of L8
/// exists to remove.</para>
///
/// <para>⚠ Both sweeps carry the two false positives L8.2's checkpoint measured and recorded
/// (`design/licensing-system.md` §55.12): <c>SizeToContent="Height"</c> ends in <c>Content</c> and must not
/// be read as one, and one match lived inside an XML comment. ⛔ A sweep that trusts a bare attribute-name
/// match reports both.</para>
/// </summary>
public sealed class XamlLocalizationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>The attributes that put words in front of a person.</summary>
    /// <remarks>
    /// ⚠ Adding a word-bearing attribute to a view without adding it here silently widens the blind spot.
    /// ⭐ The list is short because Avalonia's word-bearing surface is small; it is not a sample.
    /// </remarks>
    private static readonly string[] WordAttributes =
    [
        "Text", "Content", "Watermark", "ToolTip.Tip", "Header", "Title", "PlaceholderText",
    ];

    /// <summary>
    /// ⛔ The literals that stay literals: BRANDING.
    /// </summary>
    /// <remarks>
    /// <para>⭐ <c>terminology.md</c> §4.4 — a brand is a technical contract, not a word: the application is
    /// called <c>EmberTern License Manager</c> in every language, and a translated product name would be a
    /// different product. ⚠ The <c>EmberTern</c> entry is the sender-name field's placeholder, which shows
    /// the brand as an example.</para>
    /// <para>⚠⚠ <b>The exemption list is itself guarded</b> — see
    /// <see cref="EveryBrandingExemption_IsActuallyPresent"/>. A stale exemption reads as coverage while
    /// covering nothing, which is the trap <c>MenuStyleDriftTests</c> already taught this repository.</para>
    /// </remarks>
    private static readonly string[] BrandingLiterals =
    [
        "EmberTern License Manager",
        "EmberTern",
    ];

    // ── The two sweeps ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ No user-visible XAML literal is left unaddressed.
    /// </summary>
    [Fact]
    public void NoUserFacingXamlLiteral_IsUnaddressed()
    {
        var offenders = new List<string>();
        var judged = 0;

        foreach (var file in XamlFiles())
        {
            foreach (var (attribute, value) in LiteralWordAttributes(file))
            {
                judged++;

                if (BrandingLiterals.Contains(value, StringComparer.Ordinal))
                {
                    continue;
                }

                offenders.Add($"{Path.GetFileName(file)}: {attribute}=\"{Shorten(value)}\"");
            }
        }

        Assert.True(
            judged > 0,
            "The sweep judged nothing at all. Either the XAML moved or the attribute list stopped "
            + "matching — and a guard that examines nothing passes for the wrong reason.");

        Assert.True(
            offenders.Count == 0,
            "These XAML values are still literals. They render correctly in English and become visible "
            + "only when somebody switches language:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// ⭐⭐ Every <c>{lm:Loc Key}</c> in the XAML names an entry that exists.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>This guard was PROMISED and did not exist.</b> <c>LocMarkup</c>, <c>Loc</c> and
    /// <c>LocalizationSource</c> all cite it by name as the thing that compensates for
    /// <c>{lm:Loc}</c> losing the compile-time key check that <c>{x:Static}</c> had — three documents
    /// describing coverage that was never written (gotcha #370's shape, met again). ⭐ A missing key is
    /// otherwise completely silent: <c>LocalizedString.Value</c> falls back to the KEY, so the window shows
    /// <c>Main.Custmoers</c> and nothing fails.
    /// </remarks>
    [Fact]
    public void NoLocKeyInXaml_IsMissingFromTheCatalog()
    {
        var declared = CatalogKeys();
        var missing = new List<string>();
        var checkedKeys = 0;

        foreach (var file in XamlFiles())
        {
            var markup = new Regex(@"\{\s*lm:Loc\s+([^}\s]+)\s*\}", RegexOptions.CultureInvariant);

            foreach (Match match in markup.Matches(CodeOf(file)))
            {
                checkedKeys++;
                var key = match.Groups[1].Value;

                if (!declared.Contains(key))
                {
                    missing.Add($"{Path.GetFileName(file)}: {key}");
                }
            }
        }

        Assert.True(checkedKeys > 0, "No {lm:Loc} usage was found — the sweep is measuring nothing.");

        Assert.True(
            missing.Count == 0,
            "These keys are used in XAML but are not in Strings.resx. The binding falls back to the KEY, "
            + "so the window would render the key itself with no error anywhere:\n  "
            + string.Join("\n  ", missing));
    }

    /// <summary>
    /// ⛔ Every branding exemption is really present in the XAML.
    /// </summary>
    /// <remarks>
    /// ⚠ Without this, an exemption outlives the literal it excused and quietly widens the hole it was
    /// meant to be a narrow exception to.
    /// </remarks>
    [Fact]
    public void EveryBrandingExemption_IsActuallyPresent()
    {
        var present = XamlFiles()
            .SelectMany(LiteralWordAttributes)
            .Select(pair => pair.Value)
            .ToHashSet(StringComparer.Ordinal);

        var stale = BrandingLiterals.Where(b => !present.Contains(b)).ToArray();

        Assert.True(
            stale.Length == 0,
            "These branding exemptions no longer match any literal in the XAML — remove them rather than "
            + "leaving them to excuse something that is not there:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// ⭐ The markup extension is usable wherever it is used — the namespace is declared.
    /// </summary>
    /// <remarks>
    /// ⚠ Avalonia would fail to load such a view at RUN TIME, not at build time, so a missing
    /// <c>xmlns:lm</c> is a window that throws when it is opened. Two of the five views did not have it
    /// before L8.3 (L8.1 declared it in three and nothing consumed it).
    /// </remarks>
    [Fact]
    public void EveryViewUsingTheMarkup_DeclaresItsNamespace()
    {
        var offenders = XamlFiles()
            .Where(f => CodeOf(f).Contains("lm:Loc", StringComparison.Ordinal))
            .Where(f => !File.ReadAllText(f).Contains("xmlns:lm=", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f) ?? string.Empty)
            .ToArray();

        Assert.Empty(offenders);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The literal, word-bearing attribute values of one XAML file.
    /// </summary>
    /// <remarks>
    /// <para>⚠ <b>`(?&lt;![\w.])` is load-bearing</b>: without it <c>SizeToContent="Height"</c> matches
    /// <c>Content=</c> and the guard reports a literal that is a layout mode. Measured — it was one of two
    /// false positives in the pre-migration count (§55.12).</para>
    /// <para>⚠ A value that starts with <c>{</c> is already a binding or markup extension; a value that is
    /// only digits and punctuation is a metric, not a word.</para>
    /// </remarks>
    private static IEnumerable<(string Attribute, string Value)> LiteralWordAttributes(string file)
    {
        var text = CodeOf(file);

        foreach (var attribute in WordAttributes)
        {
            var pattern = @"(?<![\w.])" + Regex.Escape(attribute) + @"\s*=\s*""([^""]*)""";

            foreach (Match match in Regex.Matches(text, pattern, RegexOptions.CultureInvariant))
            {
                var value = match.Groups[1].Value;

                if (string.IsNullOrWhiteSpace(value) ||
                    value.StartsWith('{') ||
                    Regex.IsMatch(value, @"^[\d\s.,]+$"))
                {
                    continue;
                }

                yield return (attribute, System.Net.WebUtility.HtmlDecode(value));
            }
        }
    }

    /// <summary>The file with XML comments removed, so prose cannot answer a question about markup (#396).</summary>
    private static string CodeOf(string file) =>
        Regex.Replace(File.ReadAllText(file), "(?s)<!--.*?-->", string.Empty);

    private static HashSet<string> CatalogKeys() =>
        XDocument
            .Load(Path.Combine(
                RepositoryRoot, "src", "EmberTern.LicenseManager", "Localization", "Strings.resx"))
            .Root!
            .Elements("data")
            .Select(d => d.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> XamlFiles() =>
        Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "src", "EmberTern.LicenseManager"), "*.axaml",
                SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal));

    private static string Shorten(string value) =>
        value.Length <= 60 ? value : value[..57] + "...";

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

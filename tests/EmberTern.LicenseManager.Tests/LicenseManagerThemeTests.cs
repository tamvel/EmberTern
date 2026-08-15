using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>CLAUDE.md's UI Review Checklist, as far as a machine can check it.</b>
///
/// <para>The checklist has eleven items. Seven of them are facts about source text and are asserted here;
/// four are judgements about how something looks and belong to a human in front of the running
/// application. Writing the seven down means the human only has to do the four — and means the seven
/// cannot quietly stop being true a year from now.</para>
///
/// <para>⭐ <b>The most important of them is <see cref="EveryBrushExistsInBothThemeDictionaries"/>.</b>
/// <c>{DynamicResource}</c> does NOT throw on a missing key — the property silently keeps its default —
/// so a token that exists in Dark and not in Light produces a window that looks fine in the theme the
/// developer happened to be using and wrong in the other one, with a green build either way. That is
/// exactly why EmberTern has <c>DesignTokenApplicationTests</c>, and this is its counterpart.</para>
/// </summary>
public sealed class LicenseManagerThemeTests
{
    private static readonly string Root = RepositoryRoot();
    private static readonly string AppFolder = Path.Combine(Root, "src", "EmberTern.LicenseManager");
    private static readonly string ThemeFolder = Path.Combine(Root, "src", "EmberTern.App", "Themes");

    private static IEnumerable<string> Markup() =>
        Directory.EnumerateFiles(AppFolder, "*.axaml", SearchOption.AllDirectories);

    private static IEnumerable<string> Code() =>
        Directory.EnumerateFiles(AppFolder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));

    /// <summary>
    /// The file's CODE, with comments removed.
    ///
    /// <para>⚠⚠ <b>Load-bearing, and learned the hard way in this very stage.</b> The first version of
    /// these guards scanned raw text, and <c>NothingDefinesAColourLocally</c> failed on the sentence in
    /// <c>LicenseManagerStyles.axaml</c> that FORBIDS a local <c>&lt;SolidColorBrush&gt;</c>. ⭐ A guard
    /// that fires on the documentation of its own rule is a guard that gets suppressed, and a suppressed
    /// guard is worse than none — it reads as coverage while providing none.</para>
    /// </summary>
    private static string ReadMarkup(string path) => Regex.Replace(
        File.ReadAllText(path), @"<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string ReadCode(string path) => Regex.Replace(
        Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
        @"//.*$", string.Empty, RegexOptions.Multiline);

    [Fact]
    public void NoMarkupCarriesAHardcodedColour()
    {
        var offenders = new List<string>();

        foreach (var file in Markup())
        {
            foreach (Match match in Regex.Matches(ReadMarkup(file), @"#[0-9A-Fa-f]{6,8}\b"))
            {
                offenders.Add($"{Path.GetRelativePath(Root, file)}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0, "Hex colours in markup: " + string.Join("; ", offenders));
    }

    [Fact]
    public void NoMarkupPaintsWithANamedColour()
    {
        // ⭐ Transparent is the one allowed literal: it is "no fill", not a theme colour.
        var offenders = new List<string>();
        var pattern = new Regex(
            @"(Background|Foreground|BorderBrush|Fill|Stroke)\s*=\s*""(?!\{|Transparent"")([A-Za-z]+)""");

        foreach (var file in Markup())
        {
            foreach (Match match in pattern.Matches(ReadMarkup(file)))
            {
                offenders.Add($"{Path.GetRelativePath(Root, file)}: {match.Value}");
            }
        }

        Assert.True(offenders.Count == 0, "Named colours in markup: " + string.Join("; ", offenders));
    }

    [Fact]
    public void NothingDefinesAColourLocally()
    {
        var offenders = new List<string>();

        foreach (var file in Markup())
        {
            var text = ReadMarkup(file);
            if (text.Contains("<SolidColorBrush", StringComparison.Ordinal) ||
                Regex.IsMatch(text, @"<Color\b"))
            {
                offenders.Add(Path.GetRelativePath(Root, file));
            }
        }

        foreach (var file in Code())
        {
            var text = ReadCode(file);
            foreach (var token in new[] { "new SolidColorBrush", "Color.Parse", "Brushes.", "Colors." })
            {
                if (text.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(Root, file)}: {token}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Local colour definitions: " + string.Join("; ", offenders));
    }

    [Fact]
    public void EveryBrushExistsInBothThemeDictionaries()
    {
        // ⚠⚠ The silent one. {DynamicResource} does not throw on a missing key, so a brush defined only
        //    in Dark leaves Light rendering a default nobody chose — green build, wrong window.
        var colours = File.ReadAllText(Path.Combine(ThemeFolder, "Colors.axaml"));
        var used = UsedResourceKeys().Where(k => k.EndsWith("Brush", StringComparison.Ordinal)).ToList();

        var missing = used
            .Where(key => Regex.Matches(colours, $@"x:Key=""{Regex.Escape(key)}""").Count < 2)
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0,
            "Brushes not defined in BOTH Dark and Light: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryResourceKeyUsedIsDefinedSomewhereInTheLinkedTokenLayer()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { "Colors.axaml", "Tokens.axaml", "Typography.axaml", "FluentBridge.axaml" })
        {
            foreach (Match match in Regex.Matches(
                         File.ReadAllText(Path.Combine(ThemeFolder, file)), @"x:Key=""([^""]+)"""))
            {
                defined.Add(match.Groups[1].Value);
            }
        }

        var missing = UsedResourceKeys().Where(key => !defined.Contains(key)).Distinct().ToList();

        Assert.True(missing.Count == 0, "Undefined resource keys: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoMetricIsWrittenAsALiteralWhereATokenNamesIt()
    {
        // Spacing, control heights, font sizes, radii and border widths all have roles in the linked
        // token files (CLAUDE.md UI rule 9). A literal here is either a token that should exist or a
        // token that already does.
        //
        // ⚠⚠ BOTH SPELLINGS, and the second one is the one that matters. The first version of this guard
        //    matched only the attribute form (Padding="8") — and a deliberately injected FontSize="17"
        //    sailed straight through, because inside a STYLE file a metric is written
        //    <Setter Property="FontSize" Value="17" />. A guard aimed at the form the offence does not
        //    take is a guard that is always green.
        //
        // ⭐ Two exemptions, each with a reason rather than a shrug:
        //    · ZERO is "none", not a measurement — the same standing that Transparent has among colours.
        //      A token named "no border" would be ceremony.
        //    · MinWidth / MinHeight are NOT listed. On a control they would be a height token; on a
        //      Window they are the smallest usable size of a layout, which no token names. Flagging both
        //      to catch one would train the reader to ignore this test, and the specific case CLAUDE.md
        //      actually warns about is already covered by TheBaseButtonStyleCarriesNoSize.
        var offenders = new List<string>();
        const string Roles = "Padding|Margin|Spacing|CornerRadius|BorderThickness|FontSize";
        var pattern = new Regex(
            $@"(?:\b(?:{Roles})\s*=\s*""(?<v>[0-9][^""]*)"")" +
            $@"|(?:Property\s*=\s*""(?:{Roles})""\s+Value\s*=\s*""(?<v>[0-9][^""]*)"")");

        foreach (var file in Markup())
        {
            foreach (Match match in pattern.Matches(ReadMarkup(file)))
            {
                if (IsNothing(match.Groups["v"].Value))
                {
                    continue;
                }

                offenders.Add($"{Path.GetRelativePath(Root, file)}: {match.Value.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, "Literal metrics: " + string.Join("; ", offenders));
    }

    [Fact]
    public void TheBaseButtonStyleCarriesNoSize()
    {
        // ⭐ CLAUDE.md UI rule 10: a control's size comes from its CONTEXT, never from its variant. In
        //    EmberTern that exact setter grew the metadata tree's expander arrow from 20 px to 100 px.
        var styles = ReadMarkup(Path.Combine(AppFolder, "Themes", "LicenseManagerStyles.axaml"));

        var baseButton = Regex.Match(
            styles, @"<Style Selector=""Button"">(.*?)</Style>", RegexOptions.Singleline);

        Assert.True(baseButton.Success, "The base Button style has gone missing.");
        Assert.DoesNotContain("MinHeight", baseButton.Groups[1].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth", baseButton.Groups[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLinkedTokenFilesAreLinkedAndNotCopied()
    {
        // ⭐ One source of every colour, for two applications. A copy drifts on the first palette change,
        //    and the drift is invisible until someone puts the two windows side by side.
        var project = File.ReadAllText(Path.Combine(AppFolder, "EmberTern.LicenseManager.csproj"));

        foreach (var file in new[] { "Colors", "Tokens", "Typography", "FluentBridge" })
        {
            Assert.Contains($@"..\EmberTern.App\Themes\{file}.axaml", project, StringComparison.Ordinal);
            Assert.False(
                File.Exists(Path.Combine(AppFolder, "Themes", $"{file}.axaml")),
                $"{file}.axaml has been COPIED into the License Manager. It must stay linked.");
        }
    }

    // "0", "0,0", "0 0 0 0" — an absence expressed in the only way the markup can express it.
    private static bool IsNothing(string value) =>
        value.All(c => c is '0' or ',' or ' ' or '.');

    private static IEnumerable<string> UsedResourceKeys()
    {
        foreach (var file in Markup())
        {
            foreach (Match match in Regex.Matches(
                         ReadMarkup(file), @"\{DynamicResource\s+([^}]+)\}"))
            {
                yield return match.Groups[1].Value.Trim();
            }
        }
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

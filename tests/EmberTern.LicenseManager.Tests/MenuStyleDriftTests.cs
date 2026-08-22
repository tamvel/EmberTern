using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The License Manager's menu appearance must not drift from EmberTern's.</b>
///
/// <para>The menu block in <c>src/EmberTern.App/Themes/ControlStyles.axaml</c> is REPRODUCED in
/// <c>src/EmberTern.LicenseManager/Themes/MenuStyles.axaml</c> rather than linked (decision D‑7 = B, taken
/// to keep <c>EmberTern.App</c> closed during L6.1a). The project's own answer to that problem twice —
/// <c>IconGeometries.axaml</c> and <c>DataGridStyles.axaml</c> — was to split and link, precisely because
/// a copy drifts. ⛔ This is what stands in for the link.</para>
///
/// <para><b>It fails in three directions, and all three matter:</b></para>
/// <list type="number">
/// <item>a setter reproduced here disagrees with EmberTern's — the copy has drifted;</item>
/// <item>EmberTern's block gains a selector this application has not decided about — a new menu rule
/// exists in the product and nobody chose whether it applies here;</item>
/// <item>this file invents a selector EmberTern does not have — a local menu rule, i.e. the beginning of
/// two menu appearances.</item>
/// </list>
///
/// <para>⚠⚠ <b>The comparison resolves TOKENS before comparing</b>, because two of EmberTern's literals
/// are written here as the tokens that already name them — <c>Pad.MenuItem</c> IS <c>10,3</c> and
/// <c>Border.All</c> IS <c>1</c>. A textual diff would fail on a difference that is not one. ⭐ And it
/// still catches the case that matters: if EmberTern ever changes <c>Pad.MenuItem</c>'s value, its own
/// literal stops agreeing with its own token and this goes red — which is the correct answer, because
/// this application follows the token and the product would no longer be following it.</para>
/// </summary>
public sealed class MenuStyleDriftTests
{
    /// <summary>
    /// ⛔ Deliberately NOT reproduced, with the reason. A selector listed here is one somebody decided
    /// about; a selector in neither list is one nobody has.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> DeliberatelyOmitted =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MenuItem /template/ TextBlock#PART_InputGestureText"] =
                "Styles the keyboard-gesture column. The License Manager has no command registry, no "
                + "gesture vocabulary and no menu row that shows one — copying it would be carrying "
                + "chrome for a surface that does not exist (the dead-surface trap, gotcha #233).",
        };

    // ⭐ The block is delimited by its first and last selector rather than by line numbers: line numbers
    //   in another project's file are exactly the kind of derived fact that goes stale silently (#284).
    private const string FirstSelector = "ContextMenu";
    private const string LastSelector = "ContextMenu > Separator, MenuItem > Separator";

    [Fact]
    public void EveryReproducedSetterAgreesWithEmberTerns()
    {
        var product = MenuStylesOf(ProductStyles());
        var manager = MenuStylesOf(ManagerMenuStyles());
        var tokens = Tokens();

        var differences = new List<string>();

        foreach (var (selector, expected) in product)
        {
            if (DeliberatelyOmitted.ContainsKey(selector))
            {
                continue;
            }

            if (!manager.TryGetValue(selector, out var actual))
            {
                continue; // reported by the next test, with a better message
            }

            foreach (var (property, productValue) in expected)
            {
                if (!actual.TryGetValue(property, out var managerValue))
                {
                    differences.Add($"{selector}: EmberTern sets {property}, the License Manager does not");
                    continue;
                }

                var a = Resolve(productValue, tokens);
                var b = Resolve(managerValue, tokens);

                if (!string.Equals(a, b, StringComparison.Ordinal))
                {
                    differences.Add(
                        $"{selector}.{property}: EmberTern = '{productValue}' ({a}), " +
                        $"License Manager = '{managerValue}' ({b})");
                }
            }

            foreach (var property in actual.Keys.Where(k => !expected.ContainsKey(k)))
            {
                differences.Add(
                    $"{selector}: the License Manager sets {property}, EmberTern does not");
            }
        }

        Assert.True(
            differences.Count == 0,
            "The reproduced menu appearance has drifted from EmberTern's:\n  "
            + string.Join("\n  ", differences));
    }

    [Fact]
    public void NeitherSideHasASelectorTheOtherHasNotDecidedAbout()
    {
        var product = MenuStylesOf(ProductStyles());
        var manager = MenuStylesOf(ManagerMenuStyles());

        var undecided = product.Keys
            .Where(s => !manager.ContainsKey(s) && !DeliberatelyOmitted.ContainsKey(s))
            .Select(s => $"EmberTern has '{s}'; this application neither reproduces nor omits it")
            .ToList();

        undecided.AddRange(manager.Keys
            .Where(s => !product.ContainsKey(s))
            .Select(s => $"'{s}' exists only here — a local menu rule, i.e. a second menu appearance"));

        Assert.True(
            undecided.Count == 0,
            "The two menu style sets no longer describe the same thing:\n  "
            + string.Join("\n  ", undecided));
    }

    /// <summary>
    /// ⭐ An omission stays a DECISION rather than becoming a hiding place: every entry must still name a
    /// selector EmberTern actually has. A stale one would silently excuse a selector nobody looked at.
    /// </summary>
    [Fact]
    public void EveryDeliberateOmissionStillNamesARealEmberTernSelector()
    {
        var product = MenuStylesOf(ProductStyles());

        var stale = DeliberatelyOmitted.Keys.Where(s => !product.ContainsKey(s)).ToList();

        Assert.True(
            stale.Count == 0,
            "These omissions name selectors EmberTern no longer has — remove them:\n  "
            + string.Join("\n  ", stale));
    }

    /// <summary>⚠ A positive control: the extraction must actually find the block, or every test above
    /// passes over an empty set (#378).</summary>
    [Fact]
    public void TheExtractionFindsARealBlockOnBothSides()
    {
        var product = MenuStylesOf(ProductStyles());
        var manager = MenuStylesOf(ManagerMenuStyles());

        Assert.True(product.Count >= 10, $"Only {product.Count} menu styles found in EmberTern's file.");
        Assert.True(manager.Count >= 10, $"Only {manager.Count} menu styles found in this application's.");
        Assert.Contains("MenuItem:disabled", product.Keys);
        Assert.Contains("MenuItem:disabled", manager.Keys);
    }

    // ── Reading ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Selector → (property → value), for the menu block only.</summary>
    private static Dictionary<string, Dictionary<string, string>> MenuStylesOf(string markup)
    {
        var styles = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var inBlock = false;

        foreach (Match style in Regex.Matches(
                     markup, @"<Style\s+Selector=""(?<sel>[^""]+)"">(?<body>.*?)</Style>", RegexOptions.Singleline))
        {
            var selector = style.Groups["sel"].Value.Trim();

            if (selector == FirstSelector)
            {
                inBlock = true;
            }

            if (!inBlock)
            {
                continue;
            }

            var setters = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match setter in Regex.Matches(
                         style.Groups["body"].Value,
                         @"<Setter\s+Property=""(?<p>[^""]+)""\s+Value=""(?<v>[^""]*)""\s*/>"))
            {
                setters[setter.Groups["p"].Value] = setter.Groups["v"].Value.Trim();
            }

            styles[selector] = setters;

            if (selector == LastSelector)
            {
                break;
            }
        }

        return styles;
    }

    /// <summary>Turns <c>{DynamicResource X}</c> into the value <c>Tokens.axaml</c> gives X.</summary>
    private static string Resolve(string value, IReadOnlyDictionary<string, string> tokens)
    {
        var match = Regex.Match(value, @"^\{(?:Dynamic|Static)Resource\s+(?<key>[^}]+)\}$");
        if (!match.Success)
        {
            return value;
        }

        var key = match.Groups["key"].Value.Trim();

        // ⚠ A brush or typography key has no numeric value to compare — it is compared BY KEY, which is
        //   the right question for a colour: both sides must name the same role.
        return tokens.TryGetValue(key, out var resolved) ? resolved : key;
    }

    private static IReadOnlyDictionary<string, string> Tokens()
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        var markup = File.ReadAllText(Path.Combine(Root, "src", "EmberTern.App", "Themes", "Tokens.axaml"));

        foreach (Match entry in Regex.Matches(
                     markup, @"<(?<type>\w+)\s+x:Key=""(?<key>[^""]+)"">(?<value>[^<]+)</\k<type>>"))
        {
            tokens[entry.Groups["key"].Value] = entry.Groups["value"].Value.Trim();
        }

        return tokens;
    }

    private static string ProductStyles() =>
        File.ReadAllText(Path.Combine(Root, "src", "EmberTern.App", "Themes", "ControlStyles.axaml"));

    private static string ManagerMenuStyles() =>
        File.ReadAllText(Path.Combine(Root, "src", "EmberTern.LicenseManager", "Themes", "MenuStyles.axaml"));

    private static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EmberTern.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root could not be located.");
    }
}

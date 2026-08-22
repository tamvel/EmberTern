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

        // ⭐ IconGeometries.axaml joined the linked layer in the L5.1 QA pass, when EmberTern's dictionary
        //   was split so its 86 pure geometries became linkable (the three ControlThemes that bound it to
        //   EmberTern.App's controls moved to IconControlThemes.axaml, which this project must NOT link).
        //   ⚠ Adding it here is not a widening of the rule — the rule is "every key comes from the linked
        //   layer", and the linked layer is what grew. Guarded from the other side by
        //   IconGeometriesSplitTests in EmberTern.Tests, which fails if that file stops being pure.
        foreach (var file in new[]
                 {
                     "Colors.axaml", "Tokens.axaml", "Typography.axaml", "FluentBridge.axaml",
                     "IconGeometries.axaml",
                 })
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
            var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');

            foreach (Match match in pattern.Matches(ReadMarkup(file)))
            {
                var value = match.Groups["v"].Value;

                if (IsNothing(value) || IsAllowed(relative, value))
                {
                    continue;
                }

                offenders.Add($"{relative}: {match.Value.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, "Literal metrics: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// ⭐⭐ <b>The ONE allowance list, and every entry names a file, a value and a reason.</b>
    ///
    /// <para>It exists for exactly one file. <c>Themes/MenuStyles.axaml</c> reproduces EmberTern's menu
    /// appearance (decision D‑7 = B), and this application's literal rule is STRICTER than the one
    /// EmberTern applies to its own <c>ControlStyles.axaml</c> — which has a per-file allowance table of
    /// its own — so a faithful reproduction cannot pass without one here too.</para>
    ///
    /// <para>⭐ Two of EmberTern's seven literals are NOT in this list, because they did not need to be:
    /// <c>Pad.MenuItem</c> already names <c>10,3</c> and <c>Border.All</c> already names <c>1</c>, so the
    /// reproduction writes the tokens and renders identically. ⛔ The five below have no token, and none
    /// is invented for them: a <c>Thickness</c> role created for one element would be starting the
    /// ratified spacing stage through the back door.</para>
    ///
    /// <para>⚠ Keyed by file AND value, so a sixth literal in the same file still fails. An allowance
    /// that covered a whole file would be an exemption, and an exemption is how a guard becomes
    /// decoration.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllowedLiterals =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/EmberTern.LicenseManager/Themes/MenuStyles.axaml"] =
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["4"] =
                        "ContextMenu's corner radius. A floating surface, deliberately 4 where "
                        + "Radius.Surface is 3 — EmberTern carries the identical note. ⛔ Not Radius.Chip "
                        + "or Radius.Tab: both happen to be 4 today, and borrowing an unrelated role "
                        + "because its number matches is how a role stops meaning anything.",
                    ["0,4"] =
                        "The menu's own top/bottom band, so the first and last row do not touch the "
                        + "border. No Thickness role names it.",
                    ["3,0"] =
                        "The inset of a row's highlight inside the menu's border. No role names it.",
                    ["0,0,4,0"] =
                        "The gap between the icon column and the label. No role names it.",
                    ["6,4"] =
                        "The separator's air, above and below. No role names it.",
                },
        };

    private static bool IsAllowed(string relativePath, string value) =>
        AllowedLiterals.TryGetValue(relativePath, out var allowed) && allowed.ContainsKey(value.Trim());

    /// <summary>
    /// ⭐ An allowance stays a DECISION rather than becoming a hiding place: an entry that no longer
    /// matches anything in its file is stale and must go, or it silently excuses a future literal.
    /// </summary>
    [Fact]
    public void EveryAllowedLiteralIsStillActuallyPresent()
    {
        var stale = new List<string>();

        foreach (var (relative, values) in AllowedLiterals)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"The allowance names a file that does not exist: {relative}");

            var markup = ReadMarkup(path);
            foreach (var value in values.Keys.Where(v => !markup.Contains($"\"{v}\"", StringComparison.Ordinal)))
            {
                stale.Add($"{relative}: '{value}' is allowed but no longer written there");
            }
        }

        Assert.True(stale.Count == 0, "Stale literal allowances: " + string.Join("; ", stale));
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

    [Fact]
    public void ActionGeometryIsDeclaredOnceForBOTHVariants()
    {
        // ⭐⭐ Copied from EmberTern together with its reason: `Button.primary` and `Button.flat` take
        //    their height, width floor and padding from ONE style, so a primary and a secondary action
        //    standing side by side cannot drift apart. Two separate declarations is not a duplicate to
        //    tidy — it is the drift, already written down.
        //
        // ⚠⚠ And the height must be the ACTION role, not the field role. Tokens.axaml records these as
        //    two independent ladders: `Size.Control` (24) is a control standing in a SERIES, where
        //    alignment decides; `Size.ControlProminent` (28) is a control standing ALONE that the user
        //    aims at — the dialog footer button by name. L3 shipped the dialog action on the field
        //    height, and that is what the user's review called a broken vertical rhythm.
        var styles = ReadMarkup(Path.Combine(AppFolder, "Themes", "LicenseManagerStyles.axaml"));

        var shared = Regex.Match(
            styles,
            @"<Style Selector=""Button\.primary,\s*Button\.flat"">(.*?)</Style>",
            RegexOptions.Singleline);

        Assert.True(shared.Success,
            "The shared action geometry style is gone. Height, width floor and padding for "
            + "Button.primary and Button.flat belong in ONE style — see EmberTern's ControlStyles.axaml.");
        Assert.Contains("Size.ControlProminent", shared.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains("Size.ActionMinWidth", shared.Groups[1].Value, StringComparison.Ordinal);

        foreach (var variant in new[] { "Button.primary", "Button.flat" })
        {
            var own = Regex.Match(
                styles, $@"<Style Selector=""{Regex.Escape(variant)}"">(.*?)</Style>",
                RegexOptions.Singleline);

            Assert.True(own.Success, $"The {variant} style has gone missing.");
            Assert.DoesNotContain("MinHeight", own.Groups[1].Value, StringComparison.Ordinal);
            Assert.DoesNotContain("MinWidth", own.Groups[1].Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryTypographyRoleIsConsumedCompleteWithItsLineHeight()
    {
        // ⭐⭐ Typography.axaml states it in its own header: a role is family + size + weight + LINE
        //    HEIGHT, and every role carries a `.LineHeight` for that reason. L3 consumed size and weight
        //    and left line height to the font default, so each text block sat on its own baseline grid —
        //    which is what a ragged vertical rhythm IS, and it is invisible in a screenshot of one label.
        //
        // ⚠ The guard keys on the SIZE token, because that is what a new role always brings with it: if
        //    a style reaches for `Text.X.Size`, it must also reach for `Text.X.LineHeight`.
        //
        // ⚠⚠ SCOPED TO `TextBlock` SELECTORS, and the scope is a fact about Avalonia rather than a
        //    convenience: `LineHeight` is a `TextBlock` property. `Button` and `TextBox` legitimately set
        //    a role's FontSize and have nowhere to put its line height — EmberTern's own base `Button`
        //    style does exactly that. Written as "everything except…" this guard fired on two styles that
        //    could not possibly comply, and a guard that demands the impossible is one that gets deleted.
        var styles = ReadMarkup(Path.Combine(AppFolder, "Themes", "LicenseManagerStyles.axaml"));
        var offenders = new List<string>();

        foreach (Match block in Regex.Matches(
                     styles, @"<Style Selector=""TextBlock[^""]*"">(.*?)</Style>", RegexOptions.Singleline))
        {
            var body = block.Groups[1].Value;
            foreach (Match size in Regex.Matches(body, @"Text\.(\w+)\.Size"))
            {
                var role = size.Groups[1].Value;

                // Code is the one role with no line height IN THE CATALOG — the editor owns its own
                // line spacing, and Typography.axaml says so where the role is defined.
                if (role == "Code" || body.Contains($"Text.{role}.LineHeight", StringComparison.Ordinal))
                {
                    continue;
                }

                offenders.Add($"Text.{role}.Size without Text.{role}.LineHeight");
            }
        }

        Assert.True(offenders.Count == 0,
            "A typography role consumed without its line height: " + string.Join("; ", offenders));
    }

    /// <summary>
    /// ⭐⭐ <b>The License Manager has its OWN icon — one file, referenced twice, and NEVER the product's.</b>
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>This guard used to assert the exact opposite</b>, and the reversal is the user's
    /// decision of 2026-08-22 rather than a drift. The old rule reasoned that the License Manager is the
    /// same product's admin side and not a second brand — sound, and the RESULT was wrong: two icons that
    /// are byte-identical are two applications nobody can tell apart in the taskbar, in Alt+Tab or in
    /// Explorer, and these two sit open side by side doing very different things, one of them holding the
    /// signing key.</para>
    /// <para>⭐ What survives from the old rule is its teeth, pointed the other way: the artwork is
    /// referenced from ONE place, and ⛔ the product's own <c>.ico</c> must not be reachable from here — a
    /// leftover reference to it is exactly how the two would silently become identical again.</para>
    /// </remarks>
    [Fact]
    public void TheApplicationIconIsItsOwnAndIsReferencedFromOnePlace()
    {
        var project = File.ReadAllText(Path.Combine(AppFolder, "EmberTern.LicenseManager.csproj"));

        // The Win32 icon compiled into the EXE — Explorer, file properties, taskbar button.
        Assert.Contains(
            @"<ApplicationIcon>Assets\Branding\EmberTernLicenseManager.ico</ApplicationIcon>",
            project, StringComparison.Ordinal);

        // ⛔ THE PRODUCT'S ICON IS NOT REACHABLE FROM HERE ANY MORE.
        Assert.DoesNotContain(
            @"..\EmberTern.App\Assets\Branding\EmberTern.ico", project, StringComparison.Ordinal);

        // ⭐ Exactly ONE .ico in this project, and it is the one both references name.
        var icons = Directory.EnumerateFiles(AppFolder, "*.ico", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToList();

        var icon = Assert.Single(icons);
        Assert.Equal("EmberTernLicenseManager.ico", Path.GetFileName(icon));

        // ⛔⛔ AND IT IS NOT A COPY OF THE PRODUCT'S. The whole point of the change is that the two
        //    differ; a future "one master, two applications" tidy-up would undo it silently.
        var product = Path.Combine(
            AppFolder, "..", "EmberTern.App", "Assets", "Branding", "EmberTern.ico");

        Assert.NotEqual(File.ReadAllBytes(product), File.ReadAllBytes(icon));

        // ⚠ The MASTER never ships — un-cropped, opaque background. Without the Remove it is embedded in
        //   the assembly and someone eventually references it from XAML and gets a black square.
        Assert.Contains(@"<AvaloniaResource Remove=""Assets\Branding\Masters\**"" />",
            project, StringComparison.Ordinal);

        Assert.True(
            File.Exists(Path.Combine(
                AppFolder, "Assets", "Branding", "Masters", "license-manager-icon-source.png")),
            "The master the .ico is rendered from is missing — the artwork could not be regenerated.");
    }

    /// <summary>
    /// ⭐⭐ Every entry in the shipped <c>.ico</c> is a well-formed 32-bit PNG frame of its declared size.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>Asserted by walking the payloads, and BRANDING.md says why in detail:</b> both obvious
    /// inspection routes lie about a perfectly good file. <c>Icon.ToBitmap()</c> decodes a PNG-compressed
    /// frame as a raw DIB and returns colour noise, and <c>new Icon(path, new Size(256,256))</c> hands back
    /// the 64px frame because GDI+ will not select PNG-compressed 256px entries at all. ⛔ Neither can
    /// express the assertion that matters.</para>
    /// <para>⭐ The 256px entry declares its width and height as <b>0</b> — the .ico spec's encoding of 256.
    /// Writing 256 there produces a file Windows silently ignores, which is invisible until somebody looks
    /// at a large icon view.</para>
    /// </remarks>
    [Fact]
    public void EveryIconEntryIsAWellFormedFrameOfItsDeclaredSize()
    {
        var data = File.ReadAllBytes(
            Path.Combine(AppFolder, "Assets", "Branding", "EmberTernLicenseManager.ico"));

        Assert.Equal(0, BitConverter.ToUInt16(data, 0));           // reserved
        Assert.Equal(1, BitConverter.ToUInt16(data, 2));           // type: icon

        var count = BitConverter.ToUInt16(data, 4);
        Assert.True(count >= 6, $"An icon with {count} entries cannot cover the sizes Windows asks for.");

        var sizes = new List<int>();

        for (var i = 0; i < count; i++)
        {
            var entry = 6 + (16 * i);
            var declared = data[entry] == 0 ? 256 : data[entry];

            Assert.Equal(data[entry], data[entry + 1]);            // square
            Assert.Equal(32, BitConverter.ToUInt16(data, entry + 6));

            var length = BitConverter.ToInt32(data, entry + 8);
            var offset = BitConverter.ToInt32(data, entry + 12);

            Assert.InRange(offset + length, 0, data.Length);

            // ⭐ The PNG signature, at the offset the table points at.
            Assert.Equal(0x89, data[offset]);
            Assert.Equal((byte)'P', data[offset + 1]);

            // ⭐ The decoded size equals the declared one. A PNG's IHDR width/height are big-endian at
            //   byte 16 of the stream, which is all this needs — ⛔ no image decoder, no GDI+.
            var width = (data[offset + 16] << 24) | (data[offset + 17] << 16)
                | (data[offset + 18] << 8) | data[offset + 19];
            var height = (data[offset + 20] << 24) | (data[offset + 21] << 16)
                | (data[offset + 22] << 8) | data[offset + 23];

            Assert.Equal(declared, width);
            Assert.Equal(declared, height);

            sizes.Add(declared);
        }

        // ⚠ The sizes Windows actually asks for. A missing 16 or 32 is the one nobody notices until the
        //   taskbar scales a 256 down and it turns to mud.
        Assert.Contains(16, sizes);
        Assert.Contains(32, sizes);
        Assert.Contains(256, sizes);
    }

    [Fact]
    public void TheWindowIconComesFromExactlyOneSetterAndNoWindowOverridesIt()
    {
        // ⭐ `CLAUDE.md` states this for EmberTern as a rule with a reason: a window that sets its own
        //    icon is the window that will one day be the only one without it.
        var styles = ReadMarkup(Path.Combine(AppFolder, "Themes", "LicenseManagerStyles.axaml"));

        Assert.Single(Regex.Matches(styles, @"Property=""Icon"""));
        Assert.Contains(
            "avares://EmberTern.LicenseManager/Assets/Branding/EmberTernLicenseManager.ico",
            styles, StringComparison.Ordinal);

        // ⚠⚠ NARROWED TO THE `<Window>` ELEMENT (L6.1a), and the narrowing is a REPAIR rather than a
        //    relaxation. This used to sweep each view for the raw substring "Icon=", which was the same
        //    set only for as long as no markup here used any OTHER property called Icon. The hamburger
        //    menu added `<MenuItem Icon="{lm:MenuIcon …}" />` — a different property on a different type,
        //    with nothing to do with the window's OS icon — and the guard went red while reporting
        //    nothing that was wrong.
        // ⭐ The guard's domain was always "a WINDOW must not set its own icon", so it is now bounded by
        //    that instead of by the text; it keeps its full strength (an `Icon=` on a Window opening tag
        //    still fails) and stops depending on what else a view happens to contain. Same lesson as
        //    gotcha #379: name the subject, do not describe it by what it looks like.
        foreach (var file in Markup().Where(f => f.Contains(
                     $"{Path.DirectorySeparatorChar}Views{Path.DirectorySeparatorChar}",
                     StringComparison.Ordinal)))
        {
            var opening = Regex.Match(ReadMarkup(file), @"<Window\b[^>]*>", RegexOptions.Singleline);
            if (!opening.Success)
            {
                continue;
            }

            Assert.DoesNotContain("Icon=", opening.Value, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheFirstRunScreenShowsNoStorageLocation()
    {
        // ⭐⭐ A REGRESSION GUARD FOR A DEFECT A HUMAN FOUND, WRITTEN SO A HUMAN NEVER HAS TO FIND IT
        //    AGAIN (user review, 2026-08-15). The first-run window displayed
        //    `%APPDATA%\EmberTern License Manager` under the passphrase field. It is infrastructure: it
        //    does not help anyone perform the one action the window exists for, and a path on a setup
        //    screen reads as something the operator is meant to act on.
        //
        // ⚠ The guard covers the VIEW MODEL too, not just the markup. Leaving the property behind would
        //    leave the next person one binding away from putting it back, and dead code is exactly how a
        //    removed decision comes back as an accident.
        var window = ReadMarkup(Path.Combine(AppFolder, "Views", "UnlockWindow.axaml"));
        var viewModel = ReadCode(Path.Combine(AppFolder, "ViewModels", "UnlockViewModel.cs"));

        // ⚠ Matched precisely, not by the bare word: `WindowStartupLocation` contains "Location" and is
        //    unrelated. A guard that fires on an innocent attribute is a guard that gets loosened.
        Assert.DoesNotContain("Binding Location", window, StringComparison.Ordinal);
        Assert.DoesNotContain("Files:", window, StringComparison.Ordinal);
        Assert.DoesNotContain("_paths.Root", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("public string Location", viewModel, StringComparison.Ordinal);
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

using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The one fact about the Design Token catalog that cannot be established by reading code or by building:
/// <b>a token declared in <c>Themes/Tokens.axaml</c> / <c>Themes/Typography.axaml</c> actually arrives at a
/// control.</b>
///
/// <para>⭐ Why a build proves nothing here. A <c>{DynamicResource Text.Title.Size}</c> setter compiles whether
/// or not the key exists, whether or not the dictionary is merged, and whether or not the value has a type the
/// target property accepts. An unresolved dynamic resource does not throw — the property silently keeps its
/// inherited default, so the failure looks like "this label is the wrong size on one screen", months later and
/// far from its cause. This is the same gap as gotcha #251 ("added" is not "paints") and the reason
/// <c>BrandingPresentationTests</c> exists.</para>
///
/// <para>⚠ Both layers are asserted, because they resolve through <i>different</i> mechanisms and one working
/// does not imply the other: the scalar layer (<c>x:Double</c>, <c>FontWeight</c>) and the composite layer
/// (<c>Thickness</c>, <c>CornerRadius</c>) — §3.2's two halves.</para>
///
/// <para>⚠ Deliberately the cheapest possible headless test: bare controls inside a bare <see cref="Window"/>,
/// no <c>MainWindow</c> (the documented hang-prone shape). A style setter reaching a control that has no XAML
/// and no code-behind can only have come from the application-level stylesheet — which is the property that has
/// to hold for every control M2b styles next.</para>
///
/// <para>⚠ It joins <see cref="HeadlessCollection"/> and never adds its own class fixture (gotchas
/// #94 / #226 / #286).</para>
///
/// <para>⛔ This test pins <b>that the token arrives</b>, not <b>which number it carries</b>. The values live in
/// the catalog and are expected to move (§4.2.4 — role before value); duplicating them here would create the
/// second copy the whole stage exists to remove. It therefore compares against the dictionary, not against a
/// literal.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class DesignTokenApplicationTests
{
    private readonly HeadlessUnitTestSession _session;

    public DesignTokenApplicationTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    [Fact]
    public async Task TypographyRole_ReachesAStyledTextBlock()
    {
        await _session.Dispatch(() =>
        {
            var header = new TextBlock { Text = "Group", Classes = { "group-header" } };
            var window = new Window { Content = header };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Text.SectionHeader — the role `group-header` carries. Compared against the catalog, so the
            // assertion survives a deliberate change of the value and fails only when the wiring breaks.
            Assert.Equal(Token<double>("Text.SectionHeader.Size"), header.FontSize);
            Assert.Equal(Token<FontWeight>("Text.SectionHeader.Weight"), header.FontWeight);

            // The guard that catches a silently unresolved resource: Avalonia's default TextBlock size is 12,
            // which happens to be a real token value elsewhere — so "it looks plausible" is not evidence. The
            // section-header role is 11, so a failed resolution is visible rather than coincidental.
            Assert.NotEqual(12d, header.FontSize);

            window.Close();
        }, default);
    }

    [Fact]
    public async Task CompositeToken_ReachesAStyledBorder()
    {
        await _session.Dispatch(() =>
        {
            var group = new Border { Classes = { "settings-group" } };
            var window = new Window { Content = group };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The composite layer (§3.2): XAML cannot compose a Thickness from two x:Double, so these are
            // pre-composed resources — a different resolution path from the scalars above.
            Assert.Equal(Token<Thickness>("Border.All"), group.BorderThickness);
            Assert.Equal(Token<Thickness>("Pad.Group"), group.Padding);
            Assert.Equal(Token<CornerRadius>("Radius.Surface"), group.CornerRadius);

            window.Close();
        }, default);
    }

    /// <summary>
    /// Release Blocker RB‑2, as a measurement rather than an intention: a <c>CheckBox</c> must fit inside a data
    /// grid row. Fluent's own template makes it ~43 px against a 22 px row — the specification calls that
    /// "unacceptable" (§6.4) — because it hard-codes <c>MinHeight=32</c> plus a 20×20 box and a 32 px column,
    /// and only the box is a named element.
    ///
    /// <para>⚠ This is the assertion that would have to fail before RB‑2 could come back. It is written against
    /// the catalog (<c>Size.Checkbox</c>, <c>Size.Row.Grid</c>) rather than against 14 and 22, so a deliberate
    /// change of either value keeps it meaningful instead of turning it into a number to "fix".</para>
    /// </summary>
    [Fact]
    public async Task CheckBox_FitsInsideAGridRow()
    {
        await _session.Dispatch(() =>
        {
            var bare = new CheckBox();
            var window = new Window { Content = bare };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // ⚠ DesiredSize, not Bounds — and the distinction is the defect itself. Bounds depends on the host
            // (as a Window's content the control stretches to the whole window), while DesiredSize is what the
            // control ASKS FOR, which is exactly what forces a data grid row to grow. Fluent asks for 32.
            //
            // ⚠⚠ Compared against the room a CELL leaves, not against the row height — §5.1's own arithmetic:
            // 22 px row − Pad.Cell top and bottom = 16 px of content. An earlier draft of the CheckBox template
            // compared against 22, passed at 20 px, and would still have pushed every row to 26 — the assertion
            // was measuring the wrong quantity, so it agreed with a template that reopened RB‑2.
            var cell = Token<Thickness>("Pad.Cell");
            var room = Token<double>("Size.Row.Grid") - cell.Top - cell.Bottom;
            Assert.True(bare.DesiredSize.Height <= room,
                $"A CheckBox asks for {bare.DesiredSize.Height} px and a grid cell leaves {room} px — this is RB‑2.");

            // The mark itself carries the catalog value; the control around it carries no height of its own.
            var box = bare.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NormalRectangle");
            Assert.Equal(Token<double>("Size.Checkbox"), box.Bounds.Height);
            Assert.Equal(Token<double>("Size.Checkbox"), box.Bounds.Width);

            // ⭐ The click target is deliberately WIDER than the mark (see the ControlTheme's comment): 14 px is
            // a small thing to hit for eight hours. Horizontally only — vertically there is no room to spare,
            // as the assertion above spells out. Pinned so a later "tidy-up" of the transparent panel is a
            // visible decision rather than an accident.
            var markArea = bare.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_MarkArea");
            Assert.True(markArea.Bounds.Width >= box.Bounds.Width + 4,
                $"The click target ({markArea.Bounds.Width} px wide) is no wider than the mark ({box.Bounds.Width} px).");

            window.Close();
        }, default);
    }

    /// <summary>
    /// <c>RadioButton</c> is a sibling of <c>CheckBox</c>, not a separate design — so it is measured against the
    /// same constraint. Fluent reports <c>MinHeight = 0</c> for it and still asks for 32 px, because the height
    /// is imposed by an unnamed element of its template: the same shape as RB‑2, one control further.
    /// </summary>
    [Fact]
    public async Task RadioButton_FitsInsideAGridRow_LikeItsCheckBoxSibling()
    {
        await _session.Dispatch(() =>
        {
            var bare = new RadioButton();
            var window = new Window { Content = bare };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var cell = Token<Thickness>("Pad.Cell");
            var room = Token<double>("Size.Row.Grid") - cell.Top - cell.Bottom;
            Assert.True(bare.DesiredSize.Height <= room,
                $"A RadioButton asks for {bare.DesiredSize.Height} px and a grid cell leaves {room} px.");

            // ⭐ The mark reads the SAME token as the CheckBox's box (§5: "box CheckBox/RadioButton"). The two
            // controls belonging to one family is the property being pinned — not the number 14.
            var ring = bare.GetVisualDescendants().OfType<Ellipse>().Single(e => e.Name == "NormalEllipse");
            Assert.Equal(Token<double>("Size.Checkbox"), ring.Bounds.Width);
            Assert.Equal(Token<double>("Size.Checkbox"), ring.Bounds.Height);

            var markArea = bare.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_MarkArea");
            Assert.True(markArea.Bounds.Width >= ring.Bounds.Width + 4,
                $"The click target ({markArea.Bounds.Width} px wide) is no wider than the mark.");

            // ⭐ The dot is CONCENTRIC with the ring — asserted because the user reported it as looking
            // off-centre and the answer had to be a measurement, not an opinion. Two independently aligned
            // shapes are concentric by construction only as long as nobody gives one of them a margin, a
            // different alignment, or an odd size; this is what makes that a decision rather than a slip.
            // (Layout level only. Device-pixel behaviour at fractional DPI is handled in the template by
            // switching layout rounding off for this mark — see its comment.)
            var dot = bare.GetVisualDescendants().OfType<Ellipse>().Single(e => e.Name == "CheckGlyph");
            Assert.Equal(ring.Bounds.Center.X, dot.Bounds.Center.X, 3);
            Assert.Equal(ring.Bounds.Center.Y, dot.Bounds.Center.Y, 3);

            window.Close();
        }, default);
    }

    /// <summary>
    /// A <c>ToolTip</c> is the cleanest case of the RB‑4 "raised" role in the whole application — it belongs to
    /// no container, it floats above everything — so it is also the check that the split from step 2 is
    /// <i>useful</i> and not merely correct.
    ///
    /// <para>⚠⚠ Asserted under the <b>Light</b> variant, and that is the whole point. In Dark the two roles
    /// deliberately carry the same value (§7.1: "chrome one step further" and "raised above its container"
    /// coincide there), so a Dark assertion could not tell a correct wiring from one pointed at the chrome
    /// token — it would pass either way. The only theme in which this test can fail is the one in which the
    /// distinction exists.</para>
    /// </summary>
    [Fact]
    public async Task ToolTip_TakesTheRaisedSurface_NotTheChromeOne()
    {
        await _session.Dispatch(() =>
        {
            var app = Application.Current!;
            var original = app.RequestedThemeVariant;
            try
            {
                app.RequestedThemeVariant = ThemeVariant.Light;

                var tip = new ToolTip();
                var window = new Window { Content = tip };
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var actual = Assert.IsType<SolidColorBrush>(tip.Background);
                Assert.Equal(ThemeToken<SolidColorBrush>("SurfaceRaisedBrush", ThemeVariant.Light).Color, actual.Color);
                Assert.NotEqual(ThemeToken<SolidColorBrush>("ChromeStrongBrush", ThemeVariant.Light).Color, actual.Color);

                Assert.Equal(Token<Thickness>("Pad.Panel"), tip.Padding);
                Assert.Equal(Token<CornerRadius>("Radius.Surface"), tip.CornerRadius);
                Assert.Equal(Token<double>("Text.Application.Size"), tip.FontSize);

                window.Close();
            }
            finally
            {
                // The headless session is shared by every test in HeadlessCollection (#94/#226/#286), so a
                // leaked theme variant would silently change what a later test measures.
                app.RequestedThemeVariant = original;
            }
        }, default);
    }

    /// <summary>
    /// The architectural bet of step 5.2, as a measurement: <b>re-pointing Fluent at our catalog actually
    /// works</b> — both halves of it.
    ///
    /// <para>⭐ The two halves travel by different routes and neither implies the other. <b>Metrics</b> reach the
    /// control through an application-level <c>Style</c> setter, which outranks Fluent's <c>ControlTheme</c>
    /// (<c>TextControlThemeMinHeight</c> is 32; ours must win). <b>Colours</b> cannot: they are painted by
    /// <c>PART_BorderElement</c> inside the template, which reads Fluent's own resource keys — so they travel
    /// through <c>FluentBridge.axaml</c> instead. Asserting the border element's brush is the only way to know
    /// the Bridge is wired at all; a setter on the TextBox would silently paint nothing.</para>
    /// </summary>
    [Fact]
    public async Task TextBox_TakesItsMetricsFromTheCatalog_AndItsColoursThroughTheBridge()
    {
        await _session.Dispatch(() =>
        {
            var box = new TextBox { Text = "abc", Width = 200 };
            var window = new Window { Content = new StackPanel { Children = { box } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Half one — metrics, via the Style. Fluent asks for 32; the catalog says otherwise.
            Assert.Equal(Token<double>("Size.Control"), box.MinHeight);
            Assert.Equal(Token<Thickness>("Pad.Control"), box.Padding);
            Assert.Equal(Token<double>("Text.Application.Size"), box.FontSize);
            Assert.NotEqual(32d, box.MinHeight);

            // Half two — colour, via the Bridge. Read off the element that actually paints it.
            var painter = box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            var background = Assert.IsType<SolidColorBrush>(painter.Background);
            Assert.Equal(ThemeToken<SolidColorBrush>("BackgroundBrush", ThemeVariant.Dark).Color, background.Color);

            window.Close();
        }, default);
    }

    /// <summary>
    /// The Bridge scaling to a SECOND control, which is the question step 5.3 exists to answer. <c>ComboBox</c>
    /// has far more template parts than <c>TextBox</c> (Background, HighlightBackground, DropDownOverlay,
    /// DropDownGlyph, PART_Popup) and still needed no template of its own.
    /// </summary>
    [Fact]
    public async Task ComboBox_TakesTheSameRoute_AsTextBox()
    {
        await _session.Dispatch(() =>
        {
            var combo = new ComboBox { Items = { "alpha", "beta" }, SelectedIndex = 0, Width = 200 };
            var window = new Window { Content = new StackPanel { Children = { combo } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Token<double>("Size.Control"), combo.MinHeight);
            Assert.Equal(Token<Thickness>("Pad.Control"), combo.Padding);
            Assert.Equal(Token<double>("Text.Application.Size"), combo.FontSize);

            var painter = combo.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "Background");
            var background = Assert.IsType<SolidColorBrush>(painter.Background);
            Assert.Equal(ThemeToken<SolidColorBrush>("BackgroundBrush", ThemeVariant.Dark).Color, background.Color);

            window.Close();
        }, default);
    }

    /// <summary>
    /// Step 5.4 — <c>Button</c>. The first control M2b restyles that <b>already had a designed family</b>
    /// (<c>.icon</c> / <c>.flat</c> / <c>.primary</c> / <c>.caption</c>), so the risk is the opposite of the
    /// previous iterations: not "does the base style arrive" but "does it arrive <i>without</i> flattening a
    /// variant that deliberately differs".
    ///
    /// <para>⚠ Style precedence in Avalonia is by declaration ORDER at equal specificity, not by selector
    /// weight — so a base <c>Button</c> style placed after <c>Button.primary</c> in the same stylesheet would
    /// silently win over it. This asserts the two heights stay different, which is the cheapest way to catch
    /// that reordering.</para>
    /// </summary>
    [Fact]
    public async Task Button_TakesTheBaseHeight_WhileThePrimaryVariantStaysTaller()
    {
        await _session.Dispatch(() =>
        {
            var plain = new Button { Content = "Run" };
            var primary = new Button { Content = "Execute", Classes = { "primary" } };
            var window = new Window { Content = new StackPanel { Children = { plain, primary } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Token<double>("Size.Control"), plain.MinHeight);
            Assert.Equal(Token<Thickness>("Pad.Button"), plain.Padding);
            Assert.Equal(Token<double>("Text.Application.Size"), plain.FontSize);
            Assert.Equal(Token<CornerRadius>("Radius.Surface"), plain.CornerRadius);

            // The variant must still outrank the base — hierarchy is the point of the second height role.
            Assert.Equal(Token<double>("Size.ControlPrimary"), primary.MinHeight);
            Assert.True(primary.MinHeight > plain.MinHeight,
                "Button.primary must stay taller than a plain Button — Size.ControlPrimary exists for that hierarchy.");

            // Colour arrives through the Bridge, on the element that paints it. Fluent's own value here is a
            // semi-transparent white (#33ffffff) whose hover state is pure White; neither belongs to the palette.
            var painter = plain.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
            var background = Assert.IsType<SolidColorBrush>(painter.Background);
            Assert.Equal(ThemeToken<SolidColorBrush>("PanelBrush", ThemeVariant.Dark).Color, background.Color);

            window.Close();
        }, default);
    }

    /// <summary>
    /// Step 5.5 — <c>NumericUpDown</c>, which is not one control but three nested ones: it wraps a
    /// <c>ButtonSpinner</c>, which wraps a <c>TextBox</c> and two <c>RepeatButton</c>s. The 32 px is imposed by
    /// the MIDDLE one, so a setter on <c>NumericUpDown</c> alone changes nothing measurable.
    ///
    /// <para>⭐ The assertion that matters is <see cref="Layoutable.DesiredSize"/>, not <c>MinHeight</c> — the
    /// property can be set correctly on the outer control while an inner one still forces the old height, which
    /// is exactly the failure this iteration exists to prevent (and the same shape as <c>RadioButton</c>'s
    /// <c>MinHeight=0</c> lie in §15.6.1).</para>
    /// </summary>
    [Fact]
    public async Task NumericUpDown_CollapsesToTheStandardControlHeight_ThroughItsNestedSpinner()
    {
        await _session.Dispatch(() =>
        {
            var spin = new NumericUpDown { Value = 1, Width = 160 };
            var window = new Window { Content = new StackPanel { Children = { spin } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            spin.Measure(new Size(300, 300));

            Assert.Equal(Token<double>("Size.Control"), spin.MinHeight);

            // The real test: what the control ASKS FOR. Fluent asks for 32 through the inner ButtonSpinner.
            Assert.Equal(Token<double>("Size.Control"), spin.DesiredSize.Height);
            Assert.NotEqual(32d, spin.DesiredSize.Height);

            // The nested TextBox took the catalog height back in step 5.2 with no change here — the bridge
            // composes into a template it was never pointed at.
            var inner = spin.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_TextBox");
            Assert.Equal(Token<double>("Size.Control"), inner.MinHeight);

            window.Close();
        }, default);
    }

    /// <summary>
    /// Step 5.6 — <c>ToggleButton</c>. It derives from <c>Button</c>, but an Avalonia type selector matches the
    /// EXACT type, so the step-5.4 style does not reach it. This pins that it now takes the same metrics, and —
    /// more importantly — that the <b>checked</b> state is the app's accent without the Bridge mapping it.
    ///
    /// <para>⭐ Why the checked state is deliberately absent from <c>FluentBridge.axaml</c>: <c>SystemAccentColor</c>
    /// is already overridden in <c>Colors.axaml</c>, so Fluent's <c>ToggleButtonBackgroundChecked</c> resolves to
    /// our accent on its own. A Bridge entry would duplicate a value we already control — the same reason
    /// <c>ControlCornerRadius</c> is absent. This test is what turns that coincidence into a checked invariant.</para>
    /// </summary>
    [Fact]
    public async Task ToggleButton_TakesTheControlMetrics_AndItsCheckedStateIsTheAppAccent()
    {
        await _session.Dispatch(() =>
        {
            var toggle = new ToggleButton { Content = "Wrap", IsChecked = true };
            var window = new Window { Content = new StackPanel { Children = { toggle } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Token<double>("Size.Control"), toggle.MinHeight);
            Assert.Equal(Token<Thickness>("Pad.Button"), toggle.Padding);
            Assert.Equal(Token<double>("Text.Application.Size"), toggle.FontSize);

            // The invariant the Bridge deliberately does NOT restate: Fluent's checked fill is already ours.
            // ⚠ Read through the THEME-scoped lookup — Fluent's keys live in ThemeDictionaries, which the
            // variant-less Token<T> cannot see (the same boundary measured in step 4).
            var accent = ThemeToken<SolidColorBrush>("AccentBrush", ThemeVariant.Dark).Color;
            var checkedFill = ThemeToken<SolidColorBrush>("ToggleButtonBackgroundChecked", ThemeVariant.Dark);
            Assert.Equal(accent, checkedFill.Color);

            window.Close();
        }, default);
    }

    /// <summary>
    /// Reads a token straight from the application's merged resources — the same lookup a style performs. If
    /// the key is missing this fails loudly here, instead of leaving a control on a silent default.
    /// </summary>
    private static T Token<T>(string key)
    {
        var app = Application.Current;
        Assert.NotNull(app);
        Assert.True(app!.TryFindResource(key, out var value), $"Token `{key}` is not in the application's resources.");
        return Assert.IsType<T>(value);
    }

    /// <summary>
    /// The same lookup for a THEME-SCOPED resource. ⚠ Measured, and worth knowing: the variant-less
    /// <see cref="Token{T}"/> above cannot see anything declared inside <c>ThemeDictionaries</c> — it reports the
    /// key as missing. That is precisely the line between the two colour-free dictionaries added in M2a
    /// (<c>Tokens</c>/<c>Typography</c>: one value, no variant) and <c>Colors.axaml</c> (one value per theme).
    /// Two kinds of resource, two lookups.
    /// </summary>
    private static T ThemeToken<T>(string key, ThemeVariant variant)
    {
        var app = Application.Current;
        Assert.NotNull(app);
        Assert.True(app!.TryFindResource(key, variant, out var value),
            $"Theme token `{key}` is not in the application's {variant} resources.");
        return Assert.IsType<T>(value);
    }
}

using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
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
    /// (<c>.icon</c> / <c>.flat</c> / <c>.primary</c> / <c>.caption</c>), so the risk was the opposite of the
    /// previous iterations: not "does the base style arrive" but "does it arrive <i>without</i> flattening a
    /// variant that deliberately differs".
    ///
    /// <para>⚠ Style precedence in Avalonia is by declaration ORDER at equal specificity, not by selector
    /// weight — a base <c>Button</c> style placed after <c>Button.primary</c> would silently win over it. That
    /// hazard is still real for COLOUR, which is what the accent assertion below covers.</para>
    ///
    /// <para>⚠⚠ REWRITTEN IN STEP 11. It used to assert that <c>.primary</c> stayed TALLER — the very rule the
    /// user's QA reversed: <b>colour may express priority, size may not.</b> The variant now differs in accent
    /// alone, so the assertion is inverted on purpose rather than deleted: an equality here is what keeps
    /// "Execute is bigger than Cancel" from coming back to all 26 dialog files at once.</para>
    /// </summary>
    [Fact]
    public async Task Button_AndItsPrimaryVariant_DifferInColourOnly()
    {
        await _session.Dispatch(() =>
        {
            var plain = new Button { Content = "Run" };
            var primary = new Button { Content = "Execute", Classes = { "primary" } };
            var window = new Window { Content = new StackPanel { Children = { plain, primary } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // ⚠ `Size.ControlProminent`, not `Size.Control` — step 8 corrected step 5.4's premise after QA:
            // a button is an ACTION (stands alone, is a mouse target), not a FIELD (stands in a series).
            Assert.Equal(Token<double>("Size.ControlProminent"), plain.MinHeight);
            Assert.Equal(Token<Thickness>("Pad.Button"), plain.Padding);
            Assert.Equal(Token<double>("Text.Application.Size"), plain.FontSize);
            Assert.Equal(Token<CornerRadius>("Radius.Surface"), plain.CornerRadius);

            // ⭐ The ratified rule, as one equality: the accent variant is the same SIZE as its neutral sibling.
            Assert.Equal(plain.MinHeight, primary.MinHeight);
            Assert.Equal(plain.Padding, primary.Padding);

            // …and differs where it is supposed to. The variant must still win on colour, which is the half of
            // the declaration-order hazard that remains.
            var accent = Assert.IsType<SolidColorBrush>(primary.Background);
            Assert.Equal(ThemeToken<SolidColorBrush>("AccentBrush", ThemeVariant.Dark).Color, accent.Color);

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

            Assert.Equal(Token<double>("Size.ControlProminent"), toggle.MinHeight);
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
    /// Step 5.7 — <c>Expander</c>, and the two facts that make it the most instructive control of M2b.
    ///
    /// <para>⚠ Its header IS a <c>ToggleButton</c>, so step 5.6's type style reached inside Fluent's template
    /// and centred the header content. A type style applies inside a foreign template too — the same property
    /// that worked FOR us in step 5.5 (the nested <c>TextBox</c> of <c>NumericUpDown</c>) works against us here.
    /// The alignment assertion is what keeps that from silently coming back.</para>
    ///
    /// <para>⭐ Its <c>MinHeight</c> could NOT be fixed by a setter: the template consumes
    /// <c>ExpanderMinHeight</c> as a LOCAL value on the element, and a local value outranks a style setter. The
    /// measured answer was a resource ALIAS in the Bridge — which also disproves §16.3's premise that XAML
    /// cannot alias a scalar. This asserts the alias arrives; without it the header sits at Fluent's 48.</para>
    /// </summary>
    [Fact]
    public async Task Expander_HeaderCollapsesThroughAResourceAlias_AndKeepsItsContentAlignment()
    {
        await _session.Dispatch(() =>
        {
            var expander = new Expander { Header = "Advanced", Content = new TextBlock { Text = "body" }, IsExpanded = true };
            var window = new Window { Content = new StackPanel { Width = 300, Children = { expander } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            expander.Measure(new Size(300, 300));

            var header = expander.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "ExpanderHeader");

            // The alias, not the setter: Fluent's ExpanderMinHeight is a template-local value that beat the
            // step-5.6 ToggleButton style. 48 is the value that must not come back.
            Assert.Equal(Token<double>("Size.Control"), header.MinHeight);
            Assert.NotEqual(48d, header.MinHeight);

            // What step 5.6 took away and this step gives back. A centred section header is the regression.
            Assert.Equal(HorizontalAlignment.Stretch, header.HorizontalContentAlignment);

            window.Close();
        }, default);
    }

    /// <summary>
    /// The measurement that step 5.7 turned into a mechanism, kept as a checked invariant: <b>Avalonia resolves
    /// a resource declared as an alias of another resource.</b> §16.3 originally claimed XAML cannot alias a
    /// scalar; it can — what it cannot do is COMPOSE one in element content (<c>&lt;x:Double&gt;</c> must hold a
    /// number). The distinction is what lets the Bridge own a metric without writing a literal.
    ///
    /// <para>⚠ If this ever fails, the three <c>Expander</c> metric aliases silently fall back to Fluent's own
    /// values and the header returns to 48 px — with a green build.</para>
    /// </summary>
    [Fact]
    public async Task Bridge_AliasesAScalarMetric_WithoutRestatingItsValue()
    {
        await _session.Dispatch(() =>
        {
            Assert.Equal(Token<double>("Size.Control"), Token<double>("ExpanderMinHeight"));
            Assert.Equal(Token<Thickness>("Pad.Control"), Token<Thickness>("ExpanderHeaderPadding"));
            Assert.Equal(Token<Thickness>("Pad.Panel"), Token<Thickness>("ExpanderContentPadding"));
        }, default);
    }

    /// <summary>
    /// Step 6 — <c>ScrollBar</c> (H‑10). Fluent paints the thumb in semi-transparent WHITE, which in the LIGHT
    /// theme is a white thumb on a near-white surface — the finding itself.
    ///
    /// <para>⚠ Written in the LIGHT variant for the same reason step 4's ToolTip test was: this is the theme in
    /// which the defect is visible. A dark-theme assertion would pass on Fluent's own value.</para>
    ///
    /// <para>⭐ It also pins the second use of the alias route (§16.3): <c>ScrollBarThumbBackgroundColor</c> is a
    /// <c>Color</c>, not a brush, so it cannot be written as <c>Color="{StaticResource …}"</c> — the mechanism
    /// measured for a metric in step 5.7 turns out to serve a colour too.</para>
    /// </summary>
    [Fact]
    public async Task ScrollBar_ThumbTakesTheCatalogColour_NotFluentsWhite()
    {
        await _session.Dispatch(() =>
        {
            var app = Application.Current!;
            var original = app.RequestedThemeVariant;
            try
            {
                app.RequestedThemeVariant = ThemeVariant.Light;

                var thumb = ThemeToken<Color>("ScrollBarThumbBackgroundColor", ThemeVariant.Light);
                Assert.Equal(ThemeToken<Color>("ScrollBarThumbColor", ThemeVariant.Light), thumb);

                // The defect in one assertion: on Light the catalog thumb must not be white-ish, or it vanishes
                // against BackgroundColor (#FCFCFD). Fluent's own value is a semi-transparent white.
                var surface = ThemeToken<Color>("BackgroundColor", ThemeVariant.Light);
                Assert.True(surface.R - thumb.R > 24,
                    $"A Light-theme scroll thumb ({thumb}) must read against the surface ({surface}) — this is H‑10.");

                var pointerOver = ThemeToken<SolidColorBrush>("ScrollBarThumbFillPointerOver", ThemeVariant.Light);
                Assert.Equal(ThemeToken<Color>("ScrollBarThumbHoverColor", ThemeVariant.Light), pointerOver.Color);
            }
            finally
            {
                app.RequestedThemeVariant = original;
            }
        }, default);
    }

    /// <summary>
    /// Step 7 — the <b>DataGrid Standard</b> (specification §8.4), which demands ONE standard across every grid:
    /// header height · row height · editor height · checkbox height · text alignment · spacing · selection ·
    /// edit behaviour.
    ///
    /// <para>⭐ The load-bearing assertion is the LAST one, and it is the spec's own sentence turned into
    /// arithmetic: <i>"no checkbox may force a row to grow"</i> generalises to <b>nothing placed in a cell may
    /// exceed the room a cell leaves</b>. That is what "behaviour during editing" means in practice — an editor
    /// taller than the row makes the row jump the moment the user clicks into it (Zero Layout Shift, §13.3).</para>
    ///
    /// <para>⚠ It asserts the ROLES agree, not the numbers: the header must stay one step taller than the row,
    /// and the row must leave room for its own cell content. Both survive a deliberate change of either value.</para>
    /// </summary>
    [Fact]
    public async Task DataGridStandard_HeaderIsTallerThanTheRow_AndNothingInACellCanForceItToGrow()
    {
        await _session.Dispatch(() =>
        {
            var row = Token<double>("Size.Row.Grid");
            var header = Token<double>("Size.Row.Header");
            var cell = Token<Thickness>("Pad.Cell");
            var room = row - cell.Top - cell.Bottom;

            // A header reads as a header because it is one step taller — not because of a frame or a rule.
            Assert.True(header > row,
                $"Size.Row.Header ({header}) must stay taller than Size.Row.Grid ({row}) — that step IS the header's frame.");

            // A row must be able to hold its own text: the grid role's line height has to fit the room left.
            Assert.True(Token<double>("Text.Grid.LineHeight") <= room,
                $"Text.Grid.LineHeight must fit the {room} px a cell leaves, or every row silently grows.");

            // ⭐ The spec's rule, as arithmetic. Each of these was fixed in its own iteration; this is the one
            // place that says they must all keep holding TOGETHER.
            var editors = new (string What, Control Control)[]
            {
                ("CheckBox (step 1)", new CheckBox()),
                ("TextBox (step 5.2)", new TextBox { Text = "x", Classes = { } }),
                ("Button (step 5.4)", new Button { Content = "…" }),
            };
            var grid = new DataGrid();
            var window = new Window { Content = new StackPanel { Children = { grid } } };
            window.Show();

            foreach (var (what, control) in editors)
            {
                var cellHost = new DataGridCell { Content = control };
                var probe = new StackPanel { Children = { cellHost } };
                var w2 = new Window { Content = probe };
                w2.Show();
                Dispatcher.UIThread.RunJobs();
                control.Measure(new Size(400, 400));

                Assert.True(control.DesiredSize.Height <= room,
                    $"{what} asks for {control.DesiredSize.Height} px inside a cell that leaves {room} px — " +
                    "entering edit would move the row (§8.4: a control in a cell must never grow it).");
                w2.Close();
            }

            window.Close();
        }, default);
    }

    /// <summary>
    /// Steps 8–9 — the ACTION height ladder the user's QA asked for, as one invariant.
    ///
    /// <para>⭐ The correction step 8 encodes: step 5.4 gave a button <c>Size.Control</c> (24) on the assumption
    /// that a button is a control like any other. It is not — a FIELD stands in a series and must align, an
    /// ACTION stands alone and is a mouse target. So there are two ladders, and the actions' has three rungs:
    /// toolbar (chrome) &lt; prominent (dialog footer) &lt; primary (main action).</para>
    ///
    /// <para>⚠ Written as ordering, not as numbers: the three must stay strictly increasing, which is the whole
    /// content of "a deliberate hierarchy rather than an accident". It survives a re-tuning of any value.</para>
    /// </summary>
    [Fact]
    public async Task ActionHeights_FormAStrictLadder_AndAToolbarButtonNeverLiftsTheBar()
    {
        await _session.Dispatch(() =>
        {
            var toolbar = Token<double>("Size.ControlToolbar");
            var prominent = Token<double>("Size.ControlProminent");
            var field = Token<double>("Size.Control");

            // ⚠ Two rungs, not three — `Size.ControlPrimary` was retired in step 11 when priority stopped
            // being expressed by size. Chrome is denser than a dialog action; that difference stays.
            Assert.True(toolbar < prominent,
                $"Chrome must stay denser than a dialog action: toolbar {toolbar} < prominent {prominent}.");

            // A field is NOT on that ladder — it is the other one. If these ever collapse into a single value
            // the distinction the QA asked for has quietly disappeared.
            Assert.NotEqual(field, prominent);

            // ⭐⭐ THE RATIFIED RULE OF STEP 11, and the one this test exists for: COLOUR may express an
            // action's priority, SIZE may not. A dialog footer is a ROW of actions, and a row must align —
            // so the accent variant and the neutral one are the SAME height. Before step 11 they were 28
            // and 26, which is why Execute was bigger than Cancel in all 26 dialog files at once.
            var close = new Button { Content = "Close", Classes = { "flat" } };
            var accept = new Button { Content = "Execute", Classes = { "primary" } };
            // …while the very same accent variant inside a chrome strip takes the chrome rung, so the bar
            // keeps one height whatever stands in it.
            var inBar = new Button { Content = "Run", Classes = { "primary" } };
            var bar = new Border { Classes = { "chrome" }, Child = new StackPanel { Children = { inBar } } };

            var window = new Window { Content = new StackPanel { Children = { close, accept, bar } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(prominent, close.MinHeight);
            Assert.Equal(prominent, accept.MinHeight);
            Assert.Equal(close.MinHeight, accept.MinHeight);

            // ⭐ The other half: a button must never raise the strip it stands in. The CONTAINER declares its
            // children's height — the same mechanism as a grid cell and the Expander header — and the style
            // that does it MUST be declared after Button.primary or this reverts silently.
            Assert.Equal(toolbar, inBar.MinHeight);

            window.Close();
        }, default);
    }

    /// <summary>
    /// Step 9 — the two list surfaces the QA reported separately ("the Settings category list is too tall and
    /// its font one step too large", "Saved Queries should be denser") turn out to be one defect:
    /// <c>ListBoxItem</c> never had a style, so both stood on Fluent.
    /// </summary>
    [Fact]
    public async Task ListRow_AndSearchField_TakeTheirRoles()
    {
        await _session.Dispatch(() =>
        {
            var list = new ListBox { ItemsSource = new[] { "General", "Editor" } };
            var search = new TextBox { Classes = { "search" } };
            var window = new Window { Content = new StackPanel { Children = { list, search } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var row = list.GetVisualDescendants().OfType<ListBoxItem>().First();
            Assert.Equal(Token<double>("Size.Row.Menu"), row.MinHeight);
            Assert.Equal(Token<double>("Text.Application.Size"), row.FontSize);
            Assert.NotEqual(14d, row.FontSize);

            // The search field is the second consumer of the prominent role — which is what makes it a ROLE
            // and not "a taller button".
            Assert.Equal(Token<double>("Size.ControlProminent"), search.MinHeight);
            Assert.Equal(VerticalAlignment.Center, search.VerticalContentAlignment);

            window.Close();
        }, default);
    }

    /// <summary>
    /// Step 11 — the proximity rule the QA report named without naming it: <b>a caption belongs to its field.</b>
    ///
    /// <para>⚠ The reported symptom was "the label looks like it belongs to the previous section", and the cause
    /// was TWO OWNERS OF ONE GAP — the caption carried a 4 px margin and its container added its own spacing on
    /// top, so caption→field ended up LARGER than field→field. The eye then attaches the caption upwards. The
    /// catalog already states the rule for <c>Margin.FieldGap</c> ("a gap has one owner"); this is where it was
    /// being broken.</para>
    ///
    /// <para>⚠ Asserted as an ORDERING, never as numbers — that is what the rule actually says, and it survives
    /// a re-tuning of either value.</para>
    /// </summary>
    [Fact]
    public async Task ACaptionSitsCloserToItsField_ThanTwoFieldsSitToEachOther()
    {
        await _session.Dispatch(() =>
        {
            var label = Token<Thickness>("Margin.LabelGap");
            var betweenFields = Token<Thickness>("Margin.FieldGap");
            var betweenOptions = Token<Thickness>("Margin.OptionGap");

            Assert.True(label.Bottom < betweenFields.Bottom,
                $"A caption ({label.Bottom}px) must sit closer to its field than two fields sit to each other " +
                $"({betweenFields.Bottom}px) — otherwise the caption reads as belonging to whatever is above it.");

            // Options of one choice are a group, so they sit tighter than separate fields but looser than a
            // caption on its own field. The three gaps form one scale rather than three unrelated numbers.
            Assert.True(label.Bottom < betweenOptions.Bottom && betweenOptions.Bottom < betweenFields.Bottom,
                $"The proximity scale must stay ordered: caption {label.Bottom} < option {betweenOptions.Bottom} " +
                $"< field {betweenFields.Bottom}.");

            var caption = new TextBlock { Text = "Search for", Classes = { "field-label" } };
            var window = new Window { Content = new StackPanel { Children = { caption } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(label, caption.Margin);
            window.Close();
        }, default);
    }

    /// <summary>
    /// Step 12 — the two halves of "these look like ONE component", both reported by eye and both fixed as
    /// rules rather than per screen.
    ///
    /// <para>⭐ A dialog footer needs a WIDTH floor, not only a shared height. Step 11 equalised the heights and
    /// the pairs still did not read as one component, because width was being set by the LABEL — a short
    /// caption gave a small button and a long one a big button, so size carried information again. The floor is
    /// a floor, not a fixed width: a long label still expands.</para>
    ///
    /// <para>⭐ A chrome strip declares the vertical alignment of its content. It already declared its children's
    /// height; without the line, it aligned boxes while the user reads text — which is exactly how the Data
    /// Import status bar ended up with three different baselines.</para>
    /// </summary>
    [Fact]
    public async Task DialogActionsShareAWidthFloor_AndAChromeStripAlignsItsContentOnOneLine()
    {
        await _session.Dispatch(() =>
        {
            var save = new Button { Content = "Save", Classes = { "primary" } };
            var cancel = new Button { Content = "Cancel", Classes = { "flat" } };

            var label = new TextBlock { Text = "Szkoleniowa · Data lane" };
            var stripButton = new Button { Content = "Clear", Classes = { "flat" } };
            var strip = new Border
            {
                Classes = { "chrome" },
                Child = new StackPanel { Children = { label, stripButton } },
            };

            var window = new Window { Content = new StackPanel { Children = { save, cancel, strip } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // ⭐ The pair reads as one component: same height AND the same width floor, so the shorter label
            // cannot shrink its button below its neighbour.
            var floor = Token<double>("Size.ActionMinWidth");
            Assert.Equal(floor, save.MinWidth);
            Assert.Equal(floor, cancel.MinWidth);
            Assert.Equal(save.MinHeight, cancel.MinHeight);

            // ⚠ …and a chrome strip opts OUT of that floor, or every toolbar icon would carry 80 px of air.
            Assert.Equal(0d, stripButton.MinWidth);
            Assert.Equal(Token<double>("Size.ControlToolbar"), stripButton.MinHeight);

            // The strip puts its text on the strip's line rather than letting each child choose.
            Assert.Equal(VerticalAlignment.Center, label.VerticalAlignment);

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

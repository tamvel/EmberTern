using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
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

            // The guard that catches a silently unresolved resource. ⚠⚠ CORRECTED 2026-08-02 (M2c step 0,
            // product-polish.md §18.0.2): this comment used to claim the inherited default is 12. MEASURED with
            // a headless probe — a bare TextBlock inherits **14** from Window.FontSize, not 12. The assertion
            // still does its job (the role is 11), but the number it must not equal is the inherited one.
            // ⭐ That measurement is load-bearing well beyond this line: it is why M2c REPLACES a local FontSize
            // on a TextBlock with a token reference and never just deletes it — deleting would raise the text
            // from 11 to 14.
            Assert.NotEqual(14d, header.FontSize);

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
            // ⚠ `.flat`, not a bare Button — since step 13 the ACTION geometry lives on the action classes,
            // so a class-less Button is deliberately not an action and comparing against it proves nothing.
            var plain = new Button { Content = "Run", Classes = { "flat" } };
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
            // ⚠ `ISolidColorBrush`, not the concrete `SolidColorBrush`: Avalonia may hand back an immutable
            // brush depending on how the value was resolved, and the assertion is about the COLOUR — pinning
            // the implementation type makes it fail for a reason that has nothing to do with the design system.
            var accent = Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background);
            Assert.Equal(ThemeToken<Color>("AccentColor", ThemeVariant.Dark), accent.Color);

            // Colour arrives through the Bridge, on the element that paints it. Fluent's own value here is a
            // semi-transparent white (#33ffffff) whose hover state is pure White; neither belongs to the palette.
            // ⚠ Read from an UNCLASSED button on purpose: the Bridge maps `ButtonBackground`, which is what a
            // button with no variant paints with. `.flat` is deliberately transparent and `.primary` is the
            // accent, so neither of them can witness the Bridge — asserting on one of those would be measuring
            // the variant's own setter and calling it proof of the mapping.
            var bare = new Button { Content = "Bare" };
            var w2 = new Window { Content = new StackPanel { Children = { bare } } };
            w2.Show();
            Dispatcher.UIThread.RunJobs();
            var painter = bare.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
            var background = Assert.IsAssignableFrom<ISolidColorBrush>(painter.Background);
            Assert.Equal(ThemeToken<Color>("PanelColor", ThemeVariant.Dark), background.Color);
            w2.Close();

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

            // ⭐⭐ MEASURED EQUALITY, not a shared setter — this is what step 12's floor failed to deliver and
            // step 13 fixed. The floor was 80 while "Cancel" needs 98 (72 text + 24 padding + 2 border), so
            // Save sat on the floor and Cancel overshot it: the pair differed by 18 px and the floor equalised
            // nothing. ⚠ A floor only works ABOVE the natural width of the labels it is meant to equalise.
            save.Measure(new Size(400, 200));
            cancel.Measure(new Size(400, 200));
            Assert.Equal(cancel.DesiredSize.Width, save.DesiredSize.Width);
            Assert.Equal(cancel.DesiredSize.Height, save.DesiredSize.Height);

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
    /// Step 13 — the regression that proved a NEGATIVE rule always leaks, kept as the guard against writing it
    /// again.
    ///
    /// <para>⚠⚠ The base <c>Button</c> style used to carry the ACTION geometry (<c>MinHeight</c> +
    /// <c>MinWidth</c> + <c>Padding</c>) — i.e. dialog-footer dimensions imposed on every button in the
    /// application, with every non-action button having to opt out. The sidebar's expander arrow declares its
    /// own <c>Width=20 Height=20</c>, and Avalonia clamps <c>Width</c> by <c>MinWidth</c> — so the base style
    /// silently grew it to 100×28 and it collided with the row's text. That is a layout regression, not a
    /// styling preference.</para>
    ///
    /// <para>⭐ The rule is positive now: geometry lives on the classes that ARE actions. This asserts a button
    /// with a declared size keeps it — which is only true while no application-level style asserts a floor on
    /// every <c>Button</c>.</para>
    /// </summary>
    [Fact]
    public async Task AButtonThatDeclaresItsOwnSize_KeepsIt()
    {
        await _session.Dispatch(() =>
        {
            var sized = new Button { Content = "›", Width = 20, Height = 20, Padding = new Thickness(0) };
            var window = new Window { Content = new StackPanel { Children = { sized } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            sized.Measure(new Size(200, 200));

            Assert.Equal(20d, sized.DesiredSize.Width);
            Assert.Equal(20d, sized.DesiredSize.Height);
            Assert.Equal(0d, sized.MinWidth);
            Assert.Equal(0d, sized.MinHeight);

            // …while a declared ACTION still gets the floor. Both halves, or the fix is just a deletion.
            var action = new Button { Content = "OK", Classes = { "flat" } };
            var w2 = new Window { Content = new StackPanel { Children = { action } } };
            w2.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Token<double>("Size.ActionMinWidth"), action.MinWidth);

            window.Close();
            w2.Close();
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
    /// ⭐ A MULTILINE text field starts its text at the TOP; a single-line one stays vertically centred.
    /// <para>Reported by the user when accepting M2c: the Description tabs (exception, object comment, CHECK
    /// body) hung a short text in the middle of a tall frame. The cause was not the sweep — M2b's <c>TextBox</c>
    /// style centres EVERY text box, which is necessary for a one-line field (<c>Pad.Control</c> has zero
    /// vertical padding, so without centring the text would sit on the top edge of a 24 px control) and wrong
    /// for a field that is fifteen lines tall.</para>
    /// <para>⚠ The pin asserts BOTH halves against a real visual tree, because the rule is a property selector
    /// layered over the base style: if the two styles were declared the other way round the multiline case would
    /// silently lose (M2b §17.5/5 — declaration order decides between equally specific styles), and if the
    /// selector did not match at all nothing would fail except the appearance.</para>
    /// </summary>
    [Fact]
    public async Task AMultilineTextBox_StartsItsTextAtTheTop_AndASingleLineOneStaysCentred()
    {
        await _session.Dispatch(() =>
        {
            var multiline = new TextBox { AcceptsReturn = true, Text = "opis" };
            var singleLine = new TextBox { Text = "nazwa" };
            var window = new Window { Content = new StackPanel { Children = { multiline, singleLine } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(VerticalAlignment.Top, multiline.VerticalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, singleLine.VerticalContentAlignment);

            window.Close();
        }, default);
    }

    /// <summary>
    /// ⭐ Każdy stan railu Status Bara ma pędzel W OBU MOTYWACH (product-polish.md §8.4.2, §19.4).
    ///
    /// <para>⚠⚠ Rail nie czyta pędzla przez <c>{DynamicResource}</c>, tylko przez
    /// <c>IconBrushConverter</c>, który przy nieznanym kluczu zwraca <c>UnsetValue</c> — a wtedy
    /// <c>BorderBrush</c> po cichu zostaje przy wartości domyślnej. Literówka albo pędzel zdefiniowany
    /// tylko w jednym motywie **nie zawiedzie buildu, nie zawiedzie żadnego innego testu i nie rzuci
    /// wyjątku** — rail po prostu przestanie sygnalizować stan, w jednym motywie albo w obu.</para>
    ///
    /// <para>⚠ Lista kluczy jest tu powtórzona świadomie: <c>RailBrushKey</c> to łańcuch priorytetów,
    /// a nie kolekcja, więc nie da się jej odczytać bez konstruowania <c>MainWindowViewModel</c> (czyli
    /// bez sklepu, serwisu i zakładek). Dodając szósty stan railu, dopisz go tutaj — dokładnie tak, jak
    /// robi to bliźniaczy strażnik severity <c>MessageBanner</c>.</para>
    /// </summary>
    [Theory]
    [InlineData("ErrorBrush")]
    [InlineData("WarningBrush")]
    [InlineData("DebugCurrentLineBarBrush")]
    [InlineData("AccentBrush")]
    [InlineData("IconColor_Query")]
    [InlineData("BorderBrush")]
    public async Task RailStateBrush_ResolvesInBothThemes(string key)
    {
        await _session.Dispatch(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                var brush = ThemeToken<SolidColorBrush>(key, variant);
                Assert.NotNull(brush);
            }
        }, default);
    }

    /// <summary>
    /// ⭐⭐ `TextBox` i `ComboBox` w siatce definicji pól należą do TEJ SAMEJ RODZINY KONTROLEK —
    /// mają jednakową wysokość (§19.9).
    ///
    /// <para>⚠⚠ Test istnieje, bo droga do tego stanu miała TRZY warstwy i każda maskowała następną.
    /// (1) `FieldGridColumns` ustawiał `VerticalAlignment`/`Padding`/`BorderThickness`/`Background`
    /// jako WARTOŚCI LOKALNE w kodzie, a wartość lokalna bije setter stylu, więc styl nie mógł ich
    /// dosięgnąć. (2) Po ich usunięciu `Stretch` już się stosował, ale wysokość dalej wynosiła 12 px
    /// przy komórce 30 px — bo `DataGridCell` ma `VerticalContentAlignment="Center"` i CENTRUJE
    /// dziecko zamiast je rozciągać. ⛔ Tamtego settera nie wolno odwrócić: pilnuje, żeby zwykły TEKST
    /// nie osiadał przy górnej krawędzi. (3) Dopiero `MinHeight` = `Size.Control` — ta sama ROLA,
    /// z której `ComboBox` bierze swoją wysokość — zrównał obie kontrolki.</para>
    ///
    /// <para>⭐ Asercja porównuje `TextBox` z `ComboBoxem` **obok, w tej samej siatce**, a nie z liczbą.
    /// Dzięki temu przetrwa zmianę wartości `Size.Control` i upadnie dokładnie wtedy, gdy jedna z tych
    /// kontrolek przestanie należeć do rodziny — czyli na tym, o co w tej poprawce chodziło.</para>
    ///
    /// <para>⚠ Druga połowa asercji pilnuje, że minimum edytora NIE PODNIOSŁO wiersza. Klasa
    /// `field-editor` istnieje właśnie po to, żeby ten setter nie sięgnął siatek DANYCH, które
    /// `ComboBoxa` nie mają — tam urósłby każdy wiersz (regresja z kroku 7 M2b).</para>
    /// </summary>
    [Fact]
    public async Task FieldGridEditors_TextBoxAndComboBox_ShareOneHeight()
    {
        await _session.Dispatch(() =>
        {
            var row = EmberTern.App.ViewModels.ProcedureVariableRowViewModel.From(
                new EmberTern.Core.Sql.ProcedureVariable { Name = "V", TypeText = "VARCHAR(20)" });

            var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { row } };
            EmberTern.App.Views.FieldGridColumns.Build(grid, includeDefault: true);
            // ⭐ The height role now comes from the ONE seam, not from Build (S-3, 2026-08-05). Build applied
            // it, which made its scope "whoever calls Build" — and the three grids that declare their columns
            // in XAML never did, which is why Table's editor stayed thin. Mirrored here so the test exercises
            // the same path the views do.
            EmberTern.App.Behaviors.EditableGridBehavior.Attach(grid);

            var window = new Window { Content = grid, Width = 1200, Height = 200 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var cells = grid.GetVisualDescendants().OfType<DataGridCell>().ToList();
            var combo = cells.SelectMany(c => c.GetVisualDescendants().OfType<ComboBox>()).First();
            // ⚠ Tylko edytory z klasy `field-editor`: kolumna Name to zwykły `DataGridTextColumn`,
            // którego `TextBox` istnieje wyłącznie w trybie edycji i mierzy 0.
            var editor = cells
                .SelectMany(c => c.GetVisualDescendants().OfType<TextBox>())
                .First(t => t.Classes.Contains("field-editor"));

            Assert.Equal(combo.Bounds.Height, editor.Bounds.Height);

            // Wiersz pozostaje własnością siatki — edytor go nie podnosi.
            var cellHeight = cells.First().Bounds.Height;
            Assert.True(editor.Bounds.Height <= cellHeight,
                $"Edytor prosi o {editor.Bounds.Height} px przy komórce {cellHeight} px — podniósłby wiersz.");

            window.Close();
        }, default);
    }

    /// <summary>
    /// ⭐⭐ POLE FILTRA NA POWIERZCHNI UNOSZĄCEJ SIĘ ODCINA SIĘ OD NIEJ JUŻ W SPOCZYNKU — próg 3:1 dla granicy
    /// nietekstowej (§10 / WCAG 1.4.11), w OBU motywach.
    ///
    /// <para>⚠⚠ Ten test istnieje, bo defekt wrócił. Pierwsza poprawka ruszyła samą ramkę i użytkownik zgłosił
    /// go PONOWNIE, na obu motywach. Pomiar pokazał wtedy dwie rzeczy naraz: selektor stosował się poprawnie
    /// (więc „nie działa" było mylące), a mimo to w motywie jasnym tło pola `#FCFCFD` stało na powierzchni
    /// `#FFFFFF` — <b>trzy jednostki</b> — i ramka dawała 2,55, czyli pod progiem. „Prawie widać" nie różni się
    /// dla użytkownika od „nie widać".</para>
    ///
    /// <para>⭐ Dlatego asercja jest na PROGU, nie na wartości: przetrwa dobranie odcienia i upadnie dokładnie
    /// wtedy, gdy pole znowu zacznie się zlewać. ⚠ I nie jest dowodem, że wygląda dobrze — kryterium jest ekran
    /// (R16); to jest podłoga, poniżej której nie wolno zejść.</para>
    /// </summary>
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task AFilterFieldOnARaisedSurface_StandsOutAtRest(string variant)
    {
        await _session.Dispatch(() =>
        {
            var box = new TextBox { Classes = { "search", "on-raised" }, Width = 200 };
            var window = new Window
            {
                Content = new Panel { Children = { box } },
                Width = 400,
                Height = 120,
                RequestedThemeVariant = variant == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light,
            };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var part = box.GetVisualDescendants().OfType<Border>()
                .FirstOrDefault(b => b.Name == "PART_BorderElement");
            Assert.NotNull(part);

            var surface = ColorOf(window.FindResource(window.ActualThemeVariant, "SurfaceRaisedBrush") as IBrush);
            var border = ColorOf(part!.BorderBrush);
            var fill = ColorOf(part.Background);

            var borderRatio = ContrastRatio(border, surface);
            Assert.True(borderRatio >= 3.0,
                $"{variant}: the field's resting border is {borderRatio:F2}:1 against the raised surface "
                + $"({border} on {surface}) — under the 3:1 floor for a non-text boundary.");

            // …and the fill recedes as well, so the field has a SHAPE and not just an outline. A bare outline is
            // what the first attempt shipped, and it was not enough.
            Assert.True(ContrastRatio(fill, surface) > 1.10,
                $"{variant}: the field's fill ({fill}) is indistinguishable from the surface ({surface}).");

            window.Close();
        }, default);
    }

    private static Color ColorOf(IBrush? brush)
        => brush is ISolidColorBrush s ? s.Color : Colors.Transparent;

    // Relative luminance per WCAG 2.x, and the ratio between two opaque colours.
    private static double ContrastRatio(Color a, Color b)
    {
        static double Channel(double v)
            => v <= 0.03928 ? v / 12.92 : System.Math.Pow((v + 0.055) / 1.055, 2.4);
        static double Luminance(Color c)
            => 0.2126 * Channel(c.R / 255.0) + 0.7152 * Channel(c.G / 255.0) + 0.0722 * Channel(c.B / 255.0);

        double la = Luminance(a), lb = Luminance(b);
        return (System.Math.Max(la, lb) + 0.05) / (System.Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// ⭐⭐ KARTA W WARSTWIE NAKŁADKI ZNIKA Z EKRANU, GDY EDYTOR, KTÓRY JĄ POKAZAŁ, ZOSTAŁ ODŁĄCZONY
    /// (zgłoszenie użytkownika 2026-08-03: karta zostawała na wierzchu NA ZAWSZE — nie usuwała jej zmiana
    /// zakładki, zejście kursorem ani klik, tylko restart aplikacji).
    ///
    /// <para><b>Mechanizm.</b> Karty (hover, Parameter Helper, menu akcji, podpowiedź konstrukcji) mieszkają
    /// w <c>OverlayLayer</c>, który należy do OKNA, więc przeżywa każdą zakładkę. Zamykanie szukało warstwy
    /// przez <c>GetOverlayLayer(_editor)</c> — a to odpowiada na pytanie „której warstwy ten edytor użyłby
    /// TERAZ". Po przełączeniu zakładki edytor jest odłączony, odpowiedź brzmi <c>null</c>, <c>Remove</c> nie
    /// robi nic, a wyzerowanie pola porzuca ostatnią referencję do karty wciąż wiszącej w nakładce.</para>
    ///
    /// <para>⭐ Reguła („usuwaj z panelu, który FAKTYCZNIE trzyma kartę") była już w kodzie — w <c>HideBulb</c>,
    /// razem z powodem. Brakowało jej w czterech pozostałych miejscach; to dokładnie ten kształt, co
    /// „częściowa kopia jednej wiedzy" (gotcha #302).</para>
    ///
    /// <para>⚠ Test odtwarza defekt bez kursora i bez sesji podpowiedzi: karta w nakładce + odłączony edytor,
    /// czyli te dwa fakty, na których stał defekt.</para>
    /// </summary>
    [Fact]
    public async Task AnOverlayCard_IsRemovedFromThePanelThatHoldsIt_EvenAfterItsEditorDetaches()
    {
        await _session.Dispatch(() =>
        {
            var editor = new TextBox();
            var host = new Panel { Children = { editor } };
            var window = new Window { Content = host, Width = 400, Height = 200 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var overlay = OverlayLayer.GetOverlayLayer(editor);
            Assert.NotNull(overlay);

            var card = new Border { Width = 80, Height = 20 };
            overlay!.Children.Add(card);

            // What a tab switch does to the content it replaces — and the overlay belongs to the WINDOW, so
            // the card stays in it.
            host.Children.Remove(editor);
            Dispatcher.UIThread.RunJobs();

            // The pre-fix close path: ask the DETACHED editor which overlay to clean up.
            Assert.Null(OverlayLayer.GetOverlayLayer(editor));
            Assert.Contains(card, overlay.Children);

            // The rule the fix applies everywhere: remove from the panel that actually holds it.
            (card.Parent as Panel)?.Children.Remove(card);
            Assert.DoesNotContain(card, overlay.Children);

            window.Close();
        }, default);
    }

    /// <summary>
    /// ⭐⭐ …I TEN SAM WYMIAR DLA EDYTORA, KTÓREGO NIE BUDUJE `FieldGridColumns` (zgłoszenie użytkownika
    /// 2026-08-03: „nadal są miejsca, gdzie TextBox jest zbyt niski, np. parametry procedury w Easy Mode").
    ///
    /// <para>⚠ Test wyżej celowo pomija kolumnę Name — z komentarzem „jej `TextBox` istnieje wyłącznie w trybie
    /// edycji i mierzy 0". To było prawdziwe stwierdzenie o teście, ale przeczytane jako stwierdzenie o
    /// APLIKACJI zamykało sprawę o jedną kolumnę za wcześnie: Name, Collate, Default i Description to zwykłe
    /// <c>DataGridTextColumn</c>, ich edytor tworzy sama siatka i nie ma na czym postawić klasy
    /// <c>field-editor</c>. Zostawały więc na <c>MinHeight</c> 0 — w PIERWSZEJ i najczęściej edytowanej
    /// kolumnie.</para>
    ///
    /// <para>⭐ Dlatego ten test wchodzi w tryb edycji, czyli mierzy element, który istnieje tylko wtedy, i
    /// porównuje go z `ComboBoxem` obok — tą samą asercją rodziny, nie liczbą.</para>
    /// </summary>
    [Fact]
    public async Task FieldGridEditors_EvenTheOnesTheGridCreatesItself_ShareThatHeight()
    {
        await _session.Dispatch(() =>
        {
            var row = EmberTern.App.ViewModels.ProcedureVariableRowViewModel.From(
                new EmberTern.Core.Sql.ProcedureVariable { Name = "V", TypeText = "VARCHAR(20)" });

            var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { row } };
            EmberTern.App.Views.FieldGridColumns.Build(grid, includeDefault: true);
            // ⭐ The height role comes from the ONE seam, not from Build (S-3, 2026-08-05) — mirrored here so
            // the test exercises the same path the views do.
            EmberTern.App.Behaviors.EditableGridBehavior.Attach(grid);

            var window = new Window { Content = grid, Width = 1200, Height = 200 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The seam marks the grid, which is what lets a style reach an editor it does not construct.
            Assert.Contains(EmberTern.App.Behaviors.EditableGridBehavior.FieldGridClass, grid.Classes);

            var combo = grid.GetVisualDescendants().OfType<DataGridCell>()
                .SelectMany(c => c.GetVisualDescendants().OfType<ComboBox>()).First();

            // Enter edit mode on the Name column — the only way its TextBox exists at all.
            grid.SelectedIndex = 0;
            grid.CurrentColumn = grid.Columns[0];
            grid.BeginEdit();
            Dispatcher.UIThread.RunJobs();

            // ⚠⚠ NOT EVERY TextBox UNDER A CELL IS A CELL EDITOR, and this exclusion was MEASURED, not assumed.
            // Avalonia's ComboBox template carries its own TextBox (type-ahead input), unrealized while the
            // control is idle — so it reports height 0 and the base control padding (8,0 rather than the cell
            // editor's 6,0). Left in, it made this assertion fail against a perfectly correct application; two
            // guesses at what it was (a SearchableComboBox popup filter, then a Popup boundary) were both wrong,
            // and printing the ancestor chain settled it in one run. An unrealized control has no height to judge
            // — the "added ≠ paints" trap of gotcha #251, met from the test side.
            var editing = grid.GetVisualDescendants().OfType<DataGridCell>()
                .SelectMany(c => c.GetVisualDescendants().OfType<TextBox>())
                .Where(t => !t.Classes.Contains("field-editor"))
                .Where(t => t.FindAncestorOfType<ComboBox>() is null)
                .Where(t => t.FindAncestorOfType<EmberTern.App.Controls.SearchableComboBox>() is null)
                .ToList();

            Assert.NotEmpty(editing);
            var report = string.Join("; ", editing.Select(b =>
                $"h={b.Bounds.Height} min={b.MinHeight} pad={b.Padding}"));
            foreach (var box in editing)
            {
                Assert.True(combo.Bounds.Height == box.Bounds.Height,
                    $"combo={combo.Bounds.Height}  editors: {report}");
            }

            grid.CancelEdit();
            window.Close();
        }, default);
    }

    /// <summary>
    /// ⭐ Pasek postępu sekcji 4 trzyma swoją STAŁĄ szerokość (§8.4.6, §19.7).
    ///
    /// <para>⚠⚠ Test istnieje z powodu jednej konkretnej pułapki: <b>Avalonia przycina <c>Width</c>
    /// przez <c>MinWidth</c></b>, a Fluent nadaje <c>ProgressBar</c> własne minimum. Bez
    /// <c>MinWidth=0</c> w stylu deklaracja „120 px" po cichu wyszłaby szersza — i nic by nie zawiodło.
    /// To dokładnie ten defekt, którym M2b zapłacił strzałkę drzewa metadanych (20 px urosło do 100 przez
    /// <c>MinWidth</c> na bazowym <c>Button</c>).</para>
    ///
    /// <para>⭐ Stała szerokość nie jest estetyką, tylko warunkiem układu: pasek rosnący z treścią
    /// przesuwałby chipy stanu przy każdej operacji, czyli §13.3 („Zero Layout Shift") rozłożony
    /// w czasie.</para>
    /// </summary>
    [Fact]
    public async Task StatusProgressBar_KeepsItsFixedSize_DespiteFluentsMinimums()
    {
        await _session.Dispatch(() =>
        {
            var bar = new ProgressBar { Classes = { "status" }, IsIndeterminate = true };
            // ⚠ W kontenerze, który NIE rozciąga — inaczej mierzylibyśmy okno, a nie kontrolkę.
            var host = new StackPanel { Orientation = Orientation.Horizontal, Children = { bar } };
            var window = new Window { Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(120, bar.Bounds.Width);
            Assert.Equal(4, bar.Bounds.Height);

            window.Close();
        }, default);
    }

    /// <summary>
    /// ⭐⭐ Znak debuggera JEST ikoną Execute — ta sama geometria, nie kopia (§19.6).
    ///
    /// <para>Decyzja użytkownika w rundzie QA M3.1e: <i>„Debugger powinien być po prostu ikoną Execute
    /// z dodaną czerwoną kropką, a nie osobnym symbolem"</i>. <c>DebuggerIcon</c> nie ma więc własnej
    /// ścieżki — jego <c>Path.Data</c> to <c>{StaticResource Icon.Play}</c>.</para>
    ///
    /// <para>⚠⚠ Ten test istnieje, bo POMIAR pokazał, że dotąd tak NIE było: znak nosił własny trójkąt
    /// <c>(6,4)(18,12)(6,20)</c>, a <c>Icon.Play</c> to <c>(8,5)(19,12)(8,19)</c>. Rodzina była
    /// przybliżeniem utrzymywanym ręcznie i rozjechała się przy pierwszej próbie poprawienia kropki —
    /// **bez żadnego sygnału**, bo dwie osobne ścieżki nie mają jak o sobie wiedzieć.</para>
    ///
    /// <para>⭐ Asercja jest o TOŻSAMOŚCI INSTANCJI, nie o równości kształtu, i to jest celowe:
    /// wpisana ścieżka o identycznych współrzędnych przeszłaby test na równość, a przywróciłaby
    /// dokładnie tę możliwość rozjazdu, którą referencja usuwa.</para>
    ///
    /// <para>⚠ Build tego nie pokrywa: <c>{StaticResource}</c> wewnątrz <c>ControlTemplate</c> rozwiązuje
    /// się przy instancjonowaniu szablonu, więc kontrolkę trzeba naprawdę zbudować i pokazać.</para>
    /// </summary>
    [Fact]
    public async Task DebuggerIcon_IsTheExecuteIcon_ByReferenceNotByCopy()
    {
        await _session.Dispatch(() =>
        {
            var icon = new EmberTern.App.Controls.DebuggerIcon();
            var window = new Window { Content = icon };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // ⚠ Kwalifikowana nazwa: `Path` jest niejednoznaczne (Avalonia.Controls.Shapes vs System.IO).
            var triangle = icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            Assert.NotNull(triangle.Data);

            // ⚠ `StreamGeometry`, nie `Geometry` — `Token<T>` porównuje typ DOKŁADNIE (`Assert.IsType`),
            // a w katalogu ikony są `StreamGeometry`.
            Assert.Same(Token<StreamGeometry>("Icon.Play"), triangle.Data);

            // Druga połowa znaku: kropka przerwania. Pinujemy, że w ogóle jest — trójkąt bez kropki to
            // po prostu ikona Execute i nic by nie zawiodło.
            var dot = icon.GetVisualDescendants().OfType<Ellipse>().Single();
            Assert.NotNull(dot.Fill);

            window.Close();
        }, default);
    }

    /// <summary>
    /// ⭐ Pędzle chipów stanu (§8.4.3 sekcja 3, §19.5–§19.6) — rozwiązują się w OBU motywach i są
    /// NIEPRZEZROCZYSTE.
    ///
    /// <para>⚠⚠ Nieprzezroczystość jest tu asercją o KONTRAŚCIE, nie o stylu, i pilnuje decyzji
    /// podjętej w M3.1e. Chip debuggera świadomie NIE dziedziczy pędzla railu: railowy
    /// <c>DebugCurrentLineBarBrush</c> ma α 0,90 (Dark) / 0,80 (Light), bo zaprojektowano go jako
    /// pasek bieżącej linii w edytorze. Na tle <c>PanelBrush</c> daje to 3,77:1 — wystarczy dla
    /// 2 px railu (próg §10 dla elementu UI to 3:1), ale NIE dla tekstu 10 px (próg 4,5:1).</para>
    ///
    /// <para>⭐ Dlatego „ujednolicenie" chipa z railem — zmiana wyglądająca na porządkowanie — obniżyłoby
    /// kontrast poniżej progu **bez żadnego sygnału**: `{DynamicResource}` przy nieznanym kluczu nic nie
    /// rzuca, a przy kluczu ISTNIEJĄCYM, lecz półprzezroczystym, nie rzuca tym bardziej. Ten test jest
    /// jedynym miejscem, które to zauważy.</para>
    ///
    /// <para>⚠ Nie liczy samego kontrastu — strażnik progów §10 dla całej aplikacji to osobna praca
    /// infrastrukturalna, świadomie odłożona przez użytkownika poza M3.1e. Tu pilnujemy warunku
    /// koniecznego, który jest tani i bezdyskusyjny.</para>
    /// </summary>
    [Theory]
    [InlineData("TransactionActiveBrush")]  // chip transakcji (M3.1d)
    [InlineData("ErrorBrush")]              // chip transakcji w stanie błędu
    [InlineData("AccentIconBrush")]         // chip debuggera (M3.1e)
    [InlineData("IconColor_Query")]         // chip Trace (M3.1e)
    public async Task StatusBarChipBrush_ResolvesInBothThemes_AndIsOpaque(string key)
    {
        await _session.Dispatch(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                var brush = ThemeToken<SolidColorBrush>(key, variant);
                Assert.NotNull(brush);
                Assert.True(
                    brush.Color.A == 255,
                    $"Pędzel tekstu chipa `{key}` jest półprzezroczysty w motywie {variant} "
                    + $"(α = {brush.Color.A}). Nakładanie się na tło obniża kontrast poniżej progu §10 "
                    + "dla tekstu (4,5:1) w sposób niewidoczny dla buildu i pozostałych testów.");
            }
        }, default);
    }

    /// <summary>
    /// ⭐ Język kolorów, krok K1 — rola <b>R‑1 „Uruchom"</b> ma WŁASNY token w obu motywach
    /// (<c>color-language.md</c> §3, §7.3, §11.2).
    ///
    /// <para>⭐ Test pilnuje dwóch rzeczy i obie są trwałe. Po pierwsze <c>ActionRunBrush</c> istnieje
    /// w Dark i w Light — token obecny tylko w jednym słowniku kompiluje się, renderuje w palecie,
    /// której akurat używa autor, i nie maluje nic w drugiej (reguła 3 UI; ta sama klasa błędu co
    /// gotcha #250). Po drugie <c>ActionRunColor</c> jest <b>osobnym kluczem</b>, a nie aliasem
    /// <c>{StaticResource SuccessIconColor}</c> — i to jest cała treść decyzji W4: alias znaczyłby
    /// „Uruchom to kolor sukcesu", więc przyszłe przestrojenie zieleni Commita przesunęłoby po cichu
    /// także Execute.</para>
    ///
    /// <para>⛔ Test świadomie NIE przypina równości <c>ActionRunColor == SuccessIconColor</c>, choć
    /// dziś zachodzi. Ta równość jest <b>chwilowa z założenia</b> (§7.3: „na razie ten sam odcień") —
    /// przypięta stałaby się blokadą dokładnie tej zmiany, dla której token powstał, czyli testem
    /// pilnującym, żeby projekt się nie wydarzył. Zerowa różnica wizualna kroku K1 jest
    /// <b>jednorazowym pomiarem odbiorczym</b> (<c>product-polish.md</c> §19.15), nie inwariantem.</para>
    /// </summary>
    [Fact]
    public async Task ActionRunBrush_IsItsOwnRoleToken_InBothThemes()
    {
        await _session.Dispatch(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                var brush = ThemeToken<SolidColorBrush>("ActionRunBrush", variant);
                Assert.Equal(255, brush.Color.A);

                // Własny klucz koloru — dowód, że rola nie jest aliasem koloru sukcesu (W4).
                var own = ThemeToken<Color>("ActionRunColor", variant);
                Assert.Equal(own, brush.Color);
            }
        }, default);
    }

    /// <summary>
    /// ⭐ Język kolorów, krok K7 — role <b>R‑2 „Zatwierdź"</b> i <b>R‑3 „Wycofaj transakcję"</b> mają
    /// wartość <b>dostrojoną OSOBNO dla każdego motywu</b> (<c>color-language.md</c> §7.2).
    ///
    /// <para>⚠⚠ To jest dokładnie ten defekt, od którego §7.2 kazało zacząć K7, a nie od podmiany
    /// odwołań: oba tokeny niosły surowy Material Design (<c>#4CAF50</c> / <c>#F44336</c>)
    /// <b>identyczny w Dark i Light</b>, wstawiony na zapas i nigdy nieużyty. Ten sam odcień nie ma
    /// poprawnego kontrastu na obu tłach, więc przyjęcie ich wprost dałoby regres kontrastu w motywie
    /// jasnym — klasa problemu V‑1.</para>
    ///
    /// <para>⭐ Test pilnuje WARUNKU, nie wartości: „te dwa tokeny są dostrojone per motyw". Wartości
    /// wolno przestrajać (po to K7 nadał rolom własne tokeny), ale powrót do jednej wartości w obu
    /// słownikach jest powrotem defektu — i jest niewidoczny dla buildu, bo token istnieje w obu
    /// motywach i poprawnie się rozwiązuje.</para>
    ///
    /// <para>⛔ Nie porównuje z <c>SuccessIconColor</c> / <c>DangerIconColor</c>, choć dziś są równe —
    /// z tego samego powodu, dla którego nie robi tego pin dla <c>ActionRunBrush</c>: równość jest
    /// stanem bieżącym, a rozdzielenie ról jest właśnie tym, po co te tokeny powstały.</para>
    /// </summary>
    [Theory]
    [InlineData("CommitButtonBrush")]
    [InlineData("RollbackButtonBrush")]
    public async Task TransactionRoleBrush_IsTunedPerTheme(string key)
    {
        await _session.Dispatch(() =>
        {
            var dark = ThemeToken<SolidColorBrush>(key, ThemeVariant.Dark).Color;
            var light = ThemeToken<SolidColorBrush>(key, ThemeVariant.Light).Color;

            Assert.Equal(255, dark.A);
            Assert.Equal(255, light.A);
            Assert.True(dark != light,
                $"`{key}` niesie tę samą wartość ({dark}) w obu motywach. Token roli transakcyjnej stoi "
                + "na chromie, która zmienia się z motywem, więc jedna wartość nie może mieć poprawnego "
                + "kontrastu na obu tłach (color-language.md §7.2 — powód, dla którego K7 zaczął się od "
                + "wartości, a nie od podmiany odwołań).");
        }, default);
    }

    /// <summary>
    /// ⭐ Kropka stanu połączenia siedzi w OSI swojego wiersza.
    ///
    /// <para>⚠⚠ <b>Ten test celowo NIE sprawdza wyrównania tekstu ani badge'a, choć jego pierwsza
    /// wersja to robiła — i była WPROST szkodliwa.</b> Porównywała środki PUDEŁEK trzech elementów,
    /// przechodziła na zielono, a na ekranie tekst nadal siedział niżej od kropki (odbiór użytkownika,
    /// 2026-08-03: <i>„użytkownik nie patrzy na środki geometryczne elementów — patrzy na efekt
    /// optyczny"</i>). Test, który świeci na zielono przy złym ekranie, jest gorszy niż brak testu:
    /// zamyka temat, zamiast go otworzyć.</para>
    ///
    /// <para>⭐ Powód, dla którego pudełko kłamie: wysokość <c>TextBlocka</c> to INTERLINIA, a nie
    /// wysokość farby — dolne ~4 px to obszar znaków schodzących, w tym napisie pusty. Farba leży więc
    /// nisko w pudełku i wyrównanie pudełek zostawia widoczny rozjazd. To samo dotyczy badge'a, gdzie
    /// wersaliki siedzą wysoko w swoim pudełku.</para>
    ///
    /// <para>⭐ <b>Kropka jest jedynym elementem tego wiersza, dla którego pudełko JEST farbą</b> —
    /// i dlatego jest jedynym, o którym maszyna ma coś sensownego do powiedzenia. Reszta bloku niesie
    /// świadomą korektę optyczną (<c>TranslateTransform</c> na tekście), której <b>kryterium odbioru
    /// jest ekran, nie liczba</b> (R8). ⛔ Nie „wzmacniaj" tego testu z powrotem o tekst i badge.</para>
    /// </summary>
    [Fact]
    public async Task StatusBarConnectionDot_SitsOnTheRowAxis()
    {
        await _session.Dispatch(() =>
        {
            var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, UseLayoutRounding = false };
            var text = new TextBlock { Text = "Szkoleniowa", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(dot);
            row.Children.Add(text);

            var window = new Window { Content = new Border { Height = 24, Child = row }, Width = 400, Height = 60 };
            window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            var dotMid = dot.Bounds.Top + dot.Bounds.Height / 2;
            var rowMid = row.Bounds.Height / 2;
            window.Close();

            Assert.True(System.Math.Abs(dotMid - rowMid) < 0.25,
                $"Kropka nie leży w osi wiersza: {dotMid:0.00} wobec {rowMid:0.00}. Element o wysokości "
                + "NIEPARZYSTEJ (7) w wierszu o PARZYSTEJ ląduje na połówce piksela, a UseLayoutRounding "
                + "przycina go w górę. Rozwiązaniem jest UseLayoutRounding=\"False\" — koło nie ma prostej "
                + "krawędzi, której przyciąganie miałoby bronić (precedens: PART_MarkArea w RadioButton).");
        }, default);
    }

    /// <summary>
    /// M3.5 / Z-1 — a DISABLED <c>Button.icon</c> paints no background and no border.
    ///
    /// <para>⭐ Why this cannot be read off the stylesheet. <c>Button.icon</c> sets
    /// <c>Background</c>/<c>BorderBrush</c> to Transparent <b>on the control</b>, while FluentTheme's
    /// <c>:disabled</c> style paints <c>/template/ ContentPresenter</c> with <c>ButtonBackgroundDisabled</c> —
    /// and a style targeting the template child BEATS a setter on the control. Both facts are visible in the
    /// source and their <i>interaction</i> is not; only the applied value on the element that actually paints
    /// answers the question. Measured in the §13.3 gate: the four disabled pagination buttons rendered as
    /// filled chips while the three enabled ones were bare.</para>
    ///
    /// <para>⚠ Asserted on the presenter, not on the Button — same reasoning as the FluentBridge tests:
    /// the assertion has to read the brush from the element that paints it.</para>
    /// </summary>
    [Fact]
    public async Task DisabledIconButton_PaintsNoChrome()
    {
        await _session.Dispatch(() =>
        {
            var enabled = new Button { Classes = { "icon" }, Content = "A" };
            var disabled = new Button { Classes = { "icon" }, Content = "B", IsEnabled = false };
            var window = new Window { Content = new StackPanel { Children = { enabled, disabled } } };
            window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            var presenter = disabled.GetVisualDescendants().OfType<ContentPresenter>().First();
            var background = presenter.Background;
            var border = presenter.BorderBrush;
            window.Close();

            Assert.True(IsTransparent(background),
                "Wyłączony `Button.icon` maluje TŁO: " + Describe(background) + ".\n"
                + "To `:disabled` Fluenta celujące w `/template/ ContentPresenter` (Bridge przypina tam "
                + "`ButtonBackgroundDisabled` → `PanelColor`); setter na kontrolce z bloku `Button.icon` "
                + "przegrywa, a `Opacity 0.4` tylko przygasza pastylkę. Potrzebny jest setter na TYM elemencie.\n"
                + "⛔ Nie naprawiać w Bridge'u — `Button.flat`/`Button.primary` MAJĄ w tym stanie wyglądać "
                + "jak przyciski.");

            Assert.True(IsTransparent(border),
                "Wyłączony `Button.icon` maluje KRAWĘDŹ: " + Describe(border) + ". Powód i lekarstwo jak wyżej.");
        }, default);
    }

    /// <summary>
    /// M3.5 / Z-2 — an unchecked CheckBox's outline clears §10's 3:1 floor against its own background,
    /// in BOTH themes.
    ///
    /// <para>⭐⭐ Pins the THRESHOLD, not the value. The hex may move (§4.2.4 — role before value); what may
    /// never move is the reason the token exists. Before M3.5 the outline took <c>BorderBrush</c> and measured
    /// 1.60:1 in Dark and 1.35:1 in Light — a control the user must click, invisible in its DEFAULT state.
    /// Asserting <c>#6A6A70</c> would duplicate the catalog and go green on a value that had drifted below the
    /// floor; asserting the ratio cannot.</para>
    ///
    /// <para>⚠ Both themes, because the two are independent values and the Light one was the worse of the pair
    /// — and because a brush looked up without a <see cref="ThemeVariant"/> returns UNSET (gotcha #250).</para>
    /// </summary>
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task UncheckedControlOutline_ClearsTheContrastFloor(string variantName)
    {
        // ⚠ `Application.Current` istnieje wyłącznie na wątku sesji headless, więc KAŻDE odczytanie
        //    zasobu motywu musi być w `Dispatch` — poza nim `ThemeToken` widzi null i test zawodzi
        //    z powodu, który nie ma nic wspólnego z jego przedmiotem.
        var variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var ratio = await _session.Dispatch(() =>
        {
            // ⚠⚠ ODCZYT Z ELEMENTU, KTÓRY MALUJE, nie z tokenu — i to jest warunek poprawności testu,
            //    nie jego staranność. Pierwsza wersja czytała `ControlOutlineBrush` z zasobów i liczyła
            //    jego kontrast: taki test przechodzi na ZIELONO, gdy ktoś cofnie `CheckBox` na
            //    `BorderBrush`, bo token nadal ma dobrą wartość, tylko nikt go nie używa. Pinuje więc
            //    dwie rzeczy naraz: że kontrolka bierze właściwą rolę I że ta rola spełnia próg.
            var box = new CheckBox { Content = "x", IsChecked = false };
            var window = new Window { Content = box, RequestedThemeVariant = variant };
            window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            var outline = box.GetVisualDescendants()
                .OfType<Border>()
                .First(b => b.Name == "NormalRectangle");
            var painted = Assert.IsAssignableFrom<ISolidColorBrush>(outline.BorderBrush);
            var surface = ThemeToken<SolidColorBrush>("BackgroundBrush", variant);
            window.Close();

            return ContrastRatio(painted.Color, surface.Color);
        }, default);

        Assert.True(ratio >= 3.0,
            $"Kontur kontrolki w motywie {variantName} ma {ratio:0.00}:1 wobec własnego tła, "
            + "a §10 wymaga 3:1 dla znaczącego elementu nietekstowego.\n"
            + "To NIE jest test wartości — hex może się zmieniać (§4.2.4). Testem jest powód istnienia roli: "
            + "niezaznaczony `CheckBox` musi być widoczny w swoim stanie DOMYŚLNYM. Na `BorderBrush` "
            + "(stan przed M3.5) było 1,60:1 w Dark i 1,35:1 w Light.");
    }

    /// <summary>
    /// M3.5 / Z-6 — the composite create icon is reachable and both of its badge brushes resolve, in both
    /// themes. ⚠ A ControlTheme keyed by type is exactly the kind of thing that compiles, builds and then
    /// renders NOTHING when its dictionary is not merged (the M3.3b lesson: a missing dictionary does not
    /// fail, it silently removes the element).
    /// </summary>
    [Fact]
    public async Task CreateIcon_AppliesItsTemplate_AndBadgeBrushesResolve()
    {
        await _session.Dispatch(() =>
        {
            // Oba pędzle badge'a, w OBU motywach — pędzel odczytany bez `ThemeVariant` zwraca UNSET
            // i `SvgIcon`/`CreateIcon` nie malują wtedy nic, przy zdrowo wyglądającym stanie (#250).
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                ThemeToken<SolidColorBrush>("AccentBrush", variant);
                ThemeToken<SolidColorBrush>("OnAccentBrush", variant);
            }

            var icon = new EmberTern.App.Controls.CreateIcon
            {
                Data = Geometry.Parse("M5 3 H19 A2 2 0 0 1 21 5 V19 A2 2 0 0 1 19 21 H5 A2 2 0 0 1 3 19 V5 A2 2 0 0 1 5 3 Z"),
            };
            var window = new Window { Content = icon };
            window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            var paths = icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Count();
            var discs = icon.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>().Count();
            window.Close();

            Assert.True(paths == 2 && discs == 1,
                $"`CreateIcon` nie złożyło się: ścieżek {paths} (oczekiwano 2 — glif + plus), "
                + $"dysków {discs} (oczekiwano 1). Najczęstsza przyczyna: ControlTheme nieosiągalny, "
                + "bo `IconGeometries.axaml` nie jest scalony — wtedy kontrolka nie zawodzi, tylko "
                + "renderuje NIC.");
        }, default);
    }

    /// <summary>
    /// ⭐⭐ M4.3c — przełącznik segmentowy. To jest test <b>BEHAWIORALNY</b> w sensie, w jakim wymaga go
    /// lekcja M4.2b: mierzy <b>ZREALIZOWANY</b> przycisk, a nie źródło.
    ///
    /// <para>⚠⚠ POWÓD, DLA KTÓREGO STRAŻNIK ŹRÓDŁOWY BY TU NIE WYSTARCZYŁ: M4.3c przeniosło `Button.seg`
    /// z dwóch bloków `UserControl.Styles` do `ControlStyles.axaml`, a realnym ryzykiem przeniesienia jest
    /// to, że styl <b>przestanie docierać do kontrolki</b> — a tego źródło nie pokazuje. Strażnik czytający
    /// tekst potwierdzi „styl istnieje, selektor się zgadza" również wtedy, gdy na ekranie nie działa;
    /// w M4.2b pięciu takich zielonych strażników opisywało PUSTY EKRAN (#338).</para>
    ///
    /// <para>⚠⚠ <b>SPROSTOWANIE WŁASNEJ PRZESŁANKI — zmierzone w M4.3c podsadzeniem.</b> Ten test powstał
    /// z założeniem, że po przeniesieniu o zwycięstwie `.seg` z bazowym `<Style Selector="Button">`
    /// decyduje KOLEJNOŚĆ w pliku. <b>Nieprawda:</b> bazowy `Button` z `Padding="99,99"` postawiony PO bloku
    /// `.seg` nie nadpisał `8,3`. Avalonia rozstrzyga między stylami <b>specyficznością selektora</b> —
    /// selektor z klasą bije goły selektor typu niezależnie od pozycji. ⚠ Mechanizm regresji §19.2 to co
    /// innego: tam styl przegrał z WARTOŚCIĄ LOKALNĄ na elemencie, a ta bije każdy setter niezależnie od
    /// tego, gdzie styl mieszka. Warunkiem bezpieczeństwa tego przeniesienia jest więc brak wartości
    /// lokalnych na segmentach, a nie pozycja bloku.</para>
    ///
    /// <para>⭐ Asercje geometrii ZOSTAJĄ mimo obalenia tamtej przesłanki, bo pilnują czego innego i nadal
    /// realnego: że segment kasuje geometrię bazowego przycisku. Gdyby `.seg` przestał docierać, segmenty
    /// dostałyby `Radius.Surface` + `Border.All`, czyli własny zaokrąglony obrys wewnątrz wspólnej ramki —
    /// i to jest defekt, który te dwie pary asercji nazywają po imieniu.</para>
    ///
    /// <para>⚠ Test dokłada się do klasy JUŻ obecnej w filtrze partycji headless, zamiast zakładać nową —
    /// ta lista nazw jest krucha i utrzymywana ręcznie (#94/#226/#286, precedens M3.5).</para>
    /// </summary>
    [Fact]
    public async Task SegmentedButton_TakesItsGeometryFromTheSharedStyle_NotFromTheBaseButton()
    {
        await _session.Dispatch(() =>
        {
            var rest = new Button { Content = "Chronologia", Classes = { "seg" } };
            var active = new Button { Content = "Transakcje", Classes = { "seg", "active" } };

            // Odtworzona STRUKTURA z obu ekranów: pasek chromy → ramka z `ClipToBounds` → segmenty.
            // ⚠ Pasek jest częścią przypadku, a nie dekoracją: to `Border.chrome Button` nadaje segmentowi
            //   wysokość, więc bez niego test mierzyłby inną kontrolkę niż ta w produkcie.
            var frame = new Border
            {
                ClipToBounds = true,
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { rest, active },
                },
            };
            var window = new Window { Content = new Border { Classes = { "chrome" }, Child = frame } };
            window.Show();
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

            var variant = window.ActualThemeVariant;

            // ── (1) PRIORYTET: segment kasuje geometrię bazowego `Button`. Ta para asercji jest całym
            //        powodem istnienia tego testu.
            Assert.Equal(new CornerRadius(0), rest.CornerRadius);
            Assert.NotEqual(Token<CornerRadius>("Radius.Surface"), rest.CornerRadius);
            Assert.Equal(new Thickness(0), rest.BorderThickness);
            Assert.NotEqual(Token<Thickness>("Border.All"), rest.BorderThickness);

            // ── (2) Obie kopie stylu zniknęły, więc oba segmenty MUSZĄ być identyczne geometrycznie.
            //        Asercja relacyjna — to rozjazd 8,3 vs 10,3 był defektem, nie konkretna liczba.
            Assert.Equal(rest.Padding, active.Padding);
            Assert.Equal(rest.CornerRadius, active.CornerRadius);
            // ⚠ A to jest już ratyfikowana DECYZJA (R18 — przy równej czytelności wygrywa gęstszy),
            //   więc zasługuje na własną asercję: gdyby ktoś wrócił do 10, ma się dowiedzieć.
            Assert.Equal(new Thickness(8, 3), rest.Padding);

            // ── (3) WYSOKOŚĆ ROZSTRZYGA KONTENER (reguła #10) — zmierzone, nie założone: styl segmentu
            //        celowo nie deklaruje `MinHeight`, bierze ją z `Border.chrome Button`.
            Assert.Equal(Token<double>("Size.ControlToolbar"), rest.MinHeight);

            // ── (4) Stan aktywny naprawdę się odróżnia — i to jest jedyna rzecz, którą użytkownik czyta
            //        z tej kontrolki („który segment jest wybrany").
            Assert.Equal(FontWeight.SemiBold, active.FontWeight);
            Assert.NotEqual(active.FontWeight, rest.FontWeight);
            Assert.Equal(
                Describe(ThemeToken<SolidColorBrush>("SelectionBrush", variant)),
                Describe(active.Background));
            Assert.Equal(
                Describe(ThemeToken<SolidColorBrush>("SubtleForegroundBrush", variant)),
                Describe(rest.Foreground));

            // ── (5) I ostatecznie: kontrolka SIĘ ZREALIZOWAŁA. W M4.2b pięciu zielonych strażników
            //        opisywało pusty ekran, więc „ma poprawne właściwości" i „w ogóle się narysowało"
            //        są dwiema różnymi asercjami.
            var height = rest.Bounds.Height;
            var width = rest.Bounds.Width;
            window.Close();

            Assert.True(height > 0 && width > 0,
                $"Segment nie zajal miejsca w ukladzie (H={height}, W={width}) — kontrolka o poprawnych "
                + "wlasciwosciach, ktora sie nie renderuje, jest dokladnie przypadkiem z M4.2b (#338).");
        }, default);
    }

    /// <summary>
    /// ⭐ Druga połowa M4.3c: styl ma ZOSTAĆ jeden. Strażnik źródłowy jest tu na miejscu, bo pilnuje
    /// nie tego, jak coś wygląda, tylko ILU jest właścicieli — a to jest fakt o tekście, nie o renderze.
    /// <para>⚠ Kopia lokalna w widoku wygrywałaby ze wspólnym stylem przez bliskość w drzewie i przywróciła
    /// dokładnie ten rozjazd, który ten etap usunął — przy zielonym teście behawioralnym powyżej, bo tamten
    /// buduje segment BEZ widoku.</para>
    /// </summary>
    [Fact]
    public void NoView_DeclaresItsOwnSegmentedButtonStyle()
    {
        var appRoot = System.IO.Path.Combine(RepositoryRoot(), "src", "EmberTern.App");
        var offenders = new List<string>();

        foreach (var folder in new[] { "Views", "Controls" })
        {
            var root = System.IO.Path.Combine(appRoot, folder);
            foreach (var file in System.IO.Directory.EnumerateFiles(root, "*.axaml", System.IO.SearchOption.AllDirectories))
            {
                var text = Regex.Replace(System.IO.File.ReadAllText(file), "<!--.*?-->", " ", RegexOptions.Singleline);
                if (Regex.IsMatch(text, @"<Style\s+Selector=""Button\.seg"))
                {
                    offenders.Add(System.IO.Path.GetRelativePath(appRoot, file).Replace('\\', '/'));
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Widok deklaruje wlasny styl `Button.seg`:\n  " + string.Join("\n  ", offenders)
            + "\n\nPo M4.3c jest JEDEN wspolny styl w `Themes/ControlStyles.axaml`. Kopia lokalna wygrywa "
            + "z nim przez bliskosc w drzewie, wiec przywraca rozjazd, ktory ten etap usunal (Session 8,3 "
            + "vs Trace 10,3 — przy komentarzu deklarujacym, ze oba ekrany mowia tym samym jezykiem).");
    }

    private static string RepositoryRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool IsTransparent(IBrush? brush)
        => brush is null || (brush is ISolidColorBrush s && s.Color.A == 0);

    private static string Describe(IBrush? brush)
        => brush is ISolidColorBrush s ? s.Color.ToString() : brush?.ToString() ?? "null";

    /// <summary>
    /// The same lookup for a THEME-SCOPED resource. ⚠ Measured, and worth knowing: the variant-less
    /// <see cref="Token{T}"/> above cannot see anything declared inside <c>ThemeDictionaries</c> — it reports the
    /// key as missing. That is precisely the line between the two colour-free dictionaries added in M2a
    /// (<c>Tokens</c>/<c>Typography</c>: one value, no variant) and <c>Colors.axaml</c> (one value per theme).
    /// Two kinds of resource, two lookups.
    /// </summary>
    // ─── M5 / §10 — kontrast całej mapy severity ──────────────────────────────────────────────────────
    //
    // ⭐⭐ POWÓD ISTNIENIA: przed M5 trzy z ośmiu kombinacji (severity × motyw) malowały TEKST 12 px pod
    //   progiem 4,5:1 — Light/Warning 3,12 · Light/Success 3,88 · Dark/Error 4,26 — i nic tego nie
    //   wykrywało, bo katalog bez strażnika odrasta (gotcha #284, precedens `Alt+F`).
    //
    // ⭐ DWA RÓŻNE PROGI, BO TO DWA RÓŻNE PYTANIA (§10): ten sam pędzel maluje w banerze TEKST (12 px,
    //   więc 4,5:1) ORAZ pasek i ikonę (element nietekstowy niosący znaczenie, więc 3:1). Rozdzielenie
    //   ich jest warunkiem poprawności: wspólny próg 4,5 zgłaszałby paski, które są w porządku,
    //   a wspólny próg 3,0 przepuściłby dokładnie ten defekt, dla którego ten test powstał.
    //
    // ⚠⚠ §10 wiersz „≥ 12 px SemiBold → 3:1" to WYMÓG WŁASNY EmberTerna, NIE „WCAG AA Large"
    //   (sprostowane w M5: WCAG wymaga 24 px albo 18,7 px bold, a najwyższa rola to 23 px). Dlatego
    //   tekst banera — 12 px Normal — podlega tu 4,5:1 BEZ WYJĄTKU i nie wolno go obniżyć powołując
    //   się na tamten wiersz.

    public static TheoryData<string> Themes => new() { "Dark", "Light" };

    /// <summary>
    /// TEKST komunikatu w <see cref="MessageBanner"/> trzyma próg 4,5:1 wobec własnego tła — dla KAŻDEJ
    /// z czterech wartości <see cref="MessageSeverity"/> i w OBU motywach.
    /// <para>⚠⚠ Odczyt jest Z ELEMENTU, KTÓRY MALUJE, nie z tokenu — ta sama reguła, którą zapisał
    /// <see cref="UncheckedControlOutline_ClearsTheContrastFloor"/>. Test czytający `ErrorBrush` z zasobów
    /// przechodziłby na zielono, gdyby ktoś przepiął `BrushKeyFor` na inny klucz albo gdyby styl przestał
    /// nadawać banerowi `PanelBrush`: token miałby nadal dobrą wartość, tylko nikt by go nie używał.
    /// Pinowane są więc trzy rzeczy naraz — mapowanie, tło ze stylu i wartość pędzla.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public async Task SeverityText_OnTheBanner_ClearsTheTextContrastFloor(string variantName)
    {
        var variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var failures = await _session.Dispatch(() =>
        {
            var bad = new List<string>();
            foreach (var severity in Enum.GetValues<MessageSeverity>())
            {
                var banner = new MessageBanner { Severity = severity, Message = "Komunikat kontrolny." };
                var window = new Window { Content = banner, RequestedThemeVariant = variant };
                window.Show();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

                // Baner pokazuje treść w SelectableTextBlock (IsExpanded domyślnie), a w stanie zwiniętym
                // w TextBlock. `SelectableTextBlock` dziedziczy po `TextBlock`, więc jedno zapytanie łapie
                // oba; wybieramy element niosący TREŚĆ, a nie etykiety przycisków.
                var body = banner.GetVisualDescendants()
                    .OfType<TextBlock>()
                    .FirstOrDefault(t => t.Text == "Komunikat kontrolny.");
                Assert.NotNull(body);

                var painted = Assert.IsAssignableFrom<ISolidColorBrush>(body!.Foreground);
                var surface = Assert.IsAssignableFrom<ISolidColorBrush>(banner.Background);
                var ratio = ContrastRatio(painted.Color, surface.Color);
                window.Close();

                if (ratio < 4.5)
                {
                    bad.Add($"{severity} = {ratio:0.00}:1 ({painted.Color} na {surface.Color})");
                }
            }

            return bad;
        }, default);

        Assert.True(failures.Count == 0,
            $"Tekst komunikatu w motywie {variantName} nie trzyma progu 4,5:1 z §10:\n  "
            + string.Join("\n  ", failures)
            + "\n\nTo NIE jest test wartości — hex wolno stroić. Testem jest to, że tekst 12 px pozostaje "
            + "czytelny na własnym tle. ⚠ Próg 3:1 z wiersza 'tekst większy' NIE ma tu zastosowania: to "
            + "wymóg własny EmberTerna dla ≥ 14 px albo ≥ 12 px SemiBold, a treść banera jest 12 px Normal.");
    }

    /// <summary>
    /// SYGNAŁ severity (pasek 3 px) trzyma próg 3:1 — inny próg niż tekst, bo to element nietekstowy
    /// niosący znaczenie (§10 / WCAG 2.1 SC 1.4.11).
    /// <para>⭐ Pasek i ikona czytają TEN SAM klucz (<c>SeverityBrushKey</c>), więc asercja na pasku
    /// pokrywa oba; pasek jest wybrany, bo jest jednoznaczny strukturalnie (kolumna 0), a ikon w banerze
    /// jest kilka — severity, Kopiuj, Zamknij — i tylko pierwsza niesie barwę stanu.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public async Task SeveritySignal_OnTheBanner_ClearsTheNonTextFloor(string variantName)
    {
        var variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var failures = await _session.Dispatch(() =>
        {
            var bad = new List<string>();
            foreach (var severity in Enum.GetValues<MessageSeverity>())
            {
                var banner = new MessageBanner { Severity = severity, Message = "x" };
                var window = new Window { Content = banner, RequestedThemeVariant = variant };
                window.Show();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

                var stripe = banner.GetVisualDescendants()
                    .OfType<Border>()
                    .FirstOrDefault(b => Grid.GetColumn(b) == 0 && b.Background is ISolidColorBrush);
                Assert.NotNull(stripe);

                var painted = Assert.IsAssignableFrom<ISolidColorBrush>(stripe!.Background);
                var surface = Assert.IsAssignableFrom<ISolidColorBrush>(banner.Background);
                var ratio = ContrastRatio(painted.Color, surface.Color);
                window.Close();

                if (ratio < 3.0)
                {
                    bad.Add($"{severity} = {ratio:0.00}:1");
                }
            }

            return bad;
        }, default);

        Assert.True(failures.Count == 0,
            $"Pasek severity w motywie {variantName} nie trzyma progu 3:1 z §10:\n  "
            + string.Join("\n  ", failures));
    }

    /// <summary>
    /// Log Messages w edytorze SQL czyta TĘ SAMĄ mapę co baner, ale na INNEJ powierzchni
    /// (<c>BackgroundBrush</c>), więc jest osobnym przypadkiem — i jego różnica wobec `PanelBrush` to
    /// tylko ~0,3, czyli dokładnie tyle, ile dzieli „przechodzi" od „nie przechodzi".
    /// <para>⭐ Mapowanie brane jest z PRODUKCYJNEJ właściwości <see cref="QueryMessageViewModel.MessageBrushKey"/>,
    /// nie przepisane do testu — więc test przewraca się także wtedy, gdy ktoś zmieni regułę „który wiersz
    /// niesie barwę stanu". ⚠ Konstruowanie `MainWindow` jest tu ZAKAZANE (kształt zawieszający suite,
    /// §13.1), a ta właściwość daje tę samą wiedzę bez okna.</para>
    /// <para>⚠ Zmierzone i celowe: w logu tylko Warning i Error niosą barwę severity
    /// (<c>ShowSeverityMarker</c>); Info i Success czytają <c>ForegroundBrush</c>, bo log jest
    /// w większości informacyjny. Test sprawdza więc to, co realnie się maluje, a nie całą mapę.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public async Task SeverityText_InTheMessagesLog_ClearsTheTextContrastFloor(string variantName)
    {
        var variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var failures = await _session.Dispatch(() =>
        {
            var bad = new List<string>();
            var surface = ThemeToken<SolidColorBrush>("BackgroundBrush", variant);

            foreach (var severity in Enum.GetValues<MessageSeverity>())
            {
                var row = new QueryMessageViewModel(severity, "Komunikat kontrolny.");
                var painted = ThemeToken<SolidColorBrush>(row.MessageBrushKey, variant);
                var ratio = ContrastRatio(painted.Color, surface.Color);
                if (ratio < 4.5)
                {
                    bad.Add($"{severity} → {row.MessageBrushKey} = {ratio:0.00}:1");
                }
            }

            return bad;
        }, default);

        Assert.True(failures.Count == 0,
            $"Tekst wiersza logu Messages w motywie {variantName} nie trzyma progu 4,5:1 z §10:\n  "
            + string.Join("\n  ", failures));
    }

    // ─── M5 / L‑1 — wskazanie focusu dla każdego wariantu przycisku ────────────────────────────────────
    //
    // ⭐⭐ DWIE ASERCJE, BO PRZED M5 KAŻDY Z DWÓCH BRAKÓW ZAWIÓDŁ INACZEJ — i test pilnujący tylko jednej
    //   z nich przepuściłby ten drugi:
    //     `Button.caption` miał `BorderThickness=0`, więc setter `BorderBrush` **nic by nie namalował**
    //       → łapie to asercja „focus musi COKOLWIEK zmienić";
    //     `Button.primary` dostałby pierścień `FocusBorderBrush` o kontraście **1,26:1** na akcencie
    //       → łapie to asercja „to, co się zmieniło, musi trzymać 3:1".
    //   Pierwsza bez drugiej przepuszcza pierścień niewidoczny; druga bez pierwszej przepuszcza styl
    //   bezczynny (bo nie ma czego mierzyć, więc nie ma co oblać).
    //
    // ⚠ Wyzwalaczem jest `NavigationMethod.Tab`, czyli `:focus-visible` — to JEST przedmiot decyzji L‑1
    //   i test przewróci się, gdy ktoś wróci na `:focus`… **nie**, i to trzeba powiedzieć wprost: NIE
    //   przewróci się, bo `:focus` też zapala się przy Tabie. Zachowania „mysz nie pokazuje obwódki"
    //   pilnuje osobny test niżej, który fokusuje wskaźnikiem i wymaga BRAKU zmiany.

    public static TheoryData<string, string> ButtonVariantsAndThemes
    {
        get
        {
            var data = new TheoryData<string, string>();
            // ⚠ „toggle-icon" to `ToggleButton Classes="icon"` — objęty świadomie, mimo że decyzja
            //    wymieniała tylko warianty `Button`; powód przy jego stylu w `ControlStyles.axaml`.
            foreach (var variant in new[] { "icon", "flat", "primary", "caption", "toggle-icon" })
            {
                foreach (var theme in new[] { "Dark", "Light" })
                {
                    data.Add(variant, theme);
                }
            }

            return data;
        }
    }

    /// <summary>
    /// Każdy wariant przycisku daje przy nawigacji klawiaturą wskazanie focusu, które (a) realnie coś
    /// zmienia i (b) trzyma wewnętrzny próg 3:1 (§10 — znaczący element nietekstowy).
    /// </summary>
    [Theory]
    [MemberData(nameof(ButtonVariantsAndThemes))]
    public async Task EveryButtonVariant_ShowsAKeyboardFocusIndication_AboveTheContrastFloor(
        string variantName, string themeName)
    {
        var theme = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var (changed, ratio, detail) = await _session.Dispatch(() =>
        {
            var rest = Snapshot(variantName, theme, focus: null);
            var focused = Snapshot(variantName, theme, focus: NavigationMethod.Tab);

            // „Cokolwiek się zmieniło" to obramowanie ALBO wypełnienie — oba są legalnymi nośnikami
            // wskazania i produkt używa obu (ramka na icon/flat/primary, tło na caption).
            var borderMoved = rest.Border != focused.Border;
            var backMoved = rest.Background != focused.Background;

            // Kontrast liczymy dla tego nośnika, który się ZMIENIŁ, wobec tego, na czym leży.
            double r = 0;
            var what = "nic";
            if (backMoved)
            {
                r = ContrastRatio(focused.Background, rest.Background);
                what = $"tło {rest.Background} → {focused.Background}";
            }
            else if (borderMoved)
            {
                r = ContrastRatio(focused.Border, focused.Background);
                what = $"ramka {rest.Border} → {focused.Border} na {focused.Background}";
            }

            return (borderMoved || backMoved, r, what);
        }, default);

        Assert.True(changed,
            $"`Button.{variantName}` ({themeName}) NIE ZMIENIA NICZEGO WIDOCZNEGO przy fokusie z klawiatury. "
            + "Przed M5 tak właśnie zachowywały się `primary` (ramka zostawała w barwie własnego tła) "
            + "i `caption` (BorderThickness=0, więc setter BorderBrush nie miał czego malować).");

        Assert.True(ratio >= 3.0,
            $"`Button.{variantName}` ({themeName}) pokazuje wskazanie focusu o kontraście {ratio:0.00}:1, "
            + $"a §10 wymaga 3:1 dla znaczącego elementu nietekstowego. Zmierzone: {detail}.\n"
            + "⚠ To jest ta asercja, która odrzuca 'skopiuj setter z Button.flat' dla `primary`: "
            + "FocusBorderBrush na akcencie to 1,26:1 w Dark i 1,17:1 w Light.");
    }

    /// <summary>
    /// ⭐⭐ Druga połowa decyzji L‑1, i ta, której poprzedni test NIE pilnuje: wskazanie pojawia się
    /// przy nawigacji KLAWIATURĄ, a <b>nie</b> po kliknięciu myszą.
    /// <para>Przed M5 `Button.icon`/`.flat` używały <c>:focus</c>, która zapala się także po
    /// <c>NavigationMethod.Pointer</c> — więc kliknięty przycisk zostawał obwiedziony, podczas gdy
    /// `CheckBox`/`RadioButton` (na <c>:focus-visible</c>) już wtedy zachowywały się poprawnie.
    /// Ten test pinuje jedną konwencję dla obu rodzin.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ButtonVariantsAndThemes))]
    public async Task ButtonFocusIndication_DoesNotAppearOnPointerFocus(string variantName, string themeName)
    {
        var theme = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
        var same = await _session.Dispatch(() =>
        {
            var rest = Snapshot(variantName, theme, focus: null);
            var pointed = Snapshot(variantName, theme, focus: NavigationMethod.Pointer);
            return rest.Border == pointed.Border && rest.Background == pointed.Background;
        }, default);

        Assert.True(same,
            $"`Button.{variantName}` ({themeName}) zmienia wygląd po fokusie WSKAŹNIKIEM. "
            + "L‑1 ratyfikowało jedną konwencję: wskazanie focusu należy do nawigacji klawiaturą "
            + "(`:focus-visible`), bo obwódka zostająca po kliknięciu myszą jest szumem, a nie sygnałem. "
            + "⚠ Najczęstsza przyczyna: selektor wrócił na `:focus`, która zapala się także od wskaźnika.");
    }

    /// <summary>Odczyt Z ELEMENTU, KTÓRY MALUJE — obramowanie i wypełnienie zrealizowanego prezentera.</summary>
    private static (Color Border, Color Background) Snapshot(string variant, ThemeVariant theme, NavigationMethod? focus)
    {
        // `ToggleButton` nie dziedziczy po `Button`, więc wariant przełącznika trzeba zbudować jego
        // własnym typem — inaczej selektor `ToggleButton.icon` nigdy by się nie zastosował i test
        // mierzyłby zwykły przycisk, meldując zgodność wariantu, którego nie dotknął.
        TemplatedControl button = variant == "toggle-icon"
            ? new ToggleButton { Classes = { "icon" }, Content = "Wykonaj" }
            : new Button { Classes = { variant }, Content = "Wykonaj" };
        var window = new Window { Content = button, RequestedThemeVariant = theme };
        window.Show();
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);

        if (focus is { } method)
        {
            button.Focus(method);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Loaded);
        }

        var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
        var border = (presenter.BorderBrush as ISolidColorBrush)?.Color ?? Colors.Transparent;
        var background = (presenter.Background as ISolidColorBrush)?.Color ?? Colors.Transparent;
        window.Close();
        return (border, background);
    }

    private static T ThemeToken<T>(string key, ThemeVariant variant)
    {
        var app = Application.Current;
        Assert.NotNull(app);
        Assert.True(app!.TryFindResource(key, variant, out var value),
            $"Theme token `{key}` is not in the application's {variant} resources.");
        return Assert.IsType<T>(value);
    }
}


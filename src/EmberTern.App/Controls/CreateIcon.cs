using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace EmberTern.App.Controls;

/// <summary>
/// A toolbar "create …" icon: the object kind's <b>full-size</b> glyph with a small accent badge
/// overlaid on its lower-right corner (Product Polish M3.5 / Z-6).
///
/// <para>⭐⭐ <b>Why this is a control and not a geometry.</b> It expresses two independent facts, and
/// <see cref="SvgIcon"/> structurally cannot: the glyph is the object <b>kind</b> (S1 — <c>IconColor_*</c>,
/// the very colour the metadata tree uses) and the badge is the <b>action</b> "create" (S2 — one colour for
/// all nine, so the mark means the same thing everywhere). <c>SvgIcon</c> renders ONE <c>Path</c> with one
/// <c>Stroke</c> and one <c>StrokeThickness=2</c> for the whole geometry, and a badge is by definition a
/// <i>smaller, denser</i> mark — "smaller but equally thick" degenerates into a blob at 16 px. Measured, not
/// assumed: the pure-geometry route was rendered and rejected in the §13.3 gate.</para>
///
/// <para>⭐⭐ <b>The second, larger gain: nine hand-maintained copies are gone.</b> The old
/// <c>Icon.TablePlus … Icon.ExceptionPlus</c> were hand-composed approximations of <c>Icon.Table …
/// Icon.Exception</c>, each compressing its glyph to ~11 of 24 units to make room for the plus. <see cref="Data"/>
/// takes the <b>plain</b> geometry <b>by reference</b>, so the toolbar now shows the same glyph as the tree and
/// improving that glyph can no longer drift away from the toolbar. This is exactly the defect
/// <see cref="DebuggerIcon"/> already diagnosed for itself ("⛔ do not swap the reference back for a written-out
/// path, not even an identical one").</para>
///
/// <para>⚠ The badge is a <b>solid</b> disc, so it simply covers what is beneath it — no knockout, therefore no
/// dependency on the surface colour behind the icon. That matters: the toolbar surface is
/// <c>ChromeStrongBrush</c> at rest but <c>IconHoverBrush</c> under the pointer, so a knockout filled with the
/// chrome colour would flash a wrong-coloured patch on hover. Same trick as the breakpoint dot sitting on the
/// Play triangle.</para>
///
/// <para>⚠ <c>AccentBrush</c> on the badge does <b>not</b> re-open P-2 (accent-on-panel = 2.89:1 in Dark). P-2 is
/// about a 2 px line carrying a signal alone; here the work is done by an 11-unit solid disc with a white plus
/// inside it — area plus internal contrast, not difference against the surface.</para>
///
/// <para>Composition and both brushes live in its ControlTheme in <c>Themes/IconGeometries.axaml</c>, so it stays
/// inside the central icon/theme system. Size via Width/Height (default 16, like <see cref="SvgIcon"/>).</para>
/// </summary>
public sealed class CreateIcon : TemplatedControl
{
    /// <summary>
    /// The object kind's <b>plain</b> geometry — <c>Icon.Table</c>, <c>Icon.View</c>, … — referenced from
    /// <c>Themes/IconGeometries.axaml</c>. ⛔ Never a "…Plus" variant and never a written-out path: the whole
    /// point of this control is that the toolbar and the tree share one geometry.
    /// </summary>
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<CreateIcon, Geometry?>(nameof(Data));

    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}

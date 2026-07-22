using Avalonia.Controls.Primitives;

namespace EmberTern.App.Controls;

/// <summary>
/// The debugger's identity mark (Stage X / D15.2 Seam B) — a blue Play triangle (the
/// execution pointer) with a small red breakpoint dot nested into its lower-right, read
/// as one "Start Debugging" glyph. Unlike <see cref="SvgIcon"/> this is intrinsically
/// two-colour and part-filled (a stroked accent-blue triangle + a filled breakpoint-red
/// dot), so it cannot be a single stroked geometry keyed into the icon dictionary. The
/// composition and its theme tokens (<c>AccentIconBrush</c> + <c>DebugBreakpointBrush</c>,
/// both reused, both dictionaries) live in its ControlTheme in
/// <c>Themes/IconGeometries.axaml</c>, so it stays inside the central icon/theme system.
/// Size via Width/Height (default 16; the workspace tab overrides to 14).
/// </summary>
public sealed class DebuggerIcon : TemplatedControl
{
}

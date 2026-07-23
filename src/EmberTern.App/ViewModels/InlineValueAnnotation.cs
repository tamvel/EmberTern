namespace EmberTern.App.ViewModels;

/// <summary>
/// One inline value annotation the debugger draws at the end of a source line (Stage X / D15.5 — Inline
/// Values). Pure presentation data computed by <see cref="DebuggerTabViewModel"/> from the paused-frame
/// roster and consumed by the view's inline-values renderer — the VM decides <b>which</b> values show and
/// <b>where</b>; the renderer only draws them (never shifting the document text). <see cref="AnchorOffset"/>
/// is any offset on the target line; the renderer resolves it to that line's end.
/// </summary>
public readonly record struct InlineValueAnnotation(int AnchorOffset, string Text);

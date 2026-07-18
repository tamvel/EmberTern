using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Hover;

/// <summary>
/// Everything one hover has to say about one offset — the composed result of
/// <see cref="HoverInfoEngine"/> (post-Stage-7 "Unified Hover Information", design
/// <c>editor-stage7-diagnostics.md</c> §15). ONE surface, not two competing tooltips: the diagnostic
/// explaining a squiggle, the semantic Quick Info for a symbol, or both as sections of a single popup.
/// <para>
/// <b>An ordered aggregate of optional sections, deliberately not an <c>IHoverProvider</c> abstraction</b>
/// (architecture rule #2 — no interfaces without two concrete implementations): the composition is a
/// handful of lines, and the real contract is the section ORDER, not a provider type. <b>Diagnostics
/// render first</b> — the reason the user hovered a squiggle is the error; the semantic info is
/// supporting context. When Quick Fixes land they become a third section here.
/// </para>
/// <para>
/// Pure data — no Avalonia. Read-only, so §0 (never lose information) holds by construction.
/// </para>
/// </summary>
public sealed class HoverInfo
{
    /// <param name="span">The region this exact content is valid for — see <see cref="Span"/>.</param>
    /// <param name="diagnostics">The findings at the offset, in <see cref="DiagnosticsEngine"/> order.</param>
    /// <param name="info">The semantic Quick Info, or null when nothing resolved there.</param>
    /// <param name="debugValue">The live debugger value for the variable/parameter at the offset (a data tip,
    /// spec §9.4), or null outside a paused debug session / for a non-variable. Rendered first — in a paused
    /// debugger the current value is the reason you hovered.</param>
    public HoverInfo(
        TextSpan span,
        IReadOnlyList<Diagnostic> diagnostics,
        QuickInfo.QuickInfo? info,
        DebugHoverValue? debugValue = null)
    {
        Span = span;
        Diagnostics = diagnostics ?? System.Array.Empty<Diagnostic>();
        Info = info;
        DebugValue = debugValue;
    }

    /// <summary>
    /// The narrowest span, among the sections being shown, that contains the queried offset — i.e. the
    /// region over which this hover's content does not change. The App keeps the popup open (and does
    /// not rebuild it) while the pointer stays inside, which is what stops a hover from flickering as
    /// the pointer drifts a few pixels across one identifier.
    /// </summary>
    public TextSpan Span { get; }

    /// <summary>
    /// The diagnostics at the offset, in the engine's own order — the FIRST section. Empty when the
    /// offset is not squiggled.
    /// <para>
    /// These are an <b>input</b> to the engine (the cached, version-matched list), never something it
    /// computed: the no-new-analysis rule is enforced by <see cref="HoverInfoEngine.GetHover"/>'s
    /// signature rather than by a rule someone has to remember.
    /// </para>
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// The semantic Quick Info for the symbol at the offset — the SECOND section — or null when
    /// nothing resolved there.
    /// <para>
    /// <b>Null is the common case for a squiggled span, not an edge case:</b> an unknown object's
    /// reference is by definition <em>unresolved</em>, so <c>QuickInfoEngine</c> returns null exactly
    /// where <c>ET0001</c> fires. "Both sections present" is the rarer path.
    /// </para>
    /// </summary>
    /// <remarks>Qualified as <c>QuickInfo.QuickInfo</c> throughout: the type shares its name with its
    /// namespace, and from a sibling namespace the bare name binds to the namespace.</remarks>
    public QuickInfo.QuickInfo? Info { get; }

    /// <summary>
    /// The live debugger data tip for the variable/parameter under the pointer — its current value in the
    /// paused frame — or null when there is no debug session, the pointer is not on a frame variable, or the
    /// caller supplied no value lookup. The FIRST section of the card (spec §9.4): "you inspect where you are
    /// looking." Supplied by the caller (a lookup delegate), never computed here — so, like <see cref="Diagnostics"/>,
    /// the no-analysis rule holds by construction.
    /// </summary>
    public DebugHoverValue? DebugValue { get; }

    /// <summary>True when there is at least one diagnostic to explain.</summary>
    public bool HasDiagnostics => Diagnostics.Count > 0;
}

/// <summary>A debugger data tip — the current value of a frame variable/parameter, already formatted by the
/// caller (the App owns value formatting; Core just carries the strings). Pure data.</summary>
/// <param name="Name">The variable/parameter name.</param>
/// <param name="ValueText">The value formatted for display (<c>&lt;null&gt;</c> for an unset/null value).</param>
/// <param name="IsNull">True when the value is null/unset.</param>
public sealed record DebugHoverValue(string Name, string ValueText, bool IsNull);

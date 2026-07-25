using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Sql.Debugging;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One line breakpoint in the debugger's Breakpoints panel (Stage X / D12, spec §9.8) — a <b>pure editable
/// projection of the Core <see cref="Breakpoint"/> stop-policy object</b>. It holds NO breakpoint logic: the
/// condition and hit-count edits pass straight through to the wrapped <see cref="Breakpoint"/> (which owns the
/// policy and the hit tally), and constructing a <see cref="HitCountPolicy"/> from the picked kind + count is
/// done by the Core factory (<see cref="HitCountPolicy.Of"/>), not here. The wrapped <see cref="Breakpoint"/>
/// is the SAME object the live session holds (the debug tab shares its <see cref="BreakpointSet"/> with the
/// session), so an edit is immediately in force — no callback, no mirroring. The panel only presents.
/// </summary>
public sealed partial class BreakpointRowViewModel : ObservableObject
{
    private readonly Breakpoint _breakpoint;
    private bool _suppress; // true while the ctor seeds the controls from the Core object (no echo back)

    public BreakpointRowViewModel(Breakpoint breakpoint, int line)
    {
        _breakpoint = breakpoint ?? throw new ArgumentNullException(nameof(breakpoint));
        Line = line;

        _suppress = true;
        _condition = breakpoint.Condition ?? string.Empty;
        _hitCountKindIndex = (int)breakpoint.HitCount.Kind;
        _hitCountValue = breakpoint.HitCount.Value <= 0 ? 1 : breakpoint.HitCount.Value;
        _suppress = false;
    }

    /// <summary>The wrapped breakpoint's step-point offset — keys the row back to the Core set for removal.</summary>
    public int Offset => _breakpoint.Offset;

    /// <summary>The 1-based source line the breakpoint sits on (for the panel label).</summary>
    public int Line { get; }

    public string LineText => string.Format(CultureInfo.CurrentCulture, UiStrings.DebuggerBreakpointLineFormat, Line);

    /// <summary>The optional boolean condition — forwarded verbatim to <see cref="Breakpoint.Condition"/>. The
    /// engine evaluates it (one D5 engine); a blank clears it. No parsing / validation here (a broken condition
    /// surfaces at break time via <c>BreakpointConditionError</c>, §F).</summary>
    [ObservableProperty]
    private string _condition;

    partial void OnConditionChanged(string value)
    {
        if (_suppress) return;
        _breakpoint.Condition = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); // live: same object the engine reads
    }

    /// <summary>The hit-count kind options, in <see cref="HitCountKind"/> order (Always / Exactly / AtLeast /
    /// Multiple) — the labels the panel's picker shows.</summary>
    public IReadOnlyList<string> HitCountKinds { get; } = new[]
    {
        UiStrings.DebuggerHitCountAlways,
        UiStrings.DebuggerHitCountExactly,
        UiStrings.DebuggerHitCountAtLeast,
        UiStrings.DebuggerHitCountMultiple,
    };

    /// <summary>The selected hit-count kind (index into <see cref="HitCountKinds"/> = the
    /// <see cref="HitCountKind"/> value). Rebuilds the wrapped breakpoint's policy via the Core factory.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHitCountValueEnabled))]
    private int _hitCountKindIndex;

    partial void OnHitCountKindIndexChanged(int value) => ApplyHitCount();

    /// <summary>The hit-count operand N (used by every kind except Always). A nullable <see cref="decimal"/> to
    /// match the NumericUpDown; coerced to a positive int (min 1) when the policy is built.</summary>
    [ObservableProperty]
    private decimal? _hitCountValue = 1;

    partial void OnHitCountValueChanged(decimal? value) => ApplyHitCount();

    /// <summary>The operand box is enabled for every kind except Always (which has no count).</summary>
    public bool IsHitCountValueEnabled => (HitCountKind)HitCountKindIndex != HitCountKind.Always;

    private void ApplyHitCount()
    {
        if (_suppress) return;
        int n = HitCountValue is { } v && v >= 1 ? (int)v : 1;
        _breakpoint.HitCount = HitCountPolicy.Of((HitCountKind)HitCountKindIndex, n); // Core owns the mapping; live
    }
}

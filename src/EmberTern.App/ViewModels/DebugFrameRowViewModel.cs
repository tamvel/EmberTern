using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One frame in the debugger's Call Stack panel (Stage X / D8, spec §5.2 — "frames are data, not windows").
/// A thin presentation row: the routine <see cref="RoutineName"/>, the <see cref="LineText"/> it is at (its
/// current statement for the innermost frame, its call site for a caller), whether it is the innermost
/// (executing) frame, and whether it is a <see cref="IsSimulated"/> frame — one reached by Step Into, which
/// EmberTern <em>interprets</em> rather than runs (spec §5.3: step-over is real execution, step-into is
/// simulation; the indicator is a fact, not a nag). Immutable — the panel is rebuilt each pause.
/// </summary>
public sealed partial class DebugFrameRowViewModel : ObservableObject
{
    public DebugFrameRowViewModel(int frameId, string routineName, string lineText, bool isCurrent, bool isSimulated)
    {
        FrameId = frameId;
        RoutineName = routineName;
        LineText = lineText;
        IsCurrent = isCurrent;
        IsSimulated = isSimulated;
    }

    /// <summary>The engine frame id (stable within the session) — keys selection back to the live frame.</summary>
    public int FrameId { get; }

    /// <summary>The routine this frame activates (the call-stack label).</summary>
    public string RoutineName { get; }

    /// <summary>A short "line N" label for the frame's current position (its statement / call site).</summary>
    public string LineText { get; }

    /// <summary>True for the innermost (currently executing) frame — the one the current-line marker is on.</summary>
    public bool IsCurrent { get; }

    /// <summary>True when this frame was reached by Step Into (interpreted = a simulation, spec §5.3) — the
    /// quiet permanent indicator. The root (launched) frame is not marked.</summary>
    public bool IsSimulated { get; }
}

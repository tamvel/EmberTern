using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// The pure stop-decision for stepping: given the command, the depth when the step began, the current
/// depth, and the next step point, decide whether the session should stop here or keep running. Every step
/// decision is a pure function of these inputs (the DoD requirement) — no state, no side effects, so it is
/// trivially unit-testable. Breakpoints compose with this in <see cref="DebugSession"/> (an additional stop
/// condition); they are deliberately not part of this movement-only decision.
/// </summary>
internal static class StepPlanner
{
    public static bool ShouldStop(
        StepKind kind, int? targetOffset, int startDepth, int currentDepth, IExecutableStatement nextStep)
        => kind switch
        {
            // Into and Over both stop at the very next step point after a single executed step; they differ
            // only in HOW a call was executed (Into descended into a frame, Over ran it on the server) —
            // which happened during execution, not here.
            StepKind.Into => true,
            StepKind.Over => true,

            // Step Out runs until the frame we started in has returned (the stack got shallower).
            StepKind.Out => currentDepth < startDepth,

            // Continue runs to completion; DebugSession adds breakpoints as an additional stop condition.
            StepKind.Continue => false,

            // Run To Cursor stops when the next step point is the target statement.
            StepKind.RunToCursor => targetOffset is int t && nextStep.Start == t,

            _ => true,
        };
}

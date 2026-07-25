using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.App.ViewModels;

// Outcome of one Save step in the "Save and close / Save and disconnect" WorkGuard.
// The individual editor compiles swallow server errors into their bound ErrorMessage
// (they do not throw to the caller), so this record carries the pass/fail back to the
// batch orchestrator; Error mirrors whatever the editor showed.
public sealed record EditorSaveResult(bool Success, string? Error);

// Implemented by every object-editor VM that can compile its unsaved buffer to the
// database. This is a THIN ADAPTER over each editor's EXISTING compile action
// (ExecuteCompileAsync / Table's CompileAsync / New Table's owner-driven create) — it
// is NOT a second save mechanism. It exists so the WorkGuard can drive every dirty
// editor through the shared group-recompilation results pipeline uniformly and get a
// structured pass/fail back.
//
// Dirtiness is deliberately NOT re-declared here: it is already surfaced by
// IUnsavedWorkSource (GetUnsavedWork() != null), which the WorkGuard reuses to pick the
// dirty tabs. Every object editor implements this (>2 impls), so the interface is
// justified under the no-interface-without-two-impls rule.
public interface ISavableObjectEditor
{
    // Compiles the editor's current unsaved buffer to the database via the editor's own
    // existing compile path, then reports whether it succeeded. On failure the editor's
    // bound ErrorMessage (shown in its view) is also set, and Error mirrors it.
    //
    // THE CONTRACT (UX Polish Seam 6b) — stated here once, obeyed by every implementation:
    // an adapter reads success as "no error after the attempt", so a compile that DID NOT RUN
    // must set its ErrorMessage. Any silent `return;` on a pre-condition (no DDL executor, an
    // empty source buffer) makes SaveAsync claim the work was written when nothing was — and
    // the save-and-close WorkGuard then discards the user's code on the strength of that lie
    // (Architecture rule #11). Refusals share one wording: UiStrings.NoConnectionMessage for a
    // missing DDL executor, UiStrings.EditorNothingToCompile for an empty buffer.
    //
    // The one exception, and it is not a silent success: a DIFF-based editor (Domain, Exception,
    // Generator, Index, Table designer) whose diff comes out empty has genuinely nothing to write
    // — the buffer holds no work to lose — so "no changes" stays an ordinary no-op rather than
    // being reported to the user as a failed save.
    Task<EditorSaveResult> SaveAsync(CancellationToken cancellationToken = default);
}

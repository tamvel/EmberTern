namespace EmberTern.App.ViewModels;

// What kind of unsaved work a tab holds. Drives wording only — the WorkGuard treats
// all of them as "will be lost on close/disconnect unless the user keeps the tab".
public enum UnsavedWorkKind
{
    // A new object that hasn't been created in the database yet (New Table form,
    // New View / New Procedure source the user has started editing).
    NewObject,

    // An existing object whose editable source was modified but not yet compiled.
    ModifiedSource,

    // A table designer with queued-but-not-yet-compiled structural changes
    // (PendingChanges: ADD/DROP/MOVE field, constraints, indexes, description).
    PendingStructure,
}

// One unsaved-work descriptor surfaced by a tab. Label is a ready-to-show summary
// line (object name included) for the disconnect / app-close dialogs.
public sealed record UnsavedWorkItem(UnsavedWorkKind Kind, string Label);

// Implemented by every closable tab VM that can hold unsaved work (NewTable,
// ViewDetail, ProcedureDetail, TableDetail — four implementations, so the
// interface is justified per the no-interface-without-two-impls rule). The
// WorkGuard on MainWindowViewModel aggregates these across open tabs to decide
// whether closing a tab, disconnecting, or exiting would lose work.
public interface IUnsavedWorkSource
{
    // Null when the tab is clean (nothing to lose). Pulled on demand at
    // close/disconnect/exit time — not a bound property, so no change notification.
    UnsavedWorkItem? GetUnsavedWork();
}

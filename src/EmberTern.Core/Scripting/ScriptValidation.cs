using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Scripting;

/// <summary>
/// Pure pre-run checks for a parsed script. The Script Executor runs the whole script in
/// ONE caller-controlled transaction on an established connection, so statements that carry
/// their own transaction control (<see cref="ScriptStatementKind.TransactionControl"/>) or
/// change the connection/session (<see cref="ScriptStatementKind.SessionControl"/>) can't be
/// honoured and must be rejected BEFORE any transaction is started — the ViewModel surfaces
/// the offending statements so the user can remove them (or, for transaction control, switch
/// to a mode that owns its own transaction, which V1 does not offer).
/// </summary>
public static class ScriptValidation
{
    /// <summary>Returns the statements that must not appear in a script run under a single
    /// executor-owned transaction, in source order. Empty when the script is runnable.</summary>
    public static IReadOnlyList<ScriptStatement> FindDisallowed(IReadOnlyList<ScriptStatement> statements)
        => statements
            .Where(s => s.Kind is ScriptStatementKind.TransactionControl or ScriptStatementKind.SessionControl)
            .ToList();
}

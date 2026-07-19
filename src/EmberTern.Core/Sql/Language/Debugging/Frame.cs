using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Debugging;

// A resumable position inside a frame's body — the interpreter's "instruction pointer" is a stack of
// these (innermost on top). Navigation (structural, no server) walks/pushes/pops them to reach the next
// step point; execution (via the executor) evaluates a condition / runs a leaf / fetches a row and then
// pushes the taken branch or loop body. Internal: the control stack is the interpreter's concern, not
// public API.

internal abstract class Activation
{
}

// Running Items[Index..] of a block (or a synthetic single-statement branch). Block is non-null for a
// real BEGIN…END (it carries the WHEN handlers the ExceptionRouter reads on a raise); null for a
// single-statement IF/loop branch.
internal sealed class SequenceActivation : Activation
{
    public SequenceActivation(BlockStatement? block, IReadOnlyList<SqlNode> items, int index)
    {
        Block = block;
        Items = items;
        Index = index;
    }

    public BlockStatement? Block { get; }
    public IReadOnlyList<SqlNode> Items { get; }
    public int Index { get; set; }

    // True once this block's WHEN handler has fired (the ExceptionRouter set it) — the block can no longer
    // catch, so an exception raised inside its own handler body propagates OUT to an enclosing block, never
    // back into this WHEN section (Firebird's handler semantics). Only meaningful when Block has handlers.
    public bool HandlerActive { get; set; }
}

// A WHILE loop in progress — its header is re-presented as the step point each time control returns here
// (the loop re-evaluates its condition per iteration).
internal sealed class WhileActivation : Activation
{
    public WhileActivation(WhileStatement node) => Node = node;

    public WhileStatement Node { get; }
}

// A FOR SELECT loop in progress — holds the live cursor across iterations (spec §7: the cursor must stay
// open while the user steps the body). Its header is the per-iteration step point.
internal sealed class ForActivation : Activation
{
    public ForActivation(ForSelectStatement node) => Node = node;

    public ForSelectStatement Node { get; }
    public IDebugCursor? Cursor { get; set; }
    public bool Opened { get; set; }
}

/// <summary>
/// One activation of a routine (or the root body) on the debug call stack — the unit spec §5 makes "data,
/// not windows". It carries the routine's <see cref="Body"/> AST, its <see cref="Values"/> (the client-side
/// truth), a <see cref="SavepointName"/> for call-atomicity reconstruction (spec §4.5), and its lexical
/// <see cref="Parent"/> for the scope chain (spec §6 closures — a sub-routine's frame's parent is the
/// declaring frame). The resumable execution position (the control stack) is internal to the interpreter.
/// </summary>
public sealed class Frame
{
    private readonly List<Activation> _control = new();

    // The names this frame's routine DECLARES (its parameters + its body's DECLARE VARIABLEs), folded. Used to
    // decide, during a scope-chain walk (spec §6 closures), which frame OWNS a name — so a not-yet-assigned
    // local resolves/writes in the frame that declares it, and an inner local correctly shadows a like-named
    // outer variable. Distinct from Values (assigned values): a declared local is "owned" here from frame
    // entry, even before its first assignment. Empty is harmless — resolution then falls back to Values.
    private readonly HashSet<string> _declaredNames = new(System.StringComparer.OrdinalIgnoreCase);

    internal Frame(
        int id,
        string routineName,
        BlockStatement body,
        Frame? parent,
        Frame? lexicalParent,
        IExecutableStatement? callSite,
        IReadOnlyDictionary<string, object?>? initialValues,
        IReadOnlyList<string>? outputParameterNames = null,
        string? source = null,
        SemanticModel? model = null,
        string? returnType = null,
        FunctionReturnContinuation? returnContinuation = null)
    {
        Id = id;
        RoutineName = routineName;
        Body = body;
        Parent = parent;
        LexicalParent = lexicalParent;
        CallSite = callSite;
        Values = new FrameValues(initialValues);
        OutputParameterNames = outputParameterNames ?? System.Array.Empty<string>();
        Source = source;
        Model = model;
        ReturnType = returnType;
        ReturnContinuation = returnContinuation;
        SavepointName = $"ET_DBG_FRAME_{id}";

        // Record the declared names (params + body local variables) so the closure scope chain resolves and
        // writes into the correct frame even before a variable is assigned (spec §6). Params: the seeded input
        // values + the output parameter names. Locals: the body's DECLARE VARIABLE section.
        if (initialValues is not null)
        {
            foreach (var name in initialValues.Keys) _declaredNames.Add(name);
        }
        foreach (var name in OutputParameterNames) _declaredNames.Add(name);
        foreach (var d in body.Declarations)
        {
            if (d is DeclareVariableStatement { Name: { } vn } && vn.Length > 0) _declaredNames.Add(vn);
        }
        // Start executing the body's statements. The body's own DECLAREs are not executed — declared
        // variables begin null (their values arrive via injection/assignment); initialValues seeds params.
        _control.Add(new SequenceActivation(body, body.Statements, 0));
    }

    /// <summary>Monotonic id, unique within a session — also keys the frame's savepoint name.</summary>
    public int Id { get; }

    /// <summary>The routine's display name (root body / callee), for the call stack and breadcrumbs.</summary>
    public string RoutineName { get; }

    /// <summary>The body this frame interprets.</summary>
    public BlockStatement Body { get; }

    /// <summary>This routine's full source text (the span backing of <see cref="Body"/> and
    /// <see cref="CallSite"/>), for the UI to show <b>this</b> frame's routine when the call stack selects it
    /// (spec §5.2) and to compute its line numbers; null when the source was not supplied (the fake-driven
    /// engine tests). The root frame's source is the launched routine's; a stepped-into callee's is the
    /// fetched callee source.</summary>
    public string? Source { get; }

    /// <summary>This routine's semantic model — the roster (declared parameters + locals, with their kinds and
    /// types) the Variables panel projects. Carried for the <b>UI's</b> benefit exactly as <see cref="Source"/>
    /// is (the interpreter never reads it): when the call stack selects a frame, the panel builds that frame's
    /// roster from <b>its own</b> model, not the root's (spec §5.2). Null when not supplied (the fake-driven
    /// engine tests). The root frame's model is the launched routine's; a stepped-into callee's is the model
    /// built from the fetched callee source (D8, on the same offsets as its <see cref="Source"/>).</summary>
    public SemanticModel? Model { get; }

    /// <summary>The <b>call-stack</b> parent — the frame that pushed this one (the caller). Walked by the
    /// call stack (spec §5) and the exception unwinder; null for the root. Distinct from
    /// <see cref="LexicalParent"/>: a called <b>stored</b> routine's caller is NOT its lexical parent (a
    /// stored routine is a closed scope), whereas a local sub-routine's declaring frame is both (spec §6).</summary>
    public Frame? Parent { get; }

    /// <summary>The <b>lexical</b> (scope-chain) parent — the frame whose variables this frame can see and
    /// write through the closure chain (<see cref="TryResolveValue"/> / <see cref="SetResolvedValue"/>).
    /// <b>null</b> for the root and for a called <b>stored</b> routine (a closed scope: its only inputs are
    /// its parameters); the <b>declaring</b> frame for a <b>local</b> sub-routine (spec §6 closures — set by
    /// D9). Deliberately separate from the call-stack <see cref="Parent"/>: D8 is where the two first
    /// diverge (a stored callee has a caller but no lexical parent).</summary>
    public Frame? LexicalParent { get; }

    /// <summary>The statement in the parent frame that pushed this frame (for the caller-line marker and the
    /// <c>RETURNING_VALUES</c> write-back on return), or null for the root.</summary>
    public IExecutableStatement? CallSite { get; }

    /// <summary>This frame's variable store — the client-side truth.</summary>
    public FrameValues Values { get; }

    /// <summary>This routine's <b>output</b> parameter names, in declaration order (empty for the root / a
    /// routine with no outputs). On this frame's normal return, its outputs are written positionally into the
    /// caller's <c>RETURNING_VALUES</c> targets (spec §5 — a real call's output binding, reconstructed).</summary>
    public IReadOnlyList<string> OutputParameterNames { get; }

    /// <summary>The SAVEPOINT name reconstructing this frame's call atomicity (spec §4.5).</summary>
    public string SavepointName { get; }

    /// <summary>True when this frame has finished (its control stack is empty).</summary>
    public bool IsComplete => _control.Count == 0;

    // ── Function return (Stage X / D9 seam c, §6.4) ──────────────────────────────────────────────
    // A function frame is one the interpreter stepped INTO via IDebugExecutor.ResolveFunction; it carries how
    // its RETURN value reaches the caller position (ReturnContinuation) and the RETURNS base type the
    // Expression Harness types the RETURN operand as (ReturnType). The root and procedure/EXECUTE-BLOCK frames
    // have neither. A stored/procedure callee returns via output parameters (OutputParameterNames), not this.

    /// <summary>The local function's <c>RETURNS</c> base type (R2) — the type the Expression Harness gives the
    /// result column that computes this frame's <c>RETURN</c> value; null for a non-function frame.</summary>
    public string? ReturnType { get; }

    /// <summary>The value this function frame's <c>RETURN</c> computed (set by the interpreter via
    /// <see cref="SetReturnValue"/>), delivered to the caller position by <see cref="ReturnContinuation"/> on
    /// normal exit; null until a <c>RETURN</c> runs (and always for a non-function frame).</summary>
    public object? ReturnValue { get; private set; }

    /// <summary>How this function frame's return value is consumed by the caller statement that stepped into
    /// it (§6.4); null for the root and for a procedure/EXECUTE-BLOCK frame.</summary>
    internal FunctionReturnContinuation? ReturnContinuation { get; }

    /// <summary>True for a stepped-into local <b>function</b> frame — its <c>RETURN &lt;expr&gt;</c> leaves are
    /// evaluated via the Expression Harness (a bare <c>RETURN</c> is invalid inside <c>EXECUTE BLOCK</c>) and
    /// its computed value is delivered by <see cref="ReturnContinuation"/> on normal exit. Equivalently: this
    /// frame was entered through <c>ResolveFunction</c>, so it carries a continuation.</summary>
    internal bool IsFunctionFrame => ReturnContinuation is not null;

    /// <summary>Records this function frame's computed <c>RETURN</c> value.</summary>
    internal void SetReturnValue(object? value) => ReturnValue = value;

    /// <summary>Terminates the whole frame for a <c>RETURN</c> — regardless of block nesting — by closing any
    /// open cursors and clearing the control stack, so <c>AdvanceToNextStepPoint</c> pops it and runs its
    /// continuation. (A <c>RETURN</c> exits a function immediately, unlike falling off the end of a block.)</summary>
    internal void TerminateForReturn()
    {
        CloseOpenCursors();
        _control.Clear();
    }

    // ── Scope chain (spec §6 — closures over the declaring frame) ────────────────────────────────

    /// <summary>Resolves a variable up the lexical scope chain (this frame, then
    /// <see cref="LexicalParent"/>, …), returning the nearest defining frame's value. This is the mechanism a
    /// local sub-routine frame uses to read an outer variable (spec §6.1); a stored routine has no lexical
    /// parent, so it resolves only its own variables (a closed scope). D1 provides it, D9 wires local
    /// routines to it.</summary>
    public bool TryResolveValue(string name, out object? value)
    {
        for (var f = this; f is not null; f = f.LexicalParent)
        {
            if (f.Owns(name))
            {
                value = f.Values.Get(name);
                return true;
            }
        }
        value = null;
        return false;
    }

    /// <summary>Writes a variable in the nearest frame up the chain that defines it (a closure write-back),
    /// or in this frame when it is defined nowhere.</summary>
    public void SetResolvedValue(string name, object? value)
    {
        for (var f = this; f is not null; f = f.LexicalParent)
        {
            if (f.Owns(name))
            {
                f.Values.Set(name, value);
                return;
            }
        }
        Values.Set(name, value);
    }

    // True when this frame owns the name: it either DECLARES it (a parameter / body local — owned from entry,
    // even before assignment, so a not-yet-assigned local shadows a like-named outer var and its write stays
    // here) or already holds an assigned value for it. The scope-chain walk stops at the first owning frame.
    private bool Owns(string name) => _declaredNames.Contains(name) || Values.Contains(name);

    // ── Control stack (internal — the interpreter drives these) ──────────────────────────────────

    internal Activation? Top => _control.Count > 0 ? _control[^1] : null;

    internal void Push(Activation a) => _control.Add(a);

    internal void Pop() => _control.RemoveAt(_control.Count - 1);

    // Pops the top activation while an exception unwinds it, closing an abandoned FOR SELECT cursor so it
    // never leaks (spec §7: the cursor lives on the session connection; Close is idempotent). Used by the
    // ExceptionRouter when it discards inner activations to reach a catching block.
    internal void PopForUnwind()
    {
        if (Top is ForActivation { Opened: true, Cursor: { } cursor }) cursor.Close();
        Pop();
    }

    // Closes every open cursor in this frame — called before the ExceptionRouter drops a whole frame that
    // failed to catch (its activations are discarded, so their cursors must be closed first).
    internal void CloseOpenCursors()
    {
        foreach (var a in _control)
        {
            if (a is ForActivation { Opened: true, Cursor: { } cursor }) cursor.Close();
        }
    }

    internal IReadOnlyList<Activation> Control => _control;

    // Pushes a branch / loop body: a real BEGIN…END keeps its block (for handlers), a single statement is
    // wrapped as a one-item sequence. A null/empty branch pushes nothing.
    internal void PushBranch(SqlNode? branch)
    {
        switch (branch)
        {
            case null:
                return;
            case BlockStatement blk:
                _control.Add(new SequenceActivation(blk, blk.Statements, 0));
                return;
            default:
                _control.Add(new SequenceActivation(null, new[] { branch }, 0));
                return;
        }
    }

    // Structural navigation to the next step point (an IExecutableStatement), or null when this frame has
    // completed. Pure — no server interaction: it only descends into nested blocks and pushes the loop
    // activations (whose header IS the step point). Condition evaluation / row fetch / leaf execution are
    // the interpreter's job (via the executor), not navigation's.
    internal IExecutableStatement? NextStepPoint()
    {
        while (_control.Count > 0)
        {
            switch (_control[^1])
            {
                case SequenceActivation seq:
                    if (seq.Index >= seq.Items.Count)
                    {
                        _control.RemoveAt(_control.Count - 1); // sequence complete
                        continue;
                    }
                    var item = seq.Items[seq.Index];
                    switch (item)
                    {
                        case BlockStatement blk:
                            seq.Index++;
                            _control.Add(new SequenceActivation(blk, blk.Statements, 0));
                            continue;
                        case WhileStatement w:
                            seq.Index++;
                            _control.Add(new WhileActivation(w));
                            continue;
                        case ForSelectStatement f:
                            seq.Index++;
                            _control.Add(new ForActivation(f));
                            continue;
                        case IExecutableStatement exec:
                            return exec; // leaf / IF / DML / EXECUTE PROCEDURE — stop here (not advanced)
                        default:
                            seq.Index++; // a non-executable node (defensive) — skip it losslessly
                            continue;
                    }
                case WhileActivation wa:
                    return wa.Node; // the WHILE header is the per-iteration step point
                case ForActivation fa:
                    return fa.Node; // the FOR header is the per-iteration step point
            }
        }
        return null;
    }
}

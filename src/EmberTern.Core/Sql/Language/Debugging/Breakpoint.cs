namespace EmberTern.Core.Sql.Debugging;

/// <summary>
/// A breakpoint as a <b>stop-policy object</b> (D12) — not a bare offset in a set. It owns everything that
/// decides whether reaching its step point should pause the session: an optional boolean
/// <see cref="Condition"/> (spec §9.8.2 — "just an expression", evaluated through the one D5 engine, never a
/// second evaluator) and a <see cref="HitCount"/> policy over a running tally of condition-satisfied
/// arrivals. Future breakpoint kinds (D12/D13) are meant to grow as further properties + policy on this one
/// model — evolving <see cref="Breakpoint"/> rather than spawning parallel collections/flags beside
/// <see cref="BreakpointSet"/>. Pure Core: the condition string is <i>held</i> here but <i>evaluated</i> by
/// <see cref="DebugSession"/> through the executor — a policy object decides, it never talks to the server.
/// </summary>
public sealed class Breakpoint
{
    private int _hits;

    /// <summary>Creates an unconditional, always-breaking breakpoint at <paramref name="offset"/> (a step
    /// point's <see cref="Ast.IExecutableStatement.Start"/>) — the pre-D12 plain breakpoint.</summary>
    public Breakpoint(int offset) => Offset = offset;

    /// <summary>The step point's source offset this breakpoint is set at.</summary>
    public int Offset { get; }

    /// <summary>An optional boolean expression gating the stop (spec §9.8.2): the session pauses only when it
    /// evaluates TRUE. Null / blank = unconditional. It is evaluated against the frame about to execute the
    /// step point, through the SAME engine as an <c>IF</c>/<c>WHILE</c> condition and Evaluate / Watches —
    /// there is no second evaluator. A condition that yields NULL is treated as not-true (three-valued logic,
    /// exactly as <c>IF</c> is); a condition that raises stops the session and surfaces the error (never
    /// silently skipped — spec §F).</summary>
    public string? Condition { get; set; }

    /// <summary>True when <see cref="Condition"/> is a non-blank expression.</summary>
    public bool HasCondition => !string.IsNullOrWhiteSpace(Condition);

    /// <summary>The hit-count policy (spec §9.8.2): break on every condition-satisfied arrival
    /// (<see cref="HitCountPolicy.Always"/>, the default) or on the Nth / ≥Nth / every-Nth arrival.</summary>
    public HitCountPolicy HitCount { get; set; } = HitCountPolicy.Always;

    /// <summary>How many times this location has been reached with its condition satisfied — a false / NULL
    /// condition never counts (matching common IDE semantics). The value <see cref="HitCount"/> is applied to.</summary>
    public int Hits => _hits;

    /// <summary>The stop decision given whether the (optional) condition was satisfied this arrival — the pure
    /// policy half (<see cref="DebugSession"/> evaluates the condition itself, via the executor). A
    /// non-satisfied condition never counts and never breaks; a satisfied one increments the tally and breaks
    /// iff the <see cref="HitCount"/> policy is met at the new tally.</summary>
    public bool ShouldBreak(bool conditionSatisfied)
    {
        if (!conditionSatisfied)
        {
            return false;
        }
        _hits++;
        return HitCount.IsMetAt(_hits);
    }

    /// <summary>Resets the hit tally to zero — called when a session (re)starts, so each run counts hits from
    /// scratch (the policy is unchanged). Lets one <see cref="Breakpoint"/> outlive a session (persist across
    /// launch/restart) without its hit-count policy drifting.</summary>
    public void ResetHits() => _hits = 0;
}

/// <summary>
/// When a breakpoint with a satisfied condition should actually break, as a function of its running hit tally
/// (spec §9.8.2). An immutable value object — the kind + its operand. <see cref="Always"/> breaks every time.
/// </summary>
public readonly record struct HitCountPolicy(HitCountKind Kind, int Value)
{
    /// <summary>Break on every condition-satisfied arrival (the default — a plain breakpoint).</summary>
    public static HitCountPolicy Always => new(HitCountKind.Always, 0);

    /// <summary>Break exactly on the <paramref name="n"/>th arrival.</summary>
    public static HitCountPolicy Exactly(int n) => new(HitCountKind.Exactly, n);

    /// <summary>Break on the <paramref name="n"/>th arrival and every one after it.</summary>
    public static HitCountPolicy AtLeast(int n) => new(HitCountKind.AtLeast, n);

    /// <summary>Break on every <paramref name="n"/>th arrival (a multiple of <paramref name="n"/>).</summary>
    public static HitCountPolicy Multiple(int n) => new(HitCountKind.Multiple, n);

    /// <summary>Builds the policy for a <paramref name="kind"/> + operand — the single construction point a UI
    /// editor uses to turn a picked kind + count into a policy, so the choice-to-policy mapping stays here in
    /// Core rather than in a ViewModel. <see cref="HitCountKind.Always"/> ignores <paramref name="value"/>.</summary>
    public static HitCountPolicy Of(HitCountKind kind, int value) => kind switch
    {
        HitCountKind.Exactly => Exactly(value),
        HitCountKind.AtLeast => AtLeast(value),
        HitCountKind.Multiple => Multiple(value),
        _ => Always,
    };

    /// <summary>Whether this policy breaks at the given (1-based) hit tally.</summary>
    public bool IsMetAt(int hits) => Kind switch
    {
        HitCountKind.Exactly => hits == Value,
        HitCountKind.AtLeast => hits >= Value,
        HitCountKind.Multiple => Value > 0 && hits % Value == 0,
        _ => true, // Always
    };
}

/// <summary>The hit-count comparison of a <see cref="HitCountPolicy"/> (spec §9.8.2).</summary>
public enum HitCountKind
{
    /// <summary>Break every time — no hit-count gate.</summary>
    Always,

    /// <summary>Break when the hit tally equals the value.</summary>
    Exactly,

    /// <summary>Break when the hit tally is at least the value.</summary>
    AtLeast,

    /// <summary>Break when the hit tally is a multiple of the value.</summary>
    Multiple,
}

using System.Collections.Generic;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>The DML event a debugged trigger is simulated as firing for (spec §8.1). A multi-action trigger
/// (<c>BEFORE INSERT OR UPDATE</c>) declares several; the user picks exactly one to simulate, which drives
/// NEW/OLD availability and the <c>INSERTING</c>/<c>UPDATING</c>/<c>DELETING</c> predicate values.</summary>
public enum TriggerEvent
{
    Insert,
    Update,
    Delete,
}

/// <summary>When a debugged trigger fires relative to the DML (spec §8.1). Only a <c>BEFORE</c> trigger may
/// write <c>NEW</c>; <c>OLD</c> is never writable.</summary>
public enum TriggerTiming
{
    Before,
    After,
}

/// <summary>Which trigger record a context column belongs to — <c>NEW</c> or <c>OLD</c>. (A dedicated enum
/// rather than the binder's <c>RecordAliasSymbol</c>, which is a semantic-model symbol; this is the debugger's
/// small value.)</summary>
public enum TriggerRecord
{
    New,
    Old,
}

/// <summary>One <c>NEW</c>/<c>OLD</c> context column referenced by a trigger body, paired with the <b>stable
/// synthetic frame-variable name</b> the harness uses in its place — because <c>NEW</c>/<c>OLD</c> do not exist
/// inside an <c>EXECUTE BLOCK</c> (spec §8.1). The synthetic name is assigned once per session over the whole
/// body (<see cref="ContextSubstitution.BuildColumns"/>), so the same <c>NEW.col</c> is the same frame variable
/// in every statement, in the frame, and in the Variables window. Pure Core.</summary>
/// <param name="Record">Whether this is a <c>NEW</c> or <c>OLD</c> column.</param>
/// <param name="Column">The column name, folded (Firebird's unquoted-identifier convention).</param>
/// <param name="Synthetic">The synthetic frame-variable name substituted for <c>NEW.col</c>/<c>OLD.col</c>.</param>
public sealed record ContextColumn(TriggerRecord Record, string Column, string Synthetic);

/// <summary>
/// The specialized state a debug <b>root</b> frame gains when the debugged routine is a <b>trigger</b> (spec
/// §8.1). Deliberately <b>not</b> a parallel model to the executor's per-routine context — it is the small,
/// cohesive value that context mounts on it as an optional field: <c>NEW</c>/<c>OLD</c> are ordinary frame
/// variables (the <see cref="Columns"/> map their references to synthetic names), so the only genuinely-new
/// state here is the simulated <see cref="Event"/> + <see cref="Timing"/>, which drive the
/// <c>INSERTING</c>/<c>UPDATING</c>/<c>DELETING</c> predicate values and the availability rules below. Pure Core.
/// </summary>
/// <param name="TargetTable">The table the trigger fires for — the columns of <c>NEW</c>/<c>OLD</c> (folded).</param>
/// <param name="Event">The single DML event being simulated (a multi-action trigger picks one).</param>
/// <param name="Timing">BEFORE or AFTER — only BEFORE makes <c>NEW</c> writable.</param>
/// <param name="Columns">The distinct <c>NEW</c>/<c>OLD</c> columns the body references, with synthetic names.</param>
public sealed record TriggerContext(
    string TargetTable,
    TriggerEvent Event,
    TriggerTiming Timing,
    IReadOnlyList<ContextColumn> Columns)
{
    /// <summary><c>OLD</c> is available for <c>UPDATE</c> and <c>DELETE</c> (absent for <c>INSERT</c>) — §8.1.</summary>
    public bool OldAvailable => Event is TriggerEvent.Update or TriggerEvent.Delete;

    /// <summary><c>NEW</c> is available for <c>INSERT</c> and <c>UPDATE</c> (absent for <c>DELETE</c>) — §8.1.</summary>
    public bool NewAvailable => Event is TriggerEvent.Insert or TriggerEvent.Update;

    /// <summary><c>NEW</c> is writable only in a <c>BEFORE INSERT</c>/<c>UPDATE</c> trigger; <c>OLD</c> is never
    /// writable (§8.1). This is the engine truth that drives the parameter editor (§9.3) and the write-back.</summary>
    public bool NewWritable => Timing == TriggerTiming.Before && NewAvailable;
}

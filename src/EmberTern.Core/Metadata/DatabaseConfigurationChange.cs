using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Metadata;

/// <summary>Which of the three editable database settings one Apply is about.</summary>
public enum DatabaseSetting
{
    SweepInterval,
    ForcedWrites,
    ReserveSpace,
}

/// <summary>
/// What Apply asks the server to change — <b>only what the user actually edited</b>.
///
/// <para>⭐ Every member is nullable and null means <i>do not touch</i>. That is what makes "opening the
/// window and pressing Apply changes nothing" true <b>by construction</b> rather than by care, and it is the
/// reason the whole record exists instead of passing a fresh <see cref="DatabaseProperties"/>: sending a full
/// snapshot back would write values the user never looked at, into a shared production database, through an
/// API with no rollback.</para>
/// </summary>
public sealed record DatabaseConfigurationChange
{
    public int? SweepInterval { get; init; }

    public bool? ForcedWrites { get; init; }

    public bool? ReserveSpace { get; init; }

    /// <summary>Whether there is anything at all to send — what the Apply button is gated on.</summary>
    public bool HasChanges => SweepInterval is not null || ForcedWrites is not null || ReserveSpace is not null;

    /// <summary>
    /// Builds the change by comparing what the user has now against what was read.
    /// <para>⚠ Pure and total: a value equal to the original yields null for that member, so a field the user
    /// typed into and then typed back is correctly NOT sent.</para>
    /// </summary>
    public static DatabaseConfigurationChange Between(DatabaseProperties original, int sweep, bool forced, bool reserve)
        => new()
        {
            SweepInterval = sweep == original.SweepInterval ? null : sweep,
            ForcedWrites = forced == original.ForcedWrites ? null : forced,
            ReserveSpace = reserve == original.ReserveSpace ? null : reserve,
        };
}

/// <summary>What happened to ONE setting during an Apply.</summary>
/// <param name="Setting">Which setting this outcome is about.</param>
/// <param name="Error">The server's raw message, or null when it succeeded.</param>
/// <param name="SqlState">SQLSTATE, when the driver reported one — the only thing a caller may branch on.</param>
/// <param name="GdsCodes">The GDS error codes, likewise.</param>
public sealed record DatabaseSettingOutcome(
    DatabaseSetting Setting,
    string? Error = null,
    string? SqlState = null,
    IReadOnlyList<int>? GdsCodes = null)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// The result of one Apply.
///
/// <para>⚠⚠ <b>Apply is NOT atomic, and that is a fact about the API rather than a design choice</b>: each
/// setting is its own Services call, so a partial success is a real, reachable state. Hence a LIST of
/// per-setting outcomes instead of a single success flag — a caller that could only say "it failed" would be
/// unable to tell the user which two of three changes are now live in the database.</para>
/// </summary>
public sealed record DatabaseConfigurationResult(IReadOnlyList<DatabaseSettingOutcome> Outcomes)
{
    public bool AllSucceeded => Outcomes.All(o => o.Succeeded);

    public bool AnySucceeded => Outcomes.Any(o => o.Succeeded);

    public bool IsPartial => AnySucceeded && Outcomes.Any(o => !o.Succeeded);

    public IEnumerable<DatabaseSettingOutcome> Failures => Outcomes.Where(o => !o.Succeeded);
}

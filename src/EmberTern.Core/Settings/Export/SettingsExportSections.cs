using System;
using System.Collections.Generic;

namespace EmberTern.Core.Settings.Export;

/// <summary>
/// The section names an export can carry, as the strings they are persisted as.
///
/// <para><b>⚠ STRINGS, NOT AN ENUM, and it is the same ratified reason <see cref="Preferences"/>' properties are
/// strings (design §5.2.3).</b> These names travel inside the payload, so they are persisted data.
/// <c>JsonStringEnumConverter</c> <b>throws</b> on a name it does not know, and in this codebase that failure is
/// total: one unrecognised member would make a whole file unreadable rather than making one section unknown. As
/// strings, a section a newer build invented is simply a name this build does not act on — which is precisely
/// the forward compatibility the format version exists to manage gracefully.</para>
///
/// <para>⭐ The general rule behind that: <b>adding a value to a persisted enum is not an additive change, even
/// though adding a property is.</b></para>
///
/// <para>Persisted verbatim, therefore <b>append-only</b>: never rename or reuse a value once shipped.</para>
/// </summary>
public static class SettingsExportSections
{
    /// <summary>The scalar user preferences — the definition of a portable user setting.</summary>
    public const string Preferences = "Preferences";

    /// <summary>Per-grid column order / widths / auto-fit. Preference, and grid ids are stable strings.</summary>
    public const string GridProfiles = "GridProfiles";

    /// <summary>The user's own organisation of their connections.</summary>
    public const string Folders = "Folders";

    /// <summary>Connection profiles — host/port/database/user/charset/dialect, which are usually identical on a
    /// second machine. ⚠ Never their passwords unless <see cref="Passwords"/> is also present.</summary>
    public const string Connections = "Connections";

    /// <summary>
    /// Marks that the <see cref="Connections"/> in this file carry their passwords (ratified Q2 — omitted by
    /// default, explicit opt-in).
    /// <para>⭐ Note what this section is <b>not</b>: it is not a data section of its own, it is a statement about
    /// another one. Recording it separately is what lets an import say <i>"this file contains database
    /// credentials"</i> without decrypting a password to find out.</para>
    /// </summary>
    public const string Passwords = "Passwords";

    /// <summary>Per-connection tabs, SQL text and saved queries (ratified Q6 — separate opt-in, off by default).
    /// This is <i>work</i> rather than settings, and it can be large.</summary>
    public const string Workspaces = "Workspaces";

    /// <summary>Data Import configurations. Genuinely reusable, but they embed source file paths, so they are
    /// machine-dependent in part — hence opt-in.</summary>
    public const string ImportProfiles = "ImportProfiles";

    /// <summary>Every section this build knows, so a test can hold all of them to the same invariants without a
    /// hand-maintained list going stale beside them.</summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Preferences, GridProfiles, Folders, Connections, Passwords, Workspaces, ImportProfiles,
    };

    /// <summary>Case-insensitive membership, matching how every other persisted key in this file is compared.</summary>
    public static bool IsKnown(string? section)
    {
        foreach (var known in All)
        {
            if (string.Equals(known, section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

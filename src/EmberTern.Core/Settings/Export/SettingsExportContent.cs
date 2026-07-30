using System;
using System.Collections.Generic;

namespace EmberTern.Core.Settings.Export;

/// <summary>
/// The <b>encrypted payload</b> of an export: which sections the file carries, and the settings themselves.
///
/// <para>⭐ <b>The settings travel as an <see cref="ApplicationSettings"/>, and that is the single most useful
/// decision in this format.</b> It is not a second representation of the settings — it is the same aggregate with
/// the excluded parts removed. Three things follow, and each would otherwise have been work:</para>
/// <list type="number">
///   <item><description>
///     <b>The existing schema-migration ladder applies unchanged.</b> An import calls
///     <c>ApplicationSettingsStore.MigrateToCurrentVersion</c> — the very method <c>LoadWithStatus</c> calls —
///     rather than anything resembling it. ⛔ A second migration path would defeat the whole point of keeping the
///     three version numbers separate (<see cref="SettingsExportFormat"/>).
///   </description></item>
///   <item><description>
///     <b>Serialization cannot drift from <c>settings.dat</c>'s.</b> Both use
///     <c>ApplicationSettingsStore.JsonOptions</c>, so the enums inside (<c>TransactionProfile</c>,
///     <c>WorkspaceTabKind</c>, <c>MetadataObjectKind</c>) are written as the same stable names in both files.
///   </description></item>
///   <item><description>
///     <b>Adding a section later is additive</b> — a new name in <see cref="SettingsExportSections"/> and a new
///     bit of an already-serialized aggregate, with the format version there to describe the change.
///   </description></item>
/// </list>
///
/// <para>⚠ <b><see cref="Sections"/> is authoritative, not inferred from what is non-empty.</b> "Not exported"
/// and "exported and empty" are different facts, and only the list can tell them apart: a user who genuinely has
/// no folders must not have an import silently decide their folders were not included — nor the reverse, where an
/// unselected section's absence reads as "this user has none".</para>
/// </summary>
public sealed class SettingsExportContent
{
    /// <summary>The sections this file carries. Names are <see cref="SettingsExportSections"/>' strings; an
    /// unrecognised one is ignored rather than fatal (see that type for why they are not an enum).</summary>
    public List<string> Sections { get; set; } = new();

    /// <summary>The exported settings, with every ❌ field already removed by the writer. Its
    /// <c>SchemaVersion</c> is the settings-shape version and is migrated by the existing ladder — distinct from
    /// the envelope's format version.</summary>
    public ApplicationSettings Settings { get; set; } = new();

    /// <summary>Whether this file claims to carry <paramref name="section"/>.</summary>
    public bool Contains(string section)
    {
        foreach (var declared in Sections)
        {
            if (string.Equals(declared, section, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Whether this file carries connection passwords — the one question an import surface must be able to answer
    /// before it merges anything, and the reason <see cref="SettingsExportSections.Passwords"/> is recorded as a
    /// section rather than left to be discovered by inspecting values.
    /// </summary>
    public bool CarriesPasswords => Contains(SettingsExportSections.Passwords);
}

using System.Collections.Generic;

namespace EmberTern.Core.Settings.Export;

/// <summary>
/// What the caller wants an export to contain. <b>The property defaults ARE the ratified content classification
/// (design §6.3.4)</b>, so a caller that passes <c>new SettingsExportOptions()</c> gets exactly the file that
/// table describes — the three sensitive or bulky sections off, everything portable on.
///
/// <para>⭐ <b>What is missing from this type is as deliberate as what is on it.</b> There is no option for
/// <c>ParameterHistory</c>, <c>DebugWatches</c> or <c>WorkspaceState.WindowBounds</c>, because those are ❌
/// rather than opt-in: execution history keyed to connection ids, and monitor geometry that can place a window
/// off-screen. Making them unrepresentable is stronger than documenting that nobody should ask.
/// ⚠ <c>ConnectionProfile.ClientLibraryPath</c> used to be listed here as a third ❌ row; the field itself was
/// removed in 2026-08-05 (S-5) because it could have no effect at all, so there is nothing left to exclude.</para>
/// </summary>
public sealed record SettingsExportOptions
{
    /// <summary>The scalar user preferences. ✅ On by default.</summary>
    public bool Preferences { get; init; } = true;

    /// <summary>Per-grid column layouts. ✅ On by default.</summary>
    public bool GridProfiles { get; init; } = true;

    /// <summary>Connection folders. ✅ On by default.</summary>
    public bool Folders { get; init; } = true;

    /// <summary>Connection profiles, without their passwords unless <see cref="Passwords"/> is also set.
    /// ✅ On by default.</summary>
    public bool Connections { get; init; } = true;

    /// <summary>
    /// ⚠ Include connection passwords — <b>off by default, ratified Q2</b>. The UI that offers this must state
    /// that the file will contain database credentials.
    ///
    /// <para>Inside <c>settings.dat</c> a password is plaintext within a DPAPI-encrypted blob, which is safe
    /// because DPAPI is bound to the account. An export is by definition <i>not</i> so bound, so this writes
    /// credentials in a form that travels.</para>
    ///
    /// <para>⭐ <b>Ratified Q2 originally also said "never export passwords into an unencrypted file — refuse
    /// that combination", and that clause must NOT be implemented.</b> Every export is encrypted (Q3), so the
    /// combination cannot arise, and an unreachable guard reads as a real safety net to the next person, who then
    /// reasons about a state the code cannot enter. The passphrase is unconditional; this flag is purely a
    /// content decision.</para>
    ///
    /// <para>Has no effect unless <see cref="Connections"/> is set — there is nothing to attach a password to.</para>
    /// </summary>
    public bool Passwords { get; init; }

    /// <summary>⚠ Per-connection tabs, SQL text and saved queries — <b>off by default, ratified Q6</b>. Work
    /// rather than settings, and potentially large. Window bounds never travel with it.</summary>
    public bool Workspaces { get; init; }

    /// <summary>⚠ Data Import configurations — off by default, because they embed source file paths.</summary>
    public bool ImportProfiles { get; init; }

    /// <summary>True when nothing at all was selected — a file worth refusing to write rather than producing an
    /// encrypted envelope around an empty payload.</summary>
    public bool IsEmpty => !Preferences && !GridProfiles && !Folders && !Connections && !Workspaces
                           && !ImportProfiles;

    /// <summary>The section names these options resolve to, in <see cref="SettingsExportSections.All"/>'s order —
    /// the list that goes into the payload and that an import reads back.</summary>
    public IReadOnlyList<string> Sections()
    {
        var sections = new List<string>();
        if (Preferences) sections.Add(SettingsExportSections.Preferences);
        if (GridProfiles) sections.Add(SettingsExportSections.GridProfiles);
        if (Folders) sections.Add(SettingsExportSections.Folders);
        if (Connections) sections.Add(SettingsExportSections.Connections);
        // Only meaningful alongside the connections it describes, so it is not recorded on its own.
        if (Connections && Passwords) sections.Add(SettingsExportSections.Passwords);
        if (Workspaces) sections.Add(SettingsExportSections.Workspaces);
        if (ImportProfiles) sections.Add(SettingsExportSections.ImportProfiles);
        return sections;
    }
}

using System;
using System.Collections.Generic;

namespace EmberTern.Core.Settings.Export;

/// <summary>
/// Which of an export's sections the user chose to <b>accept</b>.
///
/// <para>⭐ <b>Deliberately a separate type from <see cref="SettingsExportOptions"/>, even though the flags line
/// up.</b> They answer different questions and their defaults are opposites: the export options' defaults ARE
/// the ratified content classification (§6.3.4 — "what should normally travel"), whereas a selection has no
/// defensible default at all, because it depends on what a particular file happens to carry. Everything here is
/// therefore <b>off</b> until something switches it on, and <see cref="EverythingIn"/> is the one place "take
/// what this file has" is expressed.</para>
///
/// <para>⚠ A flag set for a section the file does not carry is <b>not</b> an error and must not be one: it means
/// "I would have taken it", and the applier can only take what is there. That is what keeps a selection
/// independent of the file it is applied to, so the same selection can be reasoned about, tested and asserted
/// without a file in hand.</para>
/// </summary>
public sealed record SettingsImportSelection
{
    /// <summary>The scalar user preferences.</summary>
    public bool Preferences { get; init; }

    /// <summary>Per-grid column layouts, merged by grid id.</summary>
    public bool GridProfiles { get; init; }

    /// <summary>Connection folders and the connection→folder assignment, merged by id.</summary>
    public bool Folders { get; init; }

    /// <summary>Connection profiles, merged by <c>ConnectionProfile.Id</c> — a re-import updates rather than
    /// duplicating (§6.3.4).</summary>
    public bool Connections { get; init; }

    /// <summary>
    /// ⚠ Take the passwords the file carries, overwriting the ones stored locally for the same connection id.
    ///
    /// <para><b>This is the one flag whose absence has to be actively honoured rather than merely ignored.</b> An
    /// export without passwords carries every connection with an <i>empty</i> password (that is how the exporter
    /// omits them), so a merge that copied the incoming profile wholesale would silently erase the password the
    /// user already had stored locally — losing a credential while "importing settings". The applier therefore
    /// keeps the local password unless this is set and the incoming one is non-empty.</para>
    ///
    /// <para>Has no effect unless <see cref="Connections"/> is also set; there is nothing to attach a password
    /// to.</para>
    /// </summary>
    public bool Passwords { get; init; }

    /// <summary>
    /// ⚠ Per-connection tabs, SQL text and saved queries.
    /// <para>Unlike every other section this one cannot take effect in the running session — see
    /// <c>SettingsImportApplier</c>'s remarks. It is written, and the surface that offers it must say that it
    /// applies on the next start.</para>
    /// </summary>
    public bool Workspaces { get; init; }

    /// <summary>Data Import configurations, merged by profile id.</summary>
    public bool ImportProfiles { get; init; }

    /// <summary>True when nothing was selected — an import that would change nothing, which the applier refuses
    /// rather than reporting as a success that did nothing.</summary>
    public bool IsEmpty => !Preferences && !GridProfiles && !Folders && !Connections && !Passwords
                           && !Workspaces && !ImportProfiles;

    /// <summary>Nothing selected. The starting point for a surface that asks.</summary>
    public static SettingsImportSelection Nothing { get; } = new();

    /// <summary>
    /// Everything <paramref name="content"/> actually declares — the natural initial state of an import surface,
    /// since a user who picked this file did so in order to take what is in it.
    /// <para>Reads <see cref="SettingsExportContent.Sections"/>, never "which properties look non-empty":
    /// "not exported" and "exported and empty" are different facts and only the section list can tell them
    /// apart.</para>
    /// </summary>
    public static SettingsImportSelection EverythingIn(SettingsExportContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new SettingsImportSelection
        {
            Preferences = content.Contains(SettingsExportSections.Preferences),
            GridProfiles = content.Contains(SettingsExportSections.GridProfiles),
            Folders = content.Contains(SettingsExportSections.Folders),
            Connections = content.Contains(SettingsExportSections.Connections),
            Passwords = content.Contains(SettingsExportSections.Passwords),
            Workspaces = content.Contains(SettingsExportSections.Workspaces),
            ImportProfiles = content.Contains(SettingsExportSections.ImportProfiles),
        };
    }

    /// <summary>The section names this selection resolves to, in <see cref="SettingsExportSections.All"/>'s
    /// order — what an import report lists as taken.</summary>
    public IReadOnlyList<string> Sections()
    {
        var sections = new List<string>();
        if (Preferences) sections.Add(SettingsExportSections.Preferences);
        if (GridProfiles) sections.Add(SettingsExportSections.GridProfiles);
        if (Folders) sections.Add(SettingsExportSections.Folders);
        if (Connections) sections.Add(SettingsExportSections.Connections);
        if (Connections && Passwords) sections.Add(SettingsExportSections.Passwords);
        if (Workspaces) sections.Add(SettingsExportSections.Workspaces);
        if (ImportProfiles) sections.Add(SettingsExportSections.ImportProfiles);
        return sections;
    }

    /// <summary>This selection narrowed to what <paramref name="content"/> can actually satisfy — the value an
    /// import report should describe, as opposed to what the user was willing to take.</summary>
    public SettingsImportSelection IntersectWith(SettingsExportContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new SettingsImportSelection
        {
            Preferences = Preferences && content.Contains(SettingsExportSections.Preferences),
            GridProfiles = GridProfiles && content.Contains(SettingsExportSections.GridProfiles),
            Folders = Folders && content.Contains(SettingsExportSections.Folders),
            Connections = Connections && content.Contains(SettingsExportSections.Connections),
            Passwords = Passwords && content.CarriesPasswords,
            Workspaces = Workspaces && content.Contains(SettingsExportSections.Workspaces),
            ImportProfiles = ImportProfiles && content.Contains(SettingsExportSections.ImportProfiles),
        };
    }
}

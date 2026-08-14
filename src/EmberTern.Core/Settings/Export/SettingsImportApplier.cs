using System;
using System.Collections.Generic;
using System.IO;
using EmberTern.Core.Localization;

namespace EmberTern.Core.Settings.Export;

/// <summary>How an attempt to write an imported configuration into <c>settings.dat</c> ended. Classified by
/// CAUSE, like every other status in this format.</summary>
public enum SettingsImportApplyStatus
{
    /// <summary>The selected sections were merged into <c>settings.dat</c>.</summary>
    Applied,

    /// <summary>Nothing was selected, or nothing selected is in the file. Deliberately not reported as a
    /// success: an import that changed nothing while claiming to have worked is the least diagnosable outcome
    /// available.</summary>
    NothingSelected,

    /// <summary>The current <c>settings.dat</c> may not be replaced — either because a newer build wrote it or
    /// because this build could not read it (audit A-03). Nothing was written.</summary>
    Refused,
}

/// <summary>The outcome of an apply, including where the pre-import copy was preserved.</summary>
public sealed class SettingsImportApplyResult
{
    internal SettingsImportApplyResult(
        SettingsImportApplyStatus status,
        string message,
        LocalizableMessage? localized,
        string? preservedAt,
        IReadOnlyList<string> appliedSections)
    {
        Status = status;
        Message = message;
        Localized = localized;
        PreservedAt = preservedAt;
        AppliedSections = appliedSections;
    }

    public SettingsImportApplyStatus Status { get; }

    /// <summary>A message fit to show the user, or empty on success.</summary>
    public string Message { get; }

    /// <summary>
    /// <inheritdoc cref="SettingsImportInspection.Localized" path="/summary/para[1]"/>
    ///
    /// <para>⚠ For <see cref="SettingsImportApplyStatus.Refused"/> this may be the settings <b>store's</b> own
    /// refusal, forwarded whole from <c>ApplicationSettingsStore</c> (C4a's keys) rather than restated here:
    /// one sentence, one producer. ⛔ It can also be null on that status — a store from a build older than
    /// C4a would supply English only, and the caller must fall back to <see cref="Message"/>.</para>
    /// </summary>
    public LocalizableMessage? Localized { get; }

    /// <summary>Where the previous <c>settings.dat</c> was copied to, or null when there was none (a first run)
    /// or the import never got that far.</summary>
    public string? PreservedAt { get; }

    /// <summary>The sections actually taken — the selection narrowed to what the file carried.</summary>
    public IReadOnlyList<string> AppliedSections { get; }

    public bool Applied => Status == SettingsImportApplyStatus.Applied;
}

/// <summary>
/// Writes an imported <see cref="SettingsExportContent"/> into <c>settings.dat</c>: <b>section by section,
/// merging rather than replacing, preserving the previous file first, and refusing rather than guessing</b>
/// (design §6.3.4 — a rule #11 surface, since it touches connection profiles and credentials).
///
/// <para><b>⭐ The three properties that make it non-destructive, each of which is a way this could have lost
/// data:</b></para>
/// <list type="number">
///   <item><description>
///     <b>An unselected section is not touched at all.</b> The merge starts from the settings currently on
///     disk, so "I only wanted the theme" leaves connections, folders, grids and workspaces byte-identical.
///   </description></item>
///   <item><description>
///     <b>Connections merge by <c>Id</c>.</b> A re-import of the same file updates the same profiles instead of
///     duplicating them, and a profile the file does not mention is left alone.
///   </description></item>
///   <item><description>
///     ⚠ <b>One connection field is taken from the LOCAL profile, not the incoming one</b> —
///     <c>Password</c>, unless passwords were both exported
///     and selected. This is the subtle one: an export without passwords carries every connection with an
///     <i>empty</i> password, so copying the incoming profile wholesale would erase a stored credential as a
///     side effect of importing a theme. <c>SettingsExporter.BuildContent</c> is where a NEW field's travel is
///     decided (and guarded by reflection); this is where "and what happens to the local value" is decided.
///   </description></item>
/// </list>
///
/// <para>⚠ <b>The <c>Workspaces</c> section cannot take effect in the running session, and that is a property
/// of the app rather than of this class.</b> The window captures the live workspace on close and saves it, so a
/// session that continues after importing workspaces would overwrite them on exit — the import would silently
/// undo itself. Writing it is correct; the caller is responsible for either applying it or suppressing that
/// capture, and for saying which. See the App's <c>SettingsPortability</c>.</para>
/// </summary>
public static class SettingsImportApplier
{
    /// <summary>
    /// Merges the selected sections of <paramref name="imported"/> into <paramref name="current"/>.
    /// </summary>
    /// <param name="current">The settings as they are now — normally freshly loaded from <c>settings.dat</c>.
    /// <b>Mutated in place</b> and returned; the caller owns it.</param>
    /// <param name="imported">The decrypted, migrated export content.</param>
    /// <param name="selection">What to take. Pass a selection already narrowed with
    /// <see cref="SettingsImportSelection.IntersectWith"/>, or accept that a flag for a section the file lacks
    /// simply does nothing.</param>
    /// <remarks>
    /// Pure apart from mutating <paramref name="current"/>: no file access, no clock, no store. That is what
    /// lets every merge rule be asserted directly rather than through an encrypt/decrypt round trip.
    /// </remarks>
    public static ApplicationSettings Merge(
        ApplicationSettings current, SettingsExportContent imported, SettingsImportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(selection);

        var incoming = imported.Settings;

        if (selection.Preferences && imported.Contains(SettingsExportSections.Preferences))
        {
            // Normalized on the way in, as at every other boundary — the reader already did it, and Validate is
            // idempotent, so this is a statement of the contract rather than a second correction.
            current.UserSettings.Preferences = PreferencesStore.Validate(incoming.UserSettings.Preferences);
        }

        if (selection.GridProfiles && imported.Contains(SettingsExportSections.GridProfiles))
        {
            foreach (var profile in incoming.UserSettings.GridProfiles)
            {
                var list = current.UserSettings.GridProfiles;
                var index = list.FindIndex(p => string.Equals(p.GridId, profile.GridId, StringComparison.Ordinal));
                if (index >= 0) list[index] = profile;
                else list.Add(profile);
            }
        }

        if (selection.Folders && imported.Contains(SettingsExportSections.Folders))
        {
            MergeFolders(current, incoming);
        }

        if (selection.Connections && imported.Contains(SettingsExportSections.Connections))
        {
            MergeConnections(current, incoming, takePasswords: selection.Passwords && imported.CarriesPasswords);
        }

        if (selection.Workspaces && imported.Contains(SettingsExportSections.Workspaces))
        {
            // ⚠ Window bounds are the local machine's, always. They are ❌ in §6.3.4 (monitor geometry can place
            // the window off-screen), so the incoming value is null by construction — and assigning the imported
            // workspace wholesale would therefore replace a perfectly good local geometry with nothing.
            var bounds = current.Workspace.WindowBounds;
            current.Workspace = incoming.Workspace;
            current.Workspace.WindowBounds = bounds;
        }

        if (selection.ImportProfiles && imported.Contains(SettingsExportSections.ImportProfiles))
        {
            foreach (var profile in incoming.UserSettings.ImportProfiles)
            {
                var list = current.UserSettings.ImportProfiles;
                var index = list.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.Ordinal));
                if (index >= 0) list[index] = profile;
                else list.Add(profile);
            }
        }

        return current;
    }

    /// <summary>
    /// Merges the selected sections into the store's <c>settings.dat</c>, preserving the previous file first.
    ///
    /// <para>The order of the steps is the design, for the same reason the import checks' order is: an empty
    /// selection is refused before anything is read, a file we may not replace is refused before anything is
    /// copied, and nothing is merged until the previous bytes are safely beside it.</para>
    /// </summary>
    public static SettingsImportApplyResult Apply(
        ApplicationSettingsStore store, SettingsExportContent content, SettingsImportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(selection);

        var effective = selection.IntersectWith(content);
        if (effective.IsEmpty)
        {
            return new SettingsImportApplyResult(
                SettingsImportApplyStatus.NothingSelected,
                "Nothing was selected to import, so nothing was changed.",
                LocalizableMessage.Of(SettingsExportMessages.NothingSelected),
                null,
                Array.Empty<string>());
        }

        // Asked BEFORE the copy: see ApplicationSettingsStore.CanSave for why the ordering matters here rather
        // than relying on Save's own refusal.
        // ⭐ The two-out overload exists for exactly this: the store's refusal is FORWARDED in both forms, so the
        // import surfaces the store's own sentence instead of a second one saying the same thing (D‑3, C4a).
        if (!store.CanSave(out var blocked, out var blockedMessage))
        {
            return new SettingsImportApplyResult(
                SettingsImportApplyStatus.Refused, blocked, blockedMessage, null, Array.Empty<string>());
        }

        string? preservedAt;
        try
        {
            preservedAt = store.CopyAsideForImport();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // ⚠ Refusing to import because we could not make the recovery copy is deliberate. The copy is what
            // makes this operation undoable by hand; importing without one would be the one irreversible write in
            // the whole feature.
            return new SettingsImportApplyResult(
                SettingsImportApplyStatus.Refused,
                "The current settings could not be copied aside, so nothing was imported: " + ex.Message,
                LocalizableMessage.Of(SettingsExportMessages.CouldNotCopyAside, ex.Message),
                null,
                Array.Empty<string>());
        }

        // ⚠ Through Update — see ApplicationSettingsStore.Update. An import MERGES, so the loaded file is the
        // merge base: `Load() ?? new ApplicationSettings()` would have merged into DEFAULTS after a transient
        // read failure and written that, dropping every section the import did not carry.
        store.Update(current => Merge(current, content, effective));

        if (store.LastSaveDiagnostic is { } diagnostic)
        {
            // Save re-checks the file it is about to replace, so this is reachable even after CanSave agreed.
            // ⚠ Forwarded in both forms, from the same setter that recorded them — never recomposed here.
            return new SettingsImportApplyResult(
                SettingsImportApplyStatus.Refused, diagnostic, store.LastSaveMessage, preservedAt,
                Array.Empty<string>());
        }

        return new SettingsImportApplyResult(
            SettingsImportApplyStatus.Applied, string.Empty, null, preservedAt, effective.Sections());
    }

    private static void MergeFolders(ApplicationSettings current, ApplicationSettings incoming)
    {
        var folders = current.Folders;

        foreach (var folder in incoming.Folders.Folders)
        {
            var index = folders.Folders.FindIndex(f => string.Equals(f.Id, folder.Id, StringComparison.Ordinal));
            if (index >= 0) folders.Folders[index] = folder;
            else folders.Folders.Add(folder);
        }

        // Per-connection facts, keyed by connection id. An entry for a connection this installation does not
        // have is harmless — ReloadConnections prunes stale mappings on the next rebuild — and keeping it is what
        // makes "import the folders now, the connections later" work.
        foreach (var pair in incoming.Folders.ConnectionFolderMap)
        {
            folders.ConnectionFolderMap[pair.Key] = pair.Value;
        }
        foreach (var pair in incoming.Folders.ConnectionSortOrders)
        {
            folders.ConnectionSortOrders[pair.Key] = pair.Value;
        }

        // Expansion is a union rather than a replacement: the set means "these nodes render expanded", and a
        // local node absent from the file has not been collapsed by anybody — it was simply not in that file.
        foreach (var id in incoming.Folders.ExpandedNodeIds)
        {
            folders.ExpandedNodeIds.Add(id);
        }

        // ⚠ ExpandStateInitialized stays LOCAL. It records that THIS installation has already run the one-time
        // seed of folder ids into ExpandedNodeIds; importing another machine's answer to that would either skip
        // a seed this installation still needs or re-run one it has already done.
    }

    private static void MergeConnections(
        ApplicationSettings current, ApplicationSettings incoming, bool takePasswords)
    {
        foreach (var profile in incoming.Connections)
        {
            var list = current.Connections;
            var index = list.FindIndex(c => string.Equals(c.Id, profile.Id, StringComparison.Ordinal));
            if (index < 0)
            {
                // A profile this installation has never seen. Its password is whatever the file carried (empty
                // when passwords were not exported) — correct: there is no local value to preserve.
                list.Add(profile);
                continue;
            }

            var local = list[index];

            // The trap this whole method exists for. Keep the stored credential unless the user asked for the
            // file's and the file actually has one.
            if (!takePasswords || string.IsNullOrEmpty(profile.Password))
            {
                profile.Password = local.Password;
            }

            list[index] = profile;
        }
    }
}

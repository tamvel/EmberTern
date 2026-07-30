namespace EmberTern.Core.Settings.Export;

/// <summary>
/// ⭐ The identity and the version contract of EmberTern's own settings-export format.
///
/// <para><b>Three version-ish numbers are in play across an import and each has exactly ONE job. Confusing them
/// is the single most likely way to damage this format, so they are named here together:</b></para>
/// <list type="table">
///   <item>
///     <term><see cref="CurrentFormatVersion"/></term>
///     <description><b>The migration contract.</b> Governs the envelope and which sections exist. Authoritative:
///     an older value is migrated stepwise, a newer one is refused.</description>
///   </item>
///   <item>
///     <term><c>ApplicationSettings.SchemaVersion</c> (inside the payload)</term>
///     <description>Governs the shape of <c>ApplicationSettings</c> itself, and is handled by the ladder that
///     already exists — <c>ApplicationSettingsStore.MigrateToCurrentVersion</c>. ⛔ <b>Do not collapse it into
///     the format version.</b> That would tie "we added a section to the export" to "the settings shape
///     changed", forcing a schema bump, which trips downgrade protection and makes older builds refuse the
///     whole <c>settings.dat</c>.</description>
///   </item>
///   <item>
///     <term><c>SettingsExportHeader.AppVersion</c></term>
///     <description>⛔ <b>Diagnostics only. Never branch on it.</b> It exists so a bug report can say "this file
///     came from 0.5.0". Keying behaviour to a version <i>string</i> is the shape gotcha #289 already burned this
///     project on; the moment any code reads it as a condition, the format has two competing contracts and the
///     weaker one wins by accident.</description>
///   </item>
/// </list>
/// </summary>
public static class SettingsExportFormat
{
    /// <summary>
    /// ⭐ The literal first bytes of an export file — its <b>identity</b>, answering <i>"is this even our
    /// file?"</i> before any parsing, versioning or passphrase prompt.
    ///
    /// <para><b>⚠ Deliberately NOT <c>settings.dat</c>'s magic, and that is ratified decision Q13.</b>
    /// <c>SettingsFileContainer.Magic</c> is <c>EMBERTERN-SETTINGS</c> and has been since the container shipped.
    /// Had the export reused it, two different formats would declare the same identity and the first check could
    /// not tell them apart — so a user who picked <c>settings.dat</c> in the import dialog would pass identity,
    /// pass version, pass scheme, be <b>asked for a passphrase</b>, and be told "wrong passphrase" about a file
    /// that never had one. That is the precise outcome the ordered checks exist to prevent: <b>never ask for a
    /// credential that cannot possibly work.</b> Identity is per FORMAT, not per product.</para>
    ///
    /// <para>⛔ <b>Never version the magic.</b> It is identity; <see cref="CurrentFormatVersion"/> is the
    /// contract. A magic that moved with the format would make an old file report "not an EmberTern settings
    /// file" instead of "an older export, migrating" — destroying exactly the diagnostic value it was added
    /// for.</para>
    ///
    /// <para>It is also self-documenting on purpose: the header is cleartext, so this file <i>will</i> be opened
    /// in a text editor by someone whose import failed, and a readable first line turns "I cannot open this
    /// file" into a self-answering situation for about thirty bytes.</para>
    /// </summary>
    public const string Magic = "EMBERTERN-SETTINGS-EXPORT";

    /// <summary>
    /// The format version this build writes, and the ceiling it can read.
    /// <para>Version 1 is the first. There is deliberately no version 0 — a header claiming one is refused
    /// rather than guessed at, which is what <c>SettingsExportMigration</c> proves about the ladder today.</para>
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// The oldest format version this build holds a migration path from.
    ///
    /// <para><b>⭐ The invariant it states: <c>SettingsExportMigration</c> has a step for every version from here
    /// up to <see cref="CurrentFormatVersion"/>.</b> That is what lets an import refuse a too-old file from the
    /// header alone — <i>before</i> the passphrase is requested — instead of discovering the gap after
    /// decrypting. Whether a step exists is a fact about the version, not about the payload, so it belongs on the
    /// early side of the credential prompt.</para>
    ///
    /// <para>⚠ <b>Raising this is dropping support for those files.</b> It is not a tidy-up: it must be a
    /// deliberate decision, and the refusal message names both numbers so the user can tell which build wrote
    /// what.</para>
    /// </summary>
    public const int OldestSupportedFormatVersion = 1;

    /// <summary>
    /// EmberTern's own extension — never <c>.json</c>.
    /// <para>The extension is part of "this is our artifact, not a public document": a <c>.json</c> file invites
    /// hand-editing of a file that is neither editable nor readable (the payload is encrypted).</para>
    /// </summary>
    public const string FileExtension = ".etsettings";

    /// <summary>
    /// Upper bound on the cleartext header line, in bytes.
    /// <para>⚠ A guard rather than a limit. The header is read from the stream <i>before</i> the file is loaded,
    /// so that accidentally picking a 2 GB file costs a few bytes and not a full read — a cap is what makes that
    /// true for a file with no newline in it at all. A real header is well under 200 bytes.</para>
    /// </summary>
    public const int MaxHeaderBytes = 1024;
}

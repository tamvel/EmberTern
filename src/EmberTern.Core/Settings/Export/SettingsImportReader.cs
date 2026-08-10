using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EmberTern.Core.Localization;
using EmberTern.Core.Security;

namespace EmberTern.Core.Settings.Export;

/// <summary>
/// How an import attempt ended. <b>Classified by CAUSE</b>, the same discipline
/// <c>ApplicationSettingsStore.LoadWithStatus</c> applies to <c>settings.dat</c> and for the same reason: the
/// causes have different prognoses, and the user's next action differs for each.
/// </summary>
public enum SettingsImportStatus
{
    /// <summary>The file is ours, we can read it, and (after <c>Open</c>) the content is available.</summary>
    Ok,

    /// <summary>The magic did not match — a ZIP, a PDF, a text file, or <c>settings.dat</c> itself. Resolved
    /// before anything is parsed and long before a passphrase is requested.</summary>
    NotAnExportFile,

    /// <summary>Ours, but broken: an unparseable header, a payload that is not valid JSON, or one whose
    /// encrypted blob is truncated. ⚠ Distinct from <see cref="WrongPassphrase"/> on purpose.</summary>
    Damaged,

    /// <summary>The file could not be read at all (locked, missing, no permission). Says nothing about its
    /// content.</summary>
    Unreadable,

    /// <summary>The format version is above what this build understands. Refused, naming the version — we cannot
    /// understand fields we have never seen, and a partial import is worse than none (rule #11).</summary>
    FutureFormatVersion,

    /// <summary>The format version is below the oldest this build can migrate from. Refused rather than guessed
    /// at.</summary>
    UnsupportedFormatVersion,

    /// <summary>The encryption scheme, key-derivation function or KDF parameters are ones this build cannot
    /// honour. Resolved <b>before</b> the passphrase is requested.</summary>
    UnsupportedEncryption,

    /// <summary>Authentication failed. ⭐ The whole reason the format uses AES-<b>GCM</b>: this is a
    /// distinguishable outcome rather than "corrupt file". Also what a deliberately modified payload
    /// produces — GCM cannot tell those apart, and neither do we claim to.</summary>
    WrongPassphrase,

    /// <summary>The payload decrypted, but the settings shape inside it is newer than this build's
    /// <c>ApplicationSettingsStore.CurrentSchemaVersion</c>. The third version axis, refused on its own terms.</summary>
    FutureSettingsSchema,
}

/// <summary>
/// The result of phase one: everything knowable about an export file <b>without its passphrase</b>.
/// <para>⭐ It carries the payload it already read, so phase two needs no second file access — which also means
/// the bytes that were validated are the bytes that get decrypted, rather than whatever the file holds by the
/// time the user has finished typing.</para>
/// </summary>
public sealed class SettingsImportInspection
{
    internal SettingsImportInspection(
        SettingsImportStatus status,
        string message,
        LocalizableMessage? localized,
        SettingsExportHeader header,
        string payload)
    {
        Status = status;
        Message = message;
        Localized = localized;
        Header = header;
        Payload = payload;
    }

    public SettingsImportStatus Status { get; }

    /// <summary>A message fit to show the user, or empty when <see cref="Status"/> is
    /// <see cref="SettingsImportStatus.Ok"/>.</summary>
    public string Message { get; }

    /// <summary>
    /// The same message as a <see cref="LocalizableMessage"/> (decision <b>D‑3</b>), or null on success.
    ///
    /// <para>⭐ Both forms exist for the reason recorded on <c>ConnectionFailedException</c> and repeated for the
    /// settings store: <see cref="Message"/> is what existing tests pin character for character and what an
    /// unmigrated path would show, so leaving English there means such a path degrades to <b>exactly today's
    /// behaviour</b> rather than to a raw key. ⚠ The duplication is guarded, not tolerated — the two must
    /// render identically in English, and a test pins it.</para>
    ///
    /// <para>⭐ Resolve with <c>Loc.Format</c> at the moment of display, never earlier.</para>
    /// </summary>
    public LocalizableMessage? Localized { get; }

    /// <summary>The cleartext header. Meaningful once the magic matched; <c>default</c> otherwise.</summary>
    public SettingsExportHeader Header { get; }

    /// <summary>True when a passphrase may now be requested — <b>and only then</b>.</summary>
    public bool CanBeOpened => Status == SettingsImportStatus.Ok;

    internal string Payload { get; }
}

/// <summary>The result of phase two: the decrypted, migrated content, or why it could not be produced.</summary>
public sealed class SettingsImportResult
{
    internal SettingsImportResult(
        SettingsImportStatus status,
        string message,
        LocalizableMessage? localized,
        SettingsExportContent? content)
    {
        Status = status;
        Message = message;
        Localized = localized;
        Content = content;
    }

    public SettingsImportStatus Status { get; }

    public string Message { get; }

    /// <inheritdoc cref="SettingsImportInspection.Localized"/>
    public LocalizableMessage? Localized { get; }

    /// <summary>The imported content, or null on any failure.</summary>
    public SettingsExportContent? Content { get; }

    public bool IsUsable => Status == SettingsImportStatus.Ok && Content is not null;
}

/// <summary>
/// Reads a settings export, in <b>two phases</b>, applying the ratified ordered check sequence (design §6.3.3).
///
/// <para><b>⭐ The order of the checks IS the design, and the split into two phases is what enforces it.</b>
/// <see cref="Inspect(Stream)"/> resolves identity, format version and encryption capability — everything that can
/// be known <i>without</i> a credential. Only then may a caller ask for a passphrase and call
/// <see cref="Open"/>.</para>
///
/// <list type="table">
///   <item><term>1</term><description>Magic, from the stream, before the file is loaded → <i>"not an EmberTern
///   settings file"</i></description></item>
///   <item><term>2</term><description>Format version newer than supported → <i>"written by a newer EmberTern
///   build (format vN)"</i></description></item>
///   <item><term>3</term><description>Format version older → migrate (refused only when no step exists)</description></item>
///   <item><term>4</term><description>Encryption scheme / KDF unknown → <i>"unsupported encryption scheme"</i></description></item>
///   <item><term>5</term><description><b>← the passphrase is requested only here</b></description></item>
///   <item><term>6</term><description>GCM authentication → <i>"wrong passphrase"</i></description></item>
/// </list>
///
/// <para>⭐ <b>Steps 1–4 all resolve before the user is asked for anything, and that is the whole win.</b> Without
/// them the flow would prompt for a credential, fail authentication, and report "wrong passphrase" — when the real
/// answer was "you picked a PDF" or "this file is from a newer build". <b>Never ask for a credential that cannot
/// possibly work</b>: a passphrase prompt is an implicit claim that the file is readable given the right one.</para>
///
/// <para>⚠ <b>Corollary for the UI (etap 5b): the passphrase dialog must not be the entry point to import.</b> The
/// file is validated first and the passphrase requested second. Wiring it the other way round is the natural shape
/// if the dialog is built first, and it silently discards every distinct message above. This API is deliberately
/// shaped so that the wrong order is not expressible — <see cref="Open"/> takes an inspection, not a path.</para>
///
/// <para><b>Why the messages live in Core rather than <c>UiStrings</c>:</b> the same reason Firebird
/// connection-failure text does — Core cannot reference <c>EmberTern.App</c>, and a status whose meaning is
/// decided here should not have its explanation decided somewhere else.</para>
///
/// <para>⭐ <b>Since C4b each message exists twice</b>: in English, for logs and for the tests that pin the exact
/// wording, and as a <see cref="LocalizableMessage"/> keyed in <see cref="SettingsExportMessages"/> for the
/// dialog to resolve in the reader's language (D‑3). ⚠ Every producer goes through <see cref="Failed"/> or
/// <see cref="Failure"/>, which take both halves together — so the pair cannot be set apart at a call site.</para>
/// </summary>
public static class SettingsImportReader
{
    /// <summary>Phase one, from a file path. I/O failures degrade to
    /// <see cref="SettingsImportStatus.Unreadable"/> rather than throwing.</summary>
    public static SettingsImportInspection Inspect(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Inspect(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                      or ArgumentException or NotSupportedException)
        {
            return Failed(SettingsImportStatus.Unreadable, $"The file could not be read: {ex.Message}",
                LocalizableMessage.Of(SettingsExportMessages.FileCouldNotBeRead, ex.Message));
        }
    }

    /// <summary>Phase one. Reads the header off <paramref name="stream"/> and runs checks 1–4.</summary>
    public static SettingsImportInspection Inspect(Stream stream)
    {
        SettingsExportHeader header;
        string payload;

        try
        {
            // CHECK 1 — identity, byte-compared, straight off the stream. A ZIP or a PDF ends here, and so does
            // settings.dat: its magic is EMBERTERN-SETTINGS, ours is EMBERTERN-SETTINGS-EXPORT, and keeping them
            // distinct is the whole of ratified decision Q13.
            var outcome = SettingsExportEnvelope.TryReadHeader(stream, out header);
            if (outcome == SettingsExportHeaderOutcome.NotAnExportFile)
            {
                return Failed(SettingsImportStatus.NotAnExportFile, "This is not an EmberTern settings file.",
                    LocalizableMessage.Of(SettingsExportMessages.NotAnExportFile));
            }
            if (outcome == SettingsExportHeaderOutcome.MalformedHeader)
            {
                return Failed(SettingsImportStatus.Damaged,
                    "This EmberTern settings file's header is damaged and cannot be read.",
                    LocalizableMessage.Of(SettingsExportMessages.HeaderDamaged));
            }

            payload = SettingsExportEnvelope.ReadPayload(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                      or ObjectDisposedException)
        {
            return Failed(SettingsImportStatus.Unreadable, $"The file could not be read: {ex.Message}",
                LocalizableMessage.Of(SettingsExportMessages.FileCouldNotBeRead, ex.Message));
        }

        // CHECK 2 — a newer format version. Same downgrade protection, and the same reason, as settings.dat's:
        // we cannot understand fields we have never seen.
        if (header.FormatVersion > SettingsExportFormat.CurrentFormatVersion)
        {
            return Failed(SettingsImportStatus.FutureFormatVersion, string.Format(
                    CultureInfo.InvariantCulture,
                    "This settings export was written by a newer EmberTern build (format v{0}; this build supports "
                    + "up to v{1}).",
                    header.FormatVersion, SettingsExportFormat.CurrentFormatVersion),
                LocalizableMessage.Of(SettingsExportMessages.FutureFormatVersion,
                    Echo(header.FormatVersion), Echo(SettingsExportFormat.CurrentFormatVersion)));
        }

        // CHECK 3 — older than the oldest version we hold a migration path for. Knowable from the version alone,
        // so it belongs HERE and not after decryption: refusing before the passphrase prompt is the point.
        if (header.FormatVersion < SettingsExportFormat.OldestSupportedFormatVersion)
        {
            return Failed(SettingsImportStatus.UnsupportedFormatVersion, string.Format(
                    CultureInfo.InvariantCulture,
                    "This settings export declares format v{0}, which this build cannot migrate from (the oldest "
                    + "supported is v{1}).",
                    header.FormatVersion, SettingsExportFormat.OldestSupportedFormatVersion),
                LocalizableMessage.Of(SettingsExportMessages.UnsupportedFormatVersion,
                    Echo(header.FormatVersion), Echo(SettingsExportFormat.OldestSupportedFormatVersion)));
        }

        // CHECK 4 — can this build decrypt it at all? Scheme, KDF and KDF parameters are all "capability" facts,
        // so they are all resolved before a credential is requested.
        if (!string.Equals(header.EncryptionScheme, EncryptionSchemes.PassphraseAes256,
                StringComparison.OrdinalIgnoreCase))
        {
            return Failed(SettingsImportStatus.UnsupportedEncryption,
                $"Unsupported encryption scheme '{header.EncryptionScheme}'.",
                LocalizableMessage.Of(SettingsExportMessages.UnsupportedScheme, header.EncryptionScheme));
        }
        if (!PassphraseProtector.IsSupportedKdf(header.Kdf))
        {
            return Failed(SettingsImportStatus.UnsupportedEncryption,
                $"Unsupported key-derivation function '{header.Kdf}'.",
                LocalizableMessage.Of(SettingsExportMessages.UnsupportedKdf, header.Kdf));
        }
        if (!PassphraseProtector.IsSupportedIterations(header.Iterations))
        {
            // ⚠ Also a denial-of-service guard: the iteration count sits in a cleartext header anyone can edit,
            // and honouring a claimed two billion would hang inside the KDF with no way out.
            return Failed(SettingsImportStatus.UnsupportedEncryption, string.Format(
                    CultureInfo.InvariantCulture,
                    "This settings export declares an unsupported key-derivation iteration count ({0}).",
                    header.Iterations),
                LocalizableMessage.Of(SettingsExportMessages.UnsupportedIterations, Echo(header.Iterations)));
        }

        return new SettingsImportInspection(SettingsImportStatus.Ok, string.Empty, null, header, payload);
    }

    /// <summary>
    /// Phase two — checks 5 and 6, then the two migration axes. Requires an inspection whose
    /// <see cref="SettingsImportInspection.CanBeOpened"/> is true.
    /// </summary>
    public static SettingsImportResult Open(SettingsImportInspection inspection, string passphrase)
    {
        ArgumentNullException.ThrowIfNull(inspection);

        // A caller that skips phase one gets phase one's answer back rather than a decryption attempt. Belt and
        // braces for the ordering the two-phase shape already makes natural.
        if (!inspection.CanBeOpened)
        {
            // ⚠ Phase one's own pair, forwarded whole — never restated. Restating it here would make one
            // sentence have two producers that could drift.
            return new SettingsImportResult(
                inspection.Status, inspection.Message, inspection.Localized, null);
        }

        if (string.IsNullOrEmpty(passphrase))
        {
            return Failure(SettingsImportStatus.WrongPassphrase,
                "A passphrase is required to open this settings export.",
                LocalizableMessage.Of(SettingsExportMessages.PassphraseRequired));
        }

        string json;
        try
        {
            var protector = PassphraseProtector.Create(
                passphrase, inspection.Header.Salt, inspection.Header.Iterations, inspection.Header.Kdf);
            json = protector.Unprotect(inspection.Payload);
        }
        // CHECK 6 — authentication. ⭐ This arm is the reason the format uses GCM: a wrong passphrase and a
        // tampered payload both land here, and neither is reported as "corrupt file".
        catch (AuthenticationTagMismatchException)
        {
            return Failure(SettingsImportStatus.WrongPassphrase,
                "Wrong passphrase (or the file has been modified since it was exported).",
                LocalizableMessage.Of(SettingsExportMessages.WrongPassphrase));
        }
        catch (CryptographicException ex)
        {
            return Failure(SettingsImportStatus.Damaged,
                $"This settings export's payload is damaged: {ex.Message}",
                LocalizableMessage.Of(SettingsExportMessages.PayloadDamaged, ex.Message));
        }
        catch (ArgumentException ex)
        {
            // The header passed check 4, so this is a genuinely malformed parameter (an empty salt, say).
            return Failure(SettingsImportStatus.Damaged,
                $"This settings export's header is damaged: {ex.Message}",
                LocalizableMessage.Of(SettingsExportMessages.HeaderParametersDamaged, ex.Message));
        }

        JsonObject payload;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject parsed)
            {
                return Damaged("its payload is not a settings export.",
                    LocalizableMessage.Of(SettingsExportMessages.DamagedNotAnExport));
            }
            payload = parsed;
        }
        catch (JsonException ex)
        {
            return Damaged($"its payload is not valid JSON ({ex.Message}).",
                LocalizableMessage.Of(SettingsExportMessages.DamagedInvalidJson, ex.Message));
        }

        // AXIS 1 — the ENVELOPE's format version: migrate stepwise up to the current one.
        if (!SettingsExportMigration.TryMigrateToCurrent(
                payload, inspection.Header.FormatVersion, out var reason, out var reasonMessage))
        {
            return Failure(SettingsImportStatus.UnsupportedFormatVersion, reason, reasonMessage);
        }

        SettingsExportContent? content;
        try
        {
            content = payload.Deserialize<SettingsExportContent>(ApplicationSettingsStore.JsonOptions);
        }
        catch (JsonException ex)
        {
            return Damaged($"its payload could not be read ({ex.Message}).",
                LocalizableMessage.Of(SettingsExportMessages.DamagedUnreadablePayload, ex.Message));
        }

        if (content?.Settings is null)
        {
            return Damaged("its payload contains no settings.",
                LocalizableMessage.Of(SettingsExportMessages.DamagedNoSettings));
        }

        // AXIS 2 — the SETTINGS SHAPE's version. A separate axis with a separate ladder, and deliberately not
        // folded into the format version: doing so would tie "we added a section to the export" to "the settings
        // shape changed", forcing a schema bump that makes older builds refuse the whole settings.dat.
        if (content.Settings.SchemaVersion > ApplicationSettingsStore.CurrentSchemaVersion)
        {
            return Failure(SettingsImportStatus.FutureSettingsSchema, string.Format(
                    CultureInfo.InvariantCulture,
                    "This settings export holds settings in a newer shape (schema v{0}; this build supports up to "
                    + "v{1}).",
                    content.Settings.SchemaVersion, ApplicationSettingsStore.CurrentSchemaVersion),
                LocalizableMessage.Of(SettingsExportMessages.FutureSettingsSchema,
                    Echo(content.Settings.SchemaVersion), Echo(ApplicationSettingsStore.CurrentSchemaVersion)));
        }

        // ⭐ THE EXISTING LADDER, called — not re-implemented. This is the same method LoadWithStatus calls, which
        // is what makes a future Migrate_2_3 apply to imports for free and is the whole reason the payload is
        // shaped as an ApplicationSettings.
        ApplicationSettingsStore.MigrateToCurrentVersion(content.Settings);

        // Preferences are normalized at every file boundary, and an import is one. Silent and total, the same as
        // PreferencesStore.Load — a value from a build that knew more options becomes this build's default rather
        // than failing the import.
        content.Settings.UserSettings.Preferences =
            PreferencesStore.Validate(content.Settings.UserSettings.Preferences);

        return new SettingsImportResult(SettingsImportStatus.Ok, string.Empty, null, content);
    }

    // ── The two producers ────────────────────────────────────────────────────────────────────────────
    // ⚠ Both halves are parameters of ONE call, so a call site cannot supply the English and forget the key.

    private static SettingsImportInspection Failed(
        SettingsImportStatus status, string message, LocalizableMessage localized)
        => new(status, message, localized, default, string.Empty);

    private static SettingsImportResult Failure(
        SettingsImportStatus status, string message, LocalizableMessage localized)
        => new(status, message, localized, null);

    /// <summary>
    /// The four "damaged payload" cases: the English half is still COMPOSED from the shipped prefix and its
    /// fragment, while the localized half is a WHOLE SENTENCE key.
    ///
    /// <para>⭐ Keeping the composition for English is deliberate and is what makes the equality guard a proof:
    /// the resource value must reproduce the concatenation this method builds, rather than a sentence someone
    /// retyped. ⛔ The prefix itself is never a key — glued to a fragment it cannot translate into a language
    /// that inflects, and the fragment is not a sentence in any language.</para>
    /// </summary>
    private static SettingsImportResult Damaged(string detail, LocalizableMessage localized)
        => new(SettingsImportStatus.Damaged, "This settings export is damaged: " + detail, localized, null);

    /// <summary>
    /// A number echoed from the file's own header or payload, formatted <b>invariantly</b> so both halves of the
    /// pair render identically on every machine. ⛔ Do not pass these as numbers — gotcha #357 and the remarks on
    /// <see cref="SettingsExportMessages"/>.
    /// </summary>
    private static string Echo(int value) => value.ToString(CultureInfo.InvariantCulture);
}

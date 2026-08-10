using EmberTern.Core.Localization;

namespace EmberTern.Core.Settings.Export;

/// <summary>
/// What an <b>import</b> says when it cannot read, open or apply a settings export — decision <b>D‑3</b>'s
/// producer for <c>Settings/Export</c>. Every one of these reaches the import dialog directly.
///
/// <para>⭐ <b>Every entry is a WHOLE SENTENCE, and for the <c>Damaged*</c> family that is the load-bearing
/// decision.</b> The producer used to compose <c>"This settings export is damaged: " + detail</c> from a fixed
/// prefix and a fragment. A prefix glued to a fragment cannot be translated into a language that inflects, and
/// the fragment (<i>"its payload is not valid JSON (…)"</i>) is not a sentence in any language — so the four
/// cases became four keys whose English values are the exact concatenations the producer built.</para>
///
/// <para>⚠ <b>Every <c>{n}</c> is DATA</b>: an exception's own message, or a version/iteration count echoed
/// from the file's cleartext header. The exception text is the platform speaking and travels verbatim, exactly
/// as Firebird's does in <c>FirebirdConnectionMessages</c>.</para>
///
/// <para>⚠⚠ <b>The echoed NUMBERS travel as invariant-formatted strings rather than as numbers — and the reason
/// is narrower than it first looks, so it is worth stating exactly.</b> The English half of each pair is a
/// literal in the producer; the localized half is a resource value a translator may edit. A translator who
/// wrote <c>{0:N0}</c> — an entirely reasonable thing to do to a nine-digit iteration count — would make the two
/// halves diverge under any grouping culture, and the equality guard would go red in a translated build for a
/// reason that has nothing to do with the sentence. Handing the value over <b>already formatted</b> makes a
/// format specifier inert, so the halves are identical by construction.</para>
///
/// <para>⭐ <b>Measured, not assumed — and the measurement corrected a plausible wrong premise.</b> A plain
/// <c>{0}</c> with an <c>int</c> does <b>not</b> group under <c>pl-PL</c>; planting the numeric argument left
/// every guard green. So this is not a defect being fixed, it is a divergence class being closed before a
/// translation can open it. The real mechanism behind gotcha #354's <c>48 102</c> is the <c>:N0</c> specifier in
/// a resource value, not the culture of a bare substitution. ⛔ Do not "simplify" these back into numbers —
/// gotcha #357 and <c>SettingsExportLocalizationTests</c>.</para>
///
/// <para>⭐ Invariant is also the right ANSWER on its own terms: the sentence says the export <i>declares</i> a
/// version or a count, so what belongs in it is a verbatim echo of the field — the same discipline that keeps a
/// raw server message verbatim.</para>
///
/// <para>⛔ <b>Not here, deliberately:</b> the EXPORT side. <c>SettingsExporter</c>'s two message-bearing
/// throws are <c>ArgumentException</c> guards against a caller error, and <c>CanExport</c> gates on both
/// conditions, so neither is reachable from the UI; the failure wrapper the dialog shows is already localized
/// in the App layer and its inner text is the platform's. ⛔ Also not here: the two refusals the applier
/// <i>forwards</i> from <c>ApplicationSettingsStore</c> — those already have keys in
/// <see cref="SettingsStoreMessages"/> and are threaded through, never restated (a second key for one sentence
/// is two answers to one question).</para>
/// </summary>
public static class SettingsExportMessages
{
    // ── Phase one: what is knowable without the passphrase ───────────────────────────────────────────

    /// <summary>The I/O error's own message as <c>{0}</c>.</summary>
    /// <remarks>⭐ One key for two call sites — <c>Inspect(path)</c> and <c>Inspect(stream)</c> — because it is
    /// one sentence about one failure at two points of the same read, not two concepts sharing a word. (The
    /// ratified "do not merge values with several owners" rule is about the opposite case: different concepts
    /// that happen to read alike in English.)</remarks>
    public static readonly MessageKey FileCouldNotBeRead = new("Settings.Import.FileCouldNotBeRead");

    /// <summary>Check 1 — the magic did not match. A ZIP, a PDF, or <c>settings.dat</c> itself.</summary>
    public static readonly MessageKey NotAnExportFile = new("Settings.Import.NotAnExportFile");

    /// <summary>Check 1 — ours, but the cleartext header does not parse.</summary>
    public static readonly MessageKey HeaderDamaged = new("Settings.Import.HeaderDamaged");

    /// <summary>Declared format version as <c>{0}</c>, this build's ceiling as <c>{1}</c>.</summary>
    public static readonly MessageKey FutureFormatVersion = new("Settings.Import.FutureFormatVersion");

    /// <summary>Declared format version as <c>{0}</c>, the oldest migratable as <c>{1}</c>.</summary>
    public static readonly MessageKey UnsupportedFormatVersion =
        new("Settings.Import.UnsupportedFormatVersion");

    /// <summary>The declared scheme name as <c>{0}</c>.</summary>
    public static readonly MessageKey UnsupportedScheme = new("Settings.Import.UnsupportedScheme");

    /// <summary>The declared KDF name as <c>{0}</c>.</summary>
    public static readonly MessageKey UnsupportedKdf = new("Settings.Import.UnsupportedKdf");

    /// <summary>The declared iteration count as <c>{0}</c> — echoed, never re-formatted (see the class remarks).
    /// </summary>
    public static readonly MessageKey UnsupportedIterations = new("Settings.Import.UnsupportedIterations");

    // ── Phase two: the passphrase, then the payload ──────────────────────────────────────────────────

    public static readonly MessageKey PassphraseRequired = new("Settings.Import.PassphraseRequired");

    /// <summary>⭐ GCM authentication failed. A separate sentence from every <c>Damaged*</c> one, which is the
    /// whole reason the format uses GCM — a wrong passphrase is a distinguishable outcome, not "corrupt".
    /// </summary>
    public static readonly MessageKey WrongPassphrase = new("Settings.Import.WrongPassphrase");

    /// <summary>The crypto error's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey PayloadDamaged = new("Settings.Import.PayloadDamaged");

    /// <summary>
    /// A malformed crypto parameter (an empty salt, say), with the error's own message as <c>{0}</c>.
    ///
    /// <para>⚠ A separate key from <see cref="HeaderDamaged"/> although both say "the header is damaged": that
    /// one is a header that would not parse at all and carries no detail, this one is a header that parsed and
    /// then failed check 4's assumptions. Different moments, different English, and folding them would have to
    /// change one of the two shipped wordings.</para>
    /// </summary>
    public static readonly MessageKey HeaderParametersDamaged =
        new("Settings.Import.HeaderParametersDamaged");

    /// <summary>Whole sentence — was <c>Damaged("its payload is not a settings export.")</c>.</summary>
    public static readonly MessageKey DamagedNotAnExport = new("Settings.Import.DamagedNotAnExport");

    /// <summary>Whole sentence, the parser's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey DamagedInvalidJson = new("Settings.Import.DamagedInvalidJson");

    /// <summary>Whole sentence, the deserializer's own message as <c>{0}</c>.</summary>
    public static readonly MessageKey DamagedUnreadablePayload =
        new("Settings.Import.DamagedUnreadablePayload");

    /// <summary>Whole sentence — the payload decrypted and parsed but carries no settings.</summary>
    public static readonly MessageKey DamagedNoSettings = new("Settings.Import.DamagedNoSettings");

    /// <summary>Declared settings schema as <c>{0}</c>, this build's ceiling as <c>{1}</c>.</summary>
    public static readonly MessageKey FutureSettingsSchema = new("Settings.Import.FutureSettingsSchema");

    /// <summary>The declared format version as <c>{0}</c> — the envelope ladder has no step for it.</summary>
    public static readonly MessageKey NoMigrationStep = new("Settings.Import.NoMigrationStep");

    // ── Applying the import ──────────────────────────────────────────────────────────────────────────

    public static readonly MessageKey NothingSelected = new("Settings.Import.NothingSelected");

    /// <summary>The I/O error's own message as <c>{0}</c>. ⚠ Refusing because the recovery copy could not be
    /// made is deliberate — see <c>SettingsImportApplier.Apply</c>.</summary>
    public static readonly MessageKey CouldNotCopyAside = new("Settings.Import.CouldNotCopyAside");
}

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.Settings;

/// <summary>
/// What the operator has chosen about the application itself.
/// </summary>
/// <remarks>
/// <para>⭐ <b>One member today, and that is the whole of L8 decision D‑4.</b> The theme is deliberately
/// NOT here: it is not persisted anywhere at all right now, so adding it would be a new feature smuggled
/// into a localization stage. ⛔ Do not add a preference here because it would be convenient — the test is
/// whether the step that needs it is scheduled.</para>
///
/// <para>⚠ <see cref="Language"/> is a code from <see cref="ApplicationLanguages"/>, and ⛔ never a
/// <see cref="System.Globalization.CultureInfo"/>: what is persisted must be a value this build can
/// normalize, and a culture object is not something a file can hold.</para>
/// </remarks>
public sealed record ManagerPreferences
{
    /// <summary>The interface language. ⭐ Always one of <see cref="ApplicationLanguages.All"/>.</summary>
    public string Language { get; init; } = ApplicationLanguages.Default;

    /// <summary>What a first run, an unreadable file or an unknown value all resolve to.</summary>
    public static ManagerPreferences Defaults { get; } = new();
}

/// <summary>
/// Reads and writes <c>ui.json</c>.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>Every failure resolves to defaults and nothing here throws.</b> This runs before any
/// window exists and carries no secret: a preference file is never worth a crash, and the product states
/// the same rule about its own option catalog — one unusable value must not make a whole settings file
/// unusable.</para>
///
/// <para>⚠ Every stored field is nullable in the wire shape, exactly as <c>smtp.dat</c> is, so a file
/// written by an older build reads cleanly and takes the default: no migration step, no rewrite on read,
/// nothing an operator has to do.</para>
///
/// <para>⛔ <b>It is not part of any backup.</b> <c>RegisterBackup</c> snapshots the register and nothing
/// else, and the JSONL export writes register rows — a UI preference must not travel to another machine,
/// and a restore must not carry one.</para>
/// </remarks>
public sealed class ManagerPreferencesStore
{
    /// <summary>The wire version this build writes.</summary>
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _path;

    /// <summary>Creates a store over a file path.</summary>
    public ManagerPreferencesStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>The store for a set of manager paths.</summary>
    public static ManagerPreferencesStore At(ManagerPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new ManagerPreferencesStore(paths.Preferences);
    }

    /// <summary>Where the file is.</summary>
    public string FilePath => _path;

    /// <summary>
    /// The stored preferences, or <see cref="ManagerPreferences.Defaults"/>.
    /// </summary>
    /// <remarks>
    /// ⚠ A file written by a NEWER build is read on its known fields rather than refused. That is the
    /// opposite call from <c>smtp.dat</c>, and deliberately: there, a partially understood configuration
    /// could send mail through settings the operator did not intend, so refusing is the safe answer. Here
    /// the worst case is an interface in the wrong language, and refusing would mean losing the choice.
    /// </remarks>
    public ManagerPreferences Load()
    {
        if (!File.Exists(_path))
        {
            return ManagerPreferences.Defaults;
        }

        Stored? stored;
        try
        {
            // ⚠⚠ THE BOM IS STRIPPED ON PURPOSE, and this is the one file in the application where that
            //    matters. `System.Text.Json` REFUSES a leading UTF-8 byte-order mark — it is not
            //    whitespace to the reader — so a file saved by Notepad, which adds one by default, would
            //    throw here and be served as DEFAULTS. ⭐ That is a silent failure with a plausible
            //    symptom: the operator edits ui.json, the application starts in the old language, and
            //    nothing anywhere says why.
            // ⚠ It matters here and not for smtp.dat because ui.json is PLAIN TEXT that a person may
            //    reasonably open and edit — indeed, until L8.5 enables the picker, hand-editing is the
            //    only way to set the language at all.
            // ⭐ Same family as gotcha #178, one direction reversed: there a BOM we WROTE broke someone
            //    else's parser; here a BOM someone else wrote breaks ours.
            stored = JsonSerializer.Deserialize<Stored>(WithoutByteOrderMark(File.ReadAllBytes(_path)), Json);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return ManagerPreferences.Defaults;
        }

        return stored is null
            ? ManagerPreferences.Defaults

            // ⭐ Through Resolve, so a code this build does not know lands on the default rather than on
            //   a culture the resource system cannot serve.
            : new ManagerPreferences { Language = ApplicationLanguages.Resolve(stored.Language) };
    }

    /// <summary>
    /// Writes <paramref name="preferences"/>, reporting whether the write reached the disk.
    /// </summary>
    /// <remarks>
    /// ⚠ Returns <see langword="false"/> rather than throwing, for the same reason <see cref="Load"/> does
    /// not: this is called from a preference change, and a read-only folder must not take the application
    /// down. ⭐ The caller's in-memory value stands either way, so the choice still applies for this
    /// session — it simply will not survive a restart, and that is the honest outcome.
    /// </remarks>
    public bool Save(ManagerPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var stored = new Stored
        {
            Version = CurrentVersion,
            Language = ApplicationLanguages.Resolve(preferences.Language),
        };

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, JsonSerializer.SerializeToUtf8Bytes(stored, Json));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>The bytes without a leading UTF-8 byte-order mark.</summary>
    /// <remarks>⚠ Only the UTF-8 mark: this file is written as UTF-8 and nothing else is offered.</remarks>
    private static ReadOnlySpan<byte> WithoutByteOrderMark(byte[] bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.AsSpan(3)
            : bytes;

    /// <summary>The wire shape. ⚠ Every field nullable, so an older file reads cleanly.</summary>
    private sealed class Stored
    {
        [JsonPropertyName("version")]
        public int? Version { get; init; }

        [JsonPropertyName("language")]
        public string? Language { get; init; }
    }
}

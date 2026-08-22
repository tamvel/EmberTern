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
/// <para>⭐ <b>Two members, and both were asked for.</b> The theme is deliberately still NOT here: it is
/// not persisted anywhere at all, so adding it would be a feature nobody requested. ⛔ Do not add a
/// preference here because it would be convenient — the test is whether the step that needs it is
/// scheduled. <see cref="WindowMaximized"/> passes that test (asked for on 2026-08-22);
/// <see cref="Language"/> is L8 decision D‑4.</para>
///
/// <para>⚠ <see cref="Language"/> is a code from <see cref="ApplicationLanguages"/>, and ⛔ never a
/// <see cref="System.Globalization.CultureInfo"/>: what is persisted must be a value this build can
/// normalize, and a culture object is not something a file can hold.</para>
/// </remarks>
public sealed record ManagerPreferences
{
    /// <summary>The interface language. ⭐ Always one of <see cref="ApplicationLanguages.All"/>.</summary>
    public string Language { get; init; } = ApplicationLanguages.Default;

    /// <summary>
    /// Whether the main window was MAXIMISED when the application last closed.
    /// </summary>
    /// <remarks>
    /// <para>⭐ A single <see cref="bool"/>, and deliberately not a rectangle. What was asked for is that a
    /// window left maximised comes back maximised; storing the geometry as well would mean deciding what to
    /// do when the monitor it was on is gone — a different feature with a different failure mode. ⛔ The
    /// declared <c>Width</c> / <c>Height</c> / <c>WindowStartupLocation</c> in <c>MainWindow.axaml</c> are
    /// untouched, so the ordinary case is exactly what it was.</para>
    /// <para>⚠ Written at CLOSING TIME and at no other moment: a maximise the operator undoes a second
    /// later must not be what the next run restores. ⛔ Not a live preference — there is nothing worth
    /// writing while a window is merely being dragged about.</para>
    /// </remarks>
    public bool WindowMaximized { get; init; }

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
            : new ManagerPreferences
            {
                Language = ApplicationLanguages.Resolve(stored.Language),
                WindowMaximized = stored.WindowMaximized ?? false,
            };
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
            WindowMaximized = preferences.WindowMaximized,
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

    /// <summary>
    /// Changes ONE preference and writes the file back, leaving every other one exactly as it was.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ <b>It exists the moment this record gained a second member, and it is not a convenience.</b>
    /// <see cref="Save"/> persists the WHOLE object, so a caller that builds a fresh
    /// <see cref="ManagerPreferences"/> to change one field silently resets every field it did not
    /// mention — the language picker would blank the window state, and the window state would blank the
    /// language. That is the defect EmberTern records about its own <c>PreferencesService</c> ("two
    /// snapshot holders overwrite each other"), and read-modify-write in ONE place is what makes it
    /// unreachable rather than something each caller has to remember.</para>
    /// <para>⚠ Not atomic against another process writing the same file, and it does not need to be: one
    /// operator, one instance, one file.</para>
    /// </remarks>
    /// <returns><see langword="false"/> when the write did not reach the disk — see <see cref="Save"/>.</returns>
    public bool Update(Func<ManagerPreferences, ManagerPreferences> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return Save(change(Load()));
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

        // ⚠ Nullable like every field here, so a file written before this existed reads cleanly and takes
        //   the default — no migration step, no rewrite on read, nothing an operator has to do.
        [JsonPropertyName("windowMaximized")]
        public bool? WindowMaximized { get; init; }
    }
}

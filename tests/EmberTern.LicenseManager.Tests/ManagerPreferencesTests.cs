using System;
using System.IO;
using System.Text;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.Settings;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// <c>ui.json</c> — the fourth file, and the only one that carries a preference.
///
/// <para>⭐ It is deliberately NOT part of the register (a preference must not travel in a backup nor
/// follow a restore onto another machine), NOT part of the keystore, and ⛔ NOT part of <c>smtp.dat</c>:
/// that file has one Save covering a whole coherent configuration, so applying a language on selection
/// through it would mean a read-modify-write on every pick.</para>
/// </summary>
public sealed class ManagerPreferencesTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "etlm-tests", Guid.NewGuid().ToString("N"));

    private ManagerPreferencesStore Store()
    {
        Directory.CreateDirectory(_folder);
        return new ManagerPreferencesStore(Path.Combine(_folder, ManagerPaths.PreferencesFileName));
    }

    /// <summary>
    /// ⚠⚠ NO byte-order mark, and stated rather than assumed: <c>Encoding.UTF8</c> emits one, and
    /// <c>File.WriteAllText</c> with an explicit encoding writes the preamble. A test that used it would
    /// hand the parser a BOM and then measure the BOM rather than the case it names — which is exactly how
    /// <see cref="AnUnknownLanguage_ResolvesToTheDefault"/> passed for the wrong reason before the BOM was
    /// handled. ⭐ The BOM has a test of its own below.
    /// </summary>
    private static readonly UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>A first run has no file and takes the default — English (decision D‑3).</summary>
    [Fact]
    public void AFirstRun_TakesTheDefault()
    {
        var loaded = Store().Load();

        Assert.Equal(ApplicationLanguages.Default, loaded.Language);
        Assert.Equal(ApplicationLanguages.English, loaded.Language);
    }

    /// <summary>⭐ The choice survives a restart — the whole point of the file.</summary>
    [Fact]
    public void TheChosenLanguage_SurvivesARestart()
    {
        var store = Store();
        Assert.True(store.Save(new ManagerPreferences { Language = ApplicationLanguages.Polish }));

        // ⚠ A SECOND store over the same path — the same thing a restart does. Re-reading through the
        //   instance that wrote it would prove nothing about the file.
        var reopened = new ManagerPreferencesStore(store.FilePath);
        Assert.Equal(ApplicationLanguages.Polish, reopened.Load().Language);
    }

    /// <summary>An unknown code in the file resolves to the default rather than being served as-is.</summary>
    [Fact]
    public void AnUnknownLanguage_ResolvesToTheDefault()
    {
        var store = Store();
        File.WriteAllText(store.FilePath, """{ "version": 1, "language": "de" }""", NoBom);

        Assert.Equal(ApplicationLanguages.Default, store.Load().Language);
    }

    /// <summary>
    /// ⭐⭐ A damaged file yields defaults and does NOT throw.
    /// </summary>
    /// <remarks>
    /// ⚠ This runs before any window exists. An exception here would turn a corrupted preference — the
    /// cheapest thing in the application to lose — into a start-up crash with no UI to report it.
    /// </remarks>
    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"language\": ")]
    [InlineData("[]")]
    public void ADamagedFile_YieldsDefaults(string contents)
    {
        var store = Store();
        File.WriteAllText(store.FilePath, contents, NoBom);

        var loaded = store.Load();
        Assert.Equal(ApplicationLanguages.Default, loaded.Language);
    }

    /// <summary>
    /// ⭐⭐ A file saved with a byte-order mark is still read — a hand edit in Notepad must not be silent.
    /// </summary>
    /// <remarks>
    /// <para>⚠⚠ <b>Found by this file's own guard, not by review.</b> <c>System.Text.Json</c> REFUSES a
    /// leading UTF-8 BOM — it is not whitespace to the reader — so the parse threw and <c>Load</c> served
    /// DEFAULTS. The symptom is the dangerous kind: the operator edits <c>ui.json</c>, the application
    /// starts in the old language, and nothing anywhere says why.</para>
    /// <para>⚠ It matters here and nowhere else in this application: <c>ui.json</c> is plain text a person
    /// may reasonably open, and until L8.5 enables the picker, hand-editing is the ONLY way to set the
    /// language.</para>
    /// </remarks>
    [Fact]
    public void AFileWithAByteOrderMark_IsStillRead()
    {
        var store = Store();
        File.WriteAllText(
            store.FilePath,
            """{ "version": 1, "language": "pl" }""",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // ⚠ The premise, asserted: without a BOM in the file this test would prove nothing.
        var bytes = File.ReadAllBytes(store.FilePath);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);

        Assert.Equal(ApplicationLanguages.Polish, store.Load().Language);
    }

    /// <summary>⭐ What this application WRITES carries no BOM — one shape on disk, not two.</summary>
    [Fact]
    public void WhatTheApplicationWrites_CarriesNoByteOrderMark()
    {
        var store = Store();
        Assert.True(store.Save(new ManagerPreferences { Language = ApplicationLanguages.Polish }));

        var bytes = File.ReadAllBytes(store.FilePath);
        Assert.NotEqual<byte[]>([0xEF, 0xBB, 0xBF], bytes[..3]);
    }

    /// <summary>
    /// A file written by a NEWER build is read on its known fields rather than refused.
    /// </summary>
    /// <remarks>
    /// ⚠ The opposite call from <c>smtp.dat</c>, deliberately: there, a partially understood configuration
    /// could send mail through settings the operator did not intend, so refusing is the safe answer. Here
    /// the worst case is an interface in the wrong language, and refusing would lose a choice that is
    /// perfectly readable.
    /// </remarks>
    [Fact]
    public void AFileFromANewerBuild_IsReadOnItsKnownFields()
    {
        var store = Store();
        File.WriteAllText(
            store.FilePath,
            """{ "version": 99, "language": "pl", "somethingNew": true }""",
            NoBom);

        Assert.Equal(ApplicationLanguages.Polish, store.Load().Language);
    }

    /// <summary>
    /// ⛔ The preferences file is not part of a backup, and a restore does not carry one.
    /// </summary>
    /// <remarks>
    /// ⭐ Asserted against the real snapshot rather than by reading the backup code: a UI preference that
    /// travelled to another machine would arrive as a choice nobody there made.
    /// </remarks>
    [Fact]
    public void ThePreferencesFile_IsNotPartOfABackup()
    {
        using var manager = new ManagerFixture();

        var store = ManagerPreferencesStore.At(manager.Paths);
        Assert.True(store.Save(new ManagerPreferences { Language = ApplicationLanguages.Polish }));

        var snapshot = manager.Register.CreateSnapshot();
        var text = Encoding.UTF8.GetString(snapshot);

        Assert.DoesNotContain(ManagerPaths.PreferencesFileName, text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"language\"", text, StringComparison.Ordinal);
    }

    /// <summary>⭐ The four files are four distinct paths — the separation is structural, not a convention.</summary>
    [Fact]
    public void TheFourFiles_AreFourDistinctPaths()
    {
        var paths = new ManagerPaths(_folder);

        string[] all = [paths.Register, paths.KeyStore, paths.SmtpSettings, paths.Preferences];
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
            // ⚠ DirectoryNotFoundException derives from IOException, so this one clause covers the
            //   "the test never created it" case as well.
        }
    }
}

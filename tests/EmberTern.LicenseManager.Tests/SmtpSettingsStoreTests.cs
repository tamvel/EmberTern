using System;
using System.IO;
using System.Text;
using EmberTern.LicenseManager.Email;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The e-mail settings at rest.
///
/// <para>⭐⭐ <b>The load contract is what these mostly measure.</b> The store answers four distinct
/// states, and it does so because <c>PreferencesService</c> in the product does NOT: it turns a failed
/// read into validated defaults, so a transient failure serves defaults for the session and the next
/// save persists them as if the user had chosen them. ⛔ A store that cannot tell "there are none yet"
/// from "I could not read them" makes that class of defect unavoidable however careful its callers are,
/// so the distinction is asserted here rather than left to the window.</para>
/// </summary>
public sealed class SmtpSettingsStoreTests : IDisposable
{
    private readonly string _folder;
    private readonly SmtpSettingsStore _store;

    public SmtpSettingsStoreTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "etlm-smtp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _store = new SmtpSettingsStore(Path.Combine(_folder, "smtp.dat"));
    }

    private static SmtpSettings Sample => new()
    {
        Host = "smtp.example.com",
        Port = 587,
        Security = SmtpSecurity.StartTls,
        FromAddress = "licencje@example.com",
        FromName = "EmberTern",
        Username = "licencje@example.com",
        Password = "app-password-16ch",
        MessageLanguage = "en",
    };

    // ── The four states ─────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ A first run is NOT a failure, and the window must be able to tell.</summary>
    [Fact]
    public void NoFileYetReportsNotConfiguredRatherThanAFailure()
    {
        var load = _store.Load();

        Assert.Equal(SmtpSettingsState.NotConfigured, load.State);
        Assert.Null(load.Problem);
        Assert.Equal(SmtpSettings.Empty, load.Settings);
    }

    [Fact]
    public void EverythingWrittenComesBack()
    {
        _store.Save(Sample);

        var load = _store.Load();

        Assert.Equal(SmtpSettingsState.Loaded, load.State);
        Assert.Null(load.Problem);
        Assert.Equal(Sample, load.Settings);
    }

    /// <summary>⛔ A file that cannot be understood must never look like "no settings yet".</summary>
    [Fact]
    public void AFileThatIsNotJsonReportsUnreadable()
    {
        File.WriteAllText(_store.FilePath, "this is not a settings file");

        var load = _store.Load();

        Assert.Equal(SmtpSettingsState.Unreadable, load.State);
        Assert.NotNull(load.Problem);
    }

    /// <summary>
    /// ⭐ Forward compatibility (§13.4): a newer container is REFUSED rather than partially read.
    /// Reading a subset and writing it back would silently delete whatever the newer build stored.
    /// </summary>
    [Fact]
    public void AFileFromANewerBuildIsRefusedRatherThanPartiallyRead()
    {
        _store.Save(Sample);

        // ⚠ Derived from CurrentVersion, never typed: written as a literal this quietly stopped matching
        //   the moment the container went from v1 to v2, and a substitution that matches nothing leaves
        //   the file perfectly readable — the test would then pass while proving nothing (#378).
        var raw = File.ReadAllText(_store.FilePath).Replace(
            $"\"version\": {SmtpSettingsStore.CurrentVersion}",
            "\"version\": 99",
            StringComparison.Ordinal);
        Assert.Contains("99", raw, StringComparison.Ordinal);
        File.WriteAllText(_store.FilePath, raw);

        var load = _store.Load();

        Assert.Equal(SmtpSettingsState.Unreadable, load.State);
        Assert.Contains("newer", load.Problem!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐⭐ The state that exists for a real operator situation: the file moved between Windows accounts.
    ///
    /// <para>A DPAPI blob from another account cannot be decrypted here, and the honest answer is
    /// "everything except the password" rather than either an exception or a silent blank.</para>
    /// </summary>
    [Fact]
    public void AnUndecryptablePasswordKeepsEverythingElseAndSaysSo()
    {
        _store.Save(Sample);

        // A blob that is valid base64 but not ours — what a file from another account looks like.
        var raw = File.ReadAllText(_store.FilePath);
        var start = raw.IndexOf("\"password\": \"", StringComparison.Ordinal) + "\"password\": \"".Length;
        var end = raw.IndexOf('"', start);
        var tampered = raw[..start] + Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }) + raw[end..];
        File.WriteAllText(_store.FilePath, tampered);

        var load = _store.Load();

        Assert.Equal(SmtpSettingsState.PasswordUnavailable, load.State);
        Assert.Equal(Sample.Host, load.Settings.Host);
        Assert.Equal(Sample.FromAddress, load.Settings.FromAddress);
        Assert.Equal(Sample.Username, load.Settings.Username);
        Assert.Empty(load.Settings.Password);
        Assert.NotNull(load.Problem);
    }

    // ── The secret ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The one assertion this whole class exists for.</b> Read the file as BYTES and prove the
    /// password is not in it — not as text, and not as its base64.
    ///
    /// <para>⚠ Asserted on the bytes rather than on "did we call the protector", because the second
    /// question can be answered correctly by code that then writes the plaintext anyway.</para>
    /// </summary>
    [Fact]
    public void ThePasswordIsNotInTheFileInAnyReadableForm()
    {
        _store.Save(Sample);

        var bytes = File.ReadAllBytes(_store.FilePath);
        var asText = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain(Sample.Password, asText, StringComparison.Ordinal);
        Assert.DoesNotContain(
            Convert.ToBase64String(Encoding.UTF8.GetBytes(Sample.Password)),
            asText,
            StringComparison.Ordinal);

        // ⭐ Positive control: the guard is not passing because the file is empty or unwritten.
        Assert.Contains(Sample.Host, asText, StringComparison.Ordinal);
        Assert.Contains("dpapi", asText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐ The entropy is this application's own. ⛔ It must never be <c>EmberTern.App</c>'s
    /// <c>"EmberTern.v1.secret"</c> — one namespace shared by two applications would make their at-rest
    /// secrets interchangeable, which is the opposite of what separate files with separate protection
    /// exist to achieve.
    /// </summary>
    [Fact]
    public void TheDpapiNamespaceIsThisApplicationsOwn()
    {
        Assert.Equal("EmberTern.LicenseManager.v1.smtp", LocalDpapiProtector.EntropyLabel);
        Assert.NotEqual("EmberTern.v1.secret", LocalDpapiProtector.EntropyLabel);
    }

    [Fact]
    public void AnEmptyPasswordStaysEmptyRatherThanBecomingCiphertext()
    {
        Assert.Empty(LocalDpapiProtector.Protect(string.Empty));
        Assert.True(LocalDpapiProtector.TryUnprotect(string.Empty, out var plain));
        Assert.Empty(plain);
    }

    [Fact]
    public void ABlobThatIsNotOursIsReportedRatherThanThrown()
    {
        Assert.False(
            LocalDpapiProtector.TryUnprotect(Convert.ToBase64String(new byte[] { 9, 9, 9 }), out _));

        // ⚠ Not base64 at all — a hand-edited file, and still an answer rather than an exception.
        Assert.False(LocalDpapiProtector.TryUnprotect("nie-jest-base64!!", out _));
    }

    // ── Housekeeping ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SavingTwiceReplacesRatherThanAppends()
    {
        _store.Save(Sample);
        _store.Save(Sample with { Host = "relay.internal", Username = string.Empty, Password = string.Empty });

        var load = _store.Load();

        Assert.Equal(SmtpSettingsState.Loaded, load.State);
        Assert.Equal("relay.internal", load.Settings.Host);
        Assert.Empty(load.Settings.Username);
    }

    [Fact]
    public void ForgettingTheSettingsReturnsToNotConfigured()
    {
        _store.Save(Sample);
        _store.Delete();

        Assert.Equal(SmtpSettingsState.NotConfigured, _store.Load().State);
    }

    /// <summary>⭐ The store creates its folder — a first run has no <c>%APPDATA%</c> folder yet.</summary>
    [Fact]
    public void SavingCreatesTheFolderItNeeds()
    {
        var nested = new SmtpSettingsStore(Path.Combine(_folder, "deeper", "still", "smtp.dat"));

        nested.Save(Sample);

        Assert.True(File.Exists(nested.FilePath));
    }


    // -- v1 -> v2 (L6.1a) -----------------------------------------------------------------------------

    /// <summary>
    /// <b>The whole point of the version bump, asserted on a REAL v1 file.</b>
    ///
    /// <para>v2 added <c>messageLanguage</c>. A file written by L6.1 has no such field, and it must still
    /// read cleanly and take the default - there is no migration step, no rewrite on read, and nothing an
    /// operator has to do. WARNING The file here is hand-built as v1 rather than produced by the current
    /// writer, because a file the current build wrote is not evidence about the previous one.</para>
    /// </summary>
    [Fact]
    public void AV1FileReadsCleanlyAndTakesTheDefaultLanguage()
    {
        File.WriteAllText(_store.FilePath, """
            {
              "version": 1,
              "host": "smtp.example.com",
              "port": 587,
              "security": "StartTls",
              "fromAddress": "licencje@example.com",
              "fromName": "EmberTern",
              "username": "licencje@example.com",
              "password": "",
              "passwordProtection": "dpapi-currentuser"
            }
            """);

        var load = _store.Load();

        Assert.Equal(SmtpSettingsState.Loaded, load.State);
        Assert.Null(load.Problem);
        Assert.Equal("smtp.example.com", load.Settings.Host);
        Assert.Equal("licencje@example.com", load.Settings.FromAddress);
        Assert.Equal(MessageLanguages.Default, load.Settings.MessageLanguage);
    }

    /// <summary>This build writes v2, and says so in the file.</summary>
    [Fact]
    public void ThisBuildWritesVersionTwo()
    {
        _store.Save(Sample);

        Assert.Equal(2, SmtpSettingsStore.CurrentVersion);
        Assert.Contains("\"version\": 2", File.ReadAllText(_store.FilePath), StringComparison.Ordinal);
    }

    /// <summary>The language survives the round trip, and is stored in clear - it is not a secret.</summary>
    [Fact]
    public void TheMessageLanguageIsStoredAndReadBack()
    {
        _store.Save(Sample);

        Assert.Equal("en", _store.Load().Settings.MessageLanguage);
        Assert.Contains("\"messageLanguage\": \"en\"", File.ReadAllText(_store.FilePath), StringComparison.Ordinal);
    }

    /// <summary>
    /// A language this build does not know is KEPT as written rather than silently corrected, so the
    /// settings window can say what it found. It is resolved to the default only when a message is
    /// composed - see <c>MessageLanguages.Resolve</c>.
    /// </summary>
    [Fact]
    public void AnUnknownLanguageIsKeptAsWrittenAndReportedByValidation()
    {
        _store.Save(Sample with { MessageLanguage = "de" });

        var load = _store.Load();

        Assert.Equal("de", load.Settings.MessageLanguage);
        Assert.NotEmpty(load.Settings.Validate());
        Assert.Equal(MessageLanguages.Default, MessageLanguages.Resolve(load.Settings.MessageLanguage));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }
}

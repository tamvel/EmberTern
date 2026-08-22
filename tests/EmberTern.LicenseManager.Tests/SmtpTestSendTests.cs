using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// <b>Send test email…</b> — the button that answers "does this SMTP configuration actually work?".
///
/// <para>⭐⭐ It sends a REAL message down the SAME path a licence takes. A cheaper check — a handshake that
/// stops before <c>DATA</c> — would prove the credentials and nothing about whether mail arrives, and L6.1
/// already recorded that a Test button reporting success without sending would be worse than none.</para>
/// </summary>
public sealed class SmtpTestSendTests : IDisposable
{
    private readonly string _folder;

    public SmtpTestSendTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "etlm-test-send-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    private SettingsViewModel Configured(FakeEmailSender sender, string host = "smtp.example.test")
    {
        var model = new SettingsViewModel(new SmtpSettingsStore(Path.Combine(_folder, "smtp.dat")))
        {
            Host = host,
            FromAddress = "licencje@example.test",
            FromName = "EmberTern — licencje",
            Username = "licencje@example.test",
            Password = "an app password",
            TestRecipient = "operator@example.test",
            TestSenderFactory = _ => sender,
            Confirm = _ => Task.FromResult(true),
        };

        return model;
    }

    // ── The recipient ───────────────────────────────────────────────────────────────────────────────

    /// <summary>⛔ Nothing can be sent until an address is typed — and it is never pre-filled.</summary>
    [Fact]
    public void ATestNeedsAnAddressAndOffersNoDefault()
    {
        var model = Configured(FakeEmailSender.Succeeding());
        Assert.True(model.CanSendTestEmail);

        model.TestRecipient = "   ";
        Assert.False(model.CanSendTestEmail);
        Assert.False(model.SendTestEmailCommand.CanExecute(null));

        // ⭐ A fresh window offers nothing at all — no customer address can arrive here by accident.
        var fresh = new SettingsViewModel(new SmtpSettingsStore(Path.Combine(_folder, "fresh.dat")));
        Assert.Equal(string.Empty, fresh.TestRecipient);
    }

    [Fact]
    public async Task AMalformedAddressIsRefusedBeforeAnythingIsSent()
    {
        var sender = FakeEmailSender.Succeeding();
        var model = Configured(sender);
        model.TestRecipient = "operator at example.test";

        await model.SendTestEmailCommand.ExecuteAsync(null);

        Assert.Empty(sender.Sent);
        Assert.True(model.IsWarning);
    }

    // ── The configuration ───────────────────────────────────────────────────────────────────────────

    /// <summary>⚠ There is nothing to test without a server — file delivery contacts nothing.</summary>
    [Fact]
    public async Task WithNoServerThereIsNothingToTest()
    {
        var sender = FakeEmailSender.Succeeding();
        var model = Configured(sender, host: string.Empty);

        await model.SendTestEmailCommand.ExecuteAsync(null);

        Assert.Empty(sender.Sent);
        Assert.True(model.IsWarning);

        // The settings' own verdict gets there first here ("credentials but no server"), and that is the
        // right answer: what matters is that nothing was attempted and the operator was told why.
        Assert.Contains("server", model.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐⭐ It tests what is ON THE PAGE, saved or not. An operator who has just typed a host expects the
    /// test to try THAT host; requiring a Save first would mean persisting a configuration in order to
    /// discover it is wrong.
    /// </summary>
    [Fact]
    public async Task ItTestsTheFormRatherThanTheSavedFile()
    {
        var sender = FakeEmailSender.Succeeding();
        var model = Configured(sender);

        SmtpSettings? used = null;
        model.TestSenderFactory = settings =>
        {
            used = settings;
            return sender;
        };

        model.Host = "just.typed.test";

        await model.SendTestEmailCommand.ExecuteAsync(null);

        Assert.Equal("just.typed.test", used!.Host);
        Assert.False(File.Exists(Path.Combine(_folder, "smtp.dat")));
    }

    // ── The confirmation ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithNoConfirmerNothingIsSent()
    {
        var sender = FakeEmailSender.Succeeding();
        var model = Configured(sender);
        model.Confirm = null;

        await model.SendTestEmailCommand.ExecuteAsync(null);

        Assert.Empty(sender.Sent);
        Assert.True(model.IsWarning);
    }

    [Fact]
    public async Task DecliningSendsNothing()
    {
        var sender = FakeEmailSender.Succeeding();
        var model = Configured(sender);
        model.Confirm = _ => Task.FromResult(false);

        await model.SendTestEmailCommand.ExecuteAsync(null);

        Assert.Empty(sender.Sent);
    }

    // ── The outcome ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASuccessfulTestSaysSoPlainly()
    {
        var sender = FakeEmailSender.Succeeding("smtp.example.test");
        var model = Configured(sender);

        await model.SendTestEmailCommand.ExecuteAsync(null);

        var sent = Assert.Single(sender.Sent);
        Assert.Equal("operator@example.test", sent.ToAddress);
        Assert.Null(sent.Attachment);

        Assert.True(model.IsSuccess);
        Assert.Contains("Test email sent successfully", model.MessageText, StringComparison.Ordinal);
    }

    /// <summary>⚠ A failure carries the server's own words — never an interpretation of them.</summary>
    [Fact]
    public async Task AFailedTestReportsWhatTheServerSaid()
    {
        var model = Configured(FakeEmailSender.Failing("5.7.8 Username and Password not accepted."));

        await model.SendTestEmailCommand.ExecuteAsync(null);

        Assert.True(model.IsError);
        Assert.Contains("5.7.8", model.MessageText, StringComparison.Ordinal);
        Assert.Contains("did not work", model.MessageText, StringComparison.Ordinal);
    }

    // ── The audit ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⛔⛔ <b>A configuration test writes NOTHING to the audit log</b>, and that is structural rather than
    /// a rule to remember: this view model holds no register, so there is nothing it could write to.
    /// ⚠ Asserted on the TYPE, because a future edit that added a register would pass every other guard here.
    /// </summary>
    [Fact]
    public void TheSettingsWindowCannotReachTheRegisterAtAll()
    {
        var reachable = typeof(SettingsViewModel)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Select(f => f.FieldType)
            .Concat(typeof(SettingsViewModel)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Select(p => p.PropertyType))
            .ToList();

        Assert.DoesNotContain(typeof(LicenseRegister), reachable);
        Assert.DoesNotContain(typeof(Services.LicenceDelivery), reachable);
    }

    // ── The window is never left "in progress" (2026-08-22) ─────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>A refused configuration ends the operation and gives the button back.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>Why this matters more than it looks:</b> <c>ShellViewModel</c> builds this view model ONCE
    /// and the menu reuses it, so <c>IsTesting</c> left <see langword="true"/> is not something closing the
    /// window clears — the operator reopens Settings and finds the same disabled button, with nothing on
    /// screen saying why. That is exactly what was reported on 2026-08-22, and the cause was upstream (see
    /// <c>SmtpTimeoutTests</c>); this pins the view model's own half of it.
    /// </remarks>
    [Fact]
    public async Task ARefusedConfigurationDoesNotLeaveTheWindowInProgress()
    {
        var model = Configured(FakeEmailSender.Failing());

        await model.SendTestEmailCommand.ExecuteAsync(null);

        Assert.False(model.IsTesting);
        Assert.True(model.CanSendTestEmail);
        Assert.True(model.SendTestEmailCommand.CanExecute(null));
        Assert.Equal(MessageSeverity.Error, model.Message?.Severity);
    }

    /// <summary>
    /// ⭐ And so does a sender that throws something nobody anticipated.
    /// </summary>
    /// <remarks>
    /// ⛔ The point is NOT that the exception is swallowed — it is not, and it should not be. The point is
    /// that the flag is cleared on the way out, so an unforeseen failure costs the operator a message
    /// rather than the use of the window until they restart the application.
    /// </remarks>
    [Fact]
    public async Task AnUnexpectedFailureDoesNotLeaveTheWindowInProgress()
    {
        var model = Configured(FakeEmailSender.Succeeding());
        model.TestSenderFactory = _ => new ThrowingSender();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => model.SendTestEmailCommand.ExecuteAsync(null));

        Assert.False(model.IsTesting);
        Assert.True(model.CanSendTestEmail);
    }

    /// <summary>⚠ Not a transport: it fails before a socket exists, which is the case a `finally` covers.</summary>
    private sealed class ThrowingSender : ILicenseEmailSender
    {
        public string Destination => "smtp.example.test";

        public Task<SendOutcome> SendAsync(
            OutgoingEmail email, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("something nobody anticipated");
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

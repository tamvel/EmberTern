using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The Send licence window's decisions, without a window.
///
/// <para>⭐⭐ <b>The preview and the send are the same value.</b> Every guard below that reads a preview
/// property and then sends asserts exactly that: what the operator confirmed is what left.</para>
/// </summary>
public sealed class SendLicenceViewModelTests : IDisposable
{
    private readonly ManagerFixture _manager = new();

    private static SmtpSettings Settings => new()
    {
        Host = "smtp.example.test",
        FromAddress = "licencje@example.test",
        FromName = "EmberTern — licencje",
        MessageLanguage = MessageLanguages.Polish,
    };

    private (SendLicenceViewModel Model, FakeEmailSender Sender, string LicenseId) Build(
        bool succeeding = true, SmtpSettings? settings = null)
    {
        var use = settings ?? Settings;
        var customer = _manager.SaveCustomer("ACME Sp. z o.o.", "biuro@acme.test");
        var licence = _manager.SaveLicense(customer);
        _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        var message = LicenseMessageComposer.Compose(
            _manager.Register.GetCurrentArtifact(licence.LicenseId)!, customer, use);

        var sender = succeeding ? FakeEmailSender.Succeeding() : FakeEmailSender.Failing();

        var model = new SendLicenceViewModel(
            message,
            use,
            new LicenceDelivery(_manager.Register),
            _ => sender,
            _ => sender);

        return (model, sender, licence.LicenseId);
    }

    private IReadOnlyList<AuditEntry> AuditFor(string licenseId) =>
        _manager.Register.GetAudit(new AuditQuery { TargetType = "licence", TargetId = licenseId });

    // ── The preview ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThePreviewIsTheComposedMessage()
    {
        var (model, _, _) = Build();

        Assert.Contains("ACME Sp. z o.o.", model.Recipient, StringComparison.Ordinal);
        Assert.Contains("biuro@acme.test", model.Recipient, StringComparison.Ordinal);
        Assert.Contains("licencje@example.test", model.Sender, StringComparison.Ordinal);
        Assert.Equal(model.Composed.Subject, model.Subject);
        Assert.Equal(model.Composed.TextBody, model.Body);
    }

    /// <summary>⭐ Name, type and size — the size is what shows an empty artifact before it is sent.</summary>
    [Fact]
    public void TheAttachmentIsDescribedInFull()
    {
        var (model, _, _) = Build();

        Assert.Contains("EmberTern.etlic", model.Attachment, StringComparison.Ordinal);
        Assert.Contains("application/octet-stream", model.Attachment, StringComparison.Ordinal);
        Assert.Contains(
            model.Composed.AttachmentBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            model.Attachment,
            StringComparison.Ordinal);
    }

    /// <summary>⚠ The window says what the preview is: the text body, with an HTML version travelling too.</summary>
    [Fact]
    public void ThePreviewSaysWhatItIs()
    {
        var (model, _, _) = Build();

        Assert.Contains("exactly what will be sent", model.PreviewNote, StringComparison.Ordinal);
        Assert.Contains("HTML", model.PreviewNote, StringComparison.Ordinal);
        Assert.Contains("smtp.example.test", model.DeliveryNote, StringComparison.Ordinal);
    }

    // ── The confirmation ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⛔⛔ <b>With no confirmer wired it REFUSES rather than proceeding</b> — the rule L6.1a's
    /// <c>Forget settings</c> established, applied to the one action in this application that reaches
    /// outside the machine.
    /// </summary>
    [Fact]
    public async Task WithNoConfirmerItSendsNothing()
    {
        var (model, sender, licenceId) = Build();

        await model.SendCommand.ExecuteAsync(null);

        Assert.Empty(sender.Sent);
        Assert.True(model.IsWarning);
        Assert.DoesNotContain(AuditFor(licenceId), a => a.Action.StartsWith("licence.send", StringComparison.Ordinal));
    }

    /// <summary>⭐ Declining changes nothing — and says nothing, because nothing happened.</summary>
    [Fact]
    public async Task DecliningTheConfirmationSendsNothing()
    {
        var (model, sender, licenceId) = Build();
        model.Confirm = _ => Task.FromResult(false);

        await model.SendCommand.ExecuteAsync(null);

        Assert.Empty(sender.Sent);
        Assert.False(model.IsSent);
        Assert.Null(model.Message);
        Assert.DoesNotContain(AuditFor(licenceId), a => a.Action.StartsWith("licence.send", StringComparison.Ordinal));
    }

    /// <summary>⚠ The confirmation names the recipient and the file, so the buttons alone are enough to read.</summary>
    [Fact]
    public async Task TheConfirmationNamesWhatWillHappen()
    {
        var (model, _, _) = Build();
        ConfirmRequest? asked = null;
        model.Confirm = request =>
        {
            asked = request;
            return Task.FromResult(false);
        };

        await model.SendCommand.ExecuteAsync(null);

        Assert.NotNull(asked);
        Assert.Contains("biuro@acme.test", asked!.Message, StringComparison.Ordinal);
        Assert.Contains("EmberTern.etlic", asked.Message, StringComparison.Ordinal);
        Assert.Equal("Send", asked.ConfirmLabel);
    }

    // ── Sending ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AConfirmedSendDeliversTheMessageAndReportsIt()
    {
        var (model, sender, licenceId) = Build();
        model.Confirm = _ => Task.FromResult(true);

        await model.SendCommand.ExecuteAsync(null);

        var wire = Assert.Single(sender.Sent);
        Assert.Equal(model.Composed.Subject, wire.Subject);
        Assert.Equal(model.Composed.TextBody, wire.TextBody);
        Assert.Equal(model.Composed.AttachmentBytes, wire.Attachment!.Content);

        Assert.True(model.IsSuccess);
        Assert.Contains("biuro@acme.test", model.MessageText, StringComparison.Ordinal);
        Assert.Single(AuditFor(licenceId), a => a.Action == "licence.sent");
    }

    /// <summary>
    /// ⭐⭐ A second click cannot send the licence twice. The operator's own reason for clicking again is
    /// usually that the first click gave no visible answer yet.
    /// </summary>
    [Fact]
    public async Task ItCannotBeSentTwice()
    {
        var (model, sender, licenceId) = Build();
        model.Confirm = _ => Task.FromResult(true);

        await model.SendCommand.ExecuteAsync(null);
        Assert.False(model.CanSend);

        await model.SendCommand.ExecuteAsync(null);

        Assert.Single(sender.Sent);
        Assert.Single(AuditFor(licenceId), a => a.Action == "licence.sent");
    }

    /// <summary>
    /// ⚠ A failure keeps Send available — the operator may fix the settings and try again — and it says
    /// what the server said, plus the way out.
    /// </summary>
    [Fact]
    public async Task AFailedSendExplainsItselfAndCanBeRetried()
    {
        var (model, _, licenceId) = Build(succeeding: false);
        model.Confirm = _ => Task.FromResult(true);

        await model.SendCommand.ExecuteAsync(null);

        Assert.True(model.IsError);
        Assert.Contains("5.7.8", model.MessageText, StringComparison.Ordinal);
        Assert.Contains(".eml", model.MessageText, StringComparison.Ordinal);
        Assert.False(model.IsSent);
        Assert.True(model.CanSend);
        Assert.Single(AuditFor(licenceId), a => a.Action == "licence.send-failed");
    }

    /// <summary>⛔ Send is unavailable when the settings name no server; the file route still is.</summary>
    [Fact]
    public void WithNoServerOnlyTheFileRouteIsOffered()
    {
        var (model, _, _) = Build(settings: Settings with { Host = string.Empty });

        Assert.False(model.CanSend);
        Assert.True(model.CanSaveFile);
        Assert.Contains(".eml", model.DeliveryNote, StringComparison.Ordinal);
    }

    // ── The file route ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavingAFileWritesTheMessageAndRecordsAnExport()
    {
        var customer = _manager.SaveCustomer("ACME Sp. z o.o.", "biuro@acme.test");
        var licence = _manager.SaveLicense(customer);
        _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);

        var message = LicenseMessageComposer.Compose(
            _manager.Register.GetCurrentArtifact(licence.LicenseId)!, customer, Settings);

        var path = Path.Combine(_manager.Paths.Root, "by-hand.eml");
        var model = new SendLicenceViewModel(message, Settings, new LicenceDelivery(_manager.Register))
        {
            SaveFilePicker = _ => Task.FromResult<string?>(path),
        };

        await model.SaveFileCommand.ExecuteAsync(null);

        Assert.True(File.Exists(path));
        Assert.True(model.IsSuccess, model.MessageText);
        Assert.Contains(
            AuditFor(licence.LicenseId), a => a.Action == "licence.exported");
        Assert.DoesNotContain(AuditFor(licence.LicenseId), a => a.Action == "licence.sent");
    }

    /// <summary>⭐ A cancelled Save-As changes nothing and says nothing.</summary>
    [Fact]
    public async Task ACancelledSaveWritesNothing()
    {
        var (model, _, licenceId) = Build();
        model.SaveFilePicker = _ => Task.FromResult<string?>(null);

        await model.SaveFileCommand.ExecuteAsync(null);

        Assert.Null(model.Message);
        Assert.DoesNotContain(AuditFor(licenceId), a => a.Action == "licence.exported");
    }

    public void Dispose() => _manager.Dispose();
}

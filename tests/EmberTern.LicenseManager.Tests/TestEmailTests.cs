using System;
using EmberTern.LicenseManager.Email;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// The SMTP configuration test message.
///
/// <para>⭐ It is the one message this application sends that is not a licence, and every guard here is
/// about keeping it that way: no attachment, no licence, no audit, and a recipient the operator typed.</para>
/// </summary>
public sealed class TestEmailTests
{
    private static SmtpSettings Settings => new()
    {
        Host = "smtp.example.test",
        Port = 587,
        FromAddress = "licencje@example.test",
        FromName = "EmberTern — licencje",
        Username = "licencje@example.test",
        Password = "an app password nobody may see",
    };

    /// <summary>
    /// ⭐ The subject names the application AND the reason. A person opening an unexplained message from a
    /// licensing system reads it as phishing — and it may well be an administrator who did not send it.
    /// </summary>
    [Fact]
    public void TheSubjectSaysWhatItIs()
    {
        var email = TestEmail.Compose(Settings, "operator@example.test");

        Assert.Contains("EmberTern License Manager", email.Subject, StringComparison.Ordinal);
        Assert.Contains("test", email.Subject, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TestEmail.Subject, email.Subject);
    }

    /// <summary>⛔⛔ No licence, no attachment. It is a diagnostic and must never carry one.</summary>
    [Fact]
    public void ItCarriesNoAttachment()
    {
        var email = TestEmail.Compose(Settings, "operator@example.test");

        Assert.Null(email.Attachment);
        Assert.DoesNotContain(".etlic", email.TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("BEGIN EMBERTERN LICENSE", email.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠ Plain text only — a diagnostic needs no HTML alternative, which is exactly why
    /// <see cref="OutgoingEmail.HtmlBody"/> is optional rather than speculative.
    /// </summary>
    [Fact]
    public void ItIsPlainTextOnly() =>
        Assert.Null(TestEmail.Compose(Settings, "operator@example.test").HtmlBody);

    /// <summary>
    /// ⭐ It states the configuration it proved — the operator is about to trust the answer for real
    /// deliveries, and "it worked" is only useful with the server and sender beside it.
    /// </summary>
    [Fact]
    public void ItStatesTheConfigurationItProved()
    {
        var email = TestEmail.Compose(Settings, "operator@example.test");

        Assert.Contains("smtp.example.test", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("587", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("STARTTLS", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("licencje@example.test", email.TextBody, StringComparison.Ordinal);
        Assert.Contains("operator@example.test", email.TextBody, StringComparison.Ordinal);
    }

    /// <summary>⛔⛔ The password is never in a message. Ever.</summary>
    [Fact]
    public void ThePasswordIsNeverInTheMessage()
    {
        var email = TestEmail.Compose(Settings, "operator@example.test");

        Assert.DoesNotContain(
            "an app password nobody may see", email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "an app password nobody may see", email.Subject, StringComparison.Ordinal);
    }

    /// <summary>⭐ It says it is only a check, so a reader knows nothing happened to any customer.</summary>
    [Fact]
    public void ItSaysItIsOnlyAConfigurationCheck()
    {
        var body = TestEmail.Compose(Settings, "operator@example.test").TextBody;

        Assert.Contains("configuration", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No licence is attached", body, StringComparison.Ordinal);
    }

    /// <summary>⭐ The sender is the configured one; the recipient is exactly what was typed, trimmed.</summary>
    [Fact]
    public void ItComesFromTheConfiguredSenderAndGoesWhereItWasTold()
    {
        var email = TestEmail.Compose(Settings, "  operator@example.test  ");

        Assert.Equal("operator@example.test", email.ToAddress);
        Assert.Equal("licencje@example.test", email.FromAddress);
        Assert.Equal("EmberTern — licencje", email.FromName);
    }

    /// <summary>⚠ An address alone is a valid sender, so the name falls back to it rather than being blank.</summary>
    [Fact]
    public void TheSenderNameFallsBackToTheAddress() =>
        Assert.Equal(
            "licencje@example.test",
            TestEmail.Compose(Settings with { FromName = "  " }, "operator@example.test").FromName);

    [Fact]
    public void ItRefusesToComposeWithoutARecipient()
    {
        Assert.Throws<ArgumentException>(() => TestEmail.Compose(Settings, "   "));
        Assert.Throws<ArgumentNullException>(() => TestEmail.Compose(Settings, null!));
    }
}

using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>L6.2's subject: composition, and nothing else.</b> Not a single test here opens a socket, writes
/// a file or signs anything — which is the point of a composer that is a pure function. The company mailbox
/// is still unmeasured (§48.1) and "can we build the right message?" must not wait on it.
///
/// <para>⛔ The attachment's bytes and their survival through a real MIME transport are the subject of
/// <see cref="LicenseMessageArtifactTests"/>; this file is about what the message SAYS.</para>
/// </summary>
public sealed class LicenseMessageTests : IDisposable
{
    private readonly ManagerFixture _manager = new();

    private static SmtpSettings Settings(string language = MessageLanguages.Polish) => new()
    {
        Host = "smtp.example.test",
        FromAddress = "licencje@example.test",
        FromName = "EmberTern — licencje",
        Username = "licencje@example.test",
        Password = "irrelevant here",
        MessageLanguage = language,
    };

    private (IssuedArtifactRecord Artifact, CustomerRecord Customer) Issue(
        string name = "ACME Sp. z o.o.", string? email = "biuro@acme.test")
    {
        var customer = _manager.SaveCustomer(name, email);
        var licence = _manager.SaveLicense(customer);
        var result = _manager.Workflow.Issue(_manager.Session, licence, customer, IssueReasons.Initial);
        return (result.Artifact, customer);
    }

    // ── What the message is, and who it is for ──────────────────────────────────────────────────

    [Fact]
    public void ItIsAddressedToTheCustomerAndSignedBySender()
    {
        var (artifact, customer) = Issue();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings());

        Assert.Equal("biuro@acme.test", message.ToAddress);
        Assert.Equal("ACME Sp. z o.o.", message.ToName);
        Assert.Equal("licencje@example.test", message.FromAddress);
        Assert.Equal("EmberTern — licencje", message.FromName);
    }

    /// <summary>
    /// ⭐ The name in the message is the name that was SIGNED, not the register's current one — otherwise a
    /// company renamed after issuing would receive a message that disagrees with its own licence.
    /// </summary>
    [Fact]
    public void TheNameItGreetsIsTheOneInsideTheArtifact()
    {
        var (artifact, customer) = Issue("Old Name Sp. z o.o.");
        var renamed = _manager.Register.SaveCustomer(customer with { Name = "New Name S.A." });

        var message = LicenseMessageComposer.Compose(artifact, renamed, Settings());

        Assert.Equal("Old Name Sp. z o.o.", message.ToName);
        Assert.Contains("Old Name Sp. z o.o.", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("New Name S.A.", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSubjectComesFromTheTemplateAndNamesTheLicensee()
    {
        var (artifact, customer) = Issue();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings());

        // The subject carries the name WITHOUT a preposition: "Licencja EmberTern dla Jan Kowalski"
        // would need the name declined, and a value taken from a signed payload may never be inflected.
        Assert.Equal("Licencja EmberTern — ACME Sp. z o.o.", message.Subject);
    }

    /// <summary>⭐ The HTML document's own subject is its title, and the two must never drift apart.</summary>
    [Theory]
    [InlineData(MessageLanguages.Polish)]
    [InlineData(MessageLanguages.English)]
    public void TheHtmlTitleIsTheSubject(string language)
    {
        var (artifact, customer) = Issue();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings(language));

        var title = Regex.Match(message.HtmlBody, "<title>(.*?)</title>", RegexOptions.Singleline);
        Assert.True(title.Success, "The HTML body carries no <title>.");
        Assert.Equal(message.Subject, WebUtility.HtmlDecode(title.Groups[1].Value.Trim()));
    }

    // ── What it says ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Both bodies state the same facts</b> — §14.2's requirement, and the reason the plain-text
    /// alternative is not a courtesy: a customer whose client strips HTML must still learn every term.
    /// </summary>
    [Theory]
    [InlineData(MessageLanguages.Polish)]
    [InlineData(MessageLanguages.English)]
    public void EveryLicenceFactAppearsInBothBodies(string language)
    {
        var (artifact, customer) = Issue();
        var licence = _manager.Register.GetLicenses(customer.CustomerId).Single();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings(language));

        foreach (var fact in new[]
                 {
                     "ACME Sp. z o.o.",
                     "5",
                     licence.LicenseId,
                     LicenseConstants.DeliveredFileName,
                     "licencje@example.test",
                 })
        {
            Assert.Contains(fact, message.TextBody, StringComparison.Ordinal);
            Assert.Contains(fact, message.HtmlBody, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// ⚠ The days the LICENCE carries, read as UTC calendar days. <c>exp</c> is the last second of the
    /// chosen day (<c>LicenseDay.EndOf</c>), so a local-time reading would move it across midnight.
    /// </summary>
    [Fact]
    public void TheValidityDatesAreTheDaysTheLicenceItselfCarries()
    {
        var (artifact, customer) = Issue();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings(MessageLanguages.English));

        // The fixture's clock is 2026-08-15, and its licences run one year.
        Assert.Contains("15 August 2026", message.TextBody, StringComparison.Ordinal);
        Assert.Contains("15 August 2027", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDatesAreWrittenInTheMessagesLanguage()
    {
        var (artifact, customer) = Issue();

        var polish = LicenseMessageComposer.Compose(artifact, customer, Settings(MessageLanguages.Polish));

        Assert.Contains("15 sierpnia 2026", polish.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("August", polish.TextBody, StringComparison.Ordinal);
    }

    // ── The language ────────────────────────────────────────────────────────────────────────────

    /// <summary>⭐ The setting picks the RESOURCE. ⛔ There is no branch anywhere that picks the words.</summary>
    [Theory]
    [InlineData(MessageLanguages.Polish, "ODBIORCA LICENCJI", "LICENCE RECIPIENT")]
    [InlineData(MessageLanguages.English, "LICENCE RECIPIENT", "ODBIORCA LICENCJI")]
    public void TheSettingChoosesTheLanguageOfTheWholeMessage(string language, string present, string absent)
    {
        var (artifact, customer) = Issue();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings(language));

        Assert.Equal(language, message.Language);
        Assert.Contains(present, message.TextBody, StringComparison.Ordinal);
        Assert.Contains(present, message.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain(absent, message.TextBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⚠ A settings file naming a language this build does not know must still produce a message — refusing
    /// to send over an unrecognised preference would fail the operation for the wrong reason.
    /// </summary>
    [Fact]
    public void AnUnknownLanguageComposesInTheDefaultOne()
    {
        var (artifact, customer) = Issue();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings("de"));

        Assert.Equal(MessageLanguages.Default, message.Language);
        Assert.Contains("ODBIORCA LICENCJI", message.TextBody, StringComparison.Ordinal);
    }

    // ── The attachment's identity (its BYTES are LicenseMessageArtifactTests') ───────────────────

    [Fact]
    public void TheAttachmentIsAlwaysEmberTernEtlicAsAnOpaqueBlob()
    {
        var (artifact, customer) = Issue("Żółć Sp. z o.o.");

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings());

        // ⛔ Never the customer's name, never a timestamp (decision O6).
        Assert.Equal("EmberTern.etlic", message.AttachmentFileName);
        Assert.Equal("application/octet-stream", message.AttachmentMediaType);
        Assert.Equal(LicenseMessage.OctetStream, message.AttachmentMediaType);
        Assert.NotEmpty(message.AttachmentBytes);
    }

    // ── Substitution ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ The failure this design can actually have: a customer reading <c>{{Seats}}</c> in their licence
    /// e-mail. Nothing shaped like a placeholder may survive into any part of a composed message.
    /// </summary>
    [Theory]
    [InlineData(MessageLanguages.Polish)]
    [InlineData(MessageLanguages.English)]
    public void NoPlaceholderSurvivesIntoTheMessage(string language)
    {
        var (artifact, customer) = Issue();

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings(language));

        foreach (var part in new[] { message.Subject, message.TextBody, message.HtmlBody })
        {
            Assert.DoesNotContain("{{", part, StringComparison.Ordinal);
            Assert.DoesNotContain("}}", part, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// ⭐⭐ A value is encoded for HTML; the markup is not. A company name carrying <c>&amp;</c> or angle
    /// brackets must reach the customer intact in both bodies, and must not break the layout in one of them.
    /// </summary>
    [Fact]
    public void AValueWithMarkupIsEncodedInTheHtmlBodyAndRawInTheText()
    {
        var (artifact, customer) = Issue("Żółć & Synowie <Test> Sp. z o.o.");

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings());

        Assert.Contains("Żółć & Synowie <Test> Sp. z o.o.", message.TextBody, StringComparison.Ordinal);
        Assert.Contains(
            "Żółć &amp; Synowie &lt;Test&gt; Sp. z o.o.", message.HtmlBody, StringComparison.Ordinal);

        // ⚠ The subject is a header, not markup — it carries the name as it was signed.
        Assert.Contains("Żółć & Synowie <Test> Sp. z o.o.", message.Subject, StringComparison.Ordinal);

        // ⛔ And the markup itself is still markup, not escaped text on screen.
        Assert.Contains("<html", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSenderNameFallsBackToTheAddressWhenNoneIsConfigured()
    {
        var (artifact, customer) = Issue();

        var message = LicenseMessageComposer.Compose(
            artifact, customer, Settings() with { FromName = "  " });

        Assert.Equal("licencje@example.test", message.FromName);
        Assert.Contains("licencje@example.test", message.TextBody, StringComparison.Ordinal);
    }

    // ── Refusals ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ACustomerWithoutAnAddressIsRefusedRatherThanComposedHalfWay()
    {
        var (artifact, customer) = Issue(email: null);

        var problems = LicenseMessageComposer.Problems(artifact, customer, Settings());

        Assert.Contains(problems, p => p.ToString().Contains("no e-mail address", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(
            () => LicenseMessageComposer.Compose(artifact, customer, Settings()));
    }

    [Fact]
    public void AMalformedCustomerAddressIsRefused()
    {
        var (artifact, customer) = Issue(email: "biuro at acme.test");

        Assert.Contains(
            LicenseMessageComposer.Problems(artifact, customer, Settings()),
            p => p.ToString().Contains("does not look like one", StringComparison.Ordinal));
    }

    /// <summary>⚠ A message has to come from somewhere — an unconfigured sender is refused.</summary>
    [Fact]
    public void SettingsWithNoSenderAddressAreRefused()
    {
        var (artifact, customer) = Issue();

        Assert.Contains(
            LicenseMessageComposer.Problems(artifact, customer, SmtpSettings.Empty),
            p => p.ToString().Contains("no usable sender address", StringComparison.Ordinal));
    }

    /// <summary>
    /// ⭐⭐ <b>Composition asks a NARROWER question than the settings window does.</b> A missing host and
    /// missing credentials stop a SEND, not a message — an <c>.eml</c> handed to Outlook needs neither — and
    /// an unrecognised message language must never fail anything, because it has a safe answer (§D-9). ⛔ So
    /// the settings' whole verdict is deliberately not folded in here.
    /// </summary>
    [Fact]
    public void TransportSettingsAndAnUnknownLanguageDoNotStopAMessageBeingComposed()
    {
        var (artifact, customer) = Issue();

        var noServer = new SmtpSettings { FromAddress = "licencje@example.test", MessageLanguage = "de" };

        // The settings window would have something to say about this configuration…
        Assert.NotEmpty(noServer.Validate());

        // …and none of it stops the message being built.
        Assert.Empty(LicenseMessageComposer.Problems(artifact, customer, noServer));
        Assert.Equal(
            MessageLanguages.Default,
            LicenseMessageComposer.Compose(artifact, customer, noServer).Language);
    }

    /// <summary>
    /// ⚠ An artifact whose stored token cannot be read is OUR fault, not the operator's — and it is refused
    /// rather than described, because a message about a licence we cannot read would be a guess.
    /// </summary>
    [Fact]
    public void AnUnreadableArtifactIsRefusedRatherThanDescribed()
    {
        var (artifact, customer) = Issue();

        var damaged = artifact with { Token = "ETL1.notatoken" };

        Assert.Contains(
            LicenseMessageComposer.Problems(damaged, customer, Settings()),
            p => p.ToString().Contains("could not be read", StringComparison.Ordinal));
    }

    // ── Purity ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>Composing signs nothing and records nothing.</b> A message is a value; issuing is an act. If
    /// composing could mint an artifact, previewing an e-mail would hand the customer a new <c>iat</c> and
    /// the register would gain a delivery that never happened.
    /// </summary>
    [Fact]
    public void ComposingChangesNothingInTheRegister()
    {
        var (artifact, customer) = Issue();
        var licence = _manager.Register.GetLicenses(customer.CustomerId).Single();

        var artifactsBefore = _manager.Register.GetArtifacts(licence.LicenseId).Count;
        var auditBefore = _manager.Register.GetAudit().Count;
        var currentBefore = _manager.Register.GetCurrentArtifact(licence.LicenseId)!.ArtifactId;

        var message = LicenseMessageComposer.Compose(artifact, customer, Settings());

        Assert.NotEmpty(message.TextBody);
        Assert.Equal(artifactsBefore, _manager.Register.GetArtifacts(licence.LicenseId).Count);
        Assert.Equal(auditBefore, _manager.Register.GetAudit().Count);
        Assert.Equal(currentBefore, _manager.Register.GetCurrentArtifact(licence.LicenseId)!.ArtifactId);
    }

    public void Dispose() => _manager.Dispose();
}

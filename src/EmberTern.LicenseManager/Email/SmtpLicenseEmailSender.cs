using System;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Email;

/// <summary>
/// Sends through an SMTP server, with <c>System.Net.Mail</c>.
///
/// <para>⭐⭐ <b>No MailKit, and that is a measured decision rather than a preference</b> (§48.1): on
/// 2026-08-19 a hand-written probe reached <c>smtp.gmail.com:587</c>, upgraded with STARTTLS and got
/// <c>235 Accepted</c> from a real <c>AUTH LOGIN</c> with an app password. Basic auth over STARTTLS is a
/// working implementation, so the BCL suffices. ⚠⚠ That measurement was taken on a GMAIL TEST ACCOUNT —
/// the company mailbox is still unmeasured, and if it turns out to require OAuth2 the answer is a NEW
/// CLASS behind <see cref="ILicenseEmailSender"/>, not a rebuild. That is what the interface is for.</para>
///
/// <para>⛔ <b>Implicit TLS (port 465) is not supported and is not offered</b>: <see cref="SmtpClient"/>
/// implements STARTTLS only, and its <c>EnableSsl</c> means "upgrade" (§48.4).</para>
///
/// <para>⚠ <b>Every failure is REPORTED, never thrown.</b> A refused login is the answer the operator
/// asked for, and it has to reach both the message strip and the audit log.</para>
/// </summary>
public sealed class SmtpLicenseEmailSender : ILicenseEmailSender
{
    /// <summary>
    /// How long to wait for the server, in milliseconds.
    ///
    /// <para>⚠ Bounded on purpose. <see cref="SmtpClient"/>'s own default is 100 seconds, which is long
    /// enough that a wrong host reads as a frozen application — and the operator is sitting in front of a
    /// modal window watching a button.</para>
    ///
    /// <para>⚠⚠ <b>MEASURED, 2026-08-22: setting <see cref="SmtpClient.Timeout"/> does NOT bound
    /// <see cref="SmtpClient.SendMailAsync(MailMessage)"/>.</b> A probe against a black-holed address with
    /// <c>Timeout = 3 000</c> took <b>21 078 ms</b> to fail — the operating system's TCP give-up, not ours.
    /// The property governs the SYNCHRONOUS <c>Send</c> only, and this application never calls that. So
    /// for five stages the number below was a promise the class did not keep, and against the worse
    /// configuration — implicit TLS on port 465, where the server waits for a ClientHello while the client
    /// waits for an SMTP banner — nothing bounded the wait at all.</para>
    ///
    /// <para>⭐ The same probe measured the answer: the <see cref="CancellationToken"/> overload DOES
    /// interrupt a connect that is going nowhere (2 995 ms against a 3 000 ms token). That is why
    /// <see cref="SendAsync"/> carries its own deadline as a token rather than trusting the property, and
    /// why the property is still set — it costs nothing and is correct for anyone who calls
    /// <c>Send</c>.</para>
    /// </summary>
    public const int TimeoutMilliseconds = 30_000;

    private readonly SmtpSettings _settings;
    private readonly TimeSpan _timeout;

    /// <summary>Creates the sender over a configuration.</summary>
    /// <exception cref="ArgumentException">
    /// The settings name no server. ⭐ Refused at construction: a sender that cannot reach anything is not
    /// a sender, and building one would push the failure to a place where it reads as a network problem.
    /// </exception>
    public SmtpLicenseEmailSender(SmtpSettings settings)
        : this(settings, TimeSpan.FromMilliseconds(TimeoutMilliseconds))
    {
    }

    /// <summary>
    /// The same sender with a different deadline. ⚠ A test seam, and it exists for a reason no other seam
    /// covers: the property being guarded is <b>how long a dead server may stall this application</b>, and
    /// a suite that waited thirty seconds to prove it would not be run.
    /// </summary>
    internal SmtpLicenseEmailSender(SmtpSettings settings, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;

        if (!settings.CanSendDirectly)
        {
            // ⚠ Reaches the strip UNFRAMED at two call sites (Settings ▸ test send, Send licence), so the
            //   sentence is ours to translate and carries its key. English is for diagnostics only.
            throw new LocalizedArgumentException(
                StatusCatalog.SettingsCarryNoSmtpHost,
                "These settings carry no SMTP host, so nothing can be sent directly. Save the message as " +
                "an .eml file instead, or add a server under Settings ▸ E-mail.",
                nameof(settings));
        }

        _settings = settings;
    }

    /// <inheritdoc />
    public string Destination => _settings.Host;

    /// <inheritdoc />
    public async Task<SendOutcome> SendAsync(
        OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        using var client = new SmtpClient(_settings.Host, _settings.Port)
        {
            DeliveryMethod = SmtpDeliveryMethod.Network,

            // ⭐ "Upgrade this connection with STARTTLS", NOT "connect encrypted" — see the type remarks.
            EnableSsl = _settings.Security == SmtpSecurity.StartTls,
            Timeout = TimeoutMilliseconds,
        };

        // ⚠ UseDefaultCredentials must be set BEFORE Credentials: setting it afterwards resets them to
        //   null, silently sending the message unauthenticated. It is a documented ordering trap in the
        //   BCL, and the symptom is a 5.7.0 the operator cannot explain.
        client.UseDefaultCredentials = false;

        if (!string.IsNullOrWhiteSpace(_settings.Username))
        {
            client.Credentials = new NetworkCredential(_settings.Username, _settings.Password);
        }

        using var mail = MailComposition.Build(email);

        // ⭐⭐ OUR deadline, carried as a token because that is the only form the BCL honours here — see
        //    TimeoutMilliseconds for the measurement. ⚠ LINKED, so the caller's own token still works and
        //    the two remain distinguishable afterwards: they must be, because one is a delivery outcome
        //    and the other is not.
        using var deadline = new CancellationTokenSource(_timeout);
        using var linked =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            await client.SendMailAsync(mail, linked.Token).ConfigureAwait(false);
            return SendOutcome.Ok(_settings.Host);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // ⭐ Cancellation is the CALLER's decision, not a delivery failure — it must not become an
            //    audit line claiming the server refused something.
            throw;
        }
        catch (OperationCanceledException)
        {
            // ⭐⭐ OUR deadline fired, which IS a delivery outcome and the operator's whole answer: the
            //    server never replied. ⛔ Not rethrown — the case above is the caller changing their mind,
            //    this one is a configuration that does not work, and reporting it is the point.
            // ⚠ Two texts, on purpose: the English one goes in the audit note, which stays invariant like
            //   every note in that register; the KEY is what the operator reads. The BCL's own words here
            //   are "A task was canceled", which explains nothing to either reader.
            return SendOutcome.Failed(
                $"No answer from {_settings.Host}:{_settings.Port} within " +
                $"{_timeout.TotalSeconds:0} s; the attempt was abandoned.",
                new LocalizedText(
                    StatusCatalog.SmtpServerDidNotAnswerInTime,
                    _settings.Host,
                    _settings.Port.ToString(CultureInfo.InvariantCulture),
                    ((int)_timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture)));
        }
        catch (Exception e)
        {
            // ⚠⚠ Deliberately broad, and this is the one place in the application where that is right.
            //    The failure modes of a socket + TLS + SMTP conversation span SmtpException,
            //    SmtpFailedRecipientsException, IOException, SocketException, AuthenticationException and
            //    InvalidOperationException, and the list differs by provider and by .NET version — an
            //    escape here would take down a window the operator is standing in front of, to no gain.
            //    ⭐ The contract is REPORT, never throw, and the server's own words are what gets reported.
            return SendOutcome.Failed(Describe(e));
        }
    }

    // ⭐ The server's message, with the inner one appended when the outer is the BCL's own wrapper — a
    //   bare "Failure sending mail." explains nothing, and the sentence underneath it is the whole answer
    //   ("5.7.8 Username and Password not accepted").
    // ⛔ Never interpreted, never rewritten: EmberTern's connection dialog already learned that lesson
    //   (mis-hinted Legacy_Auth on unrelated failures), and it is recorded in CLAUDE.md.
    private static string Describe(Exception e) =>
        e.InnerException is { } inner && !string.IsNullOrWhiteSpace(inner.Message)
            ? $"{e.Message} {inner.Message}"
            : e.Message;
}

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>A wrong SMTP configuration must not leave the application looking frozen.</b>
///
/// <para>⚠⚠ <b>The defect these were written for, and it was live for five stages:</b>
/// <c>SmtpLicenseEmailSender</c> declared <c>Timeout = 30 000</c> on its <see cref="System.Net.Mail.SmtpClient"/>
/// and documented why — and the property does not bound <c>SendMailAsync</c>. It governs the SYNCHRONOUS
/// <c>Send</c>, which this application never calls. Measured with <c>tools/probes/SmtpTimeoutProbe</c>: a
/// black-holed address with <c>Timeout = 3 000</c> took <b>21 078 ms</b> to fail — the operating system's
/// TCP give-up, not ours. Against the worse configuration, implicit TLS on port 465, nothing bounded the
/// wait at all: the server waits for a ClientHello while the client waits for an SMTP banner.</para>
///
/// <para>⭐ The same probe measured the answer, which is why the fix is a token rather than a rebuild: the
/// <see cref="CancellationToken"/> overload DOES interrupt a connect going nowhere (2 995 ms against a
/// 3 000 ms token).</para>
///
/// <para>⚠ Everything here is LOCAL: a listener on <c>127.0.0.1</c> that accepts and then says nothing
/// reproduces the deadlock exactly, with no outside service, no address that might route somewhere on
/// another network, and no wait longer than the deadline the test itself chooses.</para>
/// </summary>
public sealed class SmtpTimeoutTests
{
    // ⚠ Short enough that the suite does not notice, long enough that a slow machine cannot beat it to
    //   the connect. The number under test is the BEHAVIOUR, not the constant.
    private static readonly TimeSpan Deadline = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// ⭐⭐ A server that accepts the connection and never answers is REPORTED, not waited on.
    /// </summary>
    /// <remarks>
    /// ⭐ The upper bound is generous on purpose — this is not a performance assertion. What it forbids is
    /// the old behaviour, which was "however long the peer feels like", and against this listener that
    /// means forever.
    /// </remarks>
    [Fact]
    public async Task ADeadServerIsReportedRatherThanWaitedOn()
    {
        using var silent = SilentServer.Start();

        var watch = Stopwatch.StartNew();
        var outcome = await Sender(silent.Port).SendAsync(TestEmail.Compose(Settings(silent.Port), "op@example.test"));
        watch.Stop();

        Assert.False(outcome.Sent);
        Assert.True(
            watch.Elapsed < TimeSpan.FromSeconds(15),
            $"The send took {watch.Elapsed.TotalSeconds:0.0} s against a {Deadline.TotalMilliseconds} ms "
            + "deadline. SmtpClient.Timeout does not bound SendMailAsync — the deadline has to be carried "
            + "as a CancellationToken.");
    }

    /// <summary>
    /// ⭐ Our own timeout is OUR sentence, and it carries the host, the port and the deadline.
    /// </summary>
    /// <remarks>
    /// ⛔ Not the BCL's words. <c>SendMailAsync</c> answers a cancelled token with
    /// <i>"A task was canceled."</i> — which tells the operator nothing and is not translatable, and which
    /// would have been what they read had the timeout simply been reported like any other exception.
    /// ⛔ And <c>Error</c> — the English half that goes into the audit note — must not be that either.
    /// </remarks>
    [Fact]
    public async Task ATimeoutSpeaksInOurWordsAndNotTheFrameworks()
    {
        using var silent = SilentServer.Start();

        var outcome = await Sender(silent.Port).SendAsync(TestEmail.Compose(Settings(silent.Port), "op@example.test"));

        var reason = Assert.IsType<EmberTern.LicenseManager.Localization.LocalizedText>(outcome.Reason);
        Assert.Equal(StatusCatalog.SmtpServerDidNotAnswerInTime, reason.Key);

        // ⭐ The three facts an operator needs to act, as ARGUMENTS rather than baked into the sentence.
        Assert.Contains("127.0.0.1", reason.Arguments);
        Assert.Contains(silent.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), reason.Arguments);

        // ⭐ Explanation is what a display site shows: our sentence here, the server's words when the
        //   server actually said something.
        Assert.Same(outcome.Reason, outcome.Explanation);

        Assert.NotNull(outcome.Error);
        Assert.DoesNotContain("task was canceled", outcome.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⛔⛔ <b>The caller's own cancellation is still RETHROWN, not reported.</b>
    /// </summary>
    /// <remarks>
    /// ⚠⚠ The half of this fix that could have been broken silently. A cancelled SMTP conversation may
    /// already have delivered the message, so it must never become an audit line claiming the server
    /// refused something (§60.6) — while OUR deadline must become exactly that. The two arrive as the same
    /// exception type from the same call, and only the token that fired distinguishes them.
    /// </remarks>
    [Fact]
    public async Task TheCallersOwnCancellationIsStillRethrown()
    {
        using var silent = SilentServer.Start();
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // ⚠ A LONG deadline, so the only thing that can end this send is the caller's token.
        var sender = new SmtpLicenseEmailSender(Settings(silent.Port), TimeSpan.FromSeconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendAsync(TestEmail.Compose(Settings(silent.Port), "op@example.test"), caller.Token));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static SmtpLicenseEmailSender Sender(int port) =>
        new(Settings(port), Deadline);

    private static SmtpSettings Settings(int port) => new()
    {
        Host = "127.0.0.1",
        Port = port,
        FromAddress = "licencje@example.test",
        FromName = "EmberTern",

        // ⚠ Plain, not STARTTLS: the point is a server that never speaks, and adding TLS would only make
        //   the deadlock harder to read in a failure message.
        Security = SmtpSecurity.None,
    };

    /// <summary>
    /// A socket that accepts a connection and then says nothing at all, for as long as it is held.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ This IS the failure the operator hit, reproduced exactly: an SMTP client waits for the server's
    /// greeting before it sends anything, so a peer that accepts and stays silent stalls the conversation
    /// forever. ⛔ A port with nothing listening would be REFUSED instantly and would prove nothing.
    /// ⚠ Port 0 lets the operating system choose, so two tests can never collide.
    /// </remarks>
    private sealed class SilentServer : IDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _stop = new();

        private SilentServer(TcpListener listener)
        {
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;

            _ = AcceptForeverAsync();
        }

        internal int Port { get; }

        internal static SilentServer Start()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return new SilentServer(listener);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _stop.Dispose();
        }

        private async Task AcceptForeverAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    // ⚠ Held, never disposed until the server is: closing the socket would send a FIN and
                    //   the client would fail FAST, which is the opposite of what this reproduces.
                    var accepted = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                    _ = accepted;
                }
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                // The listener was stopped. That is how this ends.
            }
        }
    }
}

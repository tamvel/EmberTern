using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Email;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// A sender that records what it was handed and answers however the test needs it to.
///
/// <para>⭐⭐ <b>The one thing that is legitimately faked in L6.3, and only this.</b> A refused login is a
/// server's decision, and no test may depend on a real server refusing one — but everything on OUR side of
/// that decision (the composed message, the attachment bytes, the audit line, the window's state) is real
/// and is asserted. ⛔ The transport itself is not faked away elsewhere: the <c>.eml</c> guards drive the
/// real <see cref="EmlFileEmailSender"/> and read the file it writes.</para>
/// </summary>
internal sealed class FakeEmailSender : ILicenseEmailSender
{
    /// <summary>
    /// The refusal a fake server gives, in the shape a real one does.
    /// </summary>
    /// <remarks>
    /// ⭐ A named constant since L10.4, so a test that asserts the server's words REACHED the report cannot
    /// drift from the sender that produced them — two copies of the same sentence is how such a test starts
    /// proving nothing. ⚠ It is not a word of ours: it is the SERVER's, and it is never translated.
    /// </remarks>
    internal const string RefusalText = "5.7.8 Username and Password not accepted.";

    private readonly SendOutcome _outcome;

    private FakeEmailSender(SendOutcome outcome, string destination)
    {
        _outcome = outcome;
        Destination = destination;
    }

    /// <summary>A sender that always succeeds.</summary>
    internal static FakeEmailSender Succeeding(string destination = "smtp.example.test") =>
        new(SendOutcome.Ok(destination), destination);

    /// <summary>A sender that always fails, with the server's words.</summary>
    internal static FakeEmailSender Failing(
        string error = RefusalText,
        string destination = "smtp.example.test") =>
        new(SendOutcome.Failed(error), destination);

    /// <inheritdoc />
    public string Destination { get; }

    /// <summary>Every message this sender was asked to deliver, in order.</summary>
    internal List<OutgoingEmail> Sent { get; } = [];

    /// <inheritdoc />
    public Task<SendOutcome> SendAsync(
        OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        Sent.Add(email);
        return Task.FromResult(_outcome);
    }
}

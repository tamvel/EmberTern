using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.LicenseManager.Email;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// A sender that succeeds until a given message, then refuses with the server's own words.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>The ONE thing faked on our side of the server's decision.</b> The plan, the composed
/// message, the audit lines, the counters and the report are all production code; only the answer a real
/// server would give is scripted — because no test may depend on a real server refusing a login.</para>
///
/// <para>⭐ <see cref="Observe"/> runs WHILE a message is in flight, which is the only moment "a run is
/// happening" is true. Anything asserted about that state afterwards would pass whatever the code did.</para>
///
/// <para>⚠ Shared by the view-model guards and the view guards on purpose: two copies of a scripted server
/// is how the two levels start proving things about different behaviour.</para>
/// </remarks>
internal sealed class ScriptedSender : ILicenseEmailSender
{
    private int _failFrom = int.MaxValue;
    private string _error = FakeEmailSender.RefusalText;
    private Action? _observe;

    /// <inheritdoc />
    public string Destination => "smtp.example.test";

    /// <summary>Every message this sender was asked to deliver, in order.</summary>
    internal List<OutgoingEmail> Sent { get; } = [];

    /// <summary>From the <paramref name="message"/>-th attempt onwards, the server refuses.</summary>
    /// <remarks>⚠ One-based, so <c>FailFrom(1)</c> refuses the very first message.</remarks>
    internal ScriptedSender FailFrom(int message, string? error = null)
    {
        _failFrom = message;
        _error = error ?? FakeEmailSender.RefusalText;
        return this;
    }

    /// <summary>Runs while a message is in flight. ⭐ See the type's remarks.</summary>
    internal ScriptedSender Observe(Action watch)
    {
        _observe = watch;
        return this;
    }

    /// <inheritdoc />
    public Task<SendOutcome> SendAsync(
        OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(email);

        Sent.Add(email);
        _observe?.Invoke();

        return Task.FromResult(Sent.Count >= _failFrom
            ? SendOutcome.Failed(_error)
            : SendOutcome.Ok(Destination));
    }
}

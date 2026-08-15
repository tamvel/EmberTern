using System;
using EmberTern.Licensing;

namespace EmberTern.App.Licensing;

/// <summary>
/// ⛔ Thrown by <see cref="LicensedConnections"/> when the licence does not permit opening a new database
/// attachment.
///
/// <para>⚠⚠ <b>Its <see cref="Exception.Message"/> is deliberately NOT the sentence the user reads</b>, and
/// that is the whole lesson of the Phase-5 charset defect (design §17.3): there, a perfectly translated
/// resource existed and a Polish user still read English, because the value was wrapped on the way out and
/// the display site read <c>ex.Message</c>. So this carries the <see cref="LicenseVerdict"/> — the DATA — and
/// every display site resolves the words through <see cref="LicenseText.ConnectionRefused"/> at the moment of
/// display. The message below is a developer's breadcrumb for a log or a debugger, in the same class as
/// <c>%TEMP%\EmberTern-debug.log</c>.</para>
///
/// <para>⭐ It is an EXCEPTION rather than a returned result because the seam must be impossible to bypass by
/// forgetting: a call site that does not handle it fails loudly instead of silently opening an attachment the
/// licence forbids. ⛔ Refuse, never repair — the same posture as <c>FirebirdCommandGuard</c>.</para>
/// </summary>
internal sealed class LicenseBlockedException : Exception
{
    internal LicenseBlockedException(LicenseVerdict verdict)
        : base($"A new database connection was refused: the licence verdict is {verdict?.Status}.")
        => Verdict = verdict ?? throw new ArgumentNullException(nameof(verdict));

    /// <summary>⭐ The verdict that refused. The display site turns THIS into words, never the message above.</summary>
    internal LicenseVerdict Verdict { get; }
}

using EmberTern.Core.Localization;

namespace EmberTern.Firebird;

/// <summary>
/// The connection messages EmberTern speaks in its own voice — decision <b>D‑3</b>'s first producer in the
/// <b>Firebird</b> assembly.
///
/// <para>⭐⭐ <b>This is the textbook D‑3 case, and the shape is the point: our sentence is the KEY, the
/// server's sentence is an ARGUMENT.</b> <see cref="Failed"/> resolves to <i>"Could not connect to {0}: {1}"</i>
/// where <c>{1}</c> is whatever Firebird said, verbatim and untranslated. That is how the ratified boundary is
/// kept without a judgement call at each site: the wrapper is ours and is localizable; the engine's words are
/// data and pass through.</para>
///
/// <para>⛔ <b>Nothing here is used to RECOGNISE an error.</b> The <c>Legacy_Auth</c> branch keys on the raw
/// server text, and must keep doing so — matching against a resolved message would compare the server's
/// English to our translated string and silently stop firing the moment anyone ships a second language. That
/// failure would be invisible in English, which is exactly the kind this codebase writes down rather than
/// discovers.</para>
///
/// <para>⚠ Not here, deliberately: <c>FirebirdDiagnostics</c>' output and <c>LogConnectionAttempt</c>'s lines.
/// They go to <c>%TEMP%\EmberTern-debug.log</c> and never to a screen — class E, and translating a developer
/// log would make it harder to compare against a user's report, not easier.</para>
/// </summary>
public static class FirebirdConnectionMessages
{
    /// <summary>Endpoint as <c>{0}</c>, the server's own message as <c>{1}</c> — verbatim.</summary>
    public static readonly MessageKey Failed = new("Firebird.Connection.Failed");

    /// <summary>
    /// The one refusal EmberTern writes itself instead of passing through, with the endpoint as <c>{0}</c>.
    ///
    /// <para>⚠ It is safe to replace the server's text here for a reason recorded in
    /// <c>FirebirdConnectionService.SrpAuthenticationMessage</c>: this wording <b>enumerates what to check</b>
    /// and asserts no cause, so it stays true for every failure the raw message covers. An earlier hint that
    /// named a culprit was removed for misfiring. ⛔ Do not "improve" the translation into a diagnosis.</para>
    /// </summary>
    public static readonly MessageKey SrpAuthentication = new("Firebird.Connection.SrpAuthentication");

    /// <summary>The detected server version as <c>{0}</c>.</summary>
    public static readonly MessageKey UnsupportedServer = new("Firebird.Connection.UnsupportedServer");

    /// <summary>
    /// The same refusal when the driver reported no version at all.
    ///
    /// <para>⚠ A separate key rather than the word <i>"unknown"</i> as an argument, following C1's ratified
    /// shape: substituting a NOUN into a sentence works in English and breaks in a language that inflects,
    /// because the argument cannot know which case the sentence needs. Each sentence the service can utter
    /// gets its own entry; only genuine data travels as an argument.</para>
    /// </summary>
    public static readonly MessageKey UnsupportedServerUnknownVersion =
        new("Firebird.Connection.UnsupportedServerUnknownVersion");
}

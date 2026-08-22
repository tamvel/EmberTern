using System;
using System.Collections.Generic;
using EmberTern.LicenseManager.Localization;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>How loudly a message speaks. Mirrors EmberTern's <c>MessageBanner</c> severities.</summary>
public enum MessageSeverity
{
    /// <summary>Neutral information.</summary>
    Info,

    /// <summary>Something worked.</summary>
    Success,

    /// <summary>Something needs attention but nothing failed.</summary>
    Warning,

    /// <summary>Something failed.</summary>
    Error,
}

/// <summary>
/// The one message surface of the License Manager — as a KEY and its ARGUMENTS, never as finished text.
///
/// <para>⛔ A message is never a loose coloured <c>TextBlock</c> in a view — same rule EmberTern applies
/// to <c>MessageBanner</c>, and for the same reason: a per-host message is a per-host colour decision,
/// and those diverge.</para>
///
/// <para>⭐⭐ <b>L8.2 (ratified decision D‑2 = B) turned this from a sentence into a key.</b> The sentence
/// used to be composed by the view model and stored here, which froze a standing message in whatever
/// language it was raised in: switching the interface language left the strip speaking the old one, with
/// nothing on screen admitting it. Now the key and its arguments are stored and
/// <see cref="Text"/> resolves them AT THE MOMENT OF THE READ, so the strip follows the language like every
/// other word in the window.</para>
///
/// <para>⚠⚠ <b><see cref="Text"/> must stay a computed property.</b> Caching it in a field is the
/// <c>static readonly</c> failure one level in — it renders correctly on first display and never changes
/// again, which is precisely the defect this record was rebuilt to remove (gotcha #397, and the same shape
/// §54.7's first injection caught in <c>LocalizedString</c>).</para>
///
/// <para>⭐ <b>A foreign message travels as an ARGUMENT, never as a key.</b> An <c>IOException</c>'s or an
/// SMTP server's own words are not ours to translate and not ours to key — our sentence is the key and
/// their text is <c>{0}</c>. That is the D‑3 pattern <c>FirebirdConnectionService</c> already uses in the
/// product. ⚠ The converse is just as load-bearing: where the words are OURS — thrown from our own code —
/// the exception carries a <see cref="MessageKey"/> and the catch site resolves it, because handing our own
/// English through <c>ex.Message</c> would freeze it exactly as this record used to freeze everything.</para>
/// </summary>
public sealed record StatusMessage
{
    private static readonly object?[] NoArguments = [];

    private readonly object?[] _arguments;

    /// <summary>Creates a message. ⚠ Prefer the severity-named factories — they read as the sentence does.</summary>
    /// <param name="key">The catalog key. ⛔ Never a sentence — see <see cref="MessageKey"/>.</param>
    /// <param name="severity">How loudly.</param>
    /// <param name="arguments">The values the sentence interpolates, in <c>{0}</c>…<c>{n}</c> order.</param>
    public StatusMessage(MessageKey key, MessageSeverity severity, params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        Key = key;
        Severity = severity;
        _arguments = arguments.Length == 0 ? NoArguments : (object?[])arguments.Clone();
    }

    /// <summary>Which sentence this is.</summary>
    public MessageKey Key { get; }

    /// <summary>How loudly.</summary>
    public MessageSeverity Severity { get; }

    /// <summary>The values the sentence interpolates.</summary>
    /// <remarks>
    /// ⚠ A defensive copy is taken at construction: an argument array mutated afterwards would change a
    /// message that is already on screen, and the change would not be announced to anything.
    /// </remarks>
    public IReadOnlyList<object?> Arguments => _arguments;

    /// <summary>
    /// What the strip says, in the language selected right now.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ Resolved on every read. ⛔ Never cache this — see the type's remarks.
    /// ⭐ Values are formatted under <see cref="Loc.Culture"/>; anything that is an ECHO of a technical
    /// field (an ISO date, a file name, a server's own message) must be handed in already rendered as a
    /// string by its producer, so no format specifier in a resource value can reach it.
    /// </remarks>
    public string Text => Count is { } count
        ? Loc.FormatCount(Key.Value, count, _arguments)
        : Loc.Format(Key.Value, _arguments);

    /// <summary>
    /// The number this sentence agrees with, when its key names a plural FAMILY.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ <b>It exists because Polish needs three forms where English has two</b> (L8.5 / C‑1).
    /// L8.2 wrote the batch results as hand-split <c>…One</c> / <c>…Many</c> keys, which was correct while
    /// the stage was forbidden from changing a single English character — a family would have. A pair
    /// cannot serve <c>one</c> / <c>few</c> / <c>many</c>, so the pair became a family and the strip has to
    /// be able to resolve one.</para>
    /// <para>⚠ The count is ALWAYS argument <c>{0}</c> — <see cref="Loc.FormatCount"/> puts it there, in one
    /// place, so <see cref="Arguments"/> must NOT repeat it. ⛔ Two readers deciding where the number lives
    /// is how a dual form drifts.</para>
    /// <para>⭐ <see langword="null"/> for every ordinary message, which is all but three of them: the
    /// resolution path above is unchanged for those.</para>
    /// </remarks>
    public long? Count { get; private init; }

    /// <summary>Nothing to say.</summary>
    public static StatusMessage? None => null;

    /// <summary>Neutral information.</summary>
    public static StatusMessage Info(MessageKey key, params object?[] arguments) =>
        new(key, MessageSeverity.Info, arguments);

    /// <summary>A sentence that agrees with a number — <paramref name="key"/> names a plural family.</summary>
    /// <remarks>⚠ ⛔ Do not pass the count again in <paramref name="arguments"/>; it is always <c>{0}</c>.</remarks>
    public static StatusMessage Counted(
        MessageKey key, MessageSeverity severity, long count, params object?[] arguments) =>
        new(key, severity, arguments) { Count = count };

    /// <summary>Something worked.</summary>
    public static StatusMessage Success(MessageKey key, params object?[] arguments) =>
        new(key, MessageSeverity.Success, arguments);

    /// <summary>Something needs attention.</summary>
    public static StatusMessage Warning(MessageKey key, params object?[] arguments) =>
        new(key, MessageSeverity.Warning, arguments);

    /// <summary>Something failed.</summary>
    public static StatusMessage Error(MessageKey key, params object?[] arguments) =>
        new(key, MessageSeverity.Error, arguments);

    /// <summary>
    /// A failure whose whole line IS the exception's own sentence — with no wording of ours around it.
    /// </summary>
    /// <remarks>
    /// <para>⭐⭐ <b>The ONE place an exception's message becomes a displayed line.</b> It answers the two
    /// cases differently and that difference is the point: an <see cref="ILocalizedError"/> is OUR sentence,
    /// so its KEY is used and it follows the language; anything else is somebody else's sentence — the
    /// operating system's, the SMTP server's — which is not ours to translate, so it travels as the single
    /// argument of <c>Status.Verbatim</c>.</para>
    /// <para>⚠ <c>Status.Verbatim</c> is deliberately just <c>"{0}"</c>: it is not a sentence and must never
    /// grow into one. The moment a site wants words around a foreign message, those words are a key of their
    /// own with the message as <c>{0}</c> — the shape every other error site here already uses.</para>
    /// <para>⛔ Do not read <c>ex.Message</c> at a display site. That is what
    /// <c>NoViewModel_PutsAnExceptionMessageWhereAKeyBelongs</c> forbids, and how this application ended up
    /// with 23 sites that would have ignored a perfectly translated catalog.</para>
    /// </remarks>
    public static StatusMessage FromError(Exception error, MessageSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error is ILocalizedError ours
            ? new StatusMessage(ours.Key, severity, [.. ours.Arguments])
            : new StatusMessage(StatusCatalog.Verbatim, severity, error.Message);
    }

    // ⛔ There is deliberately no "which style class" member here. The views bind Classes.info /
    //    Classes.success / Classes.warning / Classes.error to the IsX properties on
    //    MessageHostViewModel, so a severity→class member would be a second answer to a question that
    //    already has one — and the one nothing reads is the one that goes wrong unnoticed.
}

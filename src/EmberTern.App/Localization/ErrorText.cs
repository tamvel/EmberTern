using System;
using EmberTern.Firebird;

namespace EmberTern.App.Localization;

/// <summary>
/// ⭐⭐ <b>The ONE place an exception becomes the sentence the user reads.</b>
///
/// <para>
/// <b>The defect this exists to stop, met in Phase 5's manual verification.</b> A layer below the App
/// (Core/Firebird) cannot speak the user's language — by decision D‑3 it hands up a
/// <see cref="EmberTern.Core.Localization.LocalizableMessage"/> and the App resolves it. But such an exception
/// is usually <i>wrapped</i> on the way out: the charset guard's refusal is translated into
/// <see cref="QueryExecutionException"/> / <see cref="DdlExecutionException"/> / <see cref="DataEditException"/>
/// so the existing error surfaces keep working. Those carry a plain <c>string</c>, so reading
/// <c>ex.Message</c> at the display site silently produced <b>an English sentence in a Polish UI</b> — with a
/// green build, green tests, and a fully localized resource entry that nothing ever read.
/// </para>
///
/// <para>
/// ⭐ So the lookup walks the <see cref="Exception.InnerException"/> chain rather than testing the outermost
/// type: whether a refusal arrives bare, wrapped once, or wrapped twice is a detail of the module it passed
/// through, and no display site should have to know.
/// </para>
///
/// <para>
/// ⛔ <b>Do not resolve earlier than display.</b> <c>Loc.Format</c> must run at the moment the text is shown —
/// resolving when the exception is built freezes the sentence in whatever language was current then, and the
/// freeze stays invisible until someone switches language with an error already on screen.
/// </para>
///
/// <para>
/// ⚠ <b>Adding a new localized exception type?</b> Extend <see cref="TryFindLocalized"/> — that is the whole
/// registry. It deliberately pattern-matches concrete types instead of introducing an interface: architecture
/// rule #2 forbids an interface without two implementations, and the existing carriers
/// (<c>ConnectionFailedException</c>, <c>ImportSourceException</c>) already have their own resolved display
/// paths that this must not disturb.
/// </para>
/// </summary>
public static class ErrorText
{
    /// <summary>
    /// What to show the user for <paramref name="exception"/>: the localized sentence when the failure (or
    /// anything it wraps) carries one, otherwise the exception's own message.
    /// <para>
    /// ⚠ The fallback is <see cref="Exception.Message"/> unchanged, so every existing surface keeps exactly
    /// today's behaviour for every failure that is not localized — most notably a raw Firebird server error,
    /// which stays the server's own words in the server's own language (decision D‑3).
    /// </para>
    /// </summary>
    public static string Of(Exception? exception)
    {
        if (exception is null) return string.Empty;

        return TryFindLocalized(exception, out var localized)
            ? Loc.Format(localized)
            : exception.Message;
    }

    /// <summary>
    /// Finds a localizable description on <paramref name="exception"/> or anywhere it wraps.
    /// ⭐ The registry of localized exception types — see the class remarks before extending it.
    /// </summary>
    private static bool TryFindLocalized(
        Exception exception, out EmberTern.Core.Localization.LocalizableMessage localized)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            if (e is CharsetRepresentationException charset)
            {
                localized = charset.Localized;
                return true;
            }
        }

        localized = null!;
        return false;
    }
}

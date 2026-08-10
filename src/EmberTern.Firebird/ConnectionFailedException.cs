using System;
using EmberTern.Core.Localization;

namespace EmberTern.Firebird;

/// <summary>
/// A connection attempt that failed, carrying <b>both</b> a localizable description and an English one.
///
/// <para>⭐ <b>Why both, rather than replacing <see cref="Exception.Message"/>.</b> The App's three connection
/// surfaces read <see cref="Localized"/> and resolve it in the reader's language (D‑3). But an exception is
/// also caught by paths nobody enumerated — a catch-all that logs or shows <c>ex.Message</c> — and those must
/// keep working. Putting a KEY in <c>Message</c> would put a raw identifier in front of whoever hit such a
/// path; leaving English there means an unmigrated path degrades to <b>exactly today's behaviour</b>, never
/// to something worse.</para>
///
/// <para>⚠ The duplication is real and is guarded rather than tolerated: <c>Message</c> must render the same
/// text that <see cref="Localized"/> resolves to in English, and a test pins it. Without that, editing the
/// resource entry alone would silently leave the log speaking an older wording than the screen.</para>
/// </summary>
public sealed class ConnectionFailedException : Exception
{
    /// <param name="localized">What to show the user — a key plus data, resolved by the App.</param>
    /// <param name="message">
    /// The same sentence in English, for logs and for any catch-all that reads <see cref="Exception.Message"/>.
    /// </param>
    public ConnectionFailedException(LocalizableMessage localized, string message, Exception? inner = null)
        : base(message, inner)
    {
        Localized = localized ?? throw new ArgumentNullException(nameof(localized));
    }

    /// <summary>The user-facing description, unresolved. ⭐ Resolve with <c>Loc.Format</c> at the moment of
    /// display — never earlier, or the text freezes in the language that was current when the connection
    /// failed.</summary>
    public LocalizableMessage Localized { get; }
}

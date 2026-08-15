using System;
using System.Text;

namespace EmberTern.Core.Connections;

/// <summary>
/// Where a piece of text stops being representable in a connection charset — the character, and where it is.
/// <para>
/// ⭐ A <c>bool</c> would have been enough to REFUSE, and useless to the user. The whole point of refusing
/// instead of repairing is that the user then has to fix something, so the message has to say what and where.
/// </para>
/// <para>
/// <see cref="Text"/> is a string rather than a <c>char</c> because a character outside the BMP is two UTF-16
/// code units, and reporting half a surrogate pair would print a replacement box in the very message that is
/// supposed to identify the problem.
/// </para>
/// </summary>
/// <param name="Text">The offending character, whole (one or two UTF-16 code units).</param>
/// <param name="Index">Its zero-based index in the inspected string, in UTF-16 code units.</param>
public readonly record struct CharsetViolation(string Text, int Index)
{
    /// <summary>The Unicode scalar in <c>U+XXXX</c> form — the half of the message that survives a font that
    /// cannot draw the character, which is the likely case for a character the charset cannot carry either.</summary>
    public string CodePoint =>
        Text.Length == 0 ? "U+????" : $"U+{char.ConvertToUtf32(Text, 0):X4}";
}

/// <summary>
/// ⭐⭐ <b>The ONE answer to "can this connection carry this text without changing it".</b>
///
/// <para>
/// <b>The failure this exists to stop.</b> Measured on FirebirdClient 10.3.4 + Firebird 5: a character absent
/// from the CONNECTION charset is destroyed <b>client-side, inside the driver's encoder, before the server ever
/// sees it</b>. The server therefore cannot help — it receives a byte sequence that is perfectly valid in the
/// declared charset and faithfully stores it. There is no exception, no warning, and no server error. The same
/// loss was measured on all three write paths (bound parameter, SQL literal in statement text, and DDL text
/// landing in <c>RDB$PROCEDURE_SOURCE</c>), and the third is architecture rule #11: EmberTern rewriting the
/// user's own source code.
/// </para>
///
/// <para>
/// ⚠⚠ <b>The symptom is NOT "it turns into <c>?</c>", and a guard written for that would miss the worst
/// cases.</b> The single-byte code pages the driver uses carry <c>InternalEncoderBestFitFallback</c>, which
/// substitutes the closest PLAUSIBLE character. Measured over U+0020–U+2FFF for WIN1250: 11 702 characters
/// become <c>?</c>, and <b>330 become a different, ordinary-looking character</c></b> — <c>£</c>→<c>L</c>,
/// <c>¼</c>→<c>1</c>, <c>À</c>→<c>A</c>, <c>²</c>→<c>2</c>. Live, a procedure body sent as
/// <c>R = 'Cena £100 ¼ À'</c> was stored as <c>R = 'Cena L100 1 A'</c>: it compiles, it reads as correct code,
/// and it is a different number. A <c>?</c> at least looks wrong.
/// </para>
///
/// <para>
/// ⭐ <b><see cref="EncoderFallback.ExceptionFallback"/> is the whole mechanism</b>, and it was verified rather
/// than assumed: over U+0020–U+2FFF it flagged <b>12 032 of the 12 032</b> characters the ordinary encoder
/// damages, with <b>zero</b> misses and <b>zero</b> false positives against the 224 it represents exactly. It
/// covers the best-fit class and the <c>?</c> class alike, because best-fit IS the fallback it replaces.
/// </para>
///
/// <para>
/// ⛔ <b>This detects; it never repairs.</b> Substituting, escaping or silently switching charset would all be
/// changing the user's data without asking, which is the defect, not the fix. Rule #11: uncertainty ⇒ do
/// nothing, or ask.
/// </para>
/// </summary>
public static class CharsetRepresentation
{
    /// <summary>
    /// The connection charset as an encoding that THROWS on an unrepresentable character instead of quietly
    /// substituting one. Resolution goes through <see cref="CharsetCatalog.ResolveWireEncoding"/> — the
    /// "what will the driver put on the wire" question, ⛔ never <see cref="CharsetCatalog.Resolve"/>, whose
    /// <c>NONE</c> answer would disable the check entirely.
    /// </summary>
    public static Encoding Strict(string? firebirdCharset)
        => StrictFor(CharsetCatalog.ResolveWireEncoding(firebirdCharset));

    /// <summary>
    /// The strict form of an already-resolved wire encoding. UTF-8 is returned unchanged: it represents every
    /// character, so the check can never fire, and <see cref="CanRepresent"/> takes a fast path on it.
    /// </summary>
    public static Encoding StrictFor(Encoding wireEncoding)
    {
        ArgumentNullException.ThrowIfNull(wireEncoding);

        if (wireEncoding.CodePage == Encoding.UTF8.CodePage) return wireEncoding;

        return Encoding.GetEncoding(
            wireEncoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>
    /// True when every character of <paramref name="text"/> survives <paramref name="strictEncoding"/> unchanged.
    /// <para>
    /// The bulk <c>GetByteCount</c> is deliberately one call rather than a per-character loop: measured at
    /// <b>0.21 µs</b> for a typical bound parameter (1 000 000 of them = 36 ms), <b>1.8 µs</b> for a ~2 KB F5
    /// statement and <b>110 µs</b> for a 78 KB procedure body — invisible beside the round-trip that follows.
    /// Locating the offender costs more, so <see cref="FindFirstUnrepresentable"/> only runs after this says no.
    /// </para>
    /// </summary>
    /// <param name="strictEncoding">An encoding from <see cref="Strict"/>. <c>null</c> skips the check.</param>
    public static bool CanRepresent(string? text, Encoding? strictEncoding)
    {
        if (string.IsNullOrEmpty(text) || strictEncoding is null) return true;
        if (strictEncoding.CodePage == Encoding.UTF8.CodePage) return true;

        try
        {
            strictEncoding.GetByteCount(text);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// The first character of <paramref name="text"/> that <paramref name="strictEncoding"/> cannot carry, and
    /// its position — or <c>null</c> when the text is entirely representable.
    /// <para>
    /// ⚠ Runs the fast bulk check first and only walks the string when that fails, so the per-code-point loop
    /// costs nothing on the overwhelmingly common success path.
    /// </para>
    /// </summary>
    public static CharsetViolation? FindFirstUnrepresentable(string? text, Encoding? strictEncoding)
    {
        if (CanRepresent(text, strictEncoding)) return null;

        var value = text!;
        for (var i = 0; i < value.Length;)
        {
            // Step by CODE POINT: a surrogate pair is one character and must be tested — and reported — whole.
            var width = char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])
                ? 2
                : 1;
            var element = value.Substring(i, width);

            try
            {
                strictEncoding!.GetByteCount(element);
            }
            catch (EncoderFallbackException)
            {
                return new CharsetViolation(element, i);
            }

            i += width;
        }

        // CanRepresent said no, so the walk must find it. Reaching here would mean the two disagree.
        return new CharsetViolation(string.Empty, 0);
    }
}

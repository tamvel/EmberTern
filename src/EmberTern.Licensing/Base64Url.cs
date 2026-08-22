using System;

namespace EmberTern.Licensing;

/// <summary>
/// Unpadded base64url (RFC 4648 §5) — the encoding both segments of an ETL1 artifact use.
///
/// <para>⭐ <b>Why this is hand-written rather than a call to the BCL's <c>Base64Url</c> helper.</b> The
/// decoder on this path is a security boundary: it is the first thing that touches attacker-supplied text,
/// and it must be <i>strict</i>. A lenient decoder that skips whitespace, tolerates padding or accepts the
/// standard base64 alphabet would let two different texts decode to the same bytes, which is precisely the
/// class of ambiguity that produces signature-confusion bugs in token formats. The rules below are
/// therefore explicit and testable, and the transform itself still goes through <see cref="Convert"/>.</para>
///
/// <para>⛔ Rejected here, deliberately: any character outside <c>A–Z a–z 0–9 - _</c>, any <c>=</c> padding,
/// any whitespace (the caller strips it once, in <see cref="LicenseArmor"/>, so a second lenient pass
/// cannot disagree with the first), and a length that is <c>1 mod 4</c> — which no base64 output can have.</para>
/// </summary>
internal static class Base64Url
{
    /// <summary>Encodes without padding, using the URL-safe alphabet.</summary>
    internal static string Encode(ReadOnlySpan<byte> bytes)
    {
        var standard = Convert.ToBase64String(bytes);

        // Trim the padding first so the span walk below has nothing to skip.
        var end = standard.Length;
        while (end > 0 && standard[end - 1] == '=')
        {
            end--;
        }

        return string.Create(end, standard, static (destination, source) =>
        {
            for (var i = 0; i < destination.Length; i++)
            {
                destination[i] = source[i] switch
                {
                    '+' => '-',
                    '/' => '_',
                    var c => c,
                };
            }
        });
    }

    /// <summary>
    /// Strict decode. Returns <see langword="false"/> for anything that is not exactly unpadded base64url;
    /// it never throws and never repairs.
    /// </summary>
    internal static bool TryDecode(string text, out byte[] bytes)
    {
        bytes = [];

        if (text.Length == 0 || text.Length % 4 == 1)
        {
            return false;
        }

        // Rebuild in the standard alphabet, validating every character on the way. Anything not in the
        // base64url alphabet — including '=' and whitespace — fails here rather than being skipped.
        var padding = (4 - (text.Length % 4)) % 4;
        var buffer = new char[text.Length + padding];

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsAsciiLetterOrDigit(c))
            {
                buffer[i] = c;
            }
            else if (c == '-')
            {
                buffer[i] = '+';
            }
            else if (c == '_')
            {
                buffer[i] = '/';
            }
            else
            {
                return false;
            }
        }

        for (var i = text.Length; i < buffer.Length; i++)
        {
            buffer[i] = '=';
        }

        var decoded = new byte[buffer.Length / 4 * 3];
        if (!Convert.TryFromBase64Chars(buffer, decoded, out var written))
        {
            return false;
        }

        bytes = decoded.Length == written ? decoded : decoded[..written];
        return true;
    }
}

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace EmberTern.Licensing;

/// <summary>
/// The <c>-----BEGIN EMBERTERN LICENSE-----</c> wrapper around an ETL1 token.
///
/// <para>⭐ <b>The armor is functional, not decorative.</b> A licence travels by e-mail, and mail clients
/// wrap long lines. A bare 450-character token pasted out of a message body arrives broken. The wrapper
/// plus <see cref="TryUnwrap"/>'s whitespace-stripping makes that harmless, and it is the same
/// convention PEM has used for decades — it copies, pastes and quotes safely.</para>
///
/// <para>⛔ <b>Nothing human-readable goes inside the wrapper.</b> An unsigned <c>Valid until: 2099</c>
/// line would be a misinformation channel for the customer and for support. The rule is absolute:
/// <b>nothing in the file may assert something the signature does not cover.</b></para>
///
/// <para>⚠ Text before <c>BEGIN</c> and after <c>END</c> is ignored on purpose — that is the greeting and
/// the mail signature around a pasted licence. Text with no markers at all is treated as a bare token, so
/// pasting either form works.</para>
/// </summary>
public static class LicenseArmor
{
    /// <summary>Opening marker.</summary>
    public const string BeginMarker = "-----BEGIN EMBERTERN LICENSE-----";

    /// <summary>Closing marker.</summary>
    public const string EndMarker = "-----END EMBERTERN LICENSE-----";

    /// <summary>
    /// Characters per body line. 64 is PEM's number; the point is only that it is well under any mail
    /// client's wrap width.
    /// </summary>
    public const int LineLength = 64;

    /// <summary>
    /// The line separator written into an armored artifact. ⭐ Fixed as CRLF rather than
    /// <see cref="Environment.NewLine"/>: the output is a file that travels between machines and through
    /// mail transports, so it must not depend on the platform that produced it — and a fixed separator is
    /// what lets a test assert the exact bytes.
    /// </summary>
    public const string LineSeparator = "\r\n";

    /// <summary>Wraps a bare ETL1 token into the armored form written to <c>EmberTern.etlic</c>.</summary>
    public static string Wrap(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var builder = new StringBuilder(token.Length + (token.Length / LineLength + 4) * 34);
        builder.Append(BeginMarker).Append(LineSeparator);

        for (var offset = 0; offset < token.Length; offset += LineLength)
        {
            var length = Math.Min(LineLength, token.Length - offset);
            builder.Append(token, offset, length).Append(LineSeparator);
        }

        builder.Append(EndMarker).Append(LineSeparator);
        return builder.ToString();
    }

    /// <summary>
    /// Recovers the bare token from armored text, a bare token, or either of those mangled by line
    /// wrapping. Never throws and never repairs anything beyond removing whitespace.
    /// </summary>
    public static bool TryUnwrap(
        string? text,
        [NotNullWhen(true)] out string? token,
        out LicenseFailure failure)
    {
        token = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            failure = LicenseFailure.NotALicense;
            return false;
        }

        var begin = text.IndexOf(BeginMarker, StringComparison.Ordinal);
        var end = text.IndexOf(EndMarker, StringComparison.Ordinal);

        string body;
        if (begin >= 0)
        {
            // ⛔ A second BEGIN means two licences in one file, and picking one of them would be a guess
            // about which the user meant. Refuse instead — the caller can show them the file.
            if (text.IndexOf(BeginMarker, begin + BeginMarker.Length, StringComparison.Ordinal) >= 0 ||
                end < 0 ||
                end < begin ||
                text.IndexOf(EndMarker, end + EndMarker.Length, StringComparison.Ordinal) >= 0)
            {
                failure = LicenseFailure.MalformedArmor;
                return false;
            }

            var start = begin + BeginMarker.Length;
            body = text[start..end];
        }
        else if (end >= 0)
        {
            // A closing marker with nothing opening it: truncated in transit, or hand-edited.
            failure = LicenseFailure.MalformedArmor;
            return false;
        }
        else
        {
            body = text;
        }

        var stripped = StripWhitespace(body);
        if (stripped.Length == 0)
        {
            failure = LicenseFailure.NotALicense;
            return false;
        }

        token = stripped;
        failure = LicenseFailure.None;
        return true;
    }

    private static string StripWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

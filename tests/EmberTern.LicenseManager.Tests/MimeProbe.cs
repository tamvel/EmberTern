using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using EmberTern.LicenseManager.Email;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// A real RFC 5322 round trip for a composed message: write it as an <c>.eml</c>, then read it back.
///
/// <para>⭐⭐ <b>It exists so the L6.2 guards can be about TRANSPORT rather than about strings in memory.</b>
/// A licence that survives composition but not base64 encoding, header folding or transfer encoding is a
/// licence the customer cannot use — and nothing in a value-level assertion would show it.</para>
///
/// <para>⭐⭐ <b>Since L6.3 it writes through the PRODUCTION file sender</b>
/// (<see cref="EmlFileEmailSender"/>, which shares its construction with the SMTP sender). ⚠ Before that
/// it built its own message, so the round-trip guards proved something about the test's own construction;
/// now they watch the application's. ⛔ Still no socket is opened — the file sender contacts nothing.</para>
///
/// <para>⚠ The reader is a MINIMAL decoder, not a MIME parser: the BCL writes MIME but cannot read it, and
/// the file being read is one this test just produced. It answers two questions and no others — what a
/// header says, and what bytes an attachment carries.</para>
/// </summary>
internal static class MimeProbe
{
    /// <summary>Writes a composed licence message as an <c>.eml</c> and returns its path.</summary>
    internal static string Write(LicenseMessage message, string folder) =>
        Write(OutgoingEmail.ForLicence(message), folder);

    /// <summary>
    /// Writes any outgoing message as an <c>.eml</c> and returns its path.
    ///
    /// <para>SINCE L6.3 THIS GOES THROUGH THE PRODUCTION SENDER. It used to build its own
    /// <c>MailMessage</c>, which meant the round-trip guards proved something about the TEST's
    /// construction rather than about the application's. Now the bytes examined below are the bytes
    /// <see cref="EmlFileEmailSender"/> writes and <see cref="SmtpLicenseEmailSender"/> puts on the wire
    /// - one builder, and the guards watch it.</para>
    ///
    /// <para>The wait is not sync-over-async in any meaningful sense: the file sender completes
    /// synchronously (its work is a file write) and returns an already-completed task.</para>
    /// </summary>
    internal static string Write(OutgoingEmail email, string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "message" + EmlFileEmailSender.FileExtension);

        var outcome = new EmlFileEmailSender(path)
            .SendAsync(email)
            .GetAwaiter()
            .GetResult();

        return outcome.Sent
            ? path
            : throw new InvalidOperationException($"The probe could not write the message: {outcome.Error}");
    }

    /// <summary>
    /// One header's value, unfolded and with RFC 2047 encoded-words decoded back to text.
    ///
    /// <para>⭐ The decoding is the point of the guard that uses this: a Polish company name travels as
    /// <c>=?utf-8?B?…?=</c>, and "it arrived" means "it decodes to exactly what was composed".</para>
    /// </summary>
    internal static string Header(string emlPath, string name)
    {
        var lines = File.ReadAllLines(emlPath, Encoding.UTF8);
        var value = new StringBuilder();
        var found = false;

        foreach (var line in lines)
        {
            if (found)
            {
                // A folded continuation begins with whitespace; anything else ends the header.
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                {
                    value.Append(line.TrimStart());
                    continue;
                }

                break;
            }

            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                value.Append(line[(name.Length + 1)..].Trim());
            }
            else if (line.Length == 0)
            {
                break; // End of the header block.
            }
        }

        return found
            ? DecodeEncodedWords(value.ToString())
            : throw new InvalidOperationException($"The message carries no '{name}' header.");
    }

    /// <summary>The bytes of the attachment whose file name is <paramref name="fileName"/>.</summary>
    internal static byte[] Attachment(string emlPath, string fileName) =>
        PartContent(
            emlPath,
            line => line.StartsWith("Content-", StringComparison.Ordinal) &&
                    line.Contains(fileName, StringComparison.Ordinal),
            $"an attachment named '{fileName}'");

    /// <summary>The decoded plain-text body — what a client that strips HTML would show.</summary>
    internal static string TextBody(string emlPath) =>
        Encoding.UTF8.GetString(PartContent(
            emlPath,
            line => line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("text/plain", StringComparison.OrdinalIgnoreCase),
            "a plain-text body"));

    /// <summary>The decoded HTML body.</summary>
    internal static string HtmlBody(string emlPath) =>
        Encoding.UTF8.GetString(PartContent(
            emlPath,
            line => line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("text/html", StringComparison.OrdinalIgnoreCase),
            "an HTML body"));

    // The content of the first part whose header block contains a line matching `header`, decoded.
    // ⚠ Base64 only, and it REFUSES anything else rather than guessing — see Write.
    private static byte[] PartContent(string emlPath, Func<string, bool> header, string what)
    {
        var lines = File.ReadAllLines(emlPath, Encoding.UTF8);

        var match = Array.FindIndex(lines, line => header(line));
        if (match < 0)
        {
            throw new InvalidOperationException($"The message carries no {what}.");
        }

        // The part's header block runs from the boundary above the match to the first blank line below it.
        var start = match;
        while (start > 0 && !lines[start - 1].StartsWith("--", StringComparison.Ordinal))
        {
            start--;
        }

        var blank = Array.FindIndex(lines, match, string.IsNullOrEmpty);
        var encoding = lines[start..blank]
            .FirstOrDefault(l => l.StartsWith("Content-Transfer-Encoding:", StringComparison.OrdinalIgnoreCase));

        if (encoding is null || !encoding.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The part holding {what} is not base64 ({encoding ?? "no Content-Transfer-Encoding"}). " +
                "The probe decodes base64 only, on purpose — see MimeProbe.Write.");
        }

        var content = new StringBuilder();
        for (var i = blank + 1; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("--", StringComparison.Ordinal))
            {
                break; // The next boundary ends the part.
            }

            content.Append(lines[i].Trim());
        }

        return Convert.FromBase64String(content.ToString());
    }

    // ⚠ Handles the two encodings .NET actually emits for a UTF-8 header, and adjacent encoded-words
    //    separated by folding whitespace (RFC 2047 §6.2 — that whitespace is not part of the value).
    private static string DecodeEncodedWords(string value)
    {
        var joined = Regex.Replace(value, @"\?=\s+=\?", "?==?");

        return Regex.Replace(
            joined,
            @"=\?(?<charset>[^?]+)\?(?<kind>[BbQq])\?(?<text>[^?]*)\?=",
            match =>
            {
                var encoding = Encoding.GetEncoding(match.Groups["charset"].Value);
                var text = match.Groups["text"].Value;

                return match.Groups["kind"].Value is "B" or "b"
                    ? encoding.GetString(Convert.FromBase64String(text))
                    : encoding.GetString(DecodeQuotedPrintable(text));
            });
    }

    private static byte[] DecodeQuotedPrintable(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '_':
                    bytes.Add((byte)' ');
                    break;
                case '=' when i + 2 < text.Length:
                    bytes.Add(Convert.ToByte(text.Substring(i + 1, 2), 16));
                    i += 2;
                    break;
                default:
                    bytes.Add((byte)text[i]);
                    break;
            }
        }

        return [.. bytes];
    }
}

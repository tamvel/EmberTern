using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace EmberTern.Core.Settings.Export;

/// <summary>
/// The parsed cleartext header of an export file. Every field is described in
/// <see cref="SettingsExportFormat"/>; the one to remember is that <see cref="AppVersion"/> is
/// <b>diagnostics only and must never be branched on</b>.
/// </summary>
/// <param name="FormatVersion">The migration contract — see <see cref="SettingsExportFormat.CurrentFormatVersion"/>.</param>
/// <param name="AppVersion">Which EmberTern wrote the file. ⛔ Never a condition.</param>
/// <param name="EncryptionScheme">One of <c>EmberTern.Core.Security.EncryptionSchemes</c>.</param>
/// <param name="Kdf">Key-derivation identifier — see <c>PassphraseProtector.Pbkdf2Sha256</c>.</param>
/// <param name="Iterations">KDF iteration count as the writer used it. Read, never assumed.</param>
/// <param name="Salt">Per-file random salt. Not secret; without it the key cannot be re-derived.</param>
public readonly record struct SettingsExportHeader(
    int FormatVersion,
    string AppVersion,
    string EncryptionScheme,
    string Kdf,
    int Iterations,
    byte[] Salt);

/// <summary>How reading an export file's header ended. Distinct from a whole import's outcome
/// (<see cref="SettingsImportStatus"/>) because this layer knows nothing about passphrases or versions.</summary>
public enum SettingsExportHeaderOutcome
{
    /// <summary>A well-formed header was read. Says nothing yet about whether we can honour it.</summary>
    Ok,

    /// <summary>The magic did not match. Not our file — a ZIP, a PDF, a <c>settings.dat</c>, a text file.</summary>
    NotAnExportFile,

    /// <summary>The magic matched but the rest of the line is not a header we can parse: damage, not mistaken
    /// identity. ⭐ The distinction is worth keeping — "you picked the wrong file" and "your export file is
    /// broken" call for different next actions.</summary>
    MalformedHeader,
}

/// <summary>
/// The on-disk envelope of an export: <b>one cleartext header line, then the encrypted payload verbatim.</b>
/// Deliberately the same shape as <c>SettingsFileContainer</c> — an established pattern extended, not a new one
/// invented — with the export's own magic (Q13) and the KDF parameters that a passphrase-derived key needs.
///
/// <code>
/// EMBERTERN-SETTINGS-EXPORT&lt;TAB&gt;1&lt;TAB&gt;&lt;appVersion&gt;&lt;TAB&gt;aes256-passphrase&lt;TAB&gt;PBKDF2-SHA256&lt;TAB&gt;600000&lt;TAB&gt;&lt;saltBase64&gt;\n
/// &lt;payload exactly as the protector produced it&gt;
/// </code>
///
/// <para><b>⭐ The header is cleartext and the payload encrypted, and that is not a compromise of "always
/// encrypted" — it is what makes versioning work at all.</b> If the whole file were opaque, a future build could
/// not tell "this is a v1 export, migrate it" from "wrong passphrase" from "corrupt". It would have to infer the
/// structure after decrypting, which is exactly the guessing this design exists to eliminate. The version has to
/// be readable <i>before</i> the passphrase is applied.</para>
///
/// <para>⚠ <b>The section list is NOT in the header.</b> It is tempting, so an import can preview contents before
/// asking for a passphrase — but a cleartext <i>"contains: Connections, Passwords"</i> advertises what is worth
/// attacking. It lives in the encrypted payload (<see cref="SettingsExportContent"/>).</para>
/// </summary>
public static class SettingsExportEnvelope
{
    private const char Separator = '\t';
    private const byte Newline = (byte)'\n';

    // Minimum: magic, format version, app version, scheme, kdf, iterations, salt. Extra trailing fields are
    // tolerated and ignored — reserved for forward-compatible header additions, the same licence
    // SettingsFileContainer.TryParse grants.
    private const int MinimumHeaderFields = 7;

    /// <summary>Composes the file: header line, newline, payload verbatim.</summary>
    /// <exception cref="ArgumentException">A header field contains the field separator or a newline, which would
    /// silently corrupt the line. Defensive: the real values (an assembly version, a scheme constant, Base64) all
    /// pass, and a caller that invents its own should fail loudly rather than write an unparseable file.</exception>
    public static string Wrap(SettingsExportHeader header, string payload)
    {
        var appVersion = header.AppVersion ?? string.Empty;
        RejectSeparators(appVersion, nameof(header.AppVersion));
        RejectSeparators(header.EncryptionScheme, nameof(header.EncryptionScheme));
        RejectSeparators(header.Kdf, nameof(header.Kdf));

        var salt = Convert.ToBase64String(header.Salt ?? Array.Empty<byte>());
        var iterations = header.Iterations.ToString(CultureInfo.InvariantCulture);
        var version = header.FormatVersion.ToString(CultureInfo.InvariantCulture);

        return string.Join(Separator,
                   SettingsExportFormat.Magic, version, appVersion,
                   header.EncryptionScheme, header.Kdf, iterations, salt)
               + "\n" + payload;
    }

    /// <summary>
    /// Reads and parses <b>only the header</b>, straight off <paramref name="stream"/>, leaving it positioned at
    /// the first byte of the payload.
    ///
    /// <para>⭐ <b>The magic is compared as BYTES, before anything is decoded or loaded.</b> Both halves matter. A
    /// ZIP begins <c>PK\x03\x04</c> and a PDF <c>%PDF-</c>; read as text those produce replacement characters or
    /// throw, depending on the path taken — and a crash here would be the one failure mode worse than the unclear
    /// message the magic replaced. And reading from the stream rather than after a <c>ReadAllText</c> is what
    /// makes rejecting an accidentally-picked huge file cost bytes instead of a full read.</para>
    /// </summary>
    public static SettingsExportHeaderOutcome TryReadHeader(Stream stream, out SettingsExportHeader header)
    {
        header = default;
        if (stream is null)
        {
            return SettingsExportHeaderOutcome.NotAnExportFile;
        }

        var magic = Encoding.UTF8.GetBytes(SettingsExportFormat.Magic);
        var lead = new byte[magic.Length];
        if (!TryFill(stream, lead) || !AreEqual(lead, magic))
        {
            return SettingsExportHeaderOutcome.NotAnExportFile;
        }

        // Identity is settled. From here every failure is MalformedHeader: this is our file and it is damaged.
        if (!TryReadRestOfLine(stream, magic.Length, out var rest))
        {
            return SettingsExportHeaderOutcome.MalformedHeader;
        }

        // The magic must be a whole token, not a prefix: without this, a first line reading
        // "EMBERTERN-SETTINGS-EXPORTX<TAB>1<TAB>…" would parse as ours.
        if (rest.Length == 0 || rest[0] != Separator)
        {
            return SettingsExportHeaderOutcome.MalformedHeader;
        }

        var fields = rest.Split(Separator);
        // fields[0] is the empty remainder of the magic token, so the separator-delimited count matches Wrap's.
        if (fields.Length < MinimumHeaderFields)
        {
            return SettingsExportHeaderOutcome.MalformedHeader;
        }

        if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var formatVersion)
            || !int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var iterations))
        {
            return SettingsExportHeaderOutcome.MalformedHeader;
        }

        byte[] salt;
        try
        {
            salt = Convert.FromBase64String(fields[6]);
        }
        catch (FormatException)
        {
            return SettingsExportHeaderOutcome.MalformedHeader;
        }

        header = new SettingsExportHeader(
            formatVersion, fields[2], fields[3], fields[4], iterations, salt);
        return SettingsExportHeaderOutcome.Ok;
    }

    /// <summary>Reads the payload that follows a header <see cref="TryReadHeader"/> has just consumed.</summary>
    public static string ReadPayload(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static void RejectSeparators(string? value, string field)
    {
        if (value is not null
            && (value.IndexOf(Separator) >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0))
        {
            throw new ArgumentException($"Header field '{field}' may not contain a tab or a newline.", field);
        }
    }

    // Reads to the header's terminating newline, capped so a file with no newline at all cannot be pulled into
    // memory whole. `consumed` is what the magic already took off the cap.
    private static bool TryReadRestOfLine(Stream stream, int consumed, out string rest)
    {
        var buffer = new MemoryStream();
        var budget = SettingsExportFormat.MaxHeaderBytes - consumed;

        while (budget-- > 0)
        {
            var next = stream.ReadByte();
            if (next < 0)
            {
                rest = string.Empty;
                return false; // magic matched but the file ends inside the header line
            }
            if (next == Newline)
            {
                var line = Encoding.UTF8.GetString(buffer.ToArray());
                // Defensive: tolerate a stray CR even though we always write a bare \n (a header line that has
                // been through a text editor or a CRLF-translating transfer).
                rest = line.EndsWith('\r') ? line[..^1] : line;
                return true;
            }
            buffer.WriteByte((byte)next);
        }

        rest = string.Empty;
        return false;
    }

    private static bool TryFill(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var got = stream.Read(buffer, read, buffer.Length - read);
            if (got <= 0)
            {
                return false; // shorter than the magic — cannot be our file
            }
            read += got;
        }
        return true;
    }

    private static bool AreEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }
        return true;
    }
}

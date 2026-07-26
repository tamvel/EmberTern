using System;
using System.Text;

namespace EmberTern.Core.Import.Providers;

/// <summary>Why an encoding was proposed — the evidence the UI shows beside the proposal (§0.4).</summary>
public enum EncodingDetectionBasis
{
    /// <summary>A byte-order mark said so. The only certain case.</summary>
    ByteOrderMark,

    /// <summary>The bytes decode as strictly valid UTF-8 AND contain at least one multi-byte sequence — that
    /// combination is very unlikely to happen by accident in a single-byte file.</summary>
    ValidUtf8MultiByte,

    /// <summary>Every byte is 7-bit ASCII, so every candidate charset decodes identically. The proposal is the
    /// caller's default, and the honest statement is that the file does not distinguish them.</summary>
    AsciiOnly,

    /// <summary>The bytes are NOT valid UTF-8, so a single-byte charset is the only reading that can work.</summary>
    NotValidUtf8,
}

/// <summary>An encoding proposal plus its basis.</summary>
/// <param name="CharsetName">A name <c>CharsetCatalog.Resolve</c> understands (<c>UTF8</c>, <c>WIN1250</c>,
/// <c>UTF16LE</c>, <c>UTF16BE</c>).</param>
public sealed record EncodingProposal(string CharsetName, EncodingDetectionBasis Basis);

/// <summary>
/// Proposes the character encoding of a text source: byte-order mark first, then a strict-UTF-8 test, then the
/// caller's single-byte default.
/// <para>
/// It proposes and explains; it never decides silently (§0.4). The order matters and each step is falsifiable:
/// a BOM is proof, valid-UTF-8-with-multi-byte-sequences is strong evidence, all-ASCII is an explicit
/// „the file cannot tell us", and invalid UTF-8 rules UTF-8 out entirely.
/// </para>
/// <para>
/// Deliberately NOT a charset-guessing library: no n-gram or language statistics. Distinguishing WIN1250 from
/// WIN1252 by content is guesswork, and a wrong guess here corrupts every accented character — so that choice
/// stays with the user, who knows where the file came from.
/// </para>
/// </summary>
public static class EncodingDetector
{
    /// <summary>Bytes inspected when no BOM is present. A file's encoding does not change halfway, and reading
    /// megabytes to answer this would defeat the streaming design.</summary>
    public const int SampleBytes = 64 * 1024;

    /// <summary>
    /// Proposes an encoding for <paramref name="sample"/> (the file's first bytes).
    /// <paramref name="singleByteDefault"/> is used when the sample cannot distinguish candidates — normally the
    /// connection profile's charset, so the proposal matches the environment the user actually works in.
    /// </summary>
    public static EncodingProposal Propose(ReadOnlySpan<byte> sample, string singleByteDefault = "WIN1250")
    {
        if (TryReadBom(sample, out var bomCharset))
        {
            return new EncodingProposal(bomCharset, EncodingDetectionBasis.ByteOrderMark);
        }

        var span = sample.Length > SampleBytes ? sample[..SampleBytes] : sample;

        if (IsAsciiOnly(span))
        {
            return new EncodingProposal(singleByteDefault, EncodingDetectionBasis.AsciiOnly);
        }

        return IsStrictUtf8(span)
            ? new EncodingProposal("UTF8", EncodingDetectionBasis.ValidUtf8MultiByte)
            : new EncodingProposal(singleByteDefault, EncodingDetectionBasis.NotValidUtf8);
    }

    /// <summary>How many leading bytes are a byte-order mark for <paramref name="charsetName"/> — the count the
    /// reader must skip so the mark never turns up as data in the first field.</summary>
    public static int ByteOrderMarkLength(string? charsetName) => (charsetName ?? "").ToUpperInvariant() switch
    {
        "UTF8" => 3,
        "UTF16LE" or "UTF16BE" => 2,
        _ => 0,
    };

    private static bool TryReadBom(ReadOnlySpan<byte> sample, out string charsetName)
    {
        if (sample.Length >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
        {
            charsetName = "UTF8";
            return true;
        }
        if (sample.Length >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
        {
            charsetName = "UTF16LE";
            return true;
        }
        if (sample.Length >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
        {
            charsetName = "UTF16BE";
            return true;
        }
        charsetName = string.Empty;
        return false;
    }

    private static bool IsAsciiOnly(ReadOnlySpan<byte> sample)
    {
        foreach (var b in sample)
        {
            if (b > 0x7F) return false;
        }
        return true;
    }

    // Strict UTF-8: the decoder is configured to THROW on an invalid sequence rather than substitute U+FFFD.
    // The same discipline the import validator uses for the connection charset — a replacement fallback is how
    // you get a confident wrong answer instead of a detectable failure (design R1, measured in I0).
    private static bool IsStrictUtf8(ReadOnlySpan<byte> sample)
    {
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            // A sample can cut a multi-byte sequence in half, which is not evidence of a non-UTF-8 file. Trim
            // the trailing continuation bytes before deciding.
            var trimmed = TrimIncompleteTrailingSequence(sample);
            strict.GetString(trimmed);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static ReadOnlySpan<byte> TrimIncompleteTrailingSequence(ReadOnlySpan<byte> sample)
    {
        // Walk back over continuation bytes (10xxxxxx) to the lead byte, and drop the run when it is shorter
        // than the length its lead byte declares.
        int i = sample.Length - 1;
        int continuations = 0;
        while (i >= 0 && (sample[i] & 0xC0) == 0x80)
        {
            continuations++;
            i--;
            if (continuations > 3) return sample; // Not a valid sequence at all; let the decoder judge it.
        }
        if (i < 0) return sample;

        var lead = sample[i];
        int declared = lead switch
        {
            >= 0xF0 => 4,
            >= 0xE0 => 3,
            >= 0xC0 => 2,
            _ => 1,
        };
        if (declared == 1) return sample;

        return continuations + 1 < declared ? sample[..i] : sample;
    }
}

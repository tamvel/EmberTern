using System.Text;
using EmberTern.Core.Connections;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I1: the delimiter and encoding detectors.
/// <para>
/// Both only ever PROPOSE (§0.4), so what these tests pin is not just the answer but the evidence: a proposal
/// that cannot say why it was made is a silent decision, which is the thing the design forbids.
/// </para>
/// </summary>
public class ImportDetectorTests
{
    private static readonly DelimitedOptions Defaults = new();

    // ── Delimiter ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Proposes_Semicolon_ForAPolishCsv()
    {
        var proposal = DelimiterDetector.Propose("a;b;c\n1;2;3\n4;5;6", Defaults);

        Assert.NotNull(proposal);
        Assert.Equal(';', proposal!.Delimiter);
        Assert.Equal(3, proposal.FieldCount);
        Assert.True(proposal.IsUnanimous);
    }

    [Fact]
    public void Proposes_Comma()
    {
        var proposal = DelimiterDetector.Propose("a,b,c\n1,2,3", Defaults);
        Assert.Equal(',', proposal!.Delimiter);
    }

    [Fact]
    public void Proposes_Tab()
    {
        var proposal = DelimiterDetector.Propose("a\tb\tc\n1\t2\t3", Defaults);
        Assert.Equal('\t', proposal!.Delimiter);
    }

    [Fact]
    public void Proposes_Pipe()
    {
        var proposal = DelimiterDetector.Propose("a|b|c\n1|2|3", Defaults);
        Assert.Equal('|', proposal!.Delimiter);
    }

    [Fact]
    public void CarriesTheEvidence_ForTheUiToShow()
    {
        var proposal = DelimiterDetector.Propose("a;b\n1;2\n3;4\n5;6", Defaults);

        Assert.Equal(4, proposal!.SampledRecords);
        Assert.Equal(4, proposal.ConsistentRecords);
        Assert.Equal(1.0, proposal.Consistency, 3);
    }

    [Fact]
    public void PrefersTheConsistentCandidate_OverTheOneThatMerelySplitsMore()
    {
        // Every record has 2 semicolon-fields; commas appear only inside one value, so a comma reading is ragged.
        var proposal = DelimiterDetector.Propose("a;b\nc,d,e;f\ng;h", Defaults);

        Assert.Equal(';', proposal!.Delimiter);
        Assert.True(proposal.IsUnanimous);
    }

    [Fact]
    public void ReportsNotUnanimous_WhenTheFileIsRagged()
    {
        var proposal = DelimiterDetector.Propose("a;b;c\n1;2\n3;4;5", Defaults);

        Assert.Equal(';', proposal!.Delimiter);
        Assert.False(proposal.IsUnanimous);
        Assert.Equal(2, proposal.ConsistentRecords);
        Assert.Equal(3, proposal.SampledRecords);
    }

    [Fact]
    public void ReturnsNull_ForASingleColumnFile_RatherThanInventingASeparator()
    {
        Assert.Null(DelimiterDetector.Propose("alfa\nbeta\ngamma", Defaults));
    }

    [Fact]
    public void ReturnsNull_ForEmptyInput()
    {
        Assert.Null(DelimiterDetector.Propose(string.Empty, Defaults));
    }

    [Fact]
    public void IgnoresDelimiters_InsideQuotedValues()
    {
        // Parsing uses the real reader, so a quoted semicolon must not count as a separator.
        var proposal = DelimiterDetector.Propose("\"a;b\";c\n\"d;e\";f", Defaults);

        Assert.Equal(';', proposal!.Delimiter);
        Assert.Equal(2, proposal.FieldCount);
    }

    // ── Encoding ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Utf8Bom_IsProof()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'a' };

        var proposal = EncodingDetector.Propose(bytes);

        Assert.Equal("UTF8", proposal.CharsetName);
        Assert.Equal(EncodingDetectionBasis.ByteOrderMark, proposal.Basis);
        Assert.Equal(3, EncodingDetector.ByteOrderMarkLength(proposal.CharsetName));
    }

    [Fact]
    public void Utf16LeBom_IsRecognised()
    {
        var proposal = EncodingDetector.Propose(new byte[] { 0xFF, 0xFE, (byte)'a', 0x00 });

        Assert.Equal("UTF16LE", proposal.CharsetName);
        Assert.Equal(EncodingDetectionBasis.ByteOrderMark, proposal.Basis);
        Assert.Equal(2, EncodingDetector.ByteOrderMarkLength(proposal.CharsetName));
    }

    [Fact]
    public void Utf16BeBom_IsRecognised()
    {
        var proposal = EncodingDetector.Propose(new byte[] { 0xFE, 0xFF, 0x00, (byte)'a' });
        Assert.Equal("UTF16BE", proposal.CharsetName);
    }

    [Fact]
    public void AsciiOnly_SaysTheFileCannotDistinguish_AndKeepsTheDefault()
    {
        var proposal = EncodingDetector.Propose(Encoding.ASCII.GetBytes("id;name\n1;abc"));

        Assert.Equal("WIN1250", proposal.CharsetName);
        Assert.Equal(EncodingDetectionBasis.AsciiOnly, proposal.Basis);
    }

    [Fact]
    public void AsciiOnly_HonoursTheCallersDefault()
    {
        var proposal = EncodingDetector.Propose(Encoding.ASCII.GetBytes("abc"), singleByteDefault: "WIN1252");
        Assert.Equal("WIN1252", proposal.CharsetName);
    }

    [Fact]
    public void ValidUtf8WithAccents_ProposesUtf8()
    {
        var proposal = EncodingDetector.Propose(Encoding.UTF8.GetBytes("zażółć gęślą jaźń"));

        Assert.Equal("UTF8", proposal.CharsetName);
        Assert.Equal(EncodingDetectionBasis.ValidUtf8MultiByte, proposal.Basis);
    }

    [Fact]
    public void Win1250Accents_AreNotValidUtf8_SoASingleByteCharsetIsProposed()
    {
        var win1250 = CharsetCatalog.Resolve("WIN1250");
        var proposal = EncodingDetector.Propose(win1250.GetBytes("zażółć gęślą jaźń"));

        Assert.Equal("WIN1250", proposal.CharsetName);
        Assert.Equal(EncodingDetectionBasis.NotValidUtf8, proposal.Basis);
    }

    [Fact]
    public void ATruncatedMultiByteSequence_DoesNotMakeAUtf8FileLookInvalid()
    {
        // The detector reads a SAMPLE, which can cut a character in half. That is not evidence about the file.
        var full = Encoding.UTF8.GetBytes("aaaą");
        var cut = full[..^1];

        var proposal = EncodingDetector.Propose(cut);

        Assert.Equal("UTF8", proposal.CharsetName);
    }

    [Fact]
    public void EmptyInput_FallsBackToTheDefault_WithoutClaimingEvidence()
    {
        var proposal = EncodingDetector.Propose(System.Array.Empty<byte>());

        Assert.Equal("WIN1250", proposal.CharsetName);
        Assert.Equal(EncodingDetectionBasis.AsciiOnly, proposal.Basis);
    }

    [Fact]
    public void ByteOrderMarkLength_IsZero_ForASingleByteCharset()
    {
        Assert.Equal(0, EncodingDetector.ByteOrderMarkLength("WIN1250"));
        Assert.Equal(0, EncodingDetector.ByteOrderMarkLength(null));
    }

    [Fact]
    public void CharsetCatalog_ResolvesTheFileOnlyUtf16Names()
    {
        // The import detectors emit these, and CharsetCatalog is the ONE owner of name -> Encoding.
        Assert.Equal(Encoding.Unicode, CharsetCatalog.Resolve("UTF16LE"));
        Assert.Equal(Encoding.BigEndianUnicode, CharsetCatalog.Resolve("UTF16BE"));
        // ...but they are not offered as CONNECTION charsets, because Firebird has no such thing.
        Assert.DoesNotContain("UTF16LE", CharsetCatalog.Supported);
        Assert.DoesNotContain("UTF16BE", CharsetCatalog.Supported);
    }
}

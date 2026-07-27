using System.IO;
using System.Linq;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I1: the RFC 4180 delimited reader. The reader is the one place that decides what a field
/// IS, so its edge cases are pinned exhaustively: a mistake here re-routes or truncates data before any
/// converter or validator gets a chance to object (§0.1).
/// </summary>
public class ImportDelimitedReaderTests
{
    private static DelimitedOptions Options(
        char delimiter = ';',
        char quote = '"',
        bool trim = false,
        LineEndingMode lineEnding = LineEndingMode.Auto)
        => new()
        {
            Delimiter = delimiter,
            Quote = quote,
            TrimWhitespace = trim,
            LineEnding = lineEnding,
        };

    private static DelimitedRecord[] Read(string text, DelimitedOptions? options = null)
    {
        var reader = new DelimitedTextReader(options ?? Options());
        using var text0 = new StringReader(text);
        return reader.ReadAll(text0).ToArray();
    }

    // ── Basic shape ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reads_SimpleRows()
    {
        var records = Read("a;b;c\n1;2;3");

        Assert.Equal(2, records.Length);
        Assert.Equal(new[] { "a", "b", "c" }, records[0].Fields);
        Assert.Equal(new[] { "1", "2", "3" }, records[1].Fields);
    }

    [Fact]
    public void NumbersRecords_From1_Sequentially()
    {
        var records = Read("a\nb\nc");
        Assert.Equal(new[] { 1, 2, 3 }, records.Select(r => r.RecordNumber));
    }

    [Fact]
    public void Reads_SingleRecord_WithoutTrailingTerminator()
    {
        var records = Read("a;b");
        Assert.Single(records);
        Assert.Equal(new[] { "a", "b" }, records[0].Fields);
    }

    [Fact]
    public void EmptyInput_YieldsNoRecords()
        => Assert.Empty(Read(string.Empty));

    [Fact]
    public void WhitespaceOnlyField_IsAField_NotAnEmptyRecord()
    {
        var records = Read(" ");
        Assert.Single(records);
        Assert.Equal(new[] { " " }, records[0].Fields);
    }

    // ── Empty fields ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EmptyFields_ArePreserved_Positionally()
    {
        var records = Read("a;;c");
        Assert.Equal(new[] { "a", "", "c" }, records[0].Fields);
    }

    [Fact]
    public void LeadingDelimiter_YieldsLeadingEmptyField()
        => Assert.Equal(new[] { "", "b" }, Read(";b")[0].Fields);

    [Fact]
    public void TrailingDelimiter_YieldsTrailingEmptyField()
        => Assert.Equal(new[] { "a", "" }, Read("a;")[0].Fields);

    [Fact]
    public void RecordOfOnlyDelimiters_YieldsAllEmptyFields()
        => Assert.Equal(new[] { "", "", "" }, Read(";;")[0].Fields);

    // ── Blank lines ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BlankLines_AreSkipped_NotReportedAsEmptyRecords()
    {
        var records = Read("a\n\n\nb");
        Assert.Equal(2, records.Length);
        Assert.Equal(new[] { "a" }, records[0].Fields);
        Assert.Equal(new[] { "b" }, records[1].Fields);
    }

    [Fact]
    public void TrailingNewline_DoesNotProduceAnExtraRecord()
        => Assert.Single(Read("a;b\n"));

    [Fact]
    public void BlankLinesDoNotConsumeRecordNumbers()
    {
        // A skipped blank line must not advance the number, or every error after it points one row off.
        var records = Read("a\n\nb");
        Assert.Equal(new[] { 1, 2 }, records.Select(r => r.RecordNumber));
    }

    // ── Line terminators ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Handles_Crlf()
        => Assert.Equal(2, Read("a\r\nb").Length);

    [Fact]
    public void Handles_Lf()
        => Assert.Equal(2, Read("a\nb").Length);

    [Fact]
    public void Handles_LoneCr()
        => Assert.Equal(2, Read("a\rb").Length);

    [Fact]
    public void Crlf_IsOneTerminator_NotTwo()
    {
        var records = Read("a\r\nb\r\n");
        Assert.Equal(2, records.Length);
    }

    [Fact]
    public void LfMode_TreatsLoneCr_AsData()
    {
        var records = Read("a\rb\nc", Options(lineEnding: LineEndingMode.Lf));

        Assert.Equal(2, records.Length);
        Assert.Equal(new[] { "a\rb" }, records[0].Fields);
        Assert.Equal(new[] { "c" }, records[1].Fields);
    }

    [Fact]
    public void CrMode_TreatsLoneLf_AsData()
    {
        var records = Read("a\nb\rc", Options(lineEnding: LineEndingMode.Cr));

        Assert.Equal(2, records.Length);
        Assert.Equal(new[] { "a\nb" }, records[0].Fields);
    }

    [Fact]
    public void CrlfMode_TreatsLoneLf_AsData()
    {
        var records = Read("a\nb\r\nc", Options(lineEnding: LineEndingMode.Crlf));

        Assert.Equal(2, records.Length);
        Assert.Equal(new[] { "a\nb" }, records[0].Fields);
        Assert.Equal(new[] { "c" }, records[1].Fields);
    }

    // ── Quoting (RFC 4180) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void QuotedField_LosesItsQuotes()
        => Assert.Equal(new[] { "a", "b" }, Read("\"a\";\"b\"")[0].Fields);

    [Fact]
    public void QuotedField_MayContainTheDelimiter()
        => Assert.Equal(new[] { "a;b", "c" }, Read("\"a;b\";c")[0].Fields);

    [Fact]
    public void QuotedField_MayContainALineBreak_AndStaysOneRecord()
    {
        var records = Read("\"line1\nline2\";b");

        Assert.Single(records);
        Assert.Equal(new[] { "line1\nline2", "b" }, records[0].Fields);
    }

    [Fact]
    public void QuotedField_PreservesCrlfVerbatim()
    {
        // Normalizing the terminator inside a value would silently rewrite the user's data (§0).
        var records = Read("\"line1\r\nline2\"");
        Assert.Equal(new[] { "line1\r\nline2" }, records[0].Fields);
    }

    [Fact]
    public void DoubledQuote_BecomesOneLiteralQuote()
        => Assert.Equal(new[] { "say \"hi\"" }, Read("\"say \"\"hi\"\"\"")[0].Fields);

    [Fact]
    public void FieldOfOnlyADoubledQuote_IsOneQuoteCharacter()
        => Assert.Equal(new[] { "\"" }, Read("\"\"\"\"")[0].Fields);

    [Fact]
    public void EmptyQuotedField_IsAnEmptyString()
        => Assert.Equal(new[] { "", "b" }, Read("\"\";b")[0].Fields);

    [Fact]
    public void QuoteInsideUnquotedField_IsOrdinaryData()
    {
        // Only a quote at the START of a field opens quoting; anything else is a character in a value.
        var records = Read("a\"b;c");
        Assert.Equal(new[] { "a\"b", "c" }, records[0].Fields);
    }

    [Fact]
    public void UnterminatedQuote_ConsumesToEndOfInput_WithoutLosingIt()
    {
        // Malformed input must still surface every character it contained — never a swallowed field (§0).
        var records = Read("\"a;b\nc");
        Assert.Single(records);
        Assert.Equal(new[] { "a;b\nc" }, records[0].Fields);
    }

    [Fact]
    public void CustomQuoteCharacter_IsHonoured()
    {
        var records = Read("'a;b';c", Options(quote: '\''));
        Assert.Equal(new[] { "a;b", "c" }, records[0].Fields);
    }

    // ── Delimiters ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TabDelimiter_IsHonoured()
        => Assert.Equal(new[] { "a", "b" }, Read("a\tb", Options(delimiter: '\t'))[0].Fields);

    [Fact]
    public void CommaDelimiter_IsHonoured()
        => Assert.Equal(new[] { "a", "b" }, Read("a,b", Options(delimiter: ','))[0].Fields);

    [Fact]
    public void OtherSeparatorCharacters_AreData()
        => Assert.Equal(new[] { "a,b" }, Read("a,b")[0].Fields);

    // ── Ragged records ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RaggedRecords_KeepTheirOwnFieldCount_AndAreNotPadded()
    {
        // Padding here would invent values; the mapper decides what an absent field means.
        var records = Read("a;b;c\n1;2\n1;2;3;4");

        Assert.Equal(3, records[0].Fields.Length);
        Assert.Equal(2, records[1].Fields.Length);
        Assert.Equal(4, records[2].Fields.Length);
    }

    // ── Whitespace trimming ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Trimming_Off_ByDefault()
        => Assert.Equal(new[] { "  a  ", " b" }, Read("  a  ; b")[0].Fields);

    [Fact]
    public void Trimming_On_TrimsUnquotedFields()
        => Assert.Equal(new[] { "a", "b" }, Read("  a  ; b ", Options(trim: true))[0].Fields);

    [Fact]
    public void Trimming_On_LeavesQuotedFieldsVerbatim()
    {
        // Spaces inside quotes were put there on purpose.
        var records = Read("\"  a  \"; b ", Options(trim: true));
        Assert.Equal(new[] { "  a  ", "b" }, records[0].Fields);
    }

    // ── Sampling ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadSample_StopsAtTheRequestedCount()
    {
        var reader = new DelimitedTextReader(Options());
        using var text = new StringReader("1\n2\n3\n4\n5");

        var sample = reader.ReadSample(text, 3);

        Assert.Equal(3, sample.Count);
        Assert.Equal(new[] { 1, 2, 3 }, sample.Select(r => r.RecordNumber));
    }

    [Fact]
    public void ReadSample_ReturnsEverything_WhenTheSourceIsShorter()
    {
        var reader = new DelimitedTextReader(Options());
        using var text = new StringReader("1\n2");
        Assert.Equal(2, reader.ReadSample(text, 100).Count);
    }

    [Fact]
    public void ReadSample_OfZero_ReadsNothing()
    {
        var reader = new DelimitedTextReader(Options());
        using var text = new StringReader("1\n2");
        Assert.Empty(reader.ReadSample(text, 0));
    }

    // ── Real-world shape ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Reads_TheShapeFromTheDesignDocument()
    {
        // The sample from the design doc's screenshots: header + rows, semicolon-delimited, one quoted value
        // containing the delimiter.
        const string text =
            "Indeks kartoteki;Nr technologii;Kod fantomu;Nazwa fantomu\n" +
            "GN-375-GTO-2KAB-EU;11881;EOP-375-GTO-EU-001;ENGINE\n" +
            "GN-375-GTO-2KAB-EU;11881;EOP-375-GTO-EU-001.1;\"(STD) MERCURY 2x400; XXL VERADO WHITE\"";

        var records = Read(text);

        Assert.Equal(3, records.Length);
        Assert.All(records, r => Assert.Equal(4, r.Fields.Length));
        Assert.Equal("(STD) MERCURY 2x400; XXL VERADO WHITE", records[2].Fields[3]);
    }
}

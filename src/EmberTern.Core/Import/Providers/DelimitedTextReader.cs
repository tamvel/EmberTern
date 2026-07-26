using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EmberTern.Core.Import.Providers;

/// <summary>One record produced by <see cref="DelimitedTextReader"/>.</summary>
/// <param name="RecordNumber">1-based RECORD number, counting a quoted field that spans several physical lines
/// as ONE record. This is the number the error report shows, so it is the number the user can act on; note that
/// it therefore diverges from a text editor's line number exactly when a field contains a line break.</param>
/// <param name="Fields">The field values, quotes removed and doubled quotes unescaped.</param>
public readonly record struct DelimitedRecord(int RecordNumber, string[] Fields);

/// <summary>
/// RFC 4180 reader for delimited text: quoted fields, doubled quotes, delimiters and line breaks inside quotes,
/// and CR / LF / CRLF terminators.
/// <para>
/// <b>Streaming by construction</b> — it pulls from a <see cref="TextReader"/> and yields one record at a time,
/// never building a list of the file. That is not an optimization but a requirement: the pipeline must survive a
/// million-row source (design R8).
/// </para>
/// <para>
/// <b>It does not convert, trim by default, or interpret.</b> Every field comes out as the text that was in the
/// file. Whitespace trimming happens only when explicitly enabled, and only for UNQUOTED fields — spaces inside
/// quotes were put there on purpose. Anything beyond that (NULL tokens, numbers, dates) belongs to the
/// converter, so there is exactly one place where text becomes a value (§0.1).
/// </para>
/// </summary>
public sealed class DelimitedTextReader
{
    private readonly DelimitedOptions _options;

    public DelimitedTextReader(DelimitedOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Reads every record in <paramref name="reader"/>, including the header record if the file has one — the
    /// caller decides what the first record means, because "is record 1 a header" is a user decision
    /// (<see cref="DelimitedOptions.HasHeader"/>) and not something to re-derive here.
    /// </summary>
    public IEnumerable<DelimitedRecord> ReadAll(TextReader reader)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        var fields = new List<string>();
        var field = new StringBuilder();
        int recordNumber = 0;

        // fieldWasQuoted drives trimming: a quoted field is returned verbatim even when trimming is on.
        bool fieldWasQuoted = false;
        bool inQuotes = false;
        bool anyContentInRecord = false;

        int next = reader.Read();
        while (next >= 0)
        {
            var c = (char)next;
            next = reader.Read();

            if (inQuotes)
            {
                if (c == _options.Quote)
                {
                    if (next >= 0 && (char)next == _options.Quote)
                    {
                        // A doubled quote is one literal quote.
                        field.Append(_options.Quote);
                        next = reader.Read();
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    // Delimiters and line breaks inside quotes are data — kept verbatim, including the exact
                    // CR/LF bytes, because normalizing them would silently rewrite the user's value.
                    field.Append(c);
                }
                continue;
            }

            if (c == _options.Quote && field.Length == 0)
            {
                inQuotes = true;
                fieldWasQuoted = true;
                anyContentInRecord = true;
                continue;
            }

            if (c == _options.Delimiter)
            {
                fields.Add(Finish(field, fieldWasQuoted));
                fieldWasQuoted = false;
                anyContentInRecord = true;
                continue;
            }

            if (IsTerminator(c, ref next, reader))
            {
                // A terminator ends the record — unless nothing at all has been seen, in which case this is a
                // blank line and is skipped rather than reported as a one-empty-field record.
                if (anyContentInRecord || field.Length > 0 || fields.Count > 0)
                {
                    fields.Add(Finish(field, fieldWasQuoted));
                    recordNumber++;
                    yield return new DelimitedRecord(recordNumber, fields.ToArray());
                    fields.Clear();
                }
                fieldWasQuoted = false;
                anyContentInRecord = false;
                continue;
            }

            field.Append(c);
            anyContentInRecord = true;
        }

        // Trailing record with no terminator at end of file.
        if (anyContentInRecord || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(Finish(field, fieldWasQuoted));
            recordNumber++;
            yield return new DelimitedRecord(recordNumber, fields.ToArray());
        }
    }

    /// <summary>
    /// Reads at most <paramref name="maxRecords"/> records — the sampling path used to derive the schema, to
    /// fill the source preview, and to propose a delimiter. Never reads the whole file for those purposes.
    /// </summary>
    public IReadOnlyList<DelimitedRecord> ReadSample(TextReader reader, int maxRecords)
    {
        if (maxRecords <= 0) return Array.Empty<DelimitedRecord>();

        var sample = new List<DelimitedRecord>(Math.Min(maxRecords, 256));
        foreach (var record in ReadAll(reader))
        {
            sample.Add(record);
            if (sample.Count >= maxRecords) break;
        }
        return sample;
    }

    // Consumes a line terminator, honouring the configured mode. In Auto every one of CR / LF / CRLF ends a
    // record; an explicit mode makes the OTHER character ordinary data, which is the point of setting it (a
    // file whose quoted text carries a lone CR must not break into records there).
    private bool IsTerminator(char c, ref int next, TextReader reader)
    {
        switch (_options.LineEnding)
        {
            case LineEndingMode.Lf:
                return c == '\n';

            case LineEndingMode.Cr:
                return c == '\r';

            case LineEndingMode.Crlf:
                if (c == '\r' && next >= 0 && (char)next == '\n')
                {
                    next = reader.Read();
                    return true;
                }
                return false;

            default:
                if (c == '\r')
                {
                    if (next >= 0 && (char)next == '\n') next = reader.Read();
                    return true;
                }
                return c == '\n';
        }
    }

    private string Finish(StringBuilder field, bool wasQuoted)
    {
        var text = field.ToString();
        field.Clear();
        return _options.TrimWhitespace && !wasQuoted ? text.Trim() : text;
    }
}

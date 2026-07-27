using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.Core.Export.Sql;

namespace EmberTern.Core.Import;

/// <summary>
/// Why one column got the type it got — the "Basis" column of §3.4, as structured facts rather than a sentence
/// (rule #6: Core holds no UI strings).
/// </summary>
/// <param name="ValuesSeen">Values that carried something. Empty fields are not evidence about a type.</param>
/// <param name="ValuesEmpty">Values that were empty or NULL.</param>
/// <param name="MaxTextLength">Longest value seen, in characters — ⭐ the number a <c>VARCHAR</c> length is
/// taken from, which is the second reason the scan covers the WHOLE source: a length measured from a sample is
/// a promise the rest of the file never agreed to, and it breaks as a rejected row in the middle of an
/// import.</param>
/// <param name="ChosenKind">The kind the type finally expresses.</param>
/// <param name="RejectedKind">The kind that fitted every value until one did not — <see cref="SqlValueKind.Unknown"/>
/// when the column never looked like anything but text. This is the R19 story: a column with 8 723 numbers and
/// one piece of text is <em>normal</em>, and the user needs to see the value that decided it.</param>
/// <param name="RejectedAtRow">Source row number of the value that killed <paramref name="RejectedKind"/>.</param>
/// <param name="RejectedByValue">That value, verbatim.</param>
public sealed record ColumnInferenceEvidence(
    long ValuesSeen,
    long ValuesEmpty,
    int MaxTextLength,
    SqlValueKind ChosenKind,
    SqlValueKind RejectedKind = SqlValueKind.Unknown,
    int RejectedAtRow = 0,
    string? RejectedByValue = null)
{
    /// <summary>True when the column held values of more than one shape, so it fell back to text (§0.3).</summary>
    public bool IsMixed => RejectedKind != SqlValueKind.Unknown;
}

/// <summary>One column as inferred: what to create, and why.</summary>
public sealed record InferredColumn(ImportColumnDefinition Definition, ColumnInferenceEvidence Evidence);

/// <summary>The whole answer for one source.</summary>
/// <param name="Columns">One per source field, in field order.</param>
/// <param name="RowsAnalysed">Records actually read. ⭐ Always shown beside the types (§3.4), because a type is
/// only as good as the evidence behind it and the user is entitled to know how much there was.</param>
/// <param name="ScanTruncated">True when the safety limit stopped the scan before the end of the source — so the
/// surface can say the types rest on part of the file rather than all of it.</param>
public sealed record ColumnTypeInference(
    IReadOnlyList<InferredColumn> Columns,
    long RowsAnalysed,
    bool ScanTruncated)
{
    public static readonly ColumnTypeInference Empty =
        new(Array.Empty<InferredColumn>(), 0, false);
}

/// <summary>
/// ⭐ Proposes a column type for every field of a source — <b>conservatively</b>, and from the WHOLE source.
/// <para>
/// <b>Why the whole source and not a sample</b> (I0 / REK-7, design R19): in the user's own real file <b>2 of 5
/// columns were of mixed type</b> — one held 8 723 integers and a single piece of text. A 240-row sample would
/// have typed it <c>INTEGER</c>, and the import would then have failed on row 8 724 — <em>after</em> the table
/// had been created and COMMITTED on the Ddl lane (§4.5 / gotcha #213), i.e. at the worst possible moment, with
/// an orphaned table left behind. The file is read twice anyway; reading it a third time is cheap next to that.
/// A safety limit of <see cref="DefaultScanLimit"/> rows keeps a pathological source from making the surface
/// unusable, and when it bites the surface says so rather than implying a full scan.
/// </para>
/// <para>
/// ⭐ <b>It owns no parser.</b> Every "could this value be an X?" is answered by
/// <see cref="ImportValueConverter"/> — the same class that will convert the value during the real run, under
/// the same <see cref="ImportCultureOptions"/>. That is not tidiness: an inferencer with its own idea of what an
/// integer looks like would propose a type the converter then refuses, which is precisely the timebomb R19
/// describes. Inference and conversion cannot disagree, because there is only one of them.
/// </para>
/// <para>
/// <b>§0.3 — ambiguity loses.</b> A candidate type has to fit <em>every single</em> non-empty value; the first
/// one it does not fit kills it, and the evidence remembers which value that was. When nothing but text fits,
/// the answer is <c>VARCHAR</c>, never "probably a number".
/// </para>
/// </summary>
public static class ColumnTypeInferencer
{
    /// <summary>Rows after which the scan stops and says it stopped. Not a sample size — a circuit breaker.</summary>
    public const long DefaultScanLimit = 1_000_000;

    /// <summary>Length given to a column that carried no value at all. <c>VARCHAR(0)</c> is not a type, and a
    /// column with no evidence gets the smallest honest text width rather than a guessed one.</summary>
    public const int EmptyColumnTextLength = 1;

    /// <summary>
    /// Longest <c>VARCHAR</c> proposed. Firebird caps a <c>VARCHAR</c> at 32 765 <b>bytes</b>, which in a
    /// multi-byte connection charset is far fewer characters — UTF8 allows 8 191. Rather than compute a limit
    /// that depends on the connection, anything longer becomes a text BLOB, which has no limit at all and which
    /// the import already supports end to end (<see cref="SqlValueKind.TextBlob"/>).
    /// </summary>
    public const int MaxVarcharLength = 8_191;

    /// <summary>Largest precision an exact numeric can carry without <c>INT128</c>. A number needing more
    /// cannot be stored exactly, and storing it approximately would be the silent loss §0.1 forbids — so such a
    /// column falls back to text.</summary>
    public const int MaxNumericPrecision = 18;

    /// <summary>Infers types by streaming the source through <paramref name="provider"/>.</summary>
    public static async Task<ColumnTypeInference> InferAsync(
        SourceSchema schema,
        IImportProvider provider,
        IImportSource source,
        ImportConfiguration configuration,
        long scanLimit = DefaultScanLimit,
        CancellationToken cancellationToken = default)
    {
        if (schema is null) throw new ArgumentNullException(nameof(schema));
        if (provider is null) throw new ArgumentNullException(nameof(provider));
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        return await InferAsync(
                schema,
                provider.ReadRecordsAsync(source, configuration, cancellationToken),
                configuration.Culture,
                scanLimit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Infers types from an already-opened stream of records. The overload the tests drive, and the one
    /// that makes the whole thing exercisable without a file.</summary>
    public static async Task<ColumnTypeInference> InferAsync(
        SourceSchema schema,
        IAsyncEnumerable<RawRecord> records,
        ImportCultureOptions culture,
        long scanLimit = DefaultScanLimit,
        CancellationToken cancellationToken = default)
    {
        if (schema is null) throw new ArgumentNullException(nameof(schema));
        if (records is null) throw new ArgumentNullException(nameof(records));
        if (culture is null) throw new ArgumentNullException(nameof(culture));

        if (schema.Fields.Count == 0) return ColumnTypeInference.Empty;

        var columns = new ColumnState[schema.Fields.Count];
        for (var i = 0; i < columns.Length; i++) columns[i] = new ColumnState();

        long rows = 0;
        var truncated = false;

        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            // Asked here as well as handed to the provider: a full-source scan is the most expensive thing this
            // surface does, and a newer edit must be able to abandon it promptly whether or not the provider
            // in hand happens to observe the token.
            cancellationToken.ThrowIfCancellationRequested();

            if (rows >= scanLimit)
            {
                truncated = true;
                break;
            }

            rows++;
            for (var i = 0; i < columns.Length; i++)
            {
                columns[i].Observe(record.ValueAt(i), record.SourceRowNumber, culture);
            }
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<InferredColumn>(columns.Length);
        for (var i = 0; i < columns.Length; i++)
        {
            var name = UniqueName(ToColumnName(schema.Fields[i].Name, i), names);
            result.Add(columns[i].Conclude(name));
        }

        return new ColumnTypeInference(result, rows, truncated);
    }

    // ── Naming ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A source field's name as a column identifier.
    /// <para>
    /// Reuses <see cref="ImportMappingPlanner.NormalizeName"/> — the module's one owner of "what is this name,
    /// really" — so <c>Nr technologii</c> becomes <c>NR_TECHNOLOGII</c> and the mapping planner then matches the
    /// two back together by name with no special case for a new table. Diacritics survive, exactly as they do
    /// there: folding them would conflate genuinely different names, and identifiers are quoted on the way into
    /// the DDL anyway.
    /// </para>
    /// </summary>
    public static string ToColumnName(string? sourceFieldName, int index)
    {
        var normalized = ImportMappingPlanner.NormalizeName(sourceFieldName);

        var builder = new StringBuilder(normalized.Length + 1);
        foreach (var ch in normalized)
        {
            // Keep what reads as a name; turn anything else into a word break rather than dropping it, so
            // "Netto (PLN)" becomes NETTO_PLN instead of NETTOPLN.
            if (char.IsLetterOrDigit(ch) || ch == '_') builder.Append(ch);
            else if (builder.Length > 0 && builder[builder.Length - 1] != '_') builder.Append('_');
        }

        while (builder.Length > 0 && builder[builder.Length - 1] == '_') builder.Length--;

        if (builder.Length == 0)
        {
            return "COLUMN_" + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        // An identifier may not begin with a digit, and a header of "2026" is an ordinary thing to meet.
        if (char.IsDigit(builder[0])) builder.Insert(0, 'C');

        return builder.ToString();
    }

    private static string UniqueName(string candidate, HashSet<string> taken)
    {
        if (taken.Add(candidate)) return candidate;

        for (var suffix = 2; ; suffix++)
        {
            var name = candidate + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            if (taken.Add(name)) return name;
        }
    }

    // ── Per-column accumulation ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The candidate types, in the order a tie is broken. Order is a decision, not an accident:
    /// <list type="bullet">
    /// <item><b>Integer before Boolean</b> — the default true/false tokens include <c>1</c> and <c>0</c>, so a
    /// column of ones and zeros would otherwise become <c>BOOLEAN</c>. It is far more often a flag stored as a
    /// number, and <c>INTEGER</c> is the reading that keeps the original value.</item>
    /// <item><b>Date before Timestamp</b> — a timestamp accepts a bare date (it just means midnight), so a
    /// column of pure dates satisfies both; <c>DATE</c> is the type that describes it.</item>
    /// </list>
    /// <para>
    /// <b>Float is deliberately absent.</b> The only text a <c>DOUBLE PRECISION</c> would accept and
    /// <c>NUMERIC</c> would not is scientific notation, which the converter does not accept either — and
    /// choosing an approximate type for values that look exact would lose digits silently (§0.1). Such a column
    /// becomes text, and the user can change it in the grid.
    /// </para>
    /// </summary>
    private static readonly SqlValueKind[] Candidates =
    {
        SqlValueKind.Integer,
        SqlValueKind.Decimal,
        SqlValueKind.Date,
        SqlValueKind.Timestamp,
        SqlValueKind.Time,
        SqlValueKind.Boolean,
    };

    private sealed class ColumnState
    {
        private readonly bool[] _alive = new bool[Candidates.Length];

        private long _seen;
        private long _empty;
        private int _maxLength;

        // Widths, tracked only while their candidate is still standing.
        private bool _fitsInt32 = true;
        private int _maxIntegerDigits;
        private int _maxScale;

        // ⭐ Per candidate, the value that ended it — the R19 story, kept so the surface can name the row.
        // Recorded for EVERY candidate rather than only the first to fall, because the two are not the same
        // question: on a column of integers the DATE candidate dies on the very first value, while the
        // interesting fact is that INTEGER survived 8 723 rows and then met "11 88x". What the user wants is
        // the type the column LOOKED like, which is the highest-priority one that died — resolved at the end.
        private readonly int[] _diedAtRow = new int[Candidates.Length];
        private readonly string?[] _diedByValue = new string?[Candidates.Length];

        public ColumnState()
        {
            for (var i = 0; i < _alive.Length; i++) _alive[i] = true;
        }

        public void Observe(object? raw, int sourceRowNumber, ImportCultureOptions culture)
        {
            var text = ImportValueConverter.AsText(raw);

            if (text is null || text.Length == 0)
            {
                _empty++;
                return;
            }

            _seen++;
            if (text.Length > _maxLength) _maxLength = text.Length;

            for (var i = 0; i < Candidates.Length; i++)
            {
                if (!_alive[i]) continue;

                var kind = Candidates[i];
                if (Accepts(raw, text, kind, culture))
                {
                    if (kind == SqlValueKind.Integer || kind == SqlValueKind.Decimal) MeasureNumber(raw, kind, culture);
                    continue;
                }

                _alive[i] = false;
                _diedAtRow[i] = sourceRowNumber;
                _diedByValue[i] = text;
            }
        }

        /// <summary>
        /// Whether <paramref name="kind"/> could hold this value — asked of the converter, never re-derived.
        /// <para>
        /// ⚠ One rule is added on top, and it is a §0.1 rule rather than a parsing one: a number written with a
        /// <b>significant leading zero</b> is refused as a number. <c>007</c> parses as 7 perfectly well, but
        /// the seven and the text are not the same data — a postal code, an index or an account number stored as
        /// <c>INTEGER</c> comes back different from what went in. That is exactly the "never modify what you
        /// cannot reproduce identically" rule (#11), and it is why such a column becomes text.
        /// </para>
        /// </summary>
        private static bool Accepts(object? raw, string text, SqlValueKind kind, ImportCultureOptions culture)
        {
            if ((kind == SqlValueKind.Integer || kind == SqlValueKind.Decimal)
                && raw is string
                && HasSignificantLeadingZero(text))
            {
                return false;
            }

            return ImportValueConverter.Convert(raw, ProbeType(kind), culture).IsSuccess;
        }

        /// <summary>Widest form of each candidate, so a value is rejected for its SHAPE, not for a width the
        /// inferencer itself is about to choose. <c>BIGINT</c> and <c>NUMERIC(18,…)</c> are the widest exact
        /// types Firebird has; the actual declared width is decided once, at the end, from what was seen.</summary>
        private static ImportTargetType ProbeType(SqlValueKind kind) => kind switch
        {
            SqlValueKind.Integer => new ImportTargetType { Kind = SqlValueKind.Integer, BaseTypeName = "BIGINT" },
            SqlValueKind.Decimal => new ImportTargetType { Kind = SqlValueKind.Decimal, BaseTypeName = "NUMERIC", Size = MaxNumericPrecision, Scale = 4 },
            SqlValueKind.Date => new ImportTargetType { Kind = SqlValueKind.Date, BaseTypeName = "DATE" },
            SqlValueKind.Timestamp => new ImportTargetType { Kind = SqlValueKind.Timestamp, BaseTypeName = "TIMESTAMP" },
            SqlValueKind.Time => new ImportTargetType { Kind = SqlValueKind.Time, BaseTypeName = "TIME" },
            _ => new ImportTargetType { Kind = SqlValueKind.Boolean, BaseTypeName = "BOOLEAN" },
        };

        /// <summary>True for <c>007</c> and <c>-0012.5</c>; false for <c>0</c>, <c>0.5</c> and <c>-0</c>.</summary>
        private static bool HasSignificantLeadingZero(string text)
        {
            var span = text.Trim();
            var i = 0;
            if (i < span.Length && (span[i] == '-' || span[i] == '+')) i++;

            if (i >= span.Length || span[i] != '0') return false;

            // A single leading zero is only significant when another DIGIT follows it. "0", "0,5" and "0.5"
            // are ordinary ways to write a number; "007" is not a number, it is a code.
            var next = i + 1;
            return next < span.Length && char.IsDigit(span[next]);
        }

        private void MeasureNumber(object? raw, SqlValueKind kind, ImportCultureOptions culture)
        {
            if (kind == SqlValueKind.Integer)
            {
                var value = ImportValueConverter.Convert(raw, ProbeType(SqlValueKind.Integer), culture);

                if (value.IsSuccess && value.Value is long l && (l < int.MinValue || l > int.MaxValue))
                {
                    _fitsInt32 = false;
                }
                return;
            }

            var parsed = ImportValueConverter.Convert(raw, ProbeType(SqlValueKind.Decimal), culture);
            if (!parsed.IsSuccess || parsed.Value is not decimal d) return;

            var scale = (decimal.GetBits(d)[3] >> 16) & 0xFF;
            if (scale > _maxScale) _maxScale = scale;

            var integerDigits = IntegerDigits(d);
            if (integerDigits > _maxIntegerDigits) _maxIntegerDigits = integerDigits;
        }

        private static int IntegerDigits(decimal value)
        {
            var whole = decimal.Truncate(Math.Abs(value));
            var digits = 1;
            while (whole >= 10m)
            {
                whole = decimal.Truncate(whole / 10m);
                digits++;
            }
            return digits;
        }

        public InferredColumn Conclude(string name)
        {
            var kind = FirstSurviving();
            var definition = Describe(name, kind);

            // ⭐ The chosen kind is READ BACK off the type text the DDL will carry, rather than restated from
            // the branch that produced it. It costs one parse and makes the two impossible to disagree: a
            // type this module emits but ImportTargetType cannot read would show up here as Unknown, and a
            // test catches it — instead of the import meeting it at run time.
            var chosen = ImportTargetType.Resolve(ImportNewTable.TypeText(definition)).Kind;

            // A column that fell back to text has a story worth telling: WHICH type it looked like and which
            // value ended that. A column that won its candidate outright has none.
            var fellBack = kind == SqlValueKind.Unknown && _seen > 0;
            var rejected = fellBack ? BestRejected() : -1;

            var evidence = new ColumnInferenceEvidence(
                _seen,
                _empty,
                _maxLength,
                chosen,
                rejected < 0 ? SqlValueKind.Unknown : Candidates[rejected],
                rejected < 0 ? 0 : _diedAtRow[rejected],
                rejected < 0 ? null : _diedByValue[rejected]);

            return new InferredColumn(definition, evidence);
        }

        /// <summary>
        /// Index of the candidate whose death is worth reporting: the <b>highest-priority</b> one that died,
        /// which is the type the column looked like it was. Not the first to fall — on a column of integers
        /// DATE dies on row one and says nothing anybody wants to hear.
        /// </summary>
        private int BestRejected()
        {
            for (var i = 0; i < Candidates.Length; i++)
            {
                if (_diedByValue[i] is not null) return i;
            }
            return -1;
        }

        /// <summary>The highest-priority candidate still standing, or <see cref="SqlValueKind.Unknown"/> when
        /// text is all that is left — which is the answer §0.3 asks for whenever anything is unclear.</summary>
        private SqlValueKind FirstSurviving()
        {
            // No value at all is not evidence for a type. An empty column is text, at its narrowest.
            if (_seen == 0) return SqlValueKind.Unknown;

            for (var i = 0; i < Candidates.Length; i++)
            {
                if (!_alive[i]) continue;

                // A NUMERIC needing more than 18 digits cannot be stored exactly; an approximation would be a
                // silent loss, so this candidate withdraws rather than being narrowed.
                if (Candidates[i] == SqlValueKind.Decimal && _maxIntegerDigits + _maxScale > MaxNumericPrecision)
                {
                    continue;
                }

                return Candidates[i];
            }

            return SqlValueKind.Unknown;
        }

        private ImportColumnDefinition Describe(string name, SqlValueKind kind) => kind switch
        {
            // SMALLINT is deliberately never proposed: it is right for the file in hand and wrong for the next
            // one, and the difference between it and INTEGER costs nothing worth a rejected row later.
            SqlValueKind.Integer => new ImportColumnDefinition
            {
                Name = name,
                BasicType = _fitsInt32 ? "INTEGER" : "BIGINT",
            },
            SqlValueKind.Decimal => new ImportColumnDefinition
            {
                Name = name,
                BasicType = "NUMERIC",
                Size = Math.Min(MaxNumericPrecision, Math.Max(1, _maxIntegerDigits + _maxScale)),
                Scale = _maxScale,
            },
            SqlValueKind.Date => new ImportColumnDefinition { Name = name, BasicType = "DATE" },
            SqlValueKind.Timestamp => new ImportColumnDefinition { Name = name, BasicType = "TIMESTAMP" },
            SqlValueKind.Time => new ImportColumnDefinition { Name = name, BasicType = "TIME" },
            SqlValueKind.Boolean => new ImportColumnDefinition { Name = name, BasicType = "BOOLEAN" },

            _ when _maxLength > MaxVarcharLength => new ImportColumnDefinition
            {
                Name = name,
                BasicType = "BLOB",
                BlobSubType = 1,
            },

            // ⭐ The length is the LONGEST value actually seen — not a rounded-up guess, and not a sample's idea
            // of the longest. The grid is editable for anyone who wants headroom.
            _ => new ImportColumnDefinition
            {
                Name = name,
                BasicType = "VARCHAR",
                Size = Math.Max(EmptyColumnTextLength, _maxLength),
            },
        };
    }
}

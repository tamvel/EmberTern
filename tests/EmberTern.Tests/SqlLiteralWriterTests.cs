using System;
using System.Globalization;
using System.Threading;
using EmberTern.Core.Export.Sql;
using Xunit;

namespace EmberTern.Tests;

// E1 (SQL Data Export) — the value→literal correctness core. Every rule here was measured against a
// live Firebird 5.0 engine first (design §1.5); these tests pin the rendering, and the probe pins that
// the engine actually accepts it. A unit test asserting '2024-03-15T13:45:59' would have passed while
// the engine rejected the literal — so neither layer replaces the other.
public class SqlLiteralWriterTests
{
    private static string Literal(object? value, SqlValueKind kind)
    {
        var r = SqlLiteralWriter.Write(value, kind);
        Assert.True(r.IsWritten, $"expected a literal, got {r.Refusal}");
        return r.Literal;
    }

    private static SqlLiteralRefusal Refusal(object? value, SqlValueKind kind)
    {
        var r = SqlLiteralWriter.Write(value, kind);
        Assert.False(r.IsWritten, $"expected a refusal, got {r.Literal}");
        Assert.Null(r.Literal);
        return r.Refusal;
    }

    // ── NULL ─────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(SqlValueKind.Integer)]
    [InlineData(SqlValueKind.Text)]
    [InlineData(SqlValueKind.Timestamp)]
    [InlineData(SqlValueKind.BinaryBlob)]
    public void Null_And_DbNull_Are_The_Bare_Null_Literal(SqlValueKind kind)
    {
        Assert.Equal("NULL", Literal(null, kind));
        Assert.Equal("NULL", Literal(DBNull.Value, kind));
    }

    // NULL is faithful for a type we could not map, so a row holding one stays copyable even though a
    // non-NULL value of that column would be refused.
    [Fact]
    public void Null_Of_An_Unknown_Kind_Still_Renders()
        => Assert.Equal("NULL", Literal(DBNull.Value, SqlValueKind.Unknown));

    [Fact]
    public void Unknown_Kind_Refuses_Every_Non_Null_Value()
        => Assert.Equal(SqlLiteralRefusal.UnsupportedKind, Refusal(1, SqlValueKind.Unknown));

    // The default(SqlValueKind) is Unknown on purpose: an unmapped column refuses, never guesses.
    [Fact]
    public void Default_Kind_Is_Unknown()
        => Assert.Equal(SqlValueKind.Unknown, default(SqlValueKind));

    // ── Culture — the pl-PL trap (design §1.5.1) ─────────────────────────────
    // The user's machine is pl-PL, where ToString() yields "123456789,1234" → Dynamic SQL Error. This
    // is the single most consequential rule in the writer, so it is pinned under the real culture.
    [Fact]
    public void Numerics_Are_Invariant_Even_Under_A_Comma_Decimal_Culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pl-PL");
            Assert.Equal("123456789.1234", Literal(123456789.1234m, SqlValueKind.Decimal));
            Assert.Equal("3.14", Literal(3.14f, SqlValueKind.Float));
            Assert.Equal("2.718281828459045", Literal(2.718281828459045d, SqlValueKind.Float));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    // The same culture cannot be allowed to reach a temporal either — pl-PL formats a date as
    // "15.03.2024", which Firebird reads as a different date or not at all.
    [Fact]
    public void Temporals_Are_Invariant_Even_Under_A_Dotted_Date_Culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("pl-PL");
            Assert.Equal("'2024-03-15'", Literal(new DateTime(2024, 3, 15), SqlValueKind.Date));
            Assert.Equal("'2024-03-15 13:45:59.1234'",
                Literal(new DateTime(2024, 3, 15, 13, 45, 59).AddTicks(1234000), SqlValueKind.Timestamp));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    // ── Integer ──────────────────────────────────────────────────────────────
    [Fact]
    public void Integers_Are_Bare_Digits()
    {
        Assert.Equal("123", Literal((short)123, SqlValueKind.Integer));
        Assert.Equal("123", Literal(123, SqlValueKind.Integer));
        Assert.Equal("32767", Literal(short.MaxValue, SqlValueKind.Integer));
    }

    // Worth pinning because it is the obvious place for a plausible-sounding refusal to creep in: one
    // might expect Firebird to reject `-9223372036854775808`, since it parses `-x` as the negation of a
    // positive literal and 9223372036854775808 overflows BIGINT. MEASURED: the engine accepts it, and
    // the round-trip is exact. So the writer must NOT refuse it — an inferred "safety" check here would
    // have been a bug, not a guard.
    [Fact]
    public void Integer_Renders_The_Bigint_Extremes_Which_The_Engine_Accepts()
    {
        Assert.Equal("-9223372036854775808", Literal(long.MinValue, SqlValueKind.Integer));
        Assert.Equal("9223372036854775807", Literal(long.MaxValue, SqlValueKind.Integer));
    }

    // A value whose CLR type is neither an integer container nor a parseable integer string is refused
    // (a fractional decimal here — "non-integral"). NB a clean integer STRING is now accepted on purpose
    // (see Integer_Renders_A_Clean_Integer_Delivered_As_A_String), so the example is a real non-integer.
    [Fact]
    public void Integer_Kind_Refuses_A_Non_Integral_Value()
        => Assert.Equal(SqlLiteralRefusal.UnexpectedValueType, Refusal(7.5m, SqlValueKind.Integer));

    // A NUMERIC/DECIMAL with precision > 18 (or a value beyond Int64) is handed back by the driver as a
    // wide integer container — Int128 / UInt128 / BigInteger. It is still an integer whose exact literal
    // is its digits; the writer must render it, not refuse it as "no exact SQL literal" (the ID_NAGL
    // report). QA: verified all common NUMERIC/INTEGER keys render; this closes the wide-container gap.
    [Fact]
    public void Integer_Renders_Wide_Integer_Containers()
    {
        Assert.Equal("170141183460469231731687303715884105727",
            Literal(Int128.MaxValue, SqlValueKind.Integer));
        Assert.Equal("-170141183460469231731687303715884105728",
            Literal(Int128.MinValue, SqlValueKind.Integer));
        Assert.Equal("340282366920938463463374607431768211455",
            Literal(UInt128.MaxValue, SqlValueKind.Integer));
        Assert.Equal("123456789012345678901234567890",
            Literal(System.Numerics.BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture),
                SqlValueKind.Integer));
    }

    // Same for the Decimal kind (scale-0 NUMERIC(38,0)/DECIMAL(38,0) surface as these too).
    [Fact]
    public void Decimal_Kind_Accepts_Wide_Integer_Containers()
    {
        Assert.Equal("9223372036854775808", Literal((Int128)long.MaxValue + 1, SqlValueKind.Decimal));
        Assert.Equal("123456789012345678901234567890",
            Literal(System.Numerics.BigInteger.Parse("123456789012345678901234567890", CultureInfo.InvariantCulture),
                SqlValueKind.Decimal));
    }

    // Measured (a real NAGL PK): the driver handed an INTEGER-kind value back as the STRING "10019".
    // The kind is authoritative that the target is an integer, so a clean integer string renders as the
    // bare literal — never quoted, never refused.
    [Fact]
    public void Integer_Renders_A_Clean_Integer_Delivered_As_A_String()
    {
        Assert.Equal("10019", Literal("10019", SqlValueKind.Integer));
        Assert.Equal("-42", Literal("-42", SqlValueKind.Integer));
        Assert.Equal("10019", Literal("  10019  ", SqlValueKind.Integer)); // CHAR padding / whitespace
        Assert.Equal("123456789012345678901234567890",
            Literal("123456789012345678901234567890", SqlValueKind.Integer)); // beyond Int64
    }

    // STRICT: a culture-formatted or non-numeric string is refused, never guessed (§0). On pl-PL a decimal
    // comma / thousands space must NOT be silently reinterpreted.
    [Theory]
    [InlineData("10 019")]   // thousands space
    [InlineData("1,5")]      // pl-PL decimal comma
    [InlineData("0x10")]
    [InlineData("abc")]
    [InlineData("")]
    public void Integer_Refuses_A_Non_Canonical_String(string s)
        => Assert.Equal(SqlLiteralRefusal.UnexpectedValueType, Refusal(s, SqlValueKind.Integer));

    // ── Decimal ──────────────────────────────────────────────────────────────
    [Fact]
    public void Decimal_Is_Exact_And_Keeps_Its_Scale()
    {
        Assert.Equal("123456789.1234", Literal(123456789.1234m, SqlValueKind.Decimal));
        Assert.Equal("1.10", Literal(1.10m, SqlValueKind.Decimal));
        Assert.Equal("0", Literal(0m, SqlValueKind.Decimal));
    }

    // NUMERIC(18,0) comes back as an integral CLR type — exact, so it renders rather than refusing.
    [Fact]
    public void Decimal_Kind_Accepts_An_Integral_Value()
        => Assert.Equal("42", Literal(42L, SqlValueKind.Decimal));

    // A decimal-kind value delivered as a string: integer form (any size) and invariant fractional form
    // render exactly; a comma-decimal (pl-PL) is refused, not misread.
    [Fact]
    public void Decimal_Renders_A_Numeric_Delivered_As_A_String()
    {
        Assert.Equal("10019", Literal("10019", SqlValueKind.Decimal));
        Assert.Equal("123456789.1234", Literal("123456789.1234", SqlValueKind.Decimal));
        Assert.Equal("123456789012345678901234567890",
            Literal("123456789012345678901234567890", SqlValueKind.Decimal)); // beyond decimal's range
        Assert.Equal(SqlLiteralRefusal.UnexpectedValueType, Refusal("1,5", SqlValueKind.Decimal));
    }

    // ── Float ────────────────────────────────────────────────────────────────
    [Fact]
    public void Float_Round_Trips_Without_Noise_Digits()
    {
        // G17 would render this 0.10000000000000001 — shortest-round-trippable is both exact and clean.
        Assert.Equal("0.1", Literal(0.1d, SqlValueKind.Float));
        Assert.Equal("2.718281828459045", Literal(2.718281828459045d, SqlValueKind.Float));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Float_Refuses_A_Non_Finite_Value(double value)
        => Assert.Equal(SqlLiteralRefusal.NotRepresentable, Refusal(value, SqlValueKind.Float));

    // MEASURED against the engine, and unguessable: Firebird's literal parser flattens a SUBNORMAL
    // double to 0. `cast(5E-324 as double precision)` is ACCEPTED and returns 0 — the statement
    // succeeds and the value is gone. Silent data loss is exactly what §0 forbids, and no
    // string-equality test could ever have caught it (the literal we emit is perfectly well-formed).
    [Theory]
    [InlineData(double.Epsilon)]   // 5E-324
    [InlineData(1e-320)]
    [InlineData(1e-310)]
    public void Float_Refuses_A_Subnormal_Double_Because_The_Engine_Silently_Zeroes_It(double value)
        => Assert.Equal(SqlLiteralRefusal.NotRepresentable, Refusal(value, SqlValueKind.Float));

    // The boundary is exactly the normal/subnormal line — the smallest NORMAL double round-trips.
    [Fact]
    public void The_Subnormal_Boundary_Is_Exactly_The_Smallest_Normal_Double()
    {
        const double smallestNormal = 2.2250738585072014e-308;
        Assert.True(SqlLiteralWriter.Write(smallestNormal, SqlValueKind.Float).IsWritten);
        Assert.False(SqlLiteralWriter.Write(double.Epsilon, SqlValueKind.Float).IsWritten);
    }

    // …but a subnormal FLOAT is a perfectly NORMAL double, so it survives the parser and round-trips
    // exactly through a FLOAT column (verified). Refusing it would be a false refusal — which is why
    // the writer tests the value AS A DOUBLE rather than asking "is this float subnormal?".
    [Fact]
    public void Float_Accepts_A_Subnormal_Single_Because_It_Is_A_Normal_Double()
    {
        Assert.True(float.IsSubnormal(float.Epsilon));      // subnormal as a float…
        Assert.False(double.IsSubnormal(float.Epsilon));    // …but not as a double
        Assert.True(SqlLiteralWriter.Write(float.Epsilon, SqlValueKind.Float).IsWritten);
    }

    // ── Text ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Text_Doubles_The_Apostrophe()
    {
        Assert.Equal("'It''s'", Literal("It's", SqlValueKind.Text));
        Assert.Equal("''''", Literal("'", SqlValueKind.Text));
        Assert.Equal("''", Literal("", SqlValueKind.Text));
    }

    // Firebird has no backslash escapes in a standard literal — a `\` is a literal backslash, and
    // touching it would corrupt the value.
    [Fact]
    public void Text_Passes_Backslashes_And_Quotes_Through_Unescaped()
        => Assert.Equal(@"'It''s a ""test"" \ n'", Literal(@"It's a ""test"" \ n", SqlValueKind.Text));

    [Fact]
    public void Text_Passes_Unicode_Through_Unescaped()
        => Assert.Equal("'Zażółć gęślą jaźń 日本語'", Literal("Zażółć gęślą jaźń 日本語", SqlValueKind.Text));

    [Fact]
    public void Text_Kind_Refuses_A_Byte_Array()
        => Assert.Equal(SqlLiteralRefusal.UnexpectedValueType,
            Refusal(new byte[] { 1, 2 }, SqlValueKind.Text));

    // A VARCHAR value cannot outgrow the literal ceiling — its own column type stops at the same limit —
    // so the text path deliberately carries no size check.
    [Fact]
    public void Text_Has_No_Size_Ceiling()
        => Assert.True(SqlLiteralWriter.Write(new string('a', 100_000), SqlValueKind.Text).IsWritten);

    // ── Text BLOB ────────────────────────────────────────────────────────────
    [Fact]
    public void TextBlob_Quotes_Like_Text()
        => Assert.Equal("'it''s text'", Literal("it's text", SqlValueKind.TextBlob));

    [Fact]
    public void TextBlob_Refuses_Above_The_Ceiling()
    {
        var limits = new SqlLiteralLimits { MaxTextBlobChars = 8 };
        Assert.True(SqlLiteralWriter.Write("12345678", SqlValueKind.TextBlob, limits).IsWritten);
        Assert.Equal(SqlLiteralRefusal.TooLarge,
            SqlLiteralWriter.Write("123456789", SqlValueKind.TextBlob, limits).Refusal);
    }

    // MEASURED: the ceiling is in CHARACTERS, not bytes. On a UTF8 connection an ASCII string and a
    // two-byte-per-char string cap at the SAME 8,191 characters, because Firebird reserves 4 bytes per
    // UTF8 character regardless of content. A UTF-8 byte count — the intuitive choice — would have been
    // the wrong unit: it would refuse 4 Polish characters under a limit that actually admits 8,191.
    [Fact]
    public void TextBlob_Ceiling_Counts_Characters_Not_Bytes()
    {
        var limits = new SqlLiteralLimits { MaxTextBlobChars = 4 };
        Assert.True(SqlLiteralWriter.Write("ążść", SqlValueKind.TextBlob, limits).IsWritten); // 4 chars, 8 UTF-8 bytes
        Assert.Equal(SqlLiteralRefusal.TooLarge,
            SqlLiteralWriter.Write("ążśćx", SqlValueKind.TextBlob, limits).Refusal);
    }

    // The defaults are measured engine facts, not round numbers — if someone "tidies" them, that is a
    // behaviour change and should fail here.
    [Fact]
    public void The_Default_Ceilings_Are_The_Measured_Engine_Limits()
    {
        Assert.Equal(32752, SqlLiteralLimits.Default.MaxBinaryBytes); // hex bytes in a minimal statement
        Assert.Equal(8191, SqlLiteralLimits.Default.MaxTextBlobChars); // UTF8 worst case (32765/4)
    }

    // ── Date ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Date_Is_Quoted_Iso_With_No_Time_Part()
        => Assert.Equal("'2024-03-15'", Literal(new DateTime(2024, 3, 15), SqlValueKind.Date));

    // DATE and TIMESTAMP are the same CLR type, which is the whole reason SqlValueKind exists: the kind
    // decides, and a value that contradicts it is refused rather than silently truncated.
    [Fact]
    public void Date_Refuses_A_Value_Carrying_A_Time_Part()
        => Assert.Equal(SqlLiteralRefusal.NotRepresentable,
            Refusal(new DateTime(2024, 3, 15, 13, 45, 0), SqlValueKind.Date));

    [Fact]
    public void The_Same_Value_Renders_Differently_As_Date_And_As_Timestamp()
    {
        var midnight = new DateTime(2024, 3, 15);
        Assert.Equal("'2024-03-15'", Literal(midnight, SqlValueKind.Date));
        Assert.Equal("'2024-03-15 00:00:00.0000'", Literal(midnight, SqlValueKind.Timestamp));
    }

    // ── Time ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Time_Comes_From_A_TimeSpan_With_Fractional_Seconds()
        => Assert.Equal("'13:45:59.1234'",
            Literal(new TimeSpan(0, 13, 45, 59).Add(TimeSpan.FromTicks(1234000)), SqlValueKind.Time));

    [Fact]
    public void Time_Keeps_A_Zero_Fraction_Rather_Than_Dropping_It()
        => Assert.Equal("'00:00:00.0000'", Literal(TimeSpan.Zero, SqlValueKind.Time));

    [Fact]
    public void Time_Refuses_A_DateTime()
        => Assert.Equal(SqlLiteralRefusal.UnexpectedValueType,
            Refusal(new DateTime(2024, 3, 15, 13, 45, 0), SqlValueKind.Time));

    [Theory]
    [InlineData(-1)]        // negative
    [InlineData(24 * 60)]   // 24:00 — outside a Firebird TIME
    public void Time_Refuses_A_Value_Outside_A_Days_Clock(int minutes)
        => Assert.Equal(SqlLiteralRefusal.NotRepresentable,
            Refusal(TimeSpan.FromMinutes(minutes), SqlValueKind.Time));

    // ── Timestamp ────────────────────────────────────────────────────────────
    [Fact]
    public void Timestamp_Is_Space_Separated_Never_Iso_T()
    {
        var literal = Literal(new DateTime(2024, 3, 15, 13, 45, 59).AddTicks(1234000), SqlValueKind.Timestamp);
        Assert.Equal("'2024-03-15 13:45:59.1234'", literal);
        Assert.DoesNotContain("T", literal, StringComparison.Ordinal);
    }

    // The failure this pins: ToString() renders "2024-03-15 13:45:59" and the .1234 is gone for good.
    [Fact]
    public void Timestamp_Never_Drops_Fractional_Seconds()
        => Assert.EndsWith(".1234'",
            Literal(new DateTime(2024, 3, 15, 13, 45, 59).AddTicks(1234000), SqlValueKind.Timestamp),
            StringComparison.Ordinal);

    // 100 ns is finer than Firebird's 0.1 ms resolution, so `.ffff` would truncate it — no Firebird
    // column can produce such a value, and the writer refuses rather than losing the difference.
    [Theory]
    [InlineData(SqlValueKind.Timestamp)]
    [InlineData(SqlValueKind.Time)]
    public void A_Temporal_Finer_Than_Firebirds_Resolution_Is_Refused(SqlValueKind kind)
    {
        object value = kind == SqlValueKind.Time
            ? TimeSpan.FromTicks(1)
            : new DateTime(2024, 3, 15).AddTicks(1);
        Assert.Equal(SqlLiteralRefusal.NotRepresentable, Refusal(value, kind));
    }

    // ── Boolean ──────────────────────────────────────────────────────────────
    [Fact]
    public void Boolean_Is_Bare_And_Lowercase()
    {
        Assert.Equal("true", Literal(true, SqlValueKind.Boolean));
        Assert.Equal("false", Literal(false, SqlValueKind.Boolean));
    }

    // ── Values delivered as strings (an environment that stringifies every cell) ──
    // Same measured cause as the integer case: the driver handed the value back as a string. Each kind
    // parses its own INVARIANT form and renders the correct literal; a culture-formatted string is
    // refused, never reinterpreted (§0).
    [Fact]
    public void Timestamp_Accepts_An_Invariant_String()
    {
        Assert.Equal("'2024-03-15 13:45:59.1234'",
            Literal("2024-03-15 13:45:59.1234", SqlValueKind.Timestamp));
        Assert.Equal("'2024-03-15 00:00:00.0000'", Literal("2024-03-15", SqlValueKind.Timestamp));
    }

    [Fact]
    public void Date_Accepts_An_Invariant_String_And_Refuses_A_Time_Part()
    {
        Assert.Equal("'2024-03-15'", Literal("2024-03-15", SqlValueKind.Date));
        Assert.Equal(SqlLiteralRefusal.NotRepresentable, Refusal("2024-03-15 13:00:00", SqlValueKind.Date));
    }

    [Fact]
    public void Time_Accepts_An_Invariant_String()
        => Assert.Equal("'13:45:59.1234'", Literal("13:45:59.1234", SqlValueKind.Time));

    [Fact]
    public void Boolean_Accepts_A_String()
    {
        Assert.Equal("true", Literal("true", SqlValueKind.Boolean));
        Assert.Equal("false", Literal("False", SqlValueKind.Boolean));
        Assert.Equal(SqlLiteralRefusal.UnexpectedValueType, Refusal("yes", SqlValueKind.Boolean));
    }

    // A pl-PL-formatted temporal string must NOT be coerced into a different date — refuse, don't guess.
    [Fact]
    public void Temporal_Refuses_A_Non_Invariant_String()
        => Assert.Equal(SqlLiteralRefusal.UnexpectedValueType, Refusal("15.03.2024", SqlValueKind.Date));

    // ── Binary BLOB ──────────────────────────────────────────────────────────
    // The corruption this replaces: ExportValueFormatter renders a byte[] as "(BLOB)", which in an
    // INSERT becomes the string literal '(BLOB)' — a silently wrong row.
    [Fact]
    public void BinaryBlob_Is_A_Hex_Literal_Not_A_Placeholder()
    {
        Assert.Equal("x'DEADBEEF00FF'",
            Literal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0xFF }, SqlValueKind.BinaryBlob));
        Assert.DoesNotContain("BLOB)", Literal(new byte[] { 1 }, SqlValueKind.BinaryBlob), StringComparison.Ordinal);
    }

    [Fact]
    public void BinaryBlob_Refuses_Above_The_Ceiling()
    {
        var limits = new SqlLiteralLimits { MaxBinaryBytes = 4 };
        Assert.True(SqlLiteralWriter.Write(new byte[4], SqlValueKind.BinaryBlob, limits).IsWritten);
        Assert.Equal(SqlLiteralRefusal.TooLarge,
            SqlLiteralWriter.Write(new byte[5], SqlValueKind.BinaryBlob, limits).Refusal);
    }

    // Truncating would be silent corruption, so the refusal must carry no partial literal at all.
    [Fact]
    public void An_Oversized_Blob_Yields_No_Partial_Literal()
    {
        var r = SqlLiteralWriter.Write(new byte[10], SqlValueKind.BinaryBlob, new SqlLiteralLimits { MaxBinaryBytes = 4 });
        Assert.Null(r.Literal);
    }

    [Fact]
    public void BinaryBlob_Refuses_A_String()
        => Assert.Equal(SqlLiteralRefusal.UnexpectedValueType, Refusal("DEADBEEF", SqlValueKind.BinaryBlob));

    // ── Result contract ──────────────────────────────────────────────────────
    [Fact]
    public void A_Refusal_Never_Carries_A_Literal_And_A_Written_Result_Always_Does()
    {
        var refused = SqlLiteralResult.Refused(SqlLiteralRefusal.TooLarge);
        Assert.False(refused.IsWritten);
        Assert.Null(refused.Literal);

        var written = SqlLiteralResult.Written("1");
        Assert.True(written.IsWritten);
        Assert.Equal(SqlLiteralRefusal.None, written.Refusal);
    }
}

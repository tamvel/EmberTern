using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using EmberTern.Core.Connections;
using EmberTern.Core.Import;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Phase 5 — the charset guard's regression net.
///
/// <para>⭐ <b>These tests pin the GUARANTEE, not the implementation.</b> The guarantee is:
/// <i>EmberTern refuses exactly the text the driver would silently rewrite, and refuses it before the driver
/// sees it.</i> A test that merely asserted "the guard throws" would stay green if the oracle drifted, if a
/// charset stopped being covered, or if a driver upgrade changed the encoder — which are the three ways this
/// can actually break.</para>
///
/// <para>⚠ Reflection on the driver's internals appears in <see cref="TheOracleAgreesWithTheDriversOwnEncoder"/>
/// and nowhere else, and deliberately so: the TEST may establish what the driver does, the PRODUCT may not
/// depend on it. <c>CharsetCatalog.ResolveWireEncoding</c> mirrors the driver through supported APIs only, and
/// that test is what keeps the mirror honest.</para>
/// </summary>
public class CharsetGuardTests
{
    // The range the offline measurement covered. Surrogate code units are excluded because a LONE surrogate is
    // not text — it is malformed UTF-16 that no EmberTern input path (editor, file decode, grid) can produce,
    // and encoders treat it as an error rather than as a representable character.
    private const int FirstCodePoint = 0x20;
    private const int LastCodePoint = 0x2FFF;

    private static IEnumerable<string> CodePoints()
    {
        for (var cp = FirstCodePoint; cp <= LastCodePoint; cp++)
        {
            if (char.IsSurrogate((char)cp)) continue;
            yield return char.ConvertFromUtf32(cp);
        }
    }

    /// <summary>Every charset the connection dialog offers.</summary>
    public static TheoryData<string> CataloguedCharsets()
    {
        var data = new TheoryData<string>();
        foreach (var c in CharsetCatalog.Supported) data.Add(c);
        return data;
    }

    // ── T1 ────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// ⭐⭐ <b>T1 — the oracle is exactly the driver's own encoder.</b>
    ///
    /// <para>The whole design rests on one claim: what our guard refuses is precisely what the driver would
    /// damage. This asserts it against the driver's REAL charset table rather than against our own
    /// restatement of it — so it fails if the driver changes its encoder, if a charset is added to
    /// <see cref="CharsetCatalog.Supported"/> without a wire mapping, or if <c>ResolveWireEncoding</c> drifts.</para>
    ///
    /// <para>⚠ Two failure directions, and both matter. A MISS is silent data loss (the defect). A FALSE
    /// POSITIVE refuses a perfectly good operation, which is the way a safety feature gets switched off.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(CataloguedCharsets))]
    public void TheOracleAgreesWithTheDriversOwnEncoder(string charset)
    {
        var driverEncoding = DriverEncodingFor(charset);
        Assert.NotNull(driverEncoding);

        var strict = CharsetRepresentation.Strict(charset);

        var missed = new List<string>();
        var falsePositives = new List<string>();

        foreach (var s in CodePoints())
        {
            // What the DRIVER would actually put on the wire and get back.
            var damaged = driverEncoding!.GetString(driverEncoding.GetBytes(s)) != s;
            var refused = !CharsetRepresentation.CanRepresent(s, strict);

            if (damaged && !refused) missed.Add($"U+{char.ConvertToUtf32(s, 0):X4} '{s}'");
            else if (!damaged && refused) falsePositives.Add($"U+{char.ConvertToUtf32(s, 0):X4} '{s}'");
        }

        Assert.True(
            missed.Count == 0,
            $"[{charset}] the driver would silently rewrite these, and the guard would let them through — "
            + $"this is the exact defect Phase 5 exists to stop ({missed.Count}): "
            + string.Join(", ", missed.Take(20)));

        Assert.True(
            falsePositives.Count == 0,
            $"[{charset}] the guard refuses text the driver carries perfectly well ({falsePositives.Count}): "
            + string.Join(", ", falsePositives.Take(20)));
    }

    /// <summary>
    /// The driver's own <c>Encoding</c> for a charset name, read from its internal table.
    /// ⛔ Test-only. See the class remarks.
    /// </summary>
    private static Encoding? DriverEncodingFor(string charset)
    {
        // Touch the service so the code-pages provider is registered exactly as at runtime.
        using var _ = new FirebirdConnectionService();

        var charsetType = typeof(FbConnection).Assembly.GetType("FirebirdSql.Data.Common.Charset");
        Assert.NotNull(charsetType);

        var table = (IDictionary?)charsetType!
            .GetField("charsetsByName", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null);
        Assert.NotNull(table);

        var entry = table![charset];
        Assert.True(entry is not null, $"the driver does not know charset '{charset}'");

        return charsetType.GetProperty("Encoding")!.GetValue(entry) as Encoding;
    }

    // ── T2 ────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// ⭐ <b>T2 — the best-fit class is detected, not just the '?' class.</b>
    ///
    /// <para>This is the finding that changed the shape of the fix. WIN1250 does not turn every foreign
    /// character into <c>?</c>: 330 of them become a different, ORDINARY-LOOKING character — <c>£</c>→<c>L</c>,
    /// <c>¼</c>→<c>1</c>, <c>À</c>→<c>A</c>. Live, a procedure body <c>R = 'Cena £100 ¼ À'</c> was stored as
    /// <c>R = 'Cena L100 1 A'</c>: it compiles and reads as correct code. A guard written against the audit's
    /// original "turns into '?'" description would be green and useless here.</para>
    /// </summary>
    [Theory]
    [InlineData("£")]   // -> 'L'
    [InlineData("À")]   // -> 'A'
    [InlineData("¼")]   // -> '1'
    [InlineData("²")]   // -> '2'
    [InlineData("¥")]   // -> 'Y'
    [InlineData("Ж")]   // -> '?'  (the class the audit measured)
    [InlineData("日")]  // -> '?'
    public void BestFitSubstitutionIsRefusedJustLikeAQuestionMark(string character)
    {
        var strict = CharsetRepresentation.Strict("WIN1250");

        Assert.False(
            CharsetRepresentation.CanRepresent(character, strict),
            $"'{character}' is silently rewritten by WIN1250 and must be refused");

        // And the loss it stands for is real, stated here so the test explains itself when it fails.
        var lossy = CharsetCatalog.ResolveWireEncoding("WIN1250");
        Assert.NotEqual(character, lossy.GetString(lossy.GetBytes(character)));
    }

    // ── T3 ────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// ⭐⭐ <b>T3 — <c>NONE</c> is a lossy single-byte charset, not UTF-8.</b>
    ///
    /// <para>This one was RED before Phase 5 and is the reason the wire question had to be separated from the
    /// decode question. <c>CharsetCatalog.Resolve("NONE")</c> answers <c>Encoding.UTF8</c> — fine for decoding
    /// bytes of unknown origin, catastrophic as a guard, because UTF-8 short-circuits every representability
    /// check. <c>NONE</c> is in <see cref="CharsetCatalog.Supported"/>, so it is reachable from the connection
    /// dialog, and the shipped import guard was blind on it.</para>
    /// </summary>
    [Fact]
    public void NoneIsTreatedAsTheLossySingleByteCharsetTheDriverActuallyUses()
    {
        var wire = CharsetCatalog.ResolveWireEncoding("NONE");

        Assert.NotEqual(Encoding.UTF8.CodePage, wire.CodePage);
        Assert.False(
            CharsetRepresentation.CanRepresent("Ж", CharsetRepresentation.Strict("NONE")),
            "a NONE connection cannot carry 'Ж', and saying it can is how import silently corrupted data");

        // ⚠ The decode question is deliberately UNCHANGED — Resolve still answers UTF-8 for NONE, and that is
        // correct for its own job. This asserts the SPLIT, which is the design.
        Assert.Equal(Encoding.UTF8.CodePage, CharsetCatalog.Resolve("NONE").CodePage);
    }

    // ── T4 ────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// <b>T4 — a representable character is not blocked.</b> The guard's value is entirely destroyed if it
    /// refuses ordinary work; Polish text on a WIN1250 connection is the everyday case for this product's user.
    /// </summary>
    [Theory]
    [InlineData("WIN1250", "Zażółć gęślą jaźń ĄĆĘŁŃÓŚŹŻ")]
    [InlineData("WIN1250", "SELECT * FROM KLIENCI WHERE NAZWA = 'Łódź'")]
    [InlineData("ISO8859_2", "Zażółć gęślą jaźń")]
    [InlineData("WIN1252", "Café résumé naïve")]
    [InlineData("WIN1250", "")]
    public void RepresentableTextPassesUntouched(string charset, string text)
    {
        Assert.True(CharsetRepresentation.CanRepresent(text, CharsetRepresentation.Strict(charset)));
        Assert.Null(CharsetRepresentation.FindFirstUnrepresentable(text, CharsetRepresentation.Strict(charset)));
    }

    // ── T5 ────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary><b>T5 — UTF8 never blocks anything.</b> It represents every character, so a refusal on a UTF8
    /// connection could only ever be a bug in the guard.</summary>
    [Theory]
    [InlineData("Ж 日 £100 ¼ À")]
    [InlineData("Zażółć gęślą jaźń")]
    [InlineData("𝔘𝔫𝔦𝔠𝔬𝔡𝔢 ponad BMP 😀")]
    public void Utf8NeverRefusesAnything(string text)
    {
        Assert.True(CharsetRepresentation.CanRepresent(text, CharsetRepresentation.Strict("UTF8")));
    }

    // ── T7 ────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// ⭐ <b>T7 — statement text and bound parameters are guarded by the SAME mechanism.</b>
    ///
    /// <para>Both were measured to lose data identically on a live server, and a guard that covered only one
    /// would leave the other silently open. Driven through the real seam on a real <see cref="FbConnection"/> —
    /// unopened, because the guard runs entirely client-side and must refuse before any connection exists.
    /// That is itself part of the guarantee: the refusal happens BEFORE the driver.</para>
    /// </summary>
    [Fact]
    public void StatementTextAndParametersAreBothRefused_ByTheSameSeam()
    {
        using var connection = Win1250Connection();

        var fromStatement = Assert.Throws<CharsetRepresentationException>(
            () => connection.CreateGuardedCommand("SELECT 'Ж' FROM RDB$DATABASE"));

        using var cmd = connection.CreateGuardedCommand("SELECT * FROM T WHERE C = @p");
        var fromParameter = Assert.Throws<CharsetRepresentationException>(
            () => cmd.AddGuardedParameter("@p", "Ж"));

        // Both name the character and the charset — the message has to be actionable, since we refuse rather
        // than repair and the user is the one who has to decide what to do.
        Assert.Contains("Ж", fromStatement.Message, StringComparison.Ordinal);
        Assert.Contains("WIN1250", fromStatement.Message, StringComparison.Ordinal);
        Assert.Contains("Ж", fromParameter.Message, StringComparison.Ordinal);
        Assert.Contains("@p", fromParameter.Message, StringComparison.Ordinal);

        // Localizable form present on both (D-3: Core/Firebird hand up a key + data, App makes the sentence).
        Assert.Equal("Charset.Unrepresentable.Statement", fromStatement.Localized.Key.Value);
        Assert.Equal("Charset.Unrepresentable.Parameter", fromParameter.Localized.Key.Value);
    }

    /// <summary>The everyday case must survive the same seam untouched — including a bound Polish value.</summary>
    [Fact]
    public void TheSeamLetsOrdinaryPolishTextThrough()
    {
        using var connection = Win1250Connection();

        using var cmd = connection.CreateGuardedCommand("SELECT * FROM KLIENCI WHERE NAZWA = @p");
        cmd.AddGuardedParameter("@p", "Zażółć gęślą jaźń");

        Assert.Equal("SELECT * FROM KLIENCI WHERE NAZWA = @p", cmd.CommandText);
        Assert.Single(cmd.Parameters);
    }

    /// <summary>A UTF8 connection carries the same text the WIN1250 one refuses — the guard follows the
    /// CONNECTION, which is the thing that actually does the encoding.</summary>
    [Fact]
    public void TheSeamFollowsTheConnectionsOwnCharset()
    {
        using var utf8 = new FbConnection(ConnectionString("UTF8"));
        using var cmd = utf8.CreateGuardedCommand("SELECT 'Ж 日 £' FROM RDB$DATABASE");
        cmd.AddGuardedParameter("@p", "Ж 日 £");

        Assert.Single(cmd.Parameters);
    }

    /// <summary>A character outside the BMP is reported WHOLE, not as half a surrogate pair.</summary>
    [Fact]
    public void AnAstralCharacterIsReportedWhole()
    {
        var violation = CharsetRepresentation.FindFirstUnrepresentable(
            "AB😀CD", CharsetRepresentation.Strict("WIN1250"));

        Assert.NotNull(violation);
        Assert.Equal("😀", violation!.Value.Text);
        Assert.Equal(2, violation.Value.Index);
        Assert.Equal("U+1F600", violation.Value.CodePoint);
    }

    /// <summary>The offender is located precisely — the message says WHERE, so the user can fix it.</summary>
    [Fact]
    public void TheFirstOffenderIsLocatedExactly()
    {
        var violation = CharsetRepresentation.FindFirstUnrepresentable(
            "Zażółć £100", CharsetRepresentation.Strict("WIN1250"));

        Assert.NotNull(violation);
        Assert.Equal("£", violation!.Value.Text);
        Assert.Equal(7, violation.Value.Index);
    }

    // ── T8 ────────────────────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// <b>T8 — import keeps its contract and loses its <c>NONE</c> blind spot.</b>
    /// <para>The forwarding must not change what import DOES with an unrepresentable value (one failed row,
    /// carrying its source row number), only what it can SEE.</para>
    /// </summary>
    [Fact]
    public void ImportGuardForwardsToTheSharedOracle_IncludingNone()
    {
        // The hole: before Phase 5 this said "fits" and the driver rewrote it.
        Assert.False(ImportCharsetGuard.CanRepresent("Ж", ImportCharsetGuard.Strict("NONE")));

        // Unchanged behaviour on the charsets it already handled.
        Assert.False(ImportCharsetGuard.CanRepresent("Ж", ImportCharsetGuard.Strict("WIN1250")));
        Assert.True(ImportCharsetGuard.CanRepresent("Zażółć", ImportCharsetGuard.Strict("WIN1250")));
        Assert.True(ImportCharsetGuard.CanRepresent("Ж", ImportCharsetGuard.Strict("UTF8")));

        // And the two owners now give the same answer, which is the point of forwarding.
        foreach (var charset in CharsetCatalog.Supported)
        {
            foreach (var sample in new[] { "Ж", "£", "Zażółć", "plain" })
            {
                Assert.Equal(
                    CharsetRepresentation.CanRepresent(sample, CharsetRepresentation.Strict(charset)),
                    ImportCharsetGuard.CanRepresent(sample, ImportCharsetGuard.Strict(charset)));
            }
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    private static FbConnection Win1250Connection() => new(ConnectionString("WIN1250"));

    // Never opened: the guard is entirely client-side, so the whole net runs without a server. The port is
    // deliberately unreachable so an accidental Open() fails loudly rather than touching a real database.
    private static string ConnectionString(string charset) => new FbConnectionStringBuilder
    {
        DataSource = "127.0.0.1",
        Port = 1,
        Database = "guard-tests.fdb",
        UserID = "SYSDBA",
        Password = "x",
        Charset = charset,
        Dialect = 3,
        Pooling = false,
    }.ToString();
}

using System;
using System.Collections.Generic;
using System.Text;

namespace EmberTern.Core.Connections;

public static class CharsetCatalog
{
    static CharsetCatalog()
    {
        // WIN1250 / WIN1252 / ISO8859_2 etc. require the code-pages provider on .NET.
        // Firebird's connection service also registers this, but Resolve() can be called
        // from contexts (like the DDL reader's blob decoder) before any connection is made.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyList<string> Supported { get; } = new[]
    {
        "UTF8",
        "WIN1250",
        "WIN1252",
        "ISO8859_1",
        "ISO8859_2",
        "NONE",
    };

    public const string Default = "WIN1250";

    // Maps a Firebird charset name (as stored in ConnectionProfile.Charset) to a .NET Encoding.
    // Used when we need to decode bytes ourselves (e.g. system catalog source BLOBs whose stored
    // bytes may not match the connection charset). Falls back to UTF-8 for unknown / NONE.
    //
    // The UTF16LE / UTF16BE names are NOT Firebird connection charsets and deliberately do not appear
    // in Supported (the connection picker's list). They exist because this is the one owner of
    // "charset name -> Encoding" in the codebase, and Data Import has to decode FILES, whose byte-order
    // mark can legitimately say UTF-16. A second name->Encoding map elsewhere is how the two would drift.
    public static Encoding Resolve(string? firebirdCharset) => (firebirdCharset ?? "").ToUpperInvariant() switch
    {
        "UTF8" or "UNICODE_FSS" => Encoding.UTF8,
        "UTF16LE" => Encoding.Unicode,
        "UTF16BE" => Encoding.BigEndianUnicode,
        "WIN1250" => Encoding.GetEncoding("windows-1250"),
        "WIN1251" => Encoding.GetEncoding("windows-1251"),
        "WIN1252" => Encoding.GetEncoding("windows-1252"),
        "WIN1253" => Encoding.GetEncoding("windows-1253"),
        "WIN1254" => Encoding.GetEncoding("windows-1254"),
        "WIN1257" => Encoding.GetEncoding("windows-1257"),
        "ISO8859_1" => Encoding.GetEncoding("iso-8859-1"),
        "ISO8859_2" => Encoding.GetEncoding("iso-8859-2"),
        _ => Encoding.UTF8,
    };

    /// <summary>
    /// ⭐ <b>A DIFFERENT QUESTION from <see cref="Resolve"/>, and the distinction is load-bearing.</b>
    /// <para>
    /// <see cref="Resolve"/> answers <i>"what do I DECODE these bytes with"</i> — bytes we already hold, whose
    /// origin we are guessing (a catalog source BLOB, an imported file). This answers
    /// <i>"what will the DRIVER ENCODE my text with when it puts it on the wire"</i>. Two questions, two
    /// owners; merging them is not a tidy-up, it is the defect below.
    /// </para>
    /// <para>
    /// ⚠⚠ <b><c>NONE</c> is why this exists.</b> <see cref="Resolve"/> sends every unknown name — including
    /// <c>NONE</c>, which is in <see cref="Supported"/> and therefore reachable from the connection dialog — down
    /// its <c>_ =&gt; Encoding.UTF8</c> branch. That is a defensible answer for decoding. It is a WRONG answer for
    /// the wire: measured against FirebirdClient 10.3.4, the driver's <c>NONE</c> charset carries the
    /// <b>ANSI code page of the process culture</b> — cp1250 under <c>pl-PL</c>, cp1251 under <c>ru-RU</c>,
    /// cp932 under <c>ja-JP</c>, cp1252 under <c>en-US</c> (7 cultures measured). It is single-byte and lossy,
    /// and it differs from machine to machine. A guard built on <see cref="Resolve"/> would conclude "UTF-8
    /// represents everything, nothing to check" and wave through exactly the corruption it exists to stop.
    /// </para>
    /// <para>
    /// ⭐ <b>Supported APIs only.</b> The driver's own charset table is <c>internal sealed</c> with no hook, so
    /// this MIRRORS it rather than reading it; production must never take a reflective dependency on the
    /// driver's internals. The mirror is not trusted on faith — <c>CharsetWireEncodingTests</c> compares it
    /// against the driver's actual encoder for every catalogued charset, so a driver upgrade that moved this
    /// fails the build instead of silently re-opening the hole.
    /// </para>
    /// <para>
    /// The ANSI code page is captured ONCE rather than read per call: the driver's table is a static built at
    /// its type initializer, so its answer is frozen at process start and ours must be too. (EmberTern's live
    /// language switch does not move it — <c>Loc</c> keeps its own <c>Culture</c> for resource lookup and never
    /// assigns <see cref="System.Globalization.CultureInfo.CurrentCulture"/>.)
    /// </para>
    /// </summary>
    public static Encoding ResolveWireEncoding(string? firebirdCharset)
        => (firebirdCharset ?? "").ToUpperInvariant() switch
        {
            // ⛔ NOT the Resolve default. See the remarks above.
            "NONE" or "" => AnsiCodePageEncoding,
            "OCTETS" => AnsiCodePageEncoding,
            _ => Resolve(firebirdCharset),
        };

    /// <summary>
    /// The process culture's ANSI code page, resolved once — see <see cref="ResolveWireEncoding"/>.
    /// <para>
    /// ⚠⚠ <b><see cref="Lazy{T}"/> is not a micro-optimisation, it is a correctness fix, and it was caught by
    /// <c>CharsetGuardTests.TheOracleAgreesWithTheDriversOwnEncoder</c> rather than by review.</b> C# runs static
    /// FIELD INITIALIZERS before the static CONSTRUCTOR body — and the constructor is what registers
    /// <see cref="CodePagesEncodingProvider"/>. Resolving eagerly therefore ran <see cref="Encoding.GetEncoding(int)"/>
    /// before code page 1250 existed, it threw, and the fallback below quietly became the answer: the guard
    /// then measured <c>NONE</c> against the wrong code page and let <b>74 characters through that the driver
    /// rewrites</b>. A silent weakening of the guard, with a green build. Deferring the lookup to first ACCESS
    /// puts it safely after type initialisation.
    /// </para>
    /// </summary>
    private static readonly Lazy<Encoding> LazyAnsiCodePage = new(ResolveAnsiCodePage);

    private static Encoding AnsiCodePageEncoding => LazyAnsiCodePage.Value;

    private static Encoding ResolveAnsiCodePage()
    {
        try
        {
            return Encoding.GetEncoding(
                System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Unreachable in practice — the driver builds its own NONE charset from the same code page, so if
            // this fails the connection could not be made either. Kept because the alternative to answering is
            // throwing during type initialisation.
            //
            // ⛔ NOT Encoding.UTF8: that would short-circuit every representability check and re-create the
            // exact blindness this method exists to remove. ASCII is the conservative answer — it refuses
            // everything it cannot prove safe, which is the right failure direction for a guard.
            return Encoding.ASCII;
        }
    }
}

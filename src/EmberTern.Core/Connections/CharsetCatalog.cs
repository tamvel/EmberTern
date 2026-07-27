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
}

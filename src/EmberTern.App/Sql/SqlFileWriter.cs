using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.App.Sql;

/// <summary>
/// Writes a <c>.sql</c> script to disk as <b>UTF-8 WITHOUT a BOM</b>. The no-BOM choice is
/// deliberate and load-bearing: a UTF-8 BOM (<c>EF BB BF</c>) prepended to the first
/// statement makes Firebird <c>isql</c> and IBExpert's script executor choke on the first
/// line, while every target editor (Notepad / Notepad++ / VS Code) reads no-BOM UTF-8
/// correctly. <b>Do NOT use <see cref="Encoding.UTF8"/> here</b> — that is
/// <c>UTF8Encoding(true)</c> and emits a BOM. Polish (and any Unicode) characters round-trip
/// losslessly because the in-memory string is already correct UTF-16.
/// </summary>
public static class SqlFileWriter
{
    public static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
        => System.IO.File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken);
}

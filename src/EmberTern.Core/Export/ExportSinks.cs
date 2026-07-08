using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EmberTern.Core.Export;

/// <summary>Writes the export to a file. Exposes both a byte <see cref="Stream"/> (for the binary XLSX
/// exporter) and a text <see cref="Writer"/> (for CSV/TXT). The <paramref name="encoding"/> decides the
/// text BOM — CSV passes UTF-8 <b>with</b> BOM (Excel needs it to read UTF-8 CSV; without it Polish
/// chars mojibake), Text passes UTF-8 without BOM. This is the opposite of the <c>.sql</c> no-BOM rule;
/// encoding is deliberately per-format, chosen by the caller, never global. The <see cref="Writer"/> is
/// created lazily (never for a binary export) over the shared <see cref="Stream"/>, so only one surface
/// is used per export.</summary>
public sealed class FileExportSink : IExportSink
{
    private readonly FileStream _stream;
    private readonly Encoding _encoding;
    private StreamWriter? _writer;

    public FileExportSink(string path, Encoding encoding)
    {
        // ReadWrite (not write-only): the binary XLSX package reads back the zip directory while
        // writing, so it requires a readable+seekable stream. Text writing is unaffected.
        _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        _encoding = encoding;
    }

    public TextWriter Writer => _writer ??= new StreamWriter(_stream, _encoding, bufferSize: 4096, leaveOpen: true);

    public Stream Stream => _stream;

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null)
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
        }
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Collects the export into an in-memory string — used for the Clipboard format. Text-only:
/// <see cref="Stream"/> throws (a binary format cannot target the clipboard). Disposing only flushes (a
/// <c>StringWriter</c> needs no real close), so <see cref="Text"/> stays readable after the
/// <c>await using</c> scope ends.</summary>
public sealed class StringExportSink : IExportSink
{
    private readonly StringWriter _writer = new();

    public TextWriter Writer => _writer;

    public Stream Stream =>
        throw new System.NotSupportedException("The clipboard/string sink is text-only; binary formats require a file sink.");

    public string Text => _writer.ToString();

    public ValueTask DisposeAsync()
    {
        _writer.Flush();
        return ValueTask.CompletedTask;
    }
}

using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace EmberTern.Core.Export;

/// <summary>Writes the export to a file. The <paramref name="encoding"/> decides the BOM — CSV passes
/// UTF-8 <b>with</b> BOM (Excel needs it to read UTF-8 CSV; without it Polish chars mojibake), Text
/// passes UTF-8 without BOM. This is the opposite of the <c>.sql</c> no-BOM rule; encoding is
/// deliberately per-format, chosen by the caller, never global.</summary>
public sealed class FileExportSink : IExportSink
{
    private readonly StreamWriter _writer;

    public FileExportSink(string path, Encoding encoding)
    {
        _writer = new StreamWriter(path, append: false, encoding);
    }

    public TextWriter Writer => _writer;

    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}

/// <summary>Collects the export into an in-memory string — used for the Clipboard format. Disposing
/// only flushes (a <c>StringWriter</c> needs no real close), so <see cref="Text"/> stays readable
/// after the <c>await using</c> scope ends.</summary>
public sealed class StringExportSink : IExportSink
{
    private readonly StringWriter _writer = new();

    public TextWriter Writer => _writer;

    public string Text => _writer.ToString();

    public ValueTask DisposeAsync()
    {
        _writer.Flush();
        return ValueTask.CompletedTask;
    }
}

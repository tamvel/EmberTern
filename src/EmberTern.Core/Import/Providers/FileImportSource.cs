using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Import.Providers;

/// <summary>
/// A file on disk. Supports both surfaces: text for the delimited providers, bytes for the spreadsheet ones.
/// <para>
/// Opens with <see cref="FileShare.ReadWrite"/> so a workbook the user still has open in Excel can be imported —
/// the common case, and refusing it would be a self-inflicted limitation. Nothing here ever writes.
/// </para>
/// </summary>
public sealed class FileImportSource : IImportSource
{
    private readonly string _path;

    public FileImportSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        _path = path;
    }

    public string Path => _path;

    public string DisplayName => System.IO.Path.GetFileName(_path);

    public long? SizeBytes
    {
        get
        {
            try
            {
                var info = new FileInfo(_path);
                return info.Exists ? info.Length : null;
            }
            // A path we cannot stat is simply a source of unknown size; progress copes (no percentage).
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }

    public bool StillExists()
    {
        try
        {
            return File.Exists(_path);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public Task<TextReader> OpenTextAsync(Encoding encoding, CancellationToken cancellationToken)
    {
        if (encoding is null) throw new ArgumentNullException(nameof(encoding));
        cancellationToken.ThrowIfCancellationRequested();

        var stream = OpenRead();
        // detectEncodingFromByteOrderMarks: true so a BOM is consumed rather than delivered as data in the
        // first field — the encoding itself is the caller's declared choice (§0.4), this only strips the mark.
        TextReader reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        return Task.FromResult(reader);
    }

    public Task<Stream> OpenStreamAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Stream>(OpenRead());
    }

    /// <summary>Reads the first bytes for <see cref="EncodingDetector"/>. Returns an empty span's worth for a
    /// missing or unreadable file rather than throwing — detection is advisory, and the readiness check is what
    /// reports an unreachable source.</summary>
    public byte[] ReadDetectionSample(int maxBytes = EncodingDetector.SampleBytes)
    {
        try
        {
            using var stream = OpenRead();
            var buffer = new byte[maxBytes];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == buffer.Length) return buffer;
            var exact = new byte[read];
            Array.Copy(buffer, exact, read);
            return exact;
        }
        catch (IOException) { return Array.Empty<byte>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<byte>(); }
    }

    private FileStream OpenRead()
        => new(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
}

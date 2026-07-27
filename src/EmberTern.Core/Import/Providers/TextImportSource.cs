using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.Core.Import.Providers;

/// <summary>
/// Text already in memory — the clipboard's source, and the one that makes every pipeline test runnable without
/// touching the disk.
/// <para>
/// This type is the whole reason the clipboard is <b>not</b> a second parser (design §1.5): App reads the
/// clipboard (Avalonia types stay in App, rule #1), hands Core a <see cref="string"/>, and the same delimited
/// provider that reads a CSV reads it. Text-only by nature, so the byte surface throws — the same honest split
/// <c>StringExportSink</c> uses on the export side.
/// </para>
/// </summary>
public sealed class TextImportSource : IImportSource
{
    private readonly string _text;

    public TextImportSource(string text, string? displayName = null)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Clipboard" : displayName;
    }

    /// <summary>Label for the UI. Not a path — this source has none.</summary>
    public string DisplayName { get; }

    /// <summary>Character count, not a byte count: nothing was encoded, so bytes would be a fabrication. It is
    /// only ever used as a progress denominator, and characters serve that just as well.</summary>
    public long? SizeBytes => _text.Length;

    /// <summary>Always true — the text is held, so it cannot go missing between configuring and reading. That is
    /// also why a clipboard configuration stores no path (§4.8.2).</summary>
    public bool StillExists() => true;

    public Task<TextReader> OpenTextAsync(Encoding encoding, CancellationToken cancellationToken)
    {
        // encoding is irrelevant here and deliberately ignored: the text is already decoded. Accepting the
        // parameter keeps ONE port shape for both origins instead of splitting the interface.
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<TextReader>(new StringReader(_text));
    }

    public Task<Stream> OpenStreamAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException(
            "A text source has no byte surface — a spreadsheet cannot be imported from the clipboard.");

    /// <summary>The held text, for the detector paths that work on a string rather than a reader.</summary>
    public string Text => _text;
}

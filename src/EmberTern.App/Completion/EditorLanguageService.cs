using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit;
using EmberTern.Core.Sql;

namespace EmberTern.App.Completion;

/// <summary>
/// Per-editor async language service (Etap 0 of the editor rebuild — design §7 / §15).
/// Owns the debounced, cancellable, off-thread work that used to run on every
/// keystroke on the UI thread. Today that work is exactly one thing —
/// <see cref="SqlAliasResolver.ParseAliases"/> over the whole document — so this
/// service caches the alias map and refreshes it on idle. Later etaps replace the
/// cached content with a full Lexer → Parser → AST → Semantic Model, but the
/// debounce/cancel/cache/marshal shape stays.
/// </summary>
/// <remarks>
/// All state (<see cref="_aliases"/>, the version counters, <see cref="_cts"/>) is
/// touched only on the UI thread: the debounce timer ticks on the UI thread, the
/// background parse resumes on the captured UI context (<c>ConfigureAwait(true)</c>),
/// and every reader (<see cref="Aliases"/>, <see cref="EnsureFreshAliases"/>) is
/// called from completion, which runs on the UI thread. So no locking is needed —
/// only the pure <see cref="SqlAliasResolver.ParseAliases"/> call runs off-thread,
/// on a captured string.
/// </remarks>
internal sealed class EditorLanguageService : IDisposable
{
    /// <summary>Idle delay before a background re-parse runs (design §4.3/§15).</summary>
    internal static readonly TimeSpan ParseDebounce = TimeSpan.FromMilliseconds(300);

    private static readonly IReadOnlyDictionary<string, string> EmptyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly TextEditor _editor;
    private readonly DispatcherTimer _debounce;

    private IReadOnlyDictionary<string, string> _aliases = EmptyAliases;
    private CancellationTokenSource? _cts;
    // Monotonic edit counter and the counter the cached map reflects. Start the
    // cache "stale" (-1 < 0) so the first deliberate trigger parses on demand
    // even if no edit has been observed yet (e.g. text set before we attached).
    private long _changeVersion;
    private long _aliasesVersion = -1;
    private bool _disposed;

    public EditorLanguageService(TextEditor editor)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _debounce = new DispatcherTimer { Interval = ParseDebounce };
        _debounce.Tick += OnDebounceTick;
        _editor.TextChanged += OnTextChanged;
    }

    /// <summary>The most recently computed alias → table map. Never null.</summary>
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    /// <summary>True when the cached map reflects the current document text.</summary>
    public bool AliasesFresh => _aliasesVersion == _changeVersion;

    /// <summary>
    /// Synchronous fallback for <b>deliberate</b> triggers (a typed <c>.</c> or
    /// Ctrl+Space): if the cached map is stale, re-parse now so the completion is
    /// correct even when the user typed an alias and immediately asked for its
    /// columns (before the ~300 ms background parse ran). No-op when already
    /// fresh — so a burst of deliberate triggers costs at most one parse. This is
    /// never called on the per-character auto-trigger path.
    /// </summary>
    public void EnsureFreshAliases()
    {
        if (_disposed || AliasesFresh) return;
        var version = _changeVersion;
        var text = _editor.Text ?? string.Empty;
        _aliases = SqlAliasResolver.ParseAliases(text);
        _aliasesVersion = version;
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _changeVersion++;
        // Restart the idle timer — a burst of keystrokes schedules exactly one parse.
        _debounce.Stop();
        _debounce.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounce.Stop();
        _ = ReparseAsync();
    }

    private async Task ReparseAsync()
    {
        _cts?.Cancel();
        var cts = new CancellationTokenSource();
        _cts = cts;

        var version = _changeVersion;
        var text = _editor.Text ?? string.Empty;   // one materialization per idle, not per keystroke
        try
        {
            var map = await Task.Run(() => SqlAliasResolver.ParseAliases(text), cts.Token)
                                .ConfigureAwait(true); // resume on the UI thread
            // Drop the result if a newer parse superseded this one or we were cancelled.
            if (cts.IsCancellationRequested || !ReferenceEquals(cts, _cts)) return;
            _aliases = map;
            _aliasesVersion = version;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit — expected.
        }
        catch (Exception)
        {
            // Best-effort: a parse failure must never crash the editor. The next
            // idle re-parse retries; deliberate triggers fall back to EnsureFreshAliases.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _editor.TextChanged -= OnTextChanged;
        _debounce.Stop();
        _debounce.Tick -= OnDebounceTick;
        _cts?.Cancel();
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.App.Completion;

/// <summary>
/// Per-editor async language service (Etap 0 of the editor rebuild — design §7 / §15).
/// Owns the debounced, cancellable, off-thread work that used to run on every
/// keystroke on the UI thread.
/// <para>
/// Etap 5 / M1 (design §22.1) adds the <see cref="SemanticModel"/>: on each idle tick
/// it captures an immutable metadata snapshot on the UI thread (via the injected
/// <see cref="_metadataSnapshot"/> factory), then off-thread parses the document and
/// builds the model, and caches it. Completion still runs off the cached alias map in
/// M1 — the model is built and cached but not yet consumed (M5 switches completion to
/// it and retires the alias path). Both the alias map and the model are refreshed in
/// the same idle tick to keep exactly one background parse per burst.
/// </para>
/// </summary>
/// <remarks>
/// All state (<see cref="_aliases"/>, <see cref="_model"/>, the version counters,
/// <see cref="_cts"/>) is touched only on the UI thread: the debounce timer ticks on
/// the UI thread, the background parse resumes on the captured UI context
/// (<c>ConfigureAwait(true)</c>), and every reader is called from completion, which
/// runs on the UI thread. So no locking is needed. Only the pure
/// <see cref="SqlAliasResolver.ParseAliases"/> + <see cref="SemanticModel.Build(string, ISqlMetadataProvider?)"/>
/// calls run off-thread, on a captured string + a captured immutable snapshot.
/// </remarks>
internal sealed class EditorLanguageService : IDisposable
{
    /// <summary>Idle delay before a background re-parse runs (design §4.3/§15).</summary>
    internal static readonly TimeSpan ParseDebounce = TimeSpan.FromMilliseconds(300);

    private static readonly IReadOnlyDictionary<string, string> EmptyAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Idle delay before a metadata-driven model refresh runs. Coalesces the burst of
    /// per-category loads on connect (prefetch raises one signal per category) into a single
    /// rebuild after they settle (design §22 — the model must reflect newly-loaded objects so
    /// FROM view / FROM proc(…) resolve once their categories finish loading).</summary>
    internal static readonly TimeSpan MetadataRefreshDebounce = TimeSpan.FromMilliseconds(200);

    private readonly TextEditor _editor;
    private readonly DispatcherTimer _debounce;
    private DispatcherTimer? _metadataRefresh;
    // Document length at the last observed change — lets OnTextChanged distinguish a single keystroke
    // (±1) from a wholesale replace (tab/saved-query switch, paste) so the latter rebuilds the model
    // immediately instead of waiting the ~300 ms debounce (the "colours pop in late" symptom).
    private int _lastTextLength;
    // Captures an immutable metadata snapshot on the UI thread; null → the model binds
    // local scope only (EmptyMetadataProvider). Injected so the service never reads live
    // VM state off-thread (design §22.1).
    private readonly Func<ISqlMetadataProvider>? _metadataSnapshot;

    private IReadOnlyDictionary<string, string> _aliases = EmptyAliases;
    private SemanticModel? _model;
    private CancellationTokenSource? _cts;
    // Monotonic edit counter and the counter each cached artifact reflects. Start the
    // caches "stale" (-1 < 0) so the first deliberate trigger parses on demand even if
    // no edit has been observed yet (e.g. text set before we attached).
    private long _changeVersion;
    private long _aliasesVersion = -1;
    private long _modelVersion = -1;
    private bool _disposed;

    public EditorLanguageService(TextEditor editor, Func<ISqlMetadataProvider>? metadataSnapshot = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _metadataSnapshot = metadataSnapshot;
        _debounce = new DispatcherTimer { Interval = ParseDebounce };
        _debounce.Tick += OnDebounceTick;
        _editor.TextChanged += OnTextChanged;
    }

    /// <summary>The most recently computed alias → table map. Never null.</summary>
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    /// <summary>True when the cached map reflects the current document text.</summary>
    public bool AliasesFresh => _aliasesVersion == _changeVersion;

    /// <summary>The most recently built semantic model, or null before the first parse.</summary>
    public SemanticModel? Model => _model;

    /// <summary>True when the cached model reflects the current document text.</summary>
    public bool ModelFresh => _modelVersion == _changeVersion;

    /// <summary>Raised (on the UI thread) whenever <see cref="Model"/> is (re)built — the signal the
    /// semantic highlighter uses to repaint. Fires on the debounced background reparse, and on the
    /// synchronous deliberate-trigger refreshes.</summary>
    public event EventHandler? ModelUpdated;

    private void RaiseModelUpdated() => ModelUpdated?.Invoke(this, EventArgs.Empty);

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

    /// <summary>
    /// Synchronous fallback for deliberate triggers that need the semantic model — the
    /// M5 completion path calls this before consulting <see cref="Model"/>, so a
    /// just-typed identifier/alias is reflected even before the idle re-parse ran.
    /// No-op when already fresh. UI-thread only (captures the snapshot synchronously).
    /// </summary>
    public void EnsureFreshModel()
    {
        if (_disposed || ModelFresh) return;
        var version = _changeVersion;
        var text = _editor.Text ?? string.Empty;
        var provider = _metadataSnapshot?.Invoke();
        _model = BuildModelSafe(text, provider);
        _modelVersion = version;
        RaiseModelUpdated();
    }

    /// <summary>
    /// Rebuilds the semantic model against a <b>fresh metadata snapshot</b> at the
    /// current document version, <i>even when the text is unchanged</i> (so
    /// <see cref="ModelFresh"/> stays true and <see cref="EnsureFreshModel"/> would
    /// no-op). Used after the App warms an uncached table's columns (M5 dot
    /// warm-then-rebuild): the warmed columns are now in the cache, so the next
    /// snapshot carries them and the rebuilt model's dot completion can list them.
    /// UI-thread only (captures the snapshot synchronously).
    /// </summary>
    public void RefreshModelWithMetadata()
    {
        if (_disposed) return;
        var version = _changeVersion;
        var text = _editor.Text ?? string.Empty;
        var provider = _metadataSnapshot?.Invoke();
        _model = BuildModelSafe(text, provider);
        _modelVersion = version;
        RaiseModelUpdated();
    }

    /// <summary>Notifies the service that the App's loaded metadata set changed (a category finished
    /// loading — prefetch on connect, an expand, or a refresh). Schedules a coalesced model rebuild
    /// against a fresh snapshot so the model reflects the newly-loaded objects — the fix for
    /// "FROM view / FROM proc(…) don't highlight/navigate": the model is otherwise built once (on the
    /// text-set) before Views/Procedures finish prefetching and is never refreshed. Idempotent burst:
    /// the ~13 per-category signals on connect collapse to one rebuild.</summary>
    public void NotifyMetadataChanged()
    {
        if (_disposed) return;
        if (_metadataRefresh is null)
        {
            _metadataRefresh = new DispatcherTimer { Interval = MetadataRefreshDebounce };
            _metadataRefresh.Tick += OnMetadataRefreshTick;
        }
        _metadataRefresh.Stop();
        _metadataRefresh.Start();
    }

    private void OnMetadataRefreshTick(object? sender, EventArgs e)
    {
        _metadataRefresh!.Stop();
        // Rebuild against a fresh metadata snapshot even though the text is unchanged (so ModelFresh
        // stays true and EnsureFreshModel would no-op) — the whole point is to pick up newly-loaded
        // objects. Fires ModelUpdated → the semantic highlighter repaints and nav/quick-info read the
        // fresh model.
        RefreshModelWithMetadata();
    }

    private void OnTextChanged(object? sender, EventArgs e)
    {
        _changeVersion++;

        int newLen = _editor.Document?.TextLength ?? 0;
        // A wholesale replace (tab/saved-query switch, a large paste) or the very first text-set builds
        // the model IMMEDIATELY, so semantic highlighting is ready when the user first sees the
        // document instead of appearing ~300 ms later. A normal keystroke (±1 char) just restarts the
        // idle debounce so a typing burst still costs exactly one parse.
        bool wholesale = _model is null || Math.Abs(newLen - _lastTextLength) > WholesaleChangeThreshold;
        _lastTextLength = newLen;
        if (wholesale)
        {
            _debounce.Stop();
            EnsureFreshModel();
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    // A change larger than this many characters is treated as a wholesale replace (immediate rebuild),
    // not incremental typing. Above a single paste of a short snippet; well below a whole document.
    private const int WholesaleChangeThreshold = 20;

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
        var provider = _metadataSnapshot?.Invoke(); // UI-thread snapshot, consumed off-thread
        try
        {
            var (map, model) = await Task.Run(() =>
            {
                var aliases = SqlAliasResolver.ParseAliases(text);
                var built = BuildModelSafe(text, provider);
                return (aliases, built);
            }, cts.Token).ConfigureAwait(true); // resume on the UI thread
            // Drop the result if a newer parse superseded this one or we were cancelled.
            if (cts.IsCancellationRequested || !ReferenceEquals(cts, _cts)) return;
            _aliases = map;
            _aliasesVersion = version;
            _model = model;
            _modelVersion = version;
            RaiseModelUpdated();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit — expected.
        }
        catch (Exception)
        {
            // Best-effort: a parse failure must never crash the editor. The next
            // idle re-parse retries; deliberate triggers fall back to EnsureFresh*.
        }
    }

    // The parser + binder are error-tolerant (never throw), but a defensive catch keeps
    // any future bug in the language front-end from ever crashing the editor.
    private static SemanticModel? BuildModelSafe(string text, ISqlMetadataProvider? provider)
    {
        try
        {
            return SemanticModel.Build(text, provider);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _editor.TextChanged -= OnTextChanged;
        _debounce.Stop();
        _debounce.Tick -= OnDebounceTick;
        if (_metadataRefresh is not null)
        {
            _metadataRefresh.Stop();
            _metadataRefresh.Tick -= OnMetadataRefreshTick;
        }
        _cts?.Cancel();
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using AvaloniaEdit;
using EmberTern.Core.Sql.Language;
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
/// builds the model, and caches it. Completion consumes the cached model directly (the
/// per-editor alias map was retired in Etap 5 / M5; its dead scaffolding was removed in
/// Etap 6.9 / B0). One background parse per burst.
/// </para>
/// </summary>
/// <remarks>
/// All state (<see cref="_model"/>, the version counters, <see cref="_cts"/>) is touched
/// only on the UI thread: the debounce timer ticks on the UI thread, the background parse
/// resumes on the captured UI context (<c>ConfigureAwait(true)</c>), and every reader is
/// called from completion, which runs on the UI thread. So no locking is needed. Only the
/// pure <see cref="SemanticModel.Build(string, ISqlMetadataProvider?)"/> call runs
/// off-thread, on a captured string + a captured immutable snapshot.
/// </remarks>
internal sealed class EditorLanguageService : IDisposable
{
    /// <summary>Idle delay before a background re-parse runs (design §4.3/§15).</summary>
    internal static readonly TimeSpan ParseDebounce = TimeSpan.FromMilliseconds(300);

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
    // The current generation of the host's loaded-object set (0 when unknown). Lets the model track
    // whether it was built against an OLDER metadata state: a deliberate refresh rebuilds when the
    // generation moved even if the text didn't. Without this, a model first built before a category
    // loaded (prefetch on connect) stayed metadata-blank until a keystroke bumped the text version —
    // the "IntelliSense dead until I edit after connecting" bug (QA Package 1).
    private readonly Func<int>? _metadataGeneration;
    private int CurrentMetadataGeneration => _metadataGeneration?.Invoke() ?? 0;
    // The metadata generation the cached model was built against.
    private int _modelMetadataGeneration;
    // Warms (loads + caches) everything the referenced objects need — columns for table-like objects
    // and the rich Quick Info detail (description / function return type / trigger header) for all —
    // returning whether ANY was newly loaded (⇒ a rebuild is warranted). Sprint 1 (point b) + Package 5
    // (Stage B/C): after each model build the pipeline warms what the CURRENT statement references, then
    // rebuilds once, so the published model is complete for what's on screen WITHOUT the user first
    // typing "table.". Metadata stays lazy at the catalog level; only referenced objects are warmed.
    // Null → no warming (tests / editors without a metadata reader).
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<bool>>? _warmReferencedMetadata;
    // One warm pass in flight at a time; a re-warm requested while a pass runs is coalesced into
    // _warmRequested and honoured when the current pass finishes. This is what makes the warm converge
    // when metadata GROWS mid-warm — a category loading during an in-flight warm (prefetch on connect)
    // triggers a rebuild whose warm must not be dropped, or its newly-resolved objects (generators,
    // functions, an existing tab's objects racing prefetch) never get their detail warmed.
    private bool _warmingMetadata;
    private bool _warmRequested;

    private SemanticModel? _model;
    // Stage 7 / S3: the diagnostics of the currently-cached model, computed on the same background
    // pass that builds the model and always consistent with it (same document version). The squiggle
    // renderer reads this cached list on each paint — it never analyses on the paint path. Empty until
    // the first model is built, and whenever the model is null.
    private IReadOnlyList<Diagnostic> _diagnostics = Array.Empty<Diagnostic>();
    private CancellationTokenSource? _cts;
    // Monotonic edit counter and the counter each cached artifact reflects. Start the
    // caches "stale" (-1 < 0) so the first deliberate trigger parses on demand even if
    // no edit has been observed yet (e.g. text set before we attached).
    private long _changeVersion;
    private long _modelVersion = -1;
    private bool _disposed;

    /// <summary>
    /// Supplies declarations that are real but live OUTSIDE this editor's text. The Easy-mode
    /// routine editors need it: their editor holds only the BODY, while the parameters and DECLAREd
    /// variables sit in the surrounding grids — so a text-only model can't see them and Ctrl+Space
    /// offered no params/locals. Seeded into the model's root scope, they become visible to every
    /// model client at once. Null (the SQL editor) = nothing ambient, the text is the whole truth.
    /// </summary>
    private readonly Func<IReadOnlyList<Symbol>>? _ambientSymbols;

    public EditorLanguageService(
        TextEditor editor,
        Func<ISqlMetadataProvider>? metadataSnapshot = null,
        Func<int>? metadataGeneration = null,
        Func<IReadOnlyList<string>, CancellationToken, Task<bool>>? warmReferencedMetadata = null,
        Func<IReadOnlyList<Symbol>>? ambientSymbols = null)
    {
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _metadataSnapshot = metadataSnapshot;
        _metadataGeneration = metadataGeneration;
        _warmReferencedMetadata = warmReferencedMetadata;
        _ambientSymbols = ambientSymbols;
        _debounce = new DispatcherTimer { Interval = ParseDebounce };
        _debounce.Tick += OnDebounceTick;
        _editor.TextChanged += OnTextChanged;
    }

    /// <summary>The most recently built semantic model, or null before the first parse.</summary>
    public SemanticModel? Model => _model;

    /// <summary>Stage 7 (Diagnostics) — the semantic diagnostics of the cached <see cref="Model"/>,
    /// computed by <see cref="DiagnosticsEngine"/> on the same background pass that builds the model, so
    /// the two always reflect the same document version. Read by the squiggle renderer on the paint path
    /// (the paint path does no analysis). Empty until the first model is built.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>True when the cached model reflects both the current document text <b>and</b> the
    /// current metadata generation. The metadata half is what lets a deliberate Ctrl+Space rebuild the
    /// model after a category loaded (prefetch on connect) without a keystroke — the text-only gate
    /// previously left the model metadata-blank "until I edit" (QA Package 1).</summary>
    public bool ModelFresh => _modelVersion == _changeVersion
                              && _modelMetadataGeneration == CurrentMetadataGeneration;

    /// <summary>Raised (on the UI thread) whenever <see cref="Model"/> is (re)built — the signal the
    /// semantic highlighter uses to repaint. Fires on the debounced background reparse, and on the
    /// synchronous deliberate-trigger refreshes.</summary>
    public event EventHandler? ModelUpdated;

    private void RaiseModelUpdated() => ModelUpdated?.Invoke(this, EventArgs.Empty);

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
        // Capture the generation BEFORE the snapshot: if a category loads in between, the recorded
        // generation is at most one behind the snapshot, so the next trigger rebuilds — never ahead,
        // which would suppress a needed rebuild.
        var generation = CurrentMetadataGeneration;
        var text = _editor.Text ?? string.Empty;
        var provider = _metadataSnapshot?.Invoke();
        _model = BuildModelSafe(text, provider, CaptureAmbientSymbols());
        _diagnostics = AnalyzeSafe(_model, CancellationToken.None);
        _modelVersion = version;
        _modelMetadataGeneration = generation;
        RaiseModelUpdated();
        BeginWarmReferencedMetadata();
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
        var generation = CurrentMetadataGeneration;
        var text = _editor.Text ?? string.Empty;
        var provider = _metadataSnapshot?.Invoke();
        _model = BuildModelSafe(text, provider, CaptureAmbientSymbols());
        _diagnostics = AnalyzeSafe(_model, CancellationToken.None);
        _modelVersion = version;
        _modelMetadataGeneration = generation;
        RaiseModelUpdated();
        BeginWarmReferencedMetadata();
    }

    // ── Referenced-metadata warming (Sprint 1 point b + Package 5 Stage B/C) ─────────────────────

    // Fire-and-forget the warm pass after a model build. When a pass is already running (e.g. a
    // metadata category just loaded and rebuilt the model mid-warm), record a pending re-warm instead
    // of dropping it — the running pass loops again and picks up the newly-resolved objects. No-op when
    // no warmer is wired or there is no model.
    private void BeginWarmReferencedMetadata()
    {
        if (_disposed || _warmReferencedMetadata is null || _model is null) return;
        if (_warmingMetadata) { _warmRequested = true; return; }
        _ = WarmReferencedMetadataAsync();
    }

    // Warms everything the current model references — columns for table-like objects AND the rich Quick
    // Info detail (description / function return type / trigger header) for every referenced object —
    // then, if anything was newly loaded, rebuilds against the now-complete snapshot. This is the
    // general form of the dot's warm-then-rebuild: done for ALL referenced objects, proactively, so
    // no "table." is required to complete the model, and Quick Info / hover show the full facts.
    // <para>Loops while a re-warm is pending (<see cref="_warmRequested"/>): each rebuild — its own or
    // one from a category that loaded mid-warm — re-collects the model's referenced objects and warms
    // any still-uncached. Converges once metadata stops growing and everything is cached (a final pass
    // warms nothing → no rebuild → exit). UI-thread affinity: resumes on the captured context.</para>
    private async Task WarmReferencedMetadataAsync()
    {
        if (_warmReferencedMetadata is null) return;
        _warmingMetadata = true;
        try
        {
            do
            {
                _warmRequested = false;
                var model = _model;
                if (model is null) break;
                var names = CollectReferencedObjectNames(model);
                if (names.Count == 0) break;

                var version = _changeVersion;
                bool loaded = await _warmReferencedMetadata(names, CancellationToken.None).ConfigureAwait(true);
                // Superseded by a newer edit, or torn down — drop the stale rebuild and stop.
                if (_disposed || version != _changeVersion) break;
                if (loaded) RefreshModelWithMetadata(); // rebuild with the warmed metadata in the snapshot
            }
            while (_warmRequested); // a category loaded (or the rebuild) requested another pass
        }
        catch (Exception)
        {
            // Best-effort: a warm failure must never crash or wedge the editor.
        }
        finally
        {
            _warmingMetadata = false;
        }
    }

    // The distinct catalog objects the model references: FROM/JOIN/DML table targets and trigger
    // NEW/OLD record targets (whose COLUMNS are warmed), plus every resolved schema object — tables,
    // views, procedures, functions, triggers, sequences (whose DETAIL is warmed). Derived tables
    // (subqueries) have no catalog identity and are skipped. The App's warmer decides, per kind, what
    // to load; here we only enumerate the names the current text depends on.
    private static IReadOnlyList<string> CollectReferencedObjectNames(SemanticModel model)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in model.AllSymbols)
        {
            switch (s)
            {
                case TableReferenceSymbol { IsDerived: false, TargetName: { Length: > 0 } target }:
                    set.Add(target);
                    break;
                case RecordAliasSymbol { TargetTable: { Length: > 0 } recordTable }:
                    set.Add(recordTable);
                    break;
                case SchemaObjectSymbol { Name: { Length: > 0 } objectName }:
                    set.Add(objectName);
                    break;
            }
        }
        return set.Count == 0 ? System.Array.Empty<string>() : new List<string>(set);
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
        var generation = CurrentMetadataGeneration; // capture with the snapshot (UI thread)
        var text = _editor.Text ?? string.Empty;   // one materialization per idle, not per keystroke
        var provider = _metadataSnapshot?.Invoke(); // UI-thread snapshot, consumed off-thread
        var ambient = CaptureAmbientSymbols();      // UI-thread snapshot (reads VM grids), same rule
        try
        {
            // Build the model AND analyse it in the same off-thread pass, under the same token — so a
            // newer edit cancels an in-flight diagnostics run (§9: no parallel analyses, stale ones
            // cancelled) and the paint path never analyses.
            var (model, diagnostics) = await Task.Run(
                () =>
                {
                    var m = BuildModelSafe(text, provider, ambient);
                    return (m, AnalyzeSafe(m, cts.Token));
                },
                cts.Token).ConfigureAwait(true); // resume on the UI thread
            // Drop the result if a newer parse superseded this one or we were cancelled.
            if (cts.IsCancellationRequested || !ReferenceEquals(cts, _cts)) return;
            _model = model;
            _diagnostics = diagnostics;
            _modelVersion = version;
            _modelMetadataGeneration = generation;
            RaiseModelUpdated();
            BeginWarmReferencedMetadata();
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
    // ambient MUST be captured on the UI thread by the caller (it reads VM collections) and passed
    // in — never resolved here, since this also runs on the thread pool.
    private static SemanticModel? BuildModelSafe(
        string text, ISqlMetadataProvider? provider, IReadOnlyList<Symbol>? ambient)
    {
        try
        {
            return SemanticModel.Build(text, provider, ambient);
        }
        catch
        {
            return null;
        }
    }

    // Runs the pure-Core DiagnosticsEngine over a freshly-built model. Returns empty for a null model.
    // Cancellation (from the shared reparse token) is allowed to propagate so Task.Run reports the pass
    // as cancelled and its result is dropped; any other failure only costs diagnostics, never the model
    // or the editor (§0 / best-effort — the model itself is already published).
    private static IReadOnlyList<Diagnostic> AnalyzeSafe(SemanticModel? model, CancellationToken ct)
    {
        if (model is null) return Array.Empty<Diagnostic>();
        try
        {
            return DiagnosticsEngine.Analyze(model, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Array.Empty<Diagnostic>();
        }
    }

    /// <summary>Snapshot the out-of-text declarations on the UI thread. Never throws — a failing
    /// provider must only cost IntelliSense richness, never the model.</summary>
    private IReadOnlyList<Symbol>? CaptureAmbientSymbols()
    {
        try { return _ambientSymbols?.Invoke(); }
        catch { return null; }
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

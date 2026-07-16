using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language.Completion;
using EmberTern.Core.Sql.Language.QuickInfo;
using EmberTern.Core.Sql.Language.Semantics;
using EmberTern.Core.Sql.Language.Signatures;
using EmberTern.Core.Sql.Language.Snippets;

namespace EmberTern.App.Completion;

/// <summary>
/// Wires Ctrl+Space + auto-trigger autocompletion to an <see cref="TextEditor"/>.
/// One controller per editor; <see cref="Detach"/> unhooks the editor events.
/// </summary>
/// <remarks>
/// Etap 5 / M5 (design §22 / §5.7): the completion list is produced by the pure Core
/// <see cref="CompletionEngine"/> against the <see cref="SemanticModel"/> the per-editor
/// <see cref="EditorLanguageService"/> builds and caches off the keystroke. The controller
/// is thin glue: it decides <i>whether/when</i> to open the window (from the few chars
/// before the caret via <see cref="CaretContext"/>, never <c>_editor.Text</c>), asks the
/// engine for the items, positions the <c>CompletionWindow</c> over the replaced segment,
/// and applies case-preserving insert. It no longer knows the keyword list, the object
/// list, or alias resolution — those all live in the engine/model now.
/// <para>
/// Responsiveness (Etap 0) is preserved: the per-character handler does no whole-document
/// work; a typed <c>.</c> and Ctrl+Space are immediate (with a synchronous model refresh
/// so a just-typed identifier resolves); the plain identifier auto-popup is idle-debounced.
/// </para>
/// </remarks>
internal sealed class SqlCompletionController
{
    /// <summary>
    /// Idle delay before the identifier auto-popup appears. Non-aggressive by
    /// design (§7.4): the list surfaces when the user pauses, not on the Nth
    /// character mid-burst. A user-configurable delay (with a full-disable
    /// option) is deferred to the app configurator — this settable property is
    /// the single wire-in point for that.
    /// </summary>
    internal static TimeSpan AutoPopupDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    private readonly TextEditor _editor;
    private readonly EditorLanguageService _language;
    private readonly Func<string, Task<IReadOnlyList<ColumnSpec>>>? _ensureColumnsAsync;
    // Warms (and caches) a routine's parameters for signature help on a cache miss —
    // the routine-param analogue of _ensureColumnsAsync (M6/M7). Null → signature help
    // shows only what the snapshot already has.
    private readonly Func<string, Task>? _ensureRoutineParamsAsync;
    // Trigger context: NEW./OLD. resolve to the trigger's relation. Needed for
    // body-only (Easy-mode) trigger editors, where the CREATE TRIGGER header is NOT
    // in the editor text, so the semantic model can't bind NEW/OLD to a table. Null
    // for ordinary editors (a full-source trigger resolves NEW/OLD via the model).
    private readonly Func<string?>? _contextTableProvider;
    // Subscribe/unsubscribe hooks for the App's "loaded metadata changed" signal (a category
    // finished loading — prefetch/expand/refresh). Scoped to the editor's visual-tree lifetime
    // (Detach() is not reliably called, so a raw subscription to the long-lived Metadata singleton
    // would leak the editor). On the signal we rebuild the model so newly-loaded objects (views,
    // procedures) start resolving. Null → no metadata-change refresh (tests / editors without a VM).
    private readonly Action<Action>? _subscribeMetadataChanged;
    private readonly Action<Action>? _unsubscribeMetadataChanged;
    private readonly Action? _metadataChangedHandler;
    private bool _metadataSubscribed;
    // Hooks for the definitive "metadata ready" signal (prefetch complete). Distinct from the debounced
    // metadata-changed hooks above: on this we do the authoritative rebuild + full warm + publish
    // (Package 5 closure). Same visual-tree-scoped lifetime so the subscription is leak-free.
    private readonly Action<Action>? _subscribeMetadataReady;
    private readonly Action<Action>? _unsubscribeMetadataReady;
    private readonly Action? _metadataReadyHandler;
    private bool _readySubscribed;
    private readonly DispatcherTimer _autoPopup;
    private CompletionWindow? _window;
    // The unified Parameter Helper (design §28) — the ONE parameter-info surface, driven by
    // SignatureHelpEngine, shown both while typing an argument list (here) and on a double-click on a
    // value (NavigationController delegates to TryShowParameterHelperAt). Replaces the old M7
    // OverloadInsightWindow signature popup.
    private readonly ParameterHelper _parameterHelper;
    // P3: on-demand Quick Info popup for a fully-typed, resolved identifier under the caret
    // (Ctrl+Space "show its facts" instead of re-listing). Created lazily on first use.
    private Popup? _quickInfo;

    public SqlCompletionController(
        TextEditor editor,
        Func<ISqlMetadataProvider> metadataSnapshot,
        Func<string, Task<IReadOnlyList<ColumnSpec>>>? ensureColumnsAsync = null,
        Func<string?>? contextTableProvider = null,
        Func<string, Task>? ensureRoutineParamsAsync = null,
        Action<Action>? subscribeMetadataChanged = null,
        Action<Action>? unsubscribeMetadataChanged = null,
        Func<int>? metadataGeneration = null,
        Func<IReadOnlyList<string>, System.Threading.CancellationToken, Task<bool>>? warmReferencedMetadata = null,
        Action<Action>? subscribeMetadataReady = null,
        Action<Action>? unsubscribeMetadataReady = null,
        Func<IReadOnlyList<EmberTern.Core.Sql.Language.Semantics.Symbol>>? ambientSymbols = null)
    {
        _editor = editor;
        _ensureColumnsAsync = ensureColumnsAsync;
        _ensureRoutineParamsAsync = ensureRoutineParamsAsync;
        _contextTableProvider = contextTableProvider;

        _language = new EditorLanguageService(
            editor, metadataSnapshot, metadataGeneration, warmReferencedMetadata, ambientSymbols);
        _parameterHelper = ParameterHelper.Attach(editor, () => _language.Model, WarmForSignatureAndRebuildAsync);
        _autoPopup = new DispatcherTimer();
        _autoPopup.Tick += OnAutoPopupTick;

        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.KeyDown += OnKeyDown;
        // A caret move (arrow key / click) makes an open Quick Info popup (P3) stale — dismiss it.
        _editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;

        // Metadata-change refresh, scoped to the editor's visual-tree lifetime (leak-free — see field
        // comment). Subscribe on attach, unsubscribe on detach; if the editor is already attached when
        // we're constructed (the detail views build us from their own OnAttachedToVisualTree), subscribe
        // now — AttachedToVisualTree won't fire again until a detach+reattach cycle.
        bool hasChanged = subscribeMetadataChanged is not null && unsubscribeMetadataChanged is not null;
        bool hasReady = subscribeMetadataReady is not null && unsubscribeMetadataReady is not null;
        if (hasChanged)
        {
            _subscribeMetadataChanged = subscribeMetadataChanged;
            _unsubscribeMetadataChanged = unsubscribeMetadataChanged;
            _metadataChangedHandler = () => _language.NotifyMetadataChanged();
        }
        if (hasReady)
        {
            _subscribeMetadataReady = subscribeMetadataReady;
            _unsubscribeMetadataReady = unsubscribeMetadataReady;
            // Definitive rebuild + full warm + publish (Package 5 closure).
            _metadataReadyHandler = () => _language.RefreshModelWithMetadata();
        }
        if (hasChanged || hasReady)
        {
            _editor.AttachedToVisualTree += OnEditorAttachedToVisualTree;
            _editor.DetachedFromVisualTree += OnEditorDetachedFromVisualTree;
            if (_editor.IsLoaded) SubscribeMetadataHooks();
        }
    }

    /// <summary>The definitive "metadata ready" initialization for a host that owns the event lifecycle
    /// (the main window ties it to its stable VM): rebuild the model against the now-complete metadata,
    /// warm all referenced objects (columns + detail + routine parameters), and publish one complete
    /// Semantic Model. Not debounced — this is the authoritative prefetch-complete step (Package 5).</summary>
    public void RefreshModelForMetadataReady() => _language.RefreshModelWithMetadata();

    private void SubscribeMetadataHooks()
    {
        if (!_metadataSubscribed && _metadataChangedHandler is not null)
        {
            _subscribeMetadataChanged!(_metadataChangedHandler);
            _metadataSubscribed = true;
        }
        if (!_readySubscribed && _metadataReadyHandler is not null)
        {
            _subscribeMetadataReady!(_metadataReadyHandler);
            _readySubscribed = true;
        }
    }

    private void UnsubscribeMetadataHooks()
    {
        if (_metadataSubscribed && _metadataChangedHandler is not null)
        {
            _unsubscribeMetadataChanged!(_metadataChangedHandler);
            _metadataSubscribed = false;
        }
        if (_readySubscribed && _metadataReadyHandler is not null)
        {
            _unsubscribeMetadataReady!(_metadataReadyHandler);
            _readySubscribed = false;
        }
    }

    private void OnEditorAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => SubscribeMetadataHooks();

    private void OnEditorDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        => UnsubscribeMetadataHooks();

    /// <summary>The editor's cached semantic model (Etap 6) — shared with the semantic highlighter so
    /// there is exactly one background parse per editor. Null before the first parse.</summary>
    public SemanticModel? Model => _language.Model;

    /// <summary>Stage 7 (Diagnostics) — the cached semantic diagnostics of <see cref="Model"/>, computed
    /// on the same background pass and consistent with it. The squiggle renderer reads this on paint, the
    /// Diagnostics panel lists it, and the unified hover explains it — all from this one cached list.</summary>
    public IReadOnlyList<EmberTern.Core.Sql.Language.Diagnostic> Diagnostics => _language.Diagnostics;

    /// <summary>
    /// True while one of this controller's popups owns the screen — the completion list, the Parameter
    /// Helper, or the on-demand Quick Info card.
    /// <para>
    /// Exposed for the unified hover, which must not stack on top of them. This controller already owns
    /// the "they shouldn't stack — the list wins" rule for all three, so the arbitration stays in ONE
    /// place rather than the hover re-deriving it from three separate handles.
    /// </para>
    /// </summary>
    public bool IsPopupOpen => _window is not null || _parameterHelper.IsOpen || _quickInfo?.IsOpen == true;

    /// <summary>Notifies the controller that the App's loaded metadata set changed (a category
    /// finished loading — prefetch/expand/refresh), scheduling a coalesced model rebuild so late-loaded
    /// objects (views / selectable procedures used in FROM) start resolving. Public so a host that
    /// owns the metadata event lifecycle (the main window ties it to its stable VM in
    /// OnDataContextChanged) can drive it directly instead of via the fragile attach-time hook.</summary>
    public void NotifyMetadataChanged() => _language.NotifyMetadataChanged();

    /// <summary>Notifies the controller that the editor's <b>ambient symbols</b> changed — the out-of-text
    /// declarations an Easy-mode routine editor supplies from its grids (params / DECLAREd variables).
    /// Schedules the same coalesced model rebuild as <see cref="NotifyMetadataChanged"/> (which re-captures
    /// the ambient symbols), so diagnostics / completion / highlighting refresh immediately after a grid
    /// edit instead of waiting for the next body-text change. Debounced — a burst of row edits collapses to
    /// one rebuild.</summary>
    public void NotifyAmbientSymbolsChanged() => _language.NotifyMetadataChanged();

    /// <summary>Raised whenever <see cref="Model"/> is (re)built — the semantic highlighter repaints
    /// on this. Forwarded from the per-editor <see cref="EditorLanguageService"/>.</summary>
    public event EventHandler ModelUpdated
    {
        add => _language.ModelUpdated += value;
        remove => _language.ModelUpdated -= value;
    }

    public void Detach()
    {
        _editor.TextArea.TextEntered -= OnTextEntered;
        _editor.TextArea.KeyDown -= OnKeyDown;
        _editor.TextArea.Caret.PositionChanged -= OnCaretPositionChanged;
        _editor.AttachedToVisualTree -= OnEditorAttachedToVisualTree;
        _editor.DetachedFromVisualTree -= OnEditorDetachedFromVisualTree;
        UnsubscribeMetadataHooks();
        CancelAutoPopup();
        _autoPopup.Tick -= OnAutoPopupTick;
        _language.Dispose();
        _window?.Close();
        _window = null;
        _parameterHelper.Detach();
        HideQuickInfo();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space — force-open the completion window immediately, regardless of
        // word length (design §7.4). Deliberate trigger → refresh the model
        // synchronously so a just-typed alias/identifier resolves.
        if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            var ctrlShift = KeyModifiers.Control | KeyModifiers.Shift;
            if ((e.KeyModifiers & ctrlShift) == ctrlShift)
            {
                // Ctrl+Shift+Space — parameter help on demand (standard IDE shortcut).
                CancelAutoPopup();
                TriggerParameterHelper();
                e.Handled = true;
                return;
            }

            CancelAutoPopup();

            // P3: Ctrl+Space on a fully-typed, resolved identifier shows ITS facts (Quick Info),
            // like IBExpert — not a fresh list. A *second* Ctrl+Space (the info is already showing)
            // escalates to the full completion list, so the user gets both (better than IBExpert,
            // which only ever shows the facts). A dot context always goes straight to columns.
            bool quickInfoWasOpen = _quickInfo is { IsOpen: true };
            HideQuickInfo();
            if (TryShowDot(force: true, allowSyncRefresh: true)) { e.Handled = true; return; }
            if (!quickInfoWasOpen && TryShowQuickInfoAtCaret(allowSyncRefresh: true)) { e.Handled = true; return; }
            ShowBaseline(force: true, allowSyncRefresh: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            // Defensive: AvaloniaEdit closes the completion window on Escape itself,
            // but an explicit close prevents stale handles if the framework changes.
            // Also dismiss the signature + Quick Info popups.
            if (_window is not null)
            {
                CancelAutoPopup();
                _window.Close();
            }
            _parameterHelper.Hide();
            HideQuickInfo();
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        var typed = e.Text;
        if (string.IsNullOrEmpty(typed)) return;

        // Any keystroke moves the caret — a Quick Info popup (P3) for the previous identifier is
        // now stale. Close it; a completion window opening below hides it via OpenWindow anyway.
        HideQuickInfo();

        var c = typed[0];

        // Dot just typed — deliberate request for ALIAS./TABLE./NEW./OLD. columns.
        // Immediate, with a synchronous model refresh so the just-typed qualifier
        // is reflected.
        if (c == '.')
        {
            CancelAutoPopup();
            _window?.Close();
            TryShowDot(force: false, allowSyncRefresh: true);
            return;
        }

        // Parameter help: open/refresh on '(' or ',' (entering / advancing an argument), re-query on
        // ')' (a close may end the call or reveal an enclosing one). Deliberate trigger → sync model
        // refresh so the just-typed paren/comma is counted.
        if (c is '(' or ',' or ')')
        {
            CancelAutoPopup();
            TriggerParameterHelper();
            return;
        }

        if (!SqlCompletionContext.IsIdentifierChar(c))
        {
            CancelAutoPopup();
            return;
        }

        // Window already open — AvaloniaEdit's CompletionList narrows by the typed
        // prefix on its own. Nothing to do.
        if (_window is not null) return;

        // Typing into a dot prefix (e.g. "N.I" after "I") while the window is closed:
        // keep the column flavor. Cache-only (no whole-document parse on the
        // per-character path); if the cached model lags the document the engine won't
        // see the dot and we fall through to the idle popup, which refreshes.
        if (TryShowDot(force: false, allowSyncRefresh: false))
        {
            CancelAutoPopup();
            return;
        }

        // Plain identifier: non-aggressive. Don't pop mid-burst — schedule the
        // popup for a short idle and let a continued burst keep resetting it.
        ScheduleAutoPopup();
    }

    private void ScheduleAutoPopup()
    {
        _autoPopup.Stop();
        _autoPopup.Interval = AutoPopupDelay; // re-read so a future config change applies
        _autoPopup.Start();
    }

    private void CancelAutoPopup() => _autoPopup.Stop();

    private void OnAutoPopupTick(object? sender, EventArgs e)
    {
        _autoPopup.Stop();
        if (_window is not null) return;

        var doc = _editor.Document;
        if (doc is null) return;
        var caret = _editor.CaretOffset;

        // The user paused — a single synchronous model refresh here is acceptable
        // (once per idle, not per keystroke) and keeps the list correct.
        // The caret may have entered a dot context during the idle → prefer columns.
        if (CaretContext.GetDotContext(doc, caret) is not null)
        {
            TryShowDot(force: false, allowSyncRefresh: true);
            return;
        }

        // ≥2 chars: a plain identifier (≥3) auto-triggers, and a 2-char prefix of a snippet keyword
        // (e.g. "if") does too — ShowBaseline applies the combined gate. A 1-char word never triggers.
        var word = CaretContext.GetCurrentWord(doc, caret);
        if (word.Text.Length >= 2)
        {
            ShowBaseline(force: false, allowSyncRefresh: true, word);
        }
    }

    // ── Dot / qualifier → columns ────────────────────────────────────────────────────────────

    // Returns true when the caret is in a dot context and the request was handled
    // (columns shown, an async warm kicked off, or — for an unresolved qualifier —
    // deliberately nothing, so we never fall back to keywords after a "."). Returns
    // false only when this is NOT a dot context (caller may show the baseline list).
    private bool TryShowDot(bool force, bool allowSyncRefresh)
    {
        var doc = _editor.Document;
        if (doc is null) return false;
        var caret = _editor.CaretOffset;

        // Cheap document pre-check — also gives the replacement-segment offsets. The
        // engine still makes the authoritative dot decision + qualifier resolution.
        var dotSeg = CaretContext.GetDotContext(doc, caret);
        if (dotSeg is null) return false;
        var seg = dotSeg.Value;

        var model = ResolveModel(allowSyncRefresh);
        if (model is null) return false;

        var result = CompletionEngine.GetCompletions(model, caret, CompletionTrigger.Dot);
        if (!result.IsDotContext)
        {
            // The cached model lagged the document (auto path, cache-only). Don't
            // guess offsets / show baseline here — the caller schedules the idle
            // popup, which refreshes the model and gets it right.
            return false;
        }

        // Resolved qualifier with cached columns → show immediately.
        if (result.Items.Count > 0)
        {
            return ShowItems(seg.PrefixStart, seg.PrefixEnd, result.Items);
        }

        // Resolved qualifier (or a NEW/OLD record in a body-only trigger editor)
        // whose columns aren't cached → warm then show. Never falls back to keywords.
        var target = result.DotTargetTable ?? ResolveContextRecordTable(seg.Qualifier);
        if (target is not null && _ensureColumnsAsync is not null)
        {
            _ = WarmAndShowAsync(target, seg.Qualifier);
            return true;
        }

        // Unresolved qualifier in a dot context — show nothing, but treat as handled
        // so Ctrl+Space doesn't surface the keyword/object baseline after a ".".
        _ = force; // (dot context is always "handled"; force only matters for the baseline path)
        return true;
    }

    // NEW./OLD. in a body-only (Easy-mode) trigger editor: the model can't bind them
    // (no CREATE TRIGGER header in the text), so resolve to the trigger's table via
    // the injected context provider. Returns null for any other qualifier.
    private string? ResolveContextRecordTable(string qualifier)
    {
        if (_contextTableProvider is null) return null;
        if (!string.Equals(qualifier, "NEW", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(qualifier, "OLD", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var t = _contextTableProvider();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private async Task WarmAndShowAsync(string table, string qualifier)
    {
        try
        {
            var cols = await _ensureColumnsAsync!(table).ConfigureAwait(true);
            // Bail if the user left the dot context or switched qualifier while we waited.
            var doc = _editor.Document;
            if (doc is null) return;
            var currentDot = CaretContext.GetDotContext(doc, _editor.CaretOffset);
            if (currentDot is null
                || !string.Equals(currentDot.Value.Qualifier, qualifier, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            // Keep the cached model in sync with the warm so a later (non-warm) dot
            // query for this table hits the engine's cache path.
            _language.RefreshModelWithMetadata();
            ShowColumns(currentDot.Value.PrefixStart, currentDot.Value.PrefixEnd, cols, table);
        }
        catch (Exception)
        {
            // Best-effort completion: a failure here should never crash the editor.
        }
    }

    // ── Baseline (keywords + objects + in-scope symbols) ─────────────────────────────────────

    private bool ShowBaseline(bool force, bool allowSyncRefresh, CurrentWord? knownWord = null)
    {
        if (_window is not null) return true; // let the open window keep filtering

        var doc = _editor.Document;
        if (doc is null) return false;
        var caret = _editor.CaretOffset;
        var word = knownWord ?? CaretContext.GetCurrentWord(doc, caret);

        var model = ResolveModel(allowSyncRefresh || force);
        if (model is null) return false;

        // Keyword live templates (M8) surface alongside keywords/objects/in-scope symbols,
        // gated by scope (PSQL control-flow in a body; DDL/EXECUTE BLOCK at the top level).
        var snippets = SnippetEngine.GetSnippets(model, caret);

        // Auto-trigger gate (Ctrl+Space / force bypasses it): a completable identifier of the
        // minimum length OR a short prefix of an applicable snippet keyword — so a 2-char "if"
        // template still auto-surfaces even though it's below the identifier threshold (P7).
        if (!force
            && !SqlCompletionContext.ShouldAutoTrigger(word.Text)
            && !WordMayTriggerSnippet(word.Text, snippets))
        {
            return false;
        }

        var trigger = force ? CompletionTrigger.Explicit : CompletionTrigger.Identifier;
        var result = CompletionEngine.GetCompletions(model, caret, trigger);
        // Defensive: if the engine reports a dot context here (rare — the caller
        // already routes dots through TryShowDot), don't show the baseline list.
        if (result.IsDotContext) return false;

        return ShowBaselineWindow(word.Start, word.End, result.Items, snippets);
    }

    // Whether <paramref name="word"/> should auto-surface the list because it is a prefix (≥2 chars)
    // of a snippet keyword valid at the caret — the exception that lets short PSQL/DDL live templates
    // (notably 2-char "if") fire without lowering the general identifier auto-trigger threshold (P7).
    internal static bool WordMayTriggerSnippet(string word, IReadOnlyList<SnippetTemplate> applicable)
    {
        if (string.IsNullOrEmpty(word) || word.Length < 2) return false;
        foreach (var t in applicable)
        {
            if (t.Keyword.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private bool ShowBaselineWindow(
        int startOffset,
        int endOffset,
        IReadOnlyList<CompletionItem> items,
        IReadOnlyList<SnippetTemplate> snippets)
    {
        if (items.Count == 0 && snippets.Count == 0) return false;

        var window = OpenWindow(startOffset, endOffset);
        var data = window.CompletionList.CompletionData;
        foreach (var item in items)
        {
            data.Add(SqlCompletionData.FromItem(item, () => BuildItemDetail(item)));
        }
        foreach (var template in snippets)
        {
            data.Add(new SnippetCompletionData(template));
        }
        return FinishWindow(window);
    }

    // ── Window plumbing ──────────────────────────────────────────────────────────────────────

    // Returns the cached semantic model, refreshing it synchronously first when the
    // trigger is deliberate (or when there is no model yet). Never null unless the
    // editor has no document/text at all.
    private SemanticModel? ResolveModel(bool allowSyncRefresh)
    {
        if (allowSyncRefresh) _language.EnsureFreshModel();
        var model = _language.Model;
        if (model is null)
        {
            // No cached model yet (before the first idle parse). Build one on demand
            // — bounded to when the cache is empty, not a per-keystroke cost.
            _language.EnsureFreshModel();
            model = _language.Model;
        }
        return model;
    }

    private bool ShowItems(int startOffset, int endOffset, IReadOnlyList<CompletionItem> items)
    {
        if (items.Count == 0) return false;

        var window = OpenWindow(startOffset, endOffset);
        var data = window.CompletionList.CompletionData;
        foreach (var item in items)
        {
            data.Add(SqlCompletionData.FromItem(item, () => BuildItemDetail(item)));
        }
        return FinishWindow(window);
    }

    private void ShowColumns(int startOffset, int endOffset, IReadOnlyList<ColumnSpec> columns, string table)
    {
        if (columns.Count == 0) return;

        var window = OpenWindow(startOffset, endOffset);
        var data = window.CompletionList.CompletionData;
        foreach (var col in columns)
        {
            var c = col; // capture per-item for the lazy detail factory
            data.Add(new SqlCompletionData(
                c.Name, SqlCompletionKind.Column, columnType: c.Type, columnDomain: c.Domain,
                detailFactory: () => BuildColumnDetail(c, table)));
        }
        FinishWindow(window);
    }

    // ── Quick Info detail pane (M5, design §8A) ──────────────────────────────────────────────

    // Builds the rich Quick Info detail control shown beside the completion list for a selected
    // item — the same QuickInfoView the Ctrl-hover tooltip uses (one source of truth). Returns
    // null (keyword, or nothing resolvable) so the list falls back to its plain kind label. Built
    // lazily (only the selected item), so it's cheap and always matches the current theme.
    private object? BuildItemDetail(CompletionItem item)
    {
        var info = ResolveItemQuickInfo(item);
        return info is null ? null : QuickInfoView.Build(info, _editor.ActualThemeVariant);
    }

    private object? BuildColumnDetail(ColumnSpec col, string table)
    {
        var symbol = new ColumnSymbol(col.Name)
        {
            OwningTable = table,
            DataType = col.Type,
            Domain = col.Domain,
            Nullable = !col.NotNull,
        };
        return QuickInfoView.Build(QuickInfoEngine.ForSymbol(symbol, _language.Model?.Metadata), _editor.ActualThemeVariant);
    }

    private QuickInfo? ResolveItemQuickInfo(CompletionItem item)
    {
        if (item.Kind == CompletionItemKind.Keyword) return null;
        var model = _language.Model;
        // Prefer the rich symbol the engine attached (a ColumnSymbol with type/domain/nullability/… ),
        // then the real in-scope symbol for locals (alias arrow, variable type, …); every other kind
        // is a catalog object we synthesize. Members (a table's columns / a routine's params) come
        // from the model's metadata snapshot when cached.
        var symbol = item.Symbol ?? FindInScopeLocal(model, item) ?? SynthesizeSymbol(item);
        return symbol is null ? null : QuickInfoEngine.ForSymbol(symbol, model?.Metadata);
    }

    // In-scope symbols are the script's locals (aliases / variables / parameters / CTEs / cursors /
    // NEW-OLD), not the catalog — so a name match for a local item kind is the real local symbol.
    private Symbol? FindInScopeLocal(SemanticModel? model, CompletionItem item)
    {
        if (model is null || !IsLocalKind(item.Kind)) return null;
        foreach (var sym in model.SymbolsInScope(_editor.CaretOffset))
        {
            if (string.Equals(sym.Name, item.InsertText, StringComparison.OrdinalIgnoreCase))
            {
                return sym;
            }
        }
        return null;
    }

    private static bool IsLocalKind(CompletionItemKind kind) => kind
        is CompletionItemKind.TableAlias or CompletionItemKind.Variable or CompletionItemKind.Parameter
        or CompletionItemKind.Cte or CompletionItemKind.Cursor or CompletionItemKind.RecordAlias;

    private static Symbol? SynthesizeSymbol(CompletionItem item)
    {
        var name = item.InsertText;
        switch (item.Kind)
        {
            case CompletionItemKind.Column: return new ColumnSymbol(name) { DataType = item.Detail };
            case CompletionItemKind.Variable: return new VariableSymbol(name) { DataType = item.Detail };
            case CompletionItemKind.Parameter: return new ParameterSymbol(name) { DataType = item.Detail };
            case CompletionItemKind.Cursor: return new CursorSymbol(name);
            case CompletionItemKind.Cte: return new CteSymbol(name);
            case CompletionItemKind.TableAlias: return new TableReferenceSymbol(name);
            case CompletionItemKind.RecordAlias: return new RecordAliasSymbol(name);
            default:
                var kind = ToSchemaSymbolKind(item.Kind);
                return kind is null ? null : new SchemaObjectSymbol(kind.Value, name);
        }
    }

    private static SymbolKind? ToSchemaSymbolKind(CompletionItemKind kind) => kind switch
    {
        CompletionItemKind.Table => SymbolKind.Table,
        CompletionItemKind.View => SymbolKind.View,
        CompletionItemKind.SystemTable => SymbolKind.SystemTable,
        CompletionItemKind.Procedure => SymbolKind.Procedure,
        CompletionItemKind.Function => SymbolKind.Function,
        CompletionItemKind.Trigger => SymbolKind.Trigger,
        CompletionItemKind.Domain => SymbolKind.Domain,
        CompletionItemKind.Exception => SymbolKind.Exception,
        CompletionItemKind.Sequence => SymbolKind.Sequence,
        CompletionItemKind.Role => SymbolKind.Role,
        CompletionItemKind.Package => SymbolKind.Package,
        CompletionItemKind.Index => SymbolKind.Index,
        _ => null,
    };

    // ── Quick Info popup on demand — Ctrl+Space on a complete identifier (P3, §8A) ────────────

    // Shows the facts (Quick Info) for a fully-typed, resolved identifier at the caret — the same
    // card the Ctrl-hover tooltip and the completion detail pane render (one source of truth via
    // QuickInfoView). Returns false when the caret is NOT on a resolved reference (a partial word
    // still being typed, a blank position, an unresolved name) so the caller falls through to the
    // completion list. Read-only — §0 holds trivially.
    private bool TryShowQuickInfoAtCaret(bool allowSyncRefresh)
    {
        var model = ResolveModel(allowSyncRefresh);
        if (model is null) return false;

        var info = QuickInfoEngine.GetQuickInfo(model, _editor.CaretOffset);
        if (info is null) return false;

        ShowQuickInfoPopup(info);
        return true;
    }

    private void ShowQuickInfoPopup(QuickInfo info)
    {
        _parameterHelper.Hide();
        EnsureQuickInfoPopup();
        var card = QuickInfoView.Build(info, _editor.ActualThemeVariant);
        card.IsHitTestVisible = false; // never intercept the pointer (mirrors the hover tooltip)
        _quickInfo!.Child = card;
        EditorPopups.PlaceAtCaret(_editor, _quickInfo);
        _quickInfo.IsOpen = false; // toggle so the placement rect is re-read
        _quickInfo.IsOpen = true;
    }

    private void EnsureQuickInfoPopup()
    {
        if (_quickInfo is not null) return;
        // IsLightDismissEnabled=false + hit-test-invisible content (like the hover tooltip) so the
        // popup never steals editor focus — a following Ctrl+Space still reaches the TextArea to
        // escalate to the list. Dismissed explicitly on type / Escape / caret move / window open.
        _quickInfo = new Popup { PlacementTarget = _editor, IsLightDismissEnabled = false };
        ((ISetLogicalParent)_quickInfo).SetParent(_editor);
    }

    private void HideQuickInfo()
    {
        if (_quickInfo is { IsOpen: true } p) p.IsOpen = false;
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e) => HideQuickInfo();

    private CompletionWindow OpenWindow(int startOffset, int endOffset)
    {
        // The completion list, the Parameter Helper, and the Quick Info popup shouldn't stack —
        // the list wins.
        _parameterHelper.Hide();
        HideQuickInfo();
        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }
        return new NonFocusClosingCompletionWindow(_editor.TextArea)
        {
            StartOffset = startOffset,
            EndOffset = endOffset,
            CloseAutomatically = true,
        };
    }

    private bool FinishWindow(CompletionWindow window)
    {
        if (window.CompletionList.CompletionData.Count == 0) return false;
        window.Closed += (_, _) => _window = null;
        _window = window;
        window.Show();
        ApplyInitialFilter(window);
        return true;
    }

    // AvaloniaEdit's CompletionWindow filters the list ONLY on a subsequent CaretPositionChanged — a
    // window opened with text already typed before the caret (StartOffset < caret) shows the FULL,
    // UNFILTERED list until the caret next moves. That was the "looks like nothing was typed" bug:
    // "n.nrdok|" + Ctrl+Space listed every column instead of narrowing to NRDOK. We apply the initial
    // filter to the already-typed prefix (StartOffset..caret) ourselves so the list narrows on open.
    // No-op when nothing is typed (a fresh "n.|" / Ctrl+Space on whitespace) → the full list stands.
    private void ApplyInitialFilter(CompletionWindow window)
    {
        var doc = _editor.Document;
        if (doc is null) return;
        int start = window.StartOffset;
        int caret = _editor.CaretOffset;
        if (caret > start && start >= 0 && caret <= doc.TextLength)
        {
            window.CompletionList.SelectItem(doc.GetText(start, caret - start));
        }
    }

    // ── Parameter Helper (design §28) — the ONE parameter-info surface ─────────────────────────

    /// <summary>Shows the unified Parameter Helper at <paramref name="offset"/> (the double-click entry
    /// point — NavigationController delegates here). Refreshes the model synchronously first so a
    /// just-typed identifier is reflected. Returns whether the offset is a parameter site.</summary>
    public bool TryShowParameterHelperAt(int offset)
    {
        _language.EnsureFreshModel();
        return _parameterHelper.ShowAt(offset);
    }

    // The typing / Ctrl+Shift+Space entry point: refresh the model (count the just-typed paren/comma),
    // then show the helper at the caret — unless the completion list is open (the list wins).
    private void TriggerParameterHelper()
    {
        if (_window is not null) { _parameterHelper.Hide(); return; }
        _language.EnsureFreshModel();
        _parameterHelper.ShowAt(_editor.CaretOffset);
    }

    // Warms the metadata a signature needs — routine params for a proc/function, columns for an
    // INSERT/UPDATE target — then rebuilds the shared model and returns it. The warm-then-rebuild dance
    // the Parameter Helper uses when a callee's parameters/columns aren't cached yet.
    private async Task<SemanticModel?> WarmForSignatureAndRebuildAsync(string label, SignatureKind kind)
    {
        try
        {
            if (kind is SignatureKind.Procedure or SignatureKind.Function)
            {
                if (_ensureRoutineParamsAsync is not null) await _ensureRoutineParamsAsync(label).ConfigureAwait(true);
            }
            else
            {
                if (_ensureColumnsAsync is not null) await _ensureColumnsAsync(label).ConfigureAwait(true);
            }
            _language.RefreshModelWithMetadata();
        }
        catch (Exception)
        {
            // Best-effort: a warm failure must never crash the editor.
        }
        return _language.Model;
    }

    // P4: a completion window that does NOT dismiss on text-area focus loss / main-window
    // deactivation. Dragging the list's OWN scrollbar opens as a separate popup window, which
    // deactivates the parent and — via AvaloniaEdit's CloseIfFocusLost — closed the list mid-scroll
    // (the reported bug). CloseOnFocusLost is a protected virtual get-only property, so overriding
    // its getter to false is the intended way to disable that path. The list still closes via
    // CloseAutomatically (the caret leaving the [Start,End] range), Escape, item selection, and
    // non-matching input — only the focus-lost close is disabled.
    private sealed class NonFocusClosingCompletionWindow : CompletionWindow
    {
        public NonFocusClosingCompletionWindow(TextArea textArea) : base(textArea) { }

        protected override bool CloseOnFocusLost => false;
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;

namespace EmberTern.App.Completion;

/// <summary>
/// Wires Ctrl+Space + auto-trigger autocompletion to an <see cref="TextEditor"/>.
/// One controller per editor; <see cref="Detach"/> unhooks the editor events.
/// </summary>
/// <remarks>
/// Etap 0 responsiveness (design §7/§15): the per-keystroke handler does <b>no
/// whole-document work</b>. It inspects only the few characters before the caret
/// (via <see cref="CaretContext"/> over the AvaloniaEdit document, never
/// <c>_editor.Text</c>) and resolves dot qualifiers against the alias map the
/// per-editor <see cref="EditorLanguageService"/> keeps cached off the keystroke.
/// The identifier auto-popup is idle-debounced (pops on a pause, not mid-burst);
/// a typed <c>.</c> and Ctrl+Space stay immediate.
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
    private readonly Func<IReadOnlyList<MetadataObject>> _objectsProvider;
    // Known table/view names for dot resolution. Null → dot completion can only
    // resolve NEW/OLD (see _contextTableProvider) and otherwise degrades.
    private readonly Func<IReadOnlyCollection<string>>? _knownTablesProvider;
    private readonly Func<string, IReadOnlyList<ColumnSpec>?>? _cachedColumnsProvider;
    private readonly Func<string, Task<IReadOnlyList<ColumnSpec>>>? _ensureColumnsAsync;
    // Trigger context: NEW./OLD. resolve to the trigger's relation. Null for
    // non-trigger editors, where NEW/OLD have no meaning. Read live so it tracks
    // the table the user picks in Easy mode.
    private readonly Func<string?>? _contextTableProvider;
    private readonly DispatcherTimer _autoPopup;
    private CompletionWindow? _window;

    public SqlCompletionController(
        TextEditor editor,
        Func<IReadOnlyList<MetadataObject>> objectsProvider,
        Func<IReadOnlyCollection<string>>? knownTablesProvider = null,
        Func<string, IReadOnlyList<ColumnSpec>?>? cachedColumnsProvider = null,
        Func<string, Task<IReadOnlyList<ColumnSpec>>>? ensureColumnsAsync = null,
        Func<string?>? contextTableProvider = null)
    {
        _editor = editor;
        _objectsProvider = objectsProvider;
        _knownTablesProvider = knownTablesProvider;
        _cachedColumnsProvider = cachedColumnsProvider;
        _ensureColumnsAsync = ensureColumnsAsync;
        _contextTableProvider = contextTableProvider;

        _language = new EditorLanguageService(editor);
        _autoPopup = new DispatcherTimer();
        _autoPopup.Tick += OnAutoPopupTick;

        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.KeyDown += OnKeyDown;
    }

    public void Detach()
    {
        _editor.TextArea.TextEntered -= OnTextEntered;
        _editor.TextArea.KeyDown -= OnKeyDown;
        CancelAutoPopup();
        _autoPopup.Tick -= OnAutoPopupTick;
        _language.Dispose();
        _window?.Close();
        _window = null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space — force-open the completion window immediately, regardless of
        // word length, from cached state (design §7.4: Ctrl+Space always works).
        if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            CancelAutoPopup();
            // Dot context wins when present (qualifier already typed). Deliberate
            // trigger → allow the synchronous alias refresh so a just-typed alias
            // resolves. Otherwise fall back to keyword/object list.
            if (!TryShowDotCompletion(force: true, allowSyncRefresh: true))
            {
                ShowCompletion(force: true);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _window is not null)
        {
            // Defensive: AvaloniaEdit closes the window on Escape itself, but an
            // explicit close prevents stale handles if the framework changes.
            CancelAutoPopup();
            _window.Close();
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        var typed = e.Text;
        if (string.IsNullOrEmpty(typed)) return;

        var c = typed[0];

        // Dot just typed — deliberate request for ALIAS./TABLE. columns. Immediate,
        // with a synchronous alias refresh on cache-miss.
        if (c == '.')
        {
            CancelAutoPopup();
            _window?.Close();
            TryShowDotCompletion(force: false, allowSyncRefresh: true);
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

        // Typing into a dot prefix (e.g. "N.I" → after "I"): keep the column flavor.
        // Cache-only (the preceding '.' keystroke already warmed the alias map), so
        // this stays free of whole-document work.
        if (TryShowDotCompletion(force: false, allowSyncRefresh: false))
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

        // The caret may have entered a dot context during the idle (rare) — prefer
        // columns there, cache-only.
        if (CaretContext.GetDotContext(doc, caret) is not null)
        {
            TryShowDotCompletion(force: false, allowSyncRefresh: false);
            return;
        }

        var word = CaretContext.GetCurrentWord(doc, caret);
        if (SqlCompletionContext.ShouldAutoTrigger(word.Text))
        {
            ShowCompletion(force: false, word);
        }
    }

    private bool TryShowDotCompletion(bool force, bool allowSyncRefresh)
    {
        if (_knownTablesProvider is null && _contextTableProvider is null) return false;

        var doc = _editor.Document;
        if (doc is null) return false;
        var caret = _editor.CaretOffset;

        var dot = CaretContext.GetDotContext(doc, caret);
        if (dot is null) return false;

        var table = ResolveTableForDot(dot.Value, allowSyncRefresh);
        if (table is null)
        {
            // Unknown qualifier — silently bail unless forced (Ctrl+Space). Falling
            // back to plain word completion on the auto path would be confusing
            // (user typed "X." expecting X's columns, not SQL keywords).
            return force && ShowWindowWithColumns(dot.Value, Array.Empty<ColumnSpec>(), force);
        }

        // Cache hit → render immediately.
        var cached = _cachedColumnsProvider?.Invoke(table);
        if (cached is not null)
        {
            ShowWindowWithColumns(dot.Value, cached, force);
            return true;
        }

        // Cache miss → fetch asynchronously, then show. The Dispatcher hop
        // guarantees we don't show columns under a stale caret position.
        if (_ensureColumnsAsync is not null)
        {
            _ = LoadAndShowAsync(dot.Value, table);
            return true;
        }

        return false;
    }

    // Resolves the qualifier before the dot to a table name. In a trigger body the
    // pseudo-records NEW and OLD resolve to the trigger's relation (context provider);
    // everything else resolves against the cached alias map + known table names.
    private string? ResolveTableForDot(DotContext dot, bool allowSyncRefresh)
    {
        if (_contextTableProvider is not null
            && (string.Equals(dot.Qualifier, "NEW", StringComparison.OrdinalIgnoreCase)
                || string.Equals(dot.Qualifier, "OLD", StringComparison.OrdinalIgnoreCase)))
        {
            var t = _contextTableProvider();
            if (!string.IsNullOrEmpty(t)) return t;
        }

        if (_knownTablesProvider is null) return null;

        // Deliberate triggers may pay for a synchronous refresh so a just-typed
        // alias resolves; the per-keystroke auto path never does (cache-only).
        if (allowSyncRefresh) _language.EnsureFreshAliases();

        return SqlAliasResolver.ResolveTableForQualifier(
            _language.Aliases, dot.Qualifier, _knownTablesProvider());
    }

    private async Task LoadAndShowAsync(DotContext dot, string table)
    {
        try
        {
            var cols = await _ensureColumnsAsync!(table).ConfigureAwait(true);
            // Bail if the user moved the caret out of the dot context while we waited.
            var doc = _editor.Document;
            if (doc is null) return;
            var currentDot = CaretContext.GetDotContext(doc, _editor.CaretOffset);
            if (currentDot is null
                || !string.Equals(currentDot.Value.Qualifier, dot.Qualifier, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            ShowWindowWithColumns(currentDot.Value, cols, force: false);
        }
        catch (Exception)
        {
            // Best-effort completion: a failure here should never crash the editor.
        }
    }

    private bool ShowWindowWithColumns(DotContext dot, IReadOnlyList<ColumnSpec> columns, bool force)
    {
        if (_window is not null)
        {
            _window.Close();
            _window = null;
        }

        if (columns.Count == 0 && !force) return false;

        var window = new CompletionWindow(_editor.TextArea)
        {
            StartOffset = dot.PrefixStart,
            EndOffset = dot.PrefixEnd,
            CloseAutomatically = true,
        };

        var data = window.CompletionList.CompletionData;
        foreach (var col in columns)
        {
            data.Add(new SqlCompletionData(col.Name, SqlCompletionKind.Column, columnType: col.Type));
        }

        if (data.Count == 0) return false;

        window.Closed += (_, _) => _window = null;
        _window = window;
        window.Show();
        return true;
    }

    private void ShowCompletion(bool force)
    {
        var doc = _editor.Document;
        if (doc is null) return;
        ShowCompletion(force, CaretContext.GetCurrentWord(doc, _editor.CaretOffset));
    }

    private void ShowCompletion(bool force, CurrentWord word)
    {
        if (_window is not null)
        {
            // Already shown — let it keep filtering.
            return;
        }

        if (!force && !SqlCompletionContext.ShouldAutoTrigger(word.Text))
        {
            return;
        }

        var window = new CompletionWindow(_editor.TextArea)
        {
            StartOffset = word.Start,
            EndOffset = word.End,
            CloseAutomatically = true,
        };

        var data = window.CompletionList.CompletionData;
        foreach (var kw in SqlKeywords.All)
        {
            data.Add(new SqlCompletionData(kw, SqlCompletionKind.Keyword));
        }

        foreach (var obj in _objectsProvider())
        {
            data.Add(new SqlCompletionData(obj.Name, MapKind(obj.Kind)));
        }

        if (data.Count == 0)
        {
            return;
        }

        window.Closed += (_, _) => _window = null;
        _window = window;
        window.Show();
    }

    private static SqlCompletionKind MapKind(MetadataObjectKind kind) => kind switch
    {
        MetadataObjectKind.Table => SqlCompletionKind.Table,
        MetadataObjectKind.SystemTable => SqlCompletionKind.Table,
        MetadataObjectKind.View => SqlCompletionKind.View,
        MetadataObjectKind.Procedure => SqlCompletionKind.Procedure,
        MetadataObjectKind.Function => SqlCompletionKind.Function,
        MetadataObjectKind.Trigger => SqlCompletionKind.Trigger,
        MetadataObjectKind.Generator => SqlCompletionKind.Generator,
        MetadataObjectKind.Domain => SqlCompletionKind.Domain,
        MetadataObjectKind.Exception => SqlCompletionKind.Exception,
        MetadataObjectKind.Package => SqlCompletionKind.Package,
        MetadataObjectKind.Role => SqlCompletionKind.Role,
        MetadataObjectKind.Index => SqlCompletionKind.Index,
        MetadataObjectKind.User => SqlCompletionKind.Role,
        _ => SqlCompletionKind.Keyword,
    };
}

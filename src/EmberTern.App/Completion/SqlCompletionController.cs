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
internal sealed class SqlCompletionController
{
    private readonly TextEditor _editor;
    private readonly Func<IReadOnlyList<MetadataObject>> _objectsProvider;
    // Step-2 callbacks. Both nullable — when null, dot completion silently
    // degrades to plain word completion.
    private readonly Func<string, int, string?>? _dotTableResolver;
    private readonly Func<string, IReadOnlyList<string>?>? _cachedColumnsProvider;
    private readonly Func<string, Task<IReadOnlyList<string>>>? _ensureColumnsAsync;
    private CompletionWindow? _window;

    public SqlCompletionController(
        TextEditor editor,
        Func<IReadOnlyList<MetadataObject>> objectsProvider,
        Func<string, int, string?>? dotTableResolver = null,
        Func<string, IReadOnlyList<string>?>? cachedColumnsProvider = null,
        Func<string, Task<IReadOnlyList<string>>>? ensureColumnsAsync = null)
    {
        _editor = editor;
        _objectsProvider = objectsProvider;
        _dotTableResolver = dotTableResolver;
        _cachedColumnsProvider = cachedColumnsProvider;
        _ensureColumnsAsync = ensureColumnsAsync;

        _editor.TextArea.TextEntered += OnTextEntered;
        _editor.TextArea.KeyDown += OnKeyDown;
    }

    public void Detach()
    {
        _editor.TextArea.TextEntered -= OnTextEntered;
        _editor.TextArea.KeyDown -= OnKeyDown;
        _window?.Close();
        _window = null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Ctrl+Space — force-open the completion window regardless of word length.
        if (e.Key == Key.Space && (e.KeyModifiers & KeyModifiers.Control) == KeyModifiers.Control)
        {
            // Dot context wins when present (qualifier already typed before the
            // Ctrl+Space). Otherwise fall back to keyword/object list.
            if (!TryShowDotCompletion(force: true))
            {
                ShowCompletion(force: true);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _window is not null)
        {
            // Defensive: AvaloniaEdit closes the window on Escape itself, but
            // explicit close prevents stale handles if the framework changes.
            _window.Close();
        }
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        var typed = e.Text;
        if (string.IsNullOrEmpty(typed)) return;

        var c = typed[0];

        // Dot just typed — try column completion for ALIAS./TABLE..
        if (c == '.')
        {
            if (_window is not null)
            {
                _window.Close();
            }
            TryShowDotCompletion(force: false);
            return;
        }

        if (!SqlCompletionContext.IsIdentifierChar(c)) return;

        if (_window is not null)
        {
            // Window is already open — AvaloniaEdit's CompletionList narrows the
            // selection by typed prefix on its own. Nothing to do.
            return;
        }

        // If we're typing into a dot prefix (e.g. "N.I" → after "I"), keep the
        // column flavor instead of falling back to plain keyword completion.
        if (TryShowDotCompletion(force: false))
        {
            return;
        }

        var word = SqlCompletionContext.GetCurrentWord(_editor.Text ?? string.Empty, _editor.CaretOffset);
        if (SqlCompletionContext.ShouldAutoTrigger(word.Text))
        {
            ShowCompletion(force: false);
        }
    }

    private bool TryShowDotCompletion(bool force)
    {
        if (_dotTableResolver is null) return false;

        var text = _editor.Text ?? string.Empty;
        var caret = _editor.CaretOffset;

        var dot = SqlCompletionContext.GetDotContext(text, caret);
        if (dot is null) return false;

        var table = _dotTableResolver(text, caret);
        if (table is null)
        {
            // Unknown qualifier — silently bail. Falling back to plain word
            // completion here would be confusing (user typed "X." expecting
            // X's columns, doesn't want SQL keywords instead).
            return force ? ShowWindowWithColumns(dot.Value, Array.Empty<string>(), force) : false;
        }

        // Cache hit → render immediately.
        var cached = _cachedColumnsProvider?.Invoke(table);
        if (cached is not null)
        {
            ShowWindowWithColumns(dot.Value, cached, force);
            return true;
        }

        // Cache miss → fetch asynchronously, then show. Don't await on the UI
        // thread; the Dispatcher hop guarantees we don't show columns under a
        // stale caret position.
        if (_ensureColumnsAsync is not null)
        {
            _ = LoadAndShowAsync(dot.Value, table);
            return true;
        }

        return false;
    }

    private async Task LoadAndShowAsync(DotContext dot, string table)
    {
        try
        {
            var cols = await _ensureColumnsAsync!(table).ConfigureAwait(true);
            // Bail if the user moved the caret out of the dot context while we waited.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var currentText = _editor.Text ?? string.Empty;
                var currentDot = SqlCompletionContext.GetDotContext(currentText, _editor.CaretOffset);
                if (currentDot is null || !string.Equals(currentDot.Value.Qualifier, dot.Qualifier, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                ShowWindowWithColumns(currentDot.Value, cols, force: false);
            });
        }
        catch (Exception)
        {
            // Best-effort completion: a failure here should never crash the editor.
        }
    }

    private bool ShowWindowWithColumns(DotContext dot, IReadOnlyList<string> columns, bool force)
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
            data.Add(new SqlCompletionData(col, SqlCompletionKind.Column));
        }

        if (data.Count == 0) return false;

        window.Closed += (_, _) => _window = null;
        _window = window;
        window.Show();
        return true;
    }

    private void ShowCompletion(bool force)
    {
        if (_window is not null)
        {
            // Already shown — let it keep filtering.
            return;
        }

        var text = _editor.Text ?? string.Empty;
        var caret = _editor.CaretOffset;
        var word = SqlCompletionContext.GetCurrentWord(text, caret);

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Sql.Templates;

namespace EmberTern.App.Sql;

/// <summary>
/// Makes a <see cref="TextEditor"/> a drop target for metadata objects dragged from the
/// sidebar: on drop it shows a flyout of the object's applicable templates (built from the
/// kind — no metadata read) at the drop point, and on pick inserts the generated snippet
/// exactly at the drop offset. One attach per editor — used by the main SQL editor and by
/// every source/PSQL editor in the detail views.
/// <para>
/// Each item carries a lightweight tooltip preview of the SQL it will insert. The preview
/// stays fast: the flyout appears instantly (kind only); the object's metadata is loaded
/// ONCE, lazily on the first hover, then cached — that same context serves every preview
/// and the final insertion, so the whole interaction costs a single catalog read.
/// </para>
/// </summary>
public sealed class SqlSnippetDropTarget
{
    /// <summary>
    /// Shared in-process drag payload format — the <see cref="MetadataObject"/> travels by
    /// reference within the app (no OS serialization). Used by the sidebar drag source and
    /// every drop target.
    /// </summary>
    public static readonly DataFormat<MetadataObject> DragFormat =
        DataFormat.CreateInProcessFormat<MetadataObject>("embertern.metadata-object");

    // Preview caps — keep the tooltip a quick confirmation, not a full document.
    private const int MaxPreviewLines = 12;
    private const int MaxPreviewChars = 500;

    private readonly TextEditor _editor;
    private readonly MainWindowViewModel _vm;
    private readonly SnippetInsertionContext _insertion;

    private SqlSnippetDropTarget(TextEditor editor, MainWindowViewModel vm, SnippetInsertionContext insertion)
    {
        _editor = editor;
        _vm = vm;
        _insertion = insertion;
    }

    public static void Attach(
        TextEditor editor,
        MainWindowViewModel vm,
        SnippetInsertionContext insertion = SnippetInsertionContext.PlainSql)
    {
        var target = new SqlSnippetDropTarget(editor, vm, insertion);
        DragDrop.SetAllowDrop(editor, true);
        editor.AddHandler(DragDrop.DragOverEvent, target.OnDragOver);
        editor.AddHandler(DragDrop.DropEvent, target.OnDrop);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer is { } dt && dt.Contains(DragFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer?.TryGetValue(DragFormat) is not { } obj) return;
        e.Handled = true;

        // Resolve the exact drop offset in the document (append at end if past the last line).
        var tvp = _editor.GetPositionFromPoint(e.GetPosition(_editor));
        var offset = tvp is null
            ? _editor.Document.TextLength
            : _editor.Document.GetOffset(tvp.Value.Location);

        // Instant, metadata-free menu built from the dropped object's kind + this editor's
        // insertion context (PSQL scaffolds only appear in a PSQL body editor).
        var descriptors = _vm.SnippetTemplatesFor(obj.Kind, _insertion);
        if (descriptors.Count == 0) return;

        // The object's metadata is loaded lazily and memoized here: previews (on hover) and
        // the insert (on click) both await this one task, so there's a single catalog read.
        Task<SnippetContext?>? contextTask = null;
        Task<SnippetContext?> Context() => contextTask ??= _vm.BuildSnippetContextAsync(obj, _insertion);

        var flyout = new MenuFlyout();
        var items = new List<(MenuItem Item, string Id)>(descriptors.Count);
        var previewsRequested = false;

        foreach (var descriptor in descriptors)
        {
            var id = descriptor.Id;
            var item = new MenuItem { Header = descriptor.Title };
            item.Click += (_, _) => InsertAtOffset(Context, id, offset);
            item.PointerEntered += (_, _) =>
            {
                if (previewsRequested) return;
                previewsRequested = true;
                _ = FillPreviewsAsync(Context, items);
            };
            items.Add((item, id));
            flyout.Items.Add(item);
        }
        flyout.ShowAt(_editor, showAtPointer: true);
    }

    private async Task FillPreviewsAsync(Func<Task<SnippetContext?>> context, List<(MenuItem Item, string Id)> items)
    {
        try
        {
            var ctx = await context();
            if (ctx is null) return;
            foreach (var (item, id) in items)
            {
                var text = _vm.GenerateSnippet(ctx, id).Text;
                ToolTip.SetTip(item, BuildPreviewTip(text));
                ToolTip.SetShowDelay(item, 250);
                // Anchor to the RIGHT edge of the row (which spans the flyout width), so the
                // preview appears beside the list — never over the items below it.
                ToolTip.SetPlacement(item, PlacementMode.Right);
            }
        }
        catch (Exception)
        {
            // Preview is best-effort; a metadata-load failure just leaves items untooltipped.
        }
    }

    private async void InsertAtOffset(Func<Task<SnippetContext?>> context, string templateId, int offset)
    {
        try
        {
            var ctx = await context();
            if (ctx is null) return;
            var snippet = _vm.GenerateSnippet(ctx, templateId);

            var insertAt = Math.Clamp(offset, 0, _editor.Document.TextLength);
            _editor.Document.Insert(insertAt, snippet.Text);

            if (snippet.Placeholders.Count > 0)
            {
                var ph = snippet.Placeholders[0];
                _editor.Select(insertAt + ph.Start, ph.Length);
            }
            else
            {
                _editor.CaretOffset = insertAt + snippet.Text.Length;
            }
            _editor.Focus();
        }
        catch (Exception)
        {
            // Slice scope: metadata-load / generation failures are swallowed here.
            // Surfacing them in the Messages log is a follow-up.
        }
    }

    private static Control BuildPreviewTip(string sql)
        => new TextBlock
        {
            Text = TruncatePreview(sql, MaxPreviewLines, MaxPreviewChars),
            FontFamily = new FontFamily("Cascadia Code,Consolas,Courier New,monospace"),
            FontSize = 12,
            TextWrapping = TextWrapping.NoWrap,
        };

    /// <summary>
    /// Cap the preview to a readable size: at most <paramref name="maxLines"/> lines and
    /// <paramref name="maxChars"/> characters, appending an ellipsis when either is exceeded.
    /// Pure — unit-tested.
    /// </summary>
    internal static string TruncatePreview(string text, int maxLines, int maxChars)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var truncated = false;

        var lines = text.Split('\n');
        if (lines.Length > maxLines)
        {
            text = string.Join("\n", lines.Take(maxLines));
            truncated = true;
        }

        if (text.Length > maxChars)
        {
            text = text.Substring(0, maxChars).TrimEnd();
            truncated = true;
        }

        return truncated ? text + "\n…" : text;
    }
}

using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.Completion;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class GlobalSearchTabView : UserControl
{
    private TextEditor? _preview;
    private SearchMatchHighlighter? _highlighter;
    private GlobalSearchTabViewModel? _currentVm;

    public GlobalSearchTabView()
    {
        InitializeComponent();
        _preview = this.FindControl<TextEditor>("PreviewEditor");
        if (_preview is not null)
        {
            _highlighter = SearchMatchHighlighter.Attach(_preview);
            _preview.AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        }
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null) _currentVm.PropertyChanged -= OnVmPropertyChanged;
        _currentVm = DataContext as GlobalSearchTabViewModel;
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            _highlighter?.SetTerm(_currentVm.Term);
            PushPreview();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GlobalSearchTabViewModel.PreviewText)) PushPreview();
    }

    // Read-only preview; push VM text in (two-way TextEditor.Text is flaky — same gotcha
    // as the SQL/DDL editors). After the text lands, jump to the first match.
    private void PushPreview()
    {
        if (_preview is null || _currentVm is null) return;
        var text = _currentVm.PreviewText ?? string.Empty;
        if (_preview.Text != text) _preview.Text = text;
        _highlighter?.SetTerm(_currentVm.Term);
        SelectFirstMatch();
    }

    private void OnResultsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_currentVm is null) return;
        if (ResultsTree.SelectedItem is SearchResultItemViewModel item)
            _currentVm.SelectedItem = item;
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_currentVm is null) return;
        // Only act on a leaf (double-clicking a group toggles it).
        if (ResultsTree.SelectedItem is SearchResultItemViewModel item)
        {
            _currentVm.Open(item);
            e.Handled = true;
        }
    }

    // F3 = next match, Shift+F3 = previous. Wraps around.
    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.F3 || _preview is null || _currentVm is null) return;
        var term = _currentVm.Term;
        if (string.IsNullOrEmpty(term)) return;
        bool previous = (e.KeyModifiers & KeyModifiers.Shift) == KeyModifiers.Shift;
        if (previous) SelectPreviousMatch(term); else SelectNextMatch(term);
        e.Handled = true;
    }

    private void SelectFirstMatch()
    {
        if (_preview is null || _currentVm is null) return;
        var term = _currentVm.Term;
        if (string.IsNullOrEmpty(term)) return;
        int idx = _preview.Text?.IndexOf(term, StringComparison.OrdinalIgnoreCase) ?? -1;
        if (idx >= 0) SelectAt(idx, term.Length);
    }

    private void SelectNextMatch(string term)
    {
        var text = _preview!.Text ?? string.Empty;
        int from = _preview.SelectionStart + Math.Max(1, _preview.SelectionLength);
        int idx = text.IndexOf(term, Math.Min(from, text.Length), StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = text.IndexOf(term, 0, StringComparison.OrdinalIgnoreCase); // wrap
        if (idx >= 0) SelectAt(idx, term.Length);
    }

    private void SelectPreviousMatch(string term)
    {
        var text = _preview!.Text ?? string.Empty;
        int before = Math.Max(0, _preview.SelectionStart - 1);
        int idx = before <= 0 ? -1 : text.LastIndexOf(term, before - 1, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) idx = text.LastIndexOf(term, StringComparison.OrdinalIgnoreCase); // wrap
        if (idx >= 0) SelectAt(idx, term.Length);
    }

    private void SelectAt(int offset, int length)
    {
        if (_preview is null) return;
        _preview.CaretOffset = offset;
        _preview.Select(offset, length);
        var loc = _preview.Document.GetLocation(offset);
        _preview.ScrollTo(loc.Line, loc.Column);
    }

    private void ApplyEditorTheme()
    {
        if (_preview is null) return;
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light ? App.FirebirdSyntaxLightName : App.FirebirdSyntaxName;
        _preview.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(name);
        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush brush)
        {
            _preview.TextArea.SelectionBrush = brush;
        }
    }
}

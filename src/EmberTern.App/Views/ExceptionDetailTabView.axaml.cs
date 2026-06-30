using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class ExceptionDetailTabView : UserControl
{
    private TextEditor? _ddlEditor;
    private ExceptionDetailTabViewModel? _currentVm;

    public ExceptionDetailTabView()
    {
        InitializeComponent();
        _ddlEditor = this.FindControl<TextEditor>("ExceptionDdlEditor");
        ApplyEditorTheme();
        ActualThemeVariantChanged += (_, _) => ApplyEditorTheme();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged -= OnVmPropertyChanged;
        }
        _currentVm = DataContext as ExceptionDetailTabViewModel;
        if (_currentVm is not null)
        {
            _currentVm.PropertyChanged += OnVmPropertyChanged;
            PushDdl();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExceptionDetailTabViewModel.DdlText))
        {
            PushDdl();
        }
    }

    // DDL editor is read-only; push VM text in (two-way TextEditor.Text binding is
    // flaky, same gotcha as the SQL / DDL editors).
    private void PushDdl()
    {
        if (_ddlEditor is null || _currentVm is null) return;
        var text = _currentVm.DdlText ?? string.Empty;
        if (_ddlEditor.Text != text) _ddlEditor.Text = text;
    }

    private void OnDependencyNodeDoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DependencyLeafNode leaf } && _currentVm is not null)
        {
            _currentVm.RequestOpen(leaf);
            e.Handled = true;
        }
    }

    private void ApplyEditorTheme()
    {
        if (_ddlEditor is null) return;
        var theme = ActualThemeVariant;
        var name = theme == ThemeVariant.Light
            ? App.FirebirdSyntaxLightName
            : App.FirebirdSyntaxName;
        _ddlEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(name);
        if (Application.Current?.Resources.TryGetResource("SelectionBrush", theme, out var res) == true
            && res is IBrush brush)
        {
            _ddlEditor.TextArea.SelectionBrush = brush;
        }
    }
}

using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class NewConnectionDialog : Window
{
    public NewConnectionDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is NewConnectionDialogViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.RequestClose += OnRequestClose;
        }
    }

    private void OnRequestClose()
    {
        var vm = DataContext as NewConnectionDialogViewModel;
        Close(vm?.Result);
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NewConnectionDialogViewModel vm)
        {
            return;
        }

        var picker = StorageProvider;
        var result = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Firebird database file",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Firebird database")
                {
                    Patterns = new[] { "*.fdb", "*.gdb" },
                },
                FilePickerFileTypes.All,
            },
        });

        var file = result.FirstOrDefault();
        if (file is not null)
        {
            vm.DatabasePath = file.Path.LocalPath;
        }
    }

    private async void OnBrowseClientLibraryClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NewConnectionDialogViewModel vm)
        {
            return;
        }

        var picker = StorageProvider;
        var result = await picker.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select fbclient.dll",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Firebird client library")
                {
                    Patterns = new[] { "fbclient.dll", "*.dll" },
                },
                FilePickerFileTypes.All,
            },
        });

        var file = result.FirstOrDefault();
        if (file is not null)
        {
            vm.ClientLibraryPath = file.Path.LocalPath;
        }
    }
}

using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using EmberTern.App.Behaviors;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

public partial class NewConnectionDialog : Window
{
    public NewConnectionDialog()
    {
        InitializeComponent();

        // ⭐ M4.4 / M‑5. JEDYNY z czterech kandydatów z prawdziwym wzrostem treści PO otwarciu: komunikat
        // testu połączenia stoi w wierszu 2, czyli POZA `ScrollViewerem`, i się zawija — długi błąd
        // Firebirda to kilka linii. Okno nie miało dotąd żadnego ograniczenia wysokości, więc rosło w dół
        // od pozycji wyśrodkowanej (#295) i spychało stopkę pod krawędź ekranu.
        // ⚠ Formularz ma własne ograniczenie 520 na `ScrollViewerze`; to ogranicza PRZEWIJANĄ część, a nie
        // okno, więc nie zastępuje sufitu — dlatego oba mechanizmy są tu potrzebne, a nie duplikują się.
        GrowingDialogBehavior.Attach(this);

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
            Title = UiStrings.FilePickerSelectDatabase,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(UiStrings.FilePickerFirebirdDatabases)
                {
                    // Real-world extensions seen in the field: .fb AND .fdb (Firebird),
                    // .gdb (legacy InterBase/FB), .ib (InterBase). .fb was the miss — the
                    // user's DB is SZKOLENIE.FB and the .fdb-only filter hid it.
                    // Avalonia glob patterns are case-sensitive on Linux/macOS, so list
                    // upper-case variants too; Windows is case-insensitive and ignores
                    // the duplicates.
                    Patterns = new[]
                    {
                        "*.fdb", "*.fb", "*.gdb", "*.ib",
                        "*.FDB", "*.FB", "*.GDB", "*.IB",
                    },
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

}

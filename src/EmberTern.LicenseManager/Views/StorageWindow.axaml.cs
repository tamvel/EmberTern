using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.ViewModels;

namespace EmberTern.LicenseManager.Views;

/// <summary>
/// The Storage window: where the register lives, how to back it up, how to restore one elsewhere.
///
/// <para>⭐ The four platform services the view model needs — save, open, pick a folder, show a folder —
/// are assigned here as delegates, so the view model stays free of Avalonia types (Architecture rule 1)
/// and every one of its decisions is reachable in a test without a window.</para>
/// </summary>
public sealed partial class StorageWindow : Window
{
    /// <summary>Creates the window.</summary>
    public StorageWindow()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (DataContext is not StorageViewModel model)
            {
                return;
            }

            model.SaveFilePicker = SaveAsync;
            model.OpenBackupPicker = OpenBackupAsync;
            model.RestoreFolderPicker = PickRestoreFolderAsync;
            model.FolderOpener = ShowFolder;

            // ⭐ The two delegates that make "replace the active register" possible at all (D‑6): only
            //    the application owns the register, and only the application can shut itself down. ⛔ The
            //    view model must hold neither — it asks, and the composition root answers.
            //    ⚠ Left unset when there is no desktop lifetime (a headless test): the view model then
            //    behaves as if the register could not be closed, which is the safe answer.
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                model.ActiveRegisterCloser = () => (Application.Current as App)?.ReleaseRegister() ?? false;
                model.ShutdownRequested = () => desktop.Shutdown();
            }
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task<string?> SaveAsync(string suggestedName)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save",
            SuggestedFileName = suggestedName,
            DefaultExtension = Path.GetExtension(suggestedName).TrimStart('.'),
            FileTypeChoices = [BackupFileType, JsonlFileType],
        });

        return file?.TryGetLocalPath();
    }

    private async Task<string?> OpenBackupAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a register backup",
            AllowMultiple = false,
            FileTypeFilter = [BackupFileType],
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private async Task<string?> PickRestoreFolderAsync()
    {
        // ⭐ A FOLDER picker, never a file one. The operator chooses where the restored register goes;
        //    they are never given the chance to point at an existing database, because there is no
        //    argument shaped like that anywhere on this path — see RestoreWorkflow.
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a NEW, empty folder to restore into",
            AllowMultiple = false,
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    // ⚠ UseShellExecute is what makes this open the file manager rather than trying to execute a
    //   directory. The path comes from ManagerPaths, never from anything the operator typed.
    private static void ShowFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // ⚠ Nothing to recover here: the folder is named on screen and can be pasted anywhere. A
            //   failure to launch a file manager must not take the window down with it.
        }
    }

    private static FilePickerFileType BackupFileType { get; } = new("EmberTern register backup")
    {
        Patterns = ["*" + RegisterBackup.FileExtension],
    };

    private static FilePickerFileType JsonlFileType { get; } = new("JSON Lines")
    {
        Patterns = ["*" + RegisterJsonl.FileExtension],
    };
}

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
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

            // ⭐ The two the signing-key task needs (L7.1). The clipboard is pure platform, exactly like
            //   the theme toggle and MainWindow's own copy handler; ⛔ the view model decides WHAT is
            //   copied and WHICH sentence confirms it, and never touches Avalonia to do either.
            model.OpenKeystorePicker = OpenKeystoreBackupAsync;
            model.TextCopier = CopyToClipboardAsync;

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
            Title = FileTypeCatalog.SaveTitle,
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
            Title = FileTypeCatalog.ChooseBackupTitle,
            AllowMultiple = false,
            FileTypeFilter = [BackupFileType],
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    /// <summary>
    /// Picks a keystore BACKUP to verify.
    ///
    /// <para>⚠ Its own filter, not the register backup's: the two are different files, and offering one
    /// picker for both is how an operator verifies the wrong thing and believes the key is safe.</para>
    /// </summary>
    private async Task<string?> OpenKeystoreBackupAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = FileTypeCatalog.ChooseKeystoreBackupTitle,
            AllowMultiple = false,
            FileTypeFilter = [KeyStoreFileType],
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private async Task CopyToClipboardAsync(string value)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(value).ConfigureAwait(true);
        }
    }

    private async Task<string?> PickRestoreFolderAsync()
    {
        // ⭐ A FOLDER picker, never a file one. The operator chooses where the restored register goes;
        //    they are never given the chance to point at an existing database, because there is no
        //    argument shaped like that anywhere on this path — see RestoreWorkflow.
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = FileTypeCatalog.ChooseRestoreFolderTitle,
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

    // ⚠⚠ COMPUTED, never `{ get; } = new(...)`. That shape is an auto-property with an initializer —
    //    i.e. a static readonly — so it resolved its name ONCE at type initialization and froze in
    //    whatever language happened to be in force then. It renders correctly, which is what makes it
    //    dangerous (Loc's class remarks; ManagerSettingsCatalog paid for this lesson in the product).
    private static FilePickerFileType BackupFileType => new(FileTypeCatalog.RegisterBackup)
    {
        Patterns = ["*" + RegisterBackup.FileExtension],
    };

    private static FilePickerFileType JsonlFileType => new(FileTypeCatalog.JsonLines)
    {
        Patterns = ["*" + RegisterJsonl.FileExtension],
    };

    // ⭐ The extension is DERIVED from the one place the file is named (`ManagerPaths.KeyStoreFileName`),
    //   never typed here. Gotcha #284: a copied derived fact goes stale silently, and the correct and the
    //   stale versions read identically.
    private static FilePickerFileType KeyStoreFileType => new(FileTypeCatalog.SigningKeystore)
    {
        Patterns = ["*" + Path.GetExtension(Services.ManagerPaths.KeyStoreFileName)],
    };
}

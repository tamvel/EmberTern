using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EmberTern.App.Settings;
using EmberTern.App.ViewModels;

namespace EmberTern.App.Views;

/// <summary>
/// EmberTern's one home for user preferences — a window, not a workspace tab.
///
/// <para>⚠ <b>The window choice is ratified (Q7) and rests on this codebase, not on convention.</b> A
/// workspace tab would automatically acquire workspace persistence, dirty tracking, the three-way close
/// guard, <c>RefreshAsync</c> dispatch and a <c>ResolveCommand</c> arm — five per-kind families it would have
/// to be threaded into or explicitly excluded from, for a surface the user visits rarely and never edits
/// <i>work</i> in.</para>
///
/// <para>⚠ <b>It takes the app's one <see cref="PreferencesService"/> rather than opening its own store.</b>
/// Two holders of a <see cref="EmberTern.Core.Settings.Preferences"/> snapshot overwrite each other's fields,
/// because the store's <c>Save</c> takes the whole object by design (etap 2, §12.3).</para>
///
/// <para>No <c>CommandId</c> and no shortcut, deliberately: a command earns an id only when a shared surface
/// must speak about it, and nothing lists this one. In particular <b>not</b> <c>Ctrl+,</c> — an unratified
/// gesture would have to pass the collision validator and then appear in Keyboard Shortcuts as a key the user
/// never chose.</para>
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>
    /// ⚠ Present for Avalonia's runtime XAML loader / previewer only (AVLN3001 asks for it). It deliberately
    /// leaves the <c>DataContext</c> unset: a parameterless path that built its own store would read — and one
    /// day write — the real <c>settings.dat</c> from a designer, and it would be the second snapshot holder
    /// this design exists to prevent. EmberTern always uses the other constructor.
    /// </summary>
    public SettingsWindow()
    {
        InitializeComponent();

        // Same reasoning as the Keyboard Shortcuts window: in a window whose left pane is a list and whose top
        // control is a search box, search is what a user reaches for first.
        Opened += (_, _) => Dispatcher.UIThread.Post(() => SearchBox.Focus());
    }

    public SettingsWindow(PreferencesService preferences, SettingsPortability portability)
        : this()
    {
        var vm = new SettingsCenterViewModel(preferences, portability);

        // The view supplies the modal owner, the shell and the pickers — the view model supplies everything that
        // can be decided without them. Same request/callback shape the data ExportDialog already uses.
        vm.RequestExport = () => new SettingsExportDialog(portability).ShowDialog(this);
        vm.RequestImport = () => new SettingsImportDialog(portability).ShowDialog(this);
        vm.RequestRevealFolder = RevealFolderAsync;

        DataContext = vm;
    }

    /// <summary>Opens the settings folder in the shell. Best-effort: a failure to open a file manager must never
    /// take the settings window with it.</summary>
    private static Task RevealFolderAsync(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException
                                      or IOException or UnauthorizedAccessException)
        {
        }

        return Task.CompletedTask;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

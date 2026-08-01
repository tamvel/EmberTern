using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
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

        // ⚠ Text input IS a case gotcha #224 is genuinely for, and — measured — Enter was not (see
        // OnNumericSettingKeyDown). A `TextBox` consumes TextInput in its own class handler, and Avalonia runs
        // class handlers BEFORE instance handlers on the same element, so a bubbling `TextInput="…"` attribute
        // would fire only after the character had already been inserted; marking it handled there is too late.
        // The tunnel is the one phase that can refuse a keystroke before the control acts on it.
        //
        // ⚠ Scoped by the SOURCE, so a window-level handler cannot become a window-wide input grab: it acts
        // only for a control whose DataContext is a numeric row, leaving the search box alone.
        AddHandler(TextInputEvent, OnNumericSettingTextInput, RoutingStrategies.Tunnel);
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

    // ── The numeric commit path (design §5.5.1) ─────────────────────────────────────────────────────────
    //
    // ⭐ Apply-on-change means on CHANGE, and for a free-text field a change is settled on BLUR or ENTER — never
    // per keystroke. Avalonia's TextBox updates its binding on every keystroke, so typing "5000" into a field
    // bound straight to a preference would be four complete encrypted rewrites of settings.dat — and, the part
    // that is not performance, four generations of the single settings.dat.bak, destroying the one hand-recovery
    // net at exactly the moment someone is editing settings.
    //
    // ⚠ So the commit lives HERE, in the view: the view is what knows when a control's value is settled. The
    // view model's EditText follows the keystrokes and persists nothing; NumericSettingViewModel.Commit parses,
    // clamps against the Core range, echoes the settled number back into the field and only then reports a
    // change. Core's API makes the wrong answer unavailable in the first place — PreferencesStore has no
    // per-property setter to stream into (§12.3).

    /// <summary>
    /// Refuses a keystroke that would leave a numeric field holding something no number could grow out of, so a
    /// stray letter simply never appears (QA, 2026-08-01).
    ///
    /// <para>⚠ It asks the ROW, never re-deciding for itself — <c>NumericSettingViewModel.AcceptsText</c> is the
    /// one definition, and it is the row that knows whether its range admits a sign. A second rule here would be
    /// the §5.2.2 drift in its most literal form: a field that accepts what the model rejects.</para>
    ///
    /// <para>⚠ It judges the <b>resulting</b> text, not the character, because acceptability is positional — a
    /// <c>-</c> is legal only first, and length only matters in context. The candidate is built the way the
    /// control would: the current text with the selection replaced.</para>
    ///
    /// <para>⚠ <b>Paste is deliberately not covered here</b> and keeps the shipped behaviour: it does not raise
    /// TextInput, so pasted junk lands and is undone by <see cref="CommitNumeric"/> at blur or Enter. Blocking
    /// it would need a second, differently-shaped hook for a far rarer path, and the tolerant
    /// <c>EditText</c> + clamping <c>Commit</c> already make the outcome correct.</para>
    /// </summary>
    private void OnNumericSettingTextInput(object? sender, TextInputEventArgs e)
    {
        if (e.Source is not TextBox { DataContext: NumericSettingViewModel row } box) return;
        if (e.Text is not { Length: > 0 } inserted) return;

        var current = box.Text ?? string.Empty;
        var start = Math.Clamp(Math.Min(box.SelectionStart, box.SelectionEnd), 0, current.Length);
        var end = Math.Clamp(Math.Max(box.SelectionStart, box.SelectionEnd), start, current.Length);

        var candidate = string.Concat(current.AsSpan(0, start), inserted, current.AsSpan(end));
        if (!row.AcceptsText(candidate)) e.Handled = true;
    }

    private void OnNumericSettingLostFocus(object? sender, RoutedEventArgs e) => CommitNumeric(sender);

    /// <summary>
    /// Enter, as an ordinary bubbling handler on the field itself — the same shape as the blur one beside it.
    ///
    /// <para>⚠ <b>Deliberately NOT a tunnelled window-level handler, and that is a measured decision.</b> Gotcha
    /// #224's tunnel exists for a key an editing control <i>claims</i>; a single-line <c>TextBox</c> does not
    /// claim Enter — probed on the headless session: with <c>AcceptsReturn=false</c> the bubbling handler on the
    /// box runs with <c>Handled=false</c> and the key reaches the window still unhandled. So a window-wide
    /// handler scoped back down by inspecting <c>e.Source</c> would be machinery guarding against nothing, and
    /// an inert guard reads to the next author as a real hazard.</para>
    ///
    /// <para>⚠ What DID have to be right is that the control's <c>DataContext</c> is the row — see the XAML.
    /// Both triggers identify what to settle from it, and on the page's own DataContext they would silently
    /// commit nothing.</para>
    /// </summary>
    private void OnNumericSettingKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        CommitNumeric(sender);

        // Handled, so Enter in a settings field cannot reach anything that treats it as "accept the dialog" —
        // there is no OK button to press (ratified Q8) and the value is already applied.
        e.Handled = true;
    }

    private static void CommitNumeric(object? sender)
    {
        if (sender is Control { DataContext: NumericSettingViewModel row })
        {
            row.Commit();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

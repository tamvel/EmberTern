using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// What a confirmation asks.
/// </summary>
/// <param name="Title">The question, as a heading. ⭐ A QUESTION, not a label.</param>
/// <param name="Message">
/// What will happen, and whether it can be undone. ⭐ The project's rule for any message: what happens ·
/// why · what to do now.
/// </param>
/// <param name="ConfirmLabel">
/// ⭐⭐ Names the ACTION, never "OK" or "Yes". An operator who reads only the buttons must still know what
/// they are about to do — the same rule EmberTern's terminology norm applies to every destructive
/// confirmation it ships.
/// </param>
/// <param name="CancelLabel">The way out.</param>
public sealed record ConfirmRequest(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel = "Cancel");

/// <summary>
/// The License Manager's confirmation dialog.
///
/// <para>⭐ <b>A mirror of EmberTern's <c>ConfirmDialog</c> + <c>ConfirmDialogViewModel</c>, not a new
/// mechanism</b> — same shape (a request in, a boolean out, a <see cref="RequestClose"/> the window turns
/// into <c>Close(result)</c>), same dialog skeleton, same button order and the same
/// <c>flat</c> / <c>primary</c> pairing. ⛔ EmberTern's own cannot be referenced: it lives in
/// <c>EmberTern.App</c>, which this application must not acquire.</para>
///
/// <para>⛔ <b>Deliberately WITHOUT the "do not ask again" checkbox</b> that EmberTern's carries. Nothing
/// here has asked for one, and a suppress option with no consumer is the dead-surface trap (gotcha #233)
/// — worse here than elsewhere, because the only thing it could suppress is a warning before a
/// destructive act.</para>
/// </summary>
public sealed partial class ConfirmViewModel : ObservableObject
{
    /// <summary>Creates the view model for a request.</summary>
    public ConfirmViewModel(ConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Title = request.Title;
        Message = request.Message;
        ConfirmLabel = request.ConfirmLabel;
        CancelLabel = request.CancelLabel;
    }

    /// <summary>The question.</summary>
    public string Title { get; }

    /// <summary>What will happen.</summary>
    public string Message { get; }

    /// <summary>The action's own name.</summary>
    public string ConfirmLabel { get; }

    /// <summary>The way out.</summary>
    public string CancelLabel { get; }

    /// <summary>
    /// What the operator chose. ⚠ <see langword="false"/> until they confirm, so every path that does not
    /// reach <see cref="Confirm"/> — including closing the window by its ✕ — means "do not do it".
    /// </summary>
    public bool Result { get; private set; }

    /// <summary>Raised when the dialog should close; the window turns it into <c>Close(Result)</c>.</summary>
    public event Action? RequestClose;

    /// <summary>Go ahead.</summary>
    [RelayCommand]
    private void Confirm()
    {
        Result = true;
        RequestClose?.Invoke();
    }

    /// <summary>⭐ Changes nothing at all — it does not even record the choice beyond leaving it false.</summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        RequestClose?.Invoke();
    }
}

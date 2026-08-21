using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.LicenseManager.Localization;

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
/// <param name="MessageArguments">
/// The values <paramref name="Message"/> interpolates, in <c>{0}</c>…<c>{n}</c> order. ⭐ A recipient, a
/// host, a file name — handed over as VALUES so the sentence stays one translatable unit.
/// </param>
public sealed record ConfirmRequest(
    MessageKey Title,
    MessageKey Message,
    MessageKey ConfirmLabel,
    params object?[] MessageArguments)
{
    /// <summary>
    /// The way out.
    /// </summary>
    /// <remarks>
    /// ⚠⚠ <b>Deliberately a property with an initialiser, and NOT a defaulted constructor parameter.</b>
    /// It used to be <c>string CancelLabel = "Cancel"</c>, and a default parameter value is copied into
    /// every caller at compile time — exactly like a <c>const</c> — so the word was pasted into all three
    /// call sites and no lookup could ever reach it. ⭐ As a key resolved at display time it behaves like
    /// every other word; a caller that ever needs a different way out sets this explicitly.
    /// </remarks>
    public MessageKey CancelLabel { get; init; } = ConfirmCatalog.Cancel;
}

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
    private readonly ConfirmRequest _request;

    /// <summary>Creates the view model for a request.</summary>
    public ConfirmViewModel(ConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        _request = request;

        // ⚠ Weak, and the handler is static — see LanguageChange.SubscribeWeak. A dialog is short-lived,
        //   which is precisely the lifetime a static event would turn into a leak.
        LanguageChange.SubscribeWeak(this, static dialog => dialog.RefreshWords());
    }

    /// <summary>The question.</summary>
    /// <remarks>
    /// ⚠ Resolved on read, like every other word (L8.2). ⛔ Do not capture these into <c>string</c> fields
    /// in the constructor: that is the <c>static readonly</c> failure — correct on first display, frozen
    /// afterwards.
    /// </remarks>
    public string Title => Loc.Text(_request.Title.Value);

    /// <summary>What will happen.</summary>
    public string Message => Loc.Format(_request.Message.Value, [.. _request.MessageArguments]);

    /// <summary>The action's own name.</summary>
    public string ConfirmLabel => Loc.Text(_request.ConfirmLabel.Value);

    /// <summary>The way out.</summary>
    public string CancelLabel => Loc.Text(_request.CancelLabel.Value);

    private void RefreshWords()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(ConfirmLabel));
        OnPropertyChanged(nameof(CancelLabel));
    }

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

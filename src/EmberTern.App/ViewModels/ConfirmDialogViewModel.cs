using System;
using CommunityToolkit.Mvvm.Input;

namespace EmberTern.App.ViewModels;

public sealed class ConfirmRequest
{
    public string Title { get; init; } = "Confirm";
    public string Message { get; init; } = string.Empty;
    public string ConfirmLabel { get; init; } = "OK";
    public string CancelLabel { get; init; } = "Cancel";
    public bool IsDestructive { get; init; }

    /// <summary>
    /// Text for an optional "do not ask again" checkbox. <c>null</c> — the default — renders no checkbox, so
    /// every existing caller is unaffected.
    /// <para>⭐ It lives on the SHARED confirm request rather than in a second dialog, because a second dialog
    /// would be free to drift from this one's chrome — the same reasoning that already turned this window into
    /// an acknowledgement dialog via an empty <see cref="CancelLabel"/>.</para>
    /// </summary>
    public string? SuppressLabel { get; init; }

    /// <summary>
    /// Whether the user ticked <see cref="SuppressLabel"/>. ⚠ Written by the dialog and read by the caller
    /// AFTER awaiting it — the only mutable member here, which is why it is <c>set</c> rather than <c>init</c>.
    /// <para>⛔ Meaningful only when the user CONFIRMED. A cancelled dialog decides nothing, so a caller must
    /// not persist this after a refusal.</para>
    /// </summary>
    public bool SuppressChecked { get; set; }
}

public partial class ConfirmDialogViewModel : ViewModelBase
{
    private readonly ConfirmRequest _request;

    public ConfirmDialogViewModel(ConfirmRequest request)
    {
        _request = request;
        Title = request.Title;
        Message = request.Message;
        ConfirmLabel = request.ConfirmLabel;
        CancelLabel = request.CancelLabel;
        IsDestructive = request.IsDestructive;
        SuppressLabel = request.SuppressLabel ?? string.Empty;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel { get; }

    /// <summary>Label of the optional "do not ask again" checkbox.</summary>
    public string SuppressLabel { get; }

    /// <summary>Whether the checkbox exists at all — an absent <c>SuppressLabel</c> means this dialog does not
    /// offer to be silenced, which is every caller but one.</summary>
    public bool HasSuppress => !string.IsNullOrEmpty(SuppressLabel);

    /// <summary>Two-way with the checkbox.</summary>
    public bool SuppressChecked { get; set; }

    /// <summary>An empty <see cref="CancelLabel"/> means this is an acknowledgement, not a choice — there is
    /// nothing to decline, so the button is not rendered.</summary>
    public bool HasCancel => !string.IsNullOrEmpty(CancelLabel);

    public bool IsDestructive { get; }

    public bool Result { get; private set; }
    public event Action? RequestClose;

    [RelayCommand]
    private void Confirm()
    {
        Result = true;
        // ⚠ Written back only on CONFIRM: a cancelled dialog decides nothing, so a ticked box on a refusal must
        // not silence a warning the user never accepted.
        _request.SuppressChecked = SuppressChecked;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        RequestClose?.Invoke();
    }
}

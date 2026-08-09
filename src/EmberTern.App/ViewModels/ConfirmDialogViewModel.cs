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
}

public partial class ConfirmDialogViewModel : ViewModelBase
{
    public ConfirmDialogViewModel(ConfirmRequest request)
    {
        Title = request.Title;
        Message = request.Message;
        ConfirmLabel = request.ConfirmLabel;
        CancelLabel = request.CancelLabel;
        IsDestructive = request.IsDestructive;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel { get; }

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
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = false;
        RequestClose?.Invoke();
    }
}

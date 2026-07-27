using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Asks the user for one line of text — the counterpart to <see cref="ConfirmRequest"/>, which asks for one
/// yes/no.
/// <para>
/// It exists because the app had a name prompt (<c>NewFolderDialog</c>) whose every caption is a folder's, so
/// reusing it for anything else would have meant either lying to the user or parameterising a dialog that
/// belongs to another feature. The shape is deliberately <see cref="ConfirmRequest"/>'s, so a reader who knows
/// one knows both.
/// </para>
/// </summary>
public sealed class TextPromptRequest
{
    public string Title { get; init; } = string.Empty;

    /// <summary>The caption above the box — what this text IS, not an instruction.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Pre-filled value, selected on open so typing replaces it.</summary>
    public string InitialText { get; init; } = string.Empty;

    public string ConfirmLabel { get; init; } = "OK";

    public string CancelLabel { get; init; } = "Cancel";
}

/// <summary>
/// A one-line text prompt. Returns the trimmed text, or <c>null</c> when the user cancelled — and <b>blank is a
/// cancel</b>: a name made of spaces is not a name, and inventing one for the user would be a decision they did
/// not take.
/// </summary>
public partial class TextPromptDialogViewModel : ViewModelBase
{
    public TextPromptDialogViewModel(TextPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Title = request.Title;
        Label = request.Label;
        Text = request.InitialText;
        ConfirmLabel = request.ConfirmLabel;
        CancelLabel = request.CancelLabel;
    }

    public string Title { get; }
    public string Label { get; }
    public string ConfirmLabel { get; }
    public string CancelLabel { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _text = string.Empty;

    /// <summary>The accepted text, or <c>null</c> when cancelled.</summary>
    public string? Result { get; private set; }

    public event Action? RequestClose;

    private bool CanConfirm => !string.IsNullOrWhiteSpace(Text);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = Text.Trim();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }
}

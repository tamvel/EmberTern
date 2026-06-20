using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;

namespace EmberTern.App.ViewModels;

// A single choice the user can pick in a ChoiceDialog. Identified by a stable Id
// (returned to the caller) rather than the label, so wording can change without
// touching the decision logic.
public sealed class ChoiceOption
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    // Rendered as the primary button + wired to Enter (IsDefault). At most one.
    public bool IsDefault { get; init; }
    // Wired to Escape (IsCancel) and to the window-close (X) path — closing the
    // dialog without an explicit pick returns this option's Id. At most one.
    public bool IsCancel { get; init; }
    public bool IsDestructive { get; init; }
}

// Multi-outcome confirmation request. The binary ConfirmDialog (ConfirmRequest →
// Task<bool>) can't express more than two outcomes; this is its N-button sibling,
// used for Commit / Roll back / Cancel (disconnect) and Cancel / Discard-and-exit
// (app close). Returns the chosen ChoiceOption.Id, or null when the dialog is
// dismissed (Esc / X) — callers treat null as "cancel".
public sealed class ChoiceRequest
{
    public string Title { get; init; } = "Confirm";
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<ChoiceOption> Options { get; init; } = Array.Empty<ChoiceOption>();
}

public partial class ChoiceDialogViewModel : ViewModelBase
{
    public ChoiceDialogViewModel(ChoiceRequest request)
    {
        Title = request.Title;
        Message = request.Message;
        Options = request.Options.Select(o => new ChoiceOptionViewModel(o, this)).ToList();
    }

    public string Title { get; }
    public string Message { get; }
    public IReadOnlyList<ChoiceOptionViewModel> Options { get; }

    // Selected option Id, or null when dismissed without a pick (Esc / X).
    public string? Result { get; private set; }
    public event Action? RequestClose;

    internal void Choose(string id)
    {
        Result = id;
        RequestClose?.Invoke();
    }
}

public partial class ChoiceOptionViewModel : ViewModelBase
{
    private readonly ChoiceDialogViewModel _owner;

    public ChoiceOptionViewModel(ChoiceOption option, ChoiceDialogViewModel owner)
    {
        _owner = owner;
        Id = option.Id;
        Label = option.Label;
        IsDefault = option.IsDefault;
        IsCancel = option.IsCancel;
        IsDestructive = option.IsDestructive;
    }

    public string Id { get; }
    public string Label { get; }
    public bool IsDefault { get; }
    public bool IsCancel { get; }
    public bool IsDestructive { get; }
    public bool IsNotDefault => !IsDefault;

    [RelayCommand]
    private void Invoke() => _owner.Choose(Id);
}

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>The compiled object + its recompilable dependent candidates, passed to the
/// post-compile "Recompile dependents?" dialog.</summary>
public sealed record RecompileDependentsRequest(MetadataObject Compiled, IReadOnlyList<MetadataObject> Candidates);

/// <summary>The user's choice from the dialog: the dependents to recompile + whether to
/// stop offering for the rest of the session.</summary>
public sealed record RecompileDependentsResult(IReadOnlyList<MetadataObject> Selected, bool DontAskAgain);

/// <summary>One dependent object row (checkbox) in the dialog.</summary>
public partial class RecompileDependentItem : ObservableObject
{
    public RecompileDependentItem(MetadataObject obj)
    {
        Object = obj;
        Name = obj.Name;
        TypeLabel = obj.Kind.ToString();
    }

    public MetadataObject Object { get; }
    public string Name { get; }
    public string TypeLabel { get; }

    [ObservableProperty] private bool _isChecked = true;
}

/// <summary>
/// Post-compile offer to recompile the dependents of the object just compiled. Nothing runs
/// automatically — the user picks which dependents (all checked by default) and confirms, or
/// skips. "Don't ask again this session" silences the offer for the rest of the session.
/// Returns the selection via <see cref="Result"/> (null on Skip/Cancel).
/// </summary>
public partial class RecompileDependentsDialogViewModel : ObservableObject
{
    public RecompileDependentsDialogViewModel(RecompileDependentsRequest request)
    {
        CompiledName = request.Compiled.Name;
        Items = new ObservableCollection<RecompileDependentItem>(
            request.Candidates.Select(c => new RecompileDependentItem(c)));
    }

    public string CompiledName { get; }
    public ObservableCollection<RecompileDependentItem> Items { get; }

    public string HeaderText =>
        string.Format(CultureInfo.CurrentCulture, UiStrings.RecompileDependentsHeaderFormat, CompiledName);

    [ObservableProperty] private bool _dontAskAgain;

    /// <summary>Selected dependents + don't-ask-again, or null on Skip/Cancel.</summary>
    public RecompileDependentsResult? Result { get; private set; }

    public event Action? RequestClose;

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var i in Items) i.IsChecked = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var i in Items) i.IsChecked = false;
    }

    [RelayCommand]
    private void Recompile()
    {
        var selected = Items.Where(i => i.IsChecked).Select(i => i.Object).ToList();
        Result = new RecompileDependentsResult(selected, DontAskAgain);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Skip()
    {
        // Still honour "don't ask again" even when skipping this one.
        Result = DontAskAgain ? new RecompileDependentsResult(Array.Empty<MetadataObject>(), true) : null;
        RequestClose?.Invoke();
    }
}

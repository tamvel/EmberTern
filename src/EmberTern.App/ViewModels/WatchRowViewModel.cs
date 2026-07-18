using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Sql.Debugging;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One row of the debugger's Watches panel (Stage X / D5, spec §9.5). A watch is an <b>expression</b>
/// (immutable <see cref="Expression"/>) that is re-evaluated after every step through the one engine
/// (<see cref="DebugSession.Evaluate"/>) — so, unlike the other read-only row VMs, this one is mutable: its
/// <see cref="ValueText"/> / <see cref="IsError"/> / <see cref="Evaluated"/> update each pause. A watch that
/// is not a pure expression (contains a side-effecting keyword) is flagged once at creation
/// (<see cref="HasSideEffect"/>) — an auto-re-evaluated watch runs real SQL in the debug transaction, so the
/// user must see which ones can bite (spec §9.5 guard).
/// </summary>
public sealed partial class WatchRowViewModel : ObservableObject
{
    public WatchRowViewModel(string expression, bool hasSideEffect)
    {
        Expression = expression;
        HasSideEffect = hasSideEffect;
        _valueText = UiStrings.DebuggerWatchNotEvaluated;
    }

    /// <summary>The watch expression (immutable — editing = remove + add).</summary>
    public string Expression { get; }

    /// <summary>True when the expression contains a side-effecting keyword (DML / EXECUTE / POST_EVENT) — it
    /// runs real SQL in the debug transaction each time it is re-evaluated (spec §9.5).</summary>
    public bool HasSideEffect { get; }

    /// <summary>The side-effect marker glyph, or empty when the watch is a pure expression.</summary>
    public string SideEffectGlyph => HasSideEffect ? "±" : string.Empty;

    /// <summary>The current value (or error) text; a placeholder until first evaluated / while not paused.</summary>
    [ObservableProperty]
    private string _valueText;

    /// <summary>True when the last evaluation raised — the value renders in the error colour.</summary>
    [ObservableProperty]
    private bool _isError;

    /// <summary>True once the watch has been evaluated against a live frame.</summary>
    [ObservableProperty]
    private bool _evaluated;

    /// <summary>Applies an evaluation result (value on success, error text on a raise).</summary>
    public void Apply(EvaluationResult? result)
    {
        Evaluated = true;
        if (result is null)
        {
            IsError = true;
            ValueText = UiStrings.DebuggerEvalErrorUnknown;
            return;
        }
        if (!result.Success)
        {
            IsError = true;
            var err = result.Error;
            ValueText = err?.Message ?? err?.ExceptionName ?? UiStrings.DebuggerEvalErrorUnknown;
            return;
        }
        IsError = false;
        ValueText = FormatValue(result.Value);
    }

    /// <summary>Resets the row to the not-evaluated placeholder (no live frame — before launch / after stop).</summary>
    public void Reset()
    {
        Evaluated = false;
        IsError = false;
        ValueText = UiStrings.DebuggerWatchNotEvaluated;
    }

    private static string FormatValue(object? value)
    {
        if (value is null || value is DBNull)
        {
            return UiStrings.DebuggerVariableNull;
        }
        return value switch
        {
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? UiStrings.DebuggerVariableNull,
        };
    }
}

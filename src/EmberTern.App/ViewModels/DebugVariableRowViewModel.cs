using System;
using System.Globalization;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One row of the debugger's Variables panel (Stage X / D4 — the <b>basic</b> list; the rich window with
/// grouping / change highlight / inline edit / data tips is D7). A read-only projection of a frame variable:
/// its name, its declared kind (parameter / local), its type text, and the current value the client-side
/// frame holds — rendered as <c>&lt;null&gt;</c> when unset. Values are opaque objects (the client owns
/// control flow, the server owns types — spec §3.5), so this only formats for display; it never coerces.
/// </summary>
public sealed class DebugVariableRowViewModel
{
    public DebugVariableRowViewModel(string name, string kind, string? typeText, bool hasValue, object? value)
    {
        Name = name;
        Kind = kind;
        TypeText = typeText ?? string.Empty;
        ValueText = FormatValue(hasValue, value);
    }

    public string Name { get; }

    /// <summary>Localized kind label ("param" / "local").</summary>
    public string Kind { get; }

    public string TypeText { get; }

    public bool HasType => TypeText.Length > 0;

    /// <summary>The current value formatted for display (<c>&lt;null&gt;</c> for an unset/null variable).</summary>
    public string ValueText { get; }

    private static string FormatValue(bool hasValue, object? value)
    {
        if (!hasValue || value is null || value is DBNull)
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

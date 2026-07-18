using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmberTern.App.ViewModels;

/// <summary>What a variable row denotes — an input parameter, an output parameter, or a local. Drives the
/// group it lands in, its kind glyph, and its themed colour key.</summary>
public enum DebugVariableKind
{
    ParameterIn,
    ParameterOut,
    Local,
}

/// <summary>
/// One row of the debugger's Variables panel (Stage X / D7 — the rich window). A projection of a frame
/// variable, updated <b>in place</b> across steps (so pins, expansion and selection survive): its immutable
/// identity (name / kind / type) is set once, while <see cref="ValueText"/>, <see cref="IsNull"/> and
/// <see cref="IsChanged"/> are refreshed each pause and <see cref="IsPinned"/> is toggled by the user.
/// <para>
/// Values are opaque objects (the client owns control flow, the server owns types — spec §3.5), so this only
/// <em>formats</em> for display; it never coerces. The kind glyph/colour distinguish IN / OUT / local (the D4
/// UX-backlog item), and colour is a theme <b>key string</b> (never a brush — rule #1 + theme rules).
/// </para>
/// </summary>
public sealed partial class DebugVariableRowViewModel : ObservableObject
{
    public DebugVariableRowViewModel(string name, DebugVariableKind kind, string? typeText)
    {
        Name = name;
        Kind = kind;
        TypeText = typeText ?? string.Empty;
    }

    public string Name { get; }

    public DebugVariableKind Kind { get; }

    public string TypeText { get; }

    public bool HasType => TypeText.Length > 0;

    /// <summary>The kind glyph (⬤ IN / ◑ OUT / ○ local) — mirrors the spec §9.4 tree.</summary>
    public string KindGlyph => Kind switch
    {
        DebugVariableKind.ParameterIn => "⬤",   // ⬤
        DebugVariableKind.ParameterOut => "◑",  // ◑
        _ => "○",                                // ○
    };

    /// <summary>Localized kind label (IN / OUT / local).</summary>
    public string KindLabel => Kind switch
    {
        DebugVariableKind.ParameterIn => UiStrings.DebuggerVariableKindIn,
        DebugVariableKind.ParameterOut => UiStrings.DebuggerVariableKindOut,
        _ => UiStrings.DebuggerVariableKindLocal,
    };

    /// <summary>Theme-token key for the kind glyph colour (resolved via <see cref="IconBrushConverter"/>):
    /// parameters use the accent, locals the subtle foreground.</summary>
    public string KindBrushKey => Kind == DebugVariableKind.Local ? "SubtleForegroundBrush" : "AccentBrush";

    /// <summary>The raw current value (the client-side frame truth), for data tips / inline edit (seam b).</summary>
    public object? RawValue { get; private set; }

    /// <summary>The current value formatted for display (<c>&lt;null&gt;</c> for an unset/null variable).</summary>
    [ObservableProperty]
    private string _valueText = UiStrings.DebuggerVariableNull;

    /// <summary>True when the current value is null/unset — the view renders it distinctly.</summary>
    [ObservableProperty]
    private bool _isNull = true;

    /// <summary>True when the last step changed this variable's value — the highest value-per-line cue.</summary>
    [ObservableProperty]
    private bool _isChanged;

    /// <summary>Pinned to the top group (session-scoped; not a Watch — §9.5).</summary>
    [ObservableProperty]
    private bool _isPinned;

    /// <summary>Refreshes the live value + change cue in place (identity is untouched).</summary>
    public void Update(bool hasValue, object? value, bool changed)
    {
        RawValue = (!hasValue || value is System.DBNull) ? null : value;
        IsNull = RawValue is null;
        ValueText = FormatValue(hasValue, value);
        IsChanged = changed;
    }

    private static string FormatValue(bool hasValue, object? value)
    {
        if (!hasValue || value is null || value is System.DBNull)
        {
            return UiStrings.DebuggerVariableNull;
        }
        return value switch
        {
            System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? UiStrings.DebuggerVariableNull,
        };
    }
}

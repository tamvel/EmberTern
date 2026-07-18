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

    /// <summary>The kind glyph — a distinct SHAPE per kind (triangle IN / diamond OUT / circle local) so the
    /// kind reads at a glance; paired with a distinct colour (<see cref="KindBrushKey"/>). Cursor variables get
    /// their own glyph/colour when a later milestone surfaces them.</summary>
    public string KindGlyph => Kind switch
    {
        // Full-size black shapes so all three carry equal optical mass (the small "▸" U+25B8 read lighter
        // than the diamond/circle — use the full "▶" U+25B6 instead; same concept, same colour).
        DebugVariableKind.ParameterIn => "▶",   // input parameter
        DebugVariableKind.ParameterOut => "◆",  // output / RETURNS
        _ => "●",                                // local
    };

    /// <summary>Localized kind label (IN / OUT / local).</summary>
    public string KindLabel => Kind switch
    {
        DebugVariableKind.ParameterIn => UiStrings.DebuggerVariableKindIn,
        DebugVariableKind.ParameterOut => UiStrings.DebuggerVariableKindOut,
        _ => UiStrings.DebuggerVariableKindLocal,
    };

    /// <summary>Theme-token key for the kind glyph colour (resolved via <see cref="IconBrushConverter"/>): a
    /// distinct hue per kind — IN blue, OUT amber, local green — so kinds are distinguishable at a glance.</summary>
    public string KindBrushKey => Kind switch
    {
        DebugVariableKind.ParameterIn => "DebugParamInBrush",
        DebugVariableKind.ParameterOut => "DebugParamOutBrush",
        _ => "DebugLocalBrush",
    };

    /// <summary>The raw current value (the client-side frame truth), for data tips / inline edit (seam b).</summary>
    public object? RawValue { get; private set; }

    /// <summary>Max characters rendered inline for a value — longer values are truncated with an ellipsis so a
    /// large text BLOB never materializes a megabyte into the panel/hover (lazy, spec §9.4).</summary>
    private const int MaxInlineLength = 256;

    /// <summary>False for a binary BLOB (a <see cref="byte"/>[]) — it is inspected, not text-edited. Text
    /// values (incl. long ones) stay editable via their full, untruncated raw string.</summary>
    public bool IsEditable => RawValue is not byte[];

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

    /// <summary>True while the value cell is being inline-edited (the view swaps the label for a text box).</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>The in-progress edit text (bound to the inline text box).</summary>
    [ObservableProperty]
    private string _editText = string.Empty;

    /// <summary>True when the last commit attempt failed to parse for the declared type (validate at edit
    /// time; the real domain CHECK still surfaces on the next injection — spec §9.4 / §3.4).</summary>
    [ObservableProperty]
    private bool _hasEditError;

    /// <summary>Enters inline-edit mode, seeding the box with the value's FULL untruncated text (never the
    /// possibly-truncated <see cref="ValueText"/> — committing that back would silently corrupt the value, §0).</summary>
    public void BeginEdit()
    {
        EditText = RawEditString();
        HasEditError = false;
        IsEditing = true;
    }

    // The full, untruncated invariant string of the raw value (empty for null) — the edit round-trips this,
    // not the display text.
    private string RawEditString() => RawValue switch
    {
        null => string.Empty,
        System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var v => v.ToString() ?? string.Empty,
    };

    /// <summary>Leaves inline-edit mode without applying.</summary>
    public void CancelEdit()
    {
        IsEditing = false;
        HasEditError = false;
    }

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
        // Binary BLOB: never materialize the bytes as text — show a lazy placeholder (a value viewer is a
        // later addition; this keeps the panel/hover cheap and honest).
        if (value is byte[] bytes)
        {
            return string.Format(CultureInfo.InvariantCulture, UiStrings.DebuggerVariableBlobFormat, bytes.Length);
        }
        string text = value switch
        {
            System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? UiStrings.DebuggerVariableNull,
        };
        // Truncate a very long value inline (a large text BLOB) — the full value is still editable (BeginEdit
        // reads the raw value), so this is display-only and loses nothing.
        return text.Length > MaxInlineLength ? text.Substring(0, MaxInlineLength) + "…" : text;
    }
}

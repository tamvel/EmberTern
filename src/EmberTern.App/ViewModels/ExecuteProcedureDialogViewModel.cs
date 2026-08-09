using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Formatting;
using EmberTern.Core.Settings;

namespace EmberTern.App.ViewModels;

/// <summary>Which input control the Execute dialog shows for a parameter, by its
/// Firebird type family.</summary>
public enum ExecuteParamKind
{
    Text,        // CHAR / VARCHAR / unknown → TextBox
    Numeric,     // SMALLINT / INTEGER / BIGINT / NUMERIC / DECIMAL / FLOAT / DOUBLE → NumericUpDown
    Date,        // DATE → CalendarDatePicker
    Time,        // TIME → text field (HH:mm:ss, typed)
    Timestamp,   // TIMESTAMP → CalendarDatePicker + text time field
    Boolean,     // BOOLEAN → CheckBox
    BlobText,    // BLOB SUB_TYPE 1 (TEXT) → multi-line TextBox
    BlobBinary,  // BLOB SUB_TYPE 0 (binary) → text input (binary file input out of scope)
}

/// <summary>Where a parameter row's current value came from. The app has ONE convention for saying "you did
/// not type this here": every mechanism that supplies a value automatically reports itself through this, so a
/// future one adds a case rather than a second kind of marker.</summary>
public enum ValueOrigin
{
    /// <summary>The user's own value — typed now, or simply the untouched default. Never marked.</summary>
    Entered,

    /// <summary>Filled from a stored value that was <b>proven</b> to still fit: the previous run's history, or
    /// the same parameter carried across a rebuilt launch panel. No inference was made.</summary>
    Restored,

    /// <summary>Filled by the only inference the panel makes: after matching by name, one parameter remained on
    /// each side with the same input kind, so the value was carried into it. The pair is unprovable — a renamed
    /// parameter and a replaced one look identical in the text — so the row says so.</summary>
    Assumed,
}

/// <summary>One input parameter row in the Execute Procedure dialog. The control
/// shown matches <see cref="Kind"/>; <see cref="Resolve"/> returns the bound CLR
/// value (or null for NULL) so it binds correctly — never as a SQL literal.</summary>
public partial class ExecuteProcedureParamRowViewModel : ObservableObject
{
    public ExecuteProcedureParamRowViewModel(string name, string typeText)
    {
        Name = name;
        TypeText = typeText;
        Kind = ClassifyKind(typeText);

        // NULL checked by default for every parameter (IBExpert-style).
        IsNull = true;

        // Sensible defaults so unchecking NULL starts from a usable value.
        var now = DateTime.Now;
        switch (Kind)
        {
            case ExecuteParamKind.Date:
                DateValue = now.Date;
                break;
            case ExecuteParamKind.Timestamp:
                // Today at midnight — TIMESTAMP columns are used as date ranges (DATAOD /
                // DATADO) far more than as "now". Defaulting to the current wall-clock time
                // was the reported annoyance.
                DateValue = now.Date;
                TimeValue = TimeSpan.Zero;
                _timeText = FormatTime(TimeSpan.Zero);
                break;
            case ExecuteParamKind.Time:
                TimeValue = now.TimeOfDay;
                _timeText = FormatTime(now.TimeOfDay);
                break;
            case ExecuteParamKind.Numeric:
                NumericValue = 0m;
                break;
            case ExecuteParamKind.Boolean:
                BoolValue = false;
                break;
            // Text/Blob default to the empty string (TextValue's initial value).
        }
    }

    public string Name { get; }
    public string TypeText { get; }
    public ExecuteParamKind Kind { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValueEnabled))]
    private bool _isNull;

    public bool IsValueEnabled => !IsNull;

    // Per-kind value holders — bound directly to the matching control's value
    // property (CalendarDatePicker.SelectedDate is DateTime?, NumericUpDown.Value is
    // decimal? — gotcha #51). Time is edited as free text (see TimeText).
    [ObservableProperty] private string _textValue = string.Empty;
    [ObservableProperty] private decimal? _numericValue;
    [ObservableProperty] private DateTime? _dateValue;
    [ObservableProperty] private TimeSpan? _timeValue;
    [ObservableProperty] private bool _boolValue;

    // Free-text time entry (Time + Timestamp). The user types 8 / 8:30 / 8:30:15;
    // CommitTime parses + normalizes to HH:mm:ss (called on focus-loss and before OK).
    // TimeValue stays the canonical value used by Resolve.
    [ObservableProperty] private string _timeText = string.Empty;

    // ─── Where this row's value came from (the one auto-fill convention) ─────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoFilled))]
    [NotifyPropertyChangedFor(nameof(IsAssumed))]
    [NotifyPropertyChangedFor(nameof(OriginLabel))]
    [NotifyPropertyChangedFor(nameof(OriginBrushKey))]
    [NotifyPropertyChangedFor(nameof(OriginTooltip))]
    private ValueOrigin _origin;

    /// <summary>True while the value on screen was supplied by the app rather than typed here — the one thing
    /// the marker in the launch panel says. <see cref="OriginTooltip"/> says by which mechanism.</summary>
    public bool IsAutoFilled => Origin != ValueOrigin.Entered;

    /// <summary>The marker's word. <b>Restored</b> is the ordinary case and reads as a quiet note; <b>Assumed</b>
    /// names the one inference the panel makes, so it is a different word rather than the same word with a
    /// footnote — the difference has to survive being glanced at.</summary>
    public string OriginLabel => Origin switch
    {
        ValueOrigin.Restored => UiStrings.LaunchValueRestoredMarker,
        ValueOrigin.Assumed => UiStrings.LaunchValueAssumedMarker,
        _ => string.Empty,
    };

    /// <summary>The theme token the marker is painted with — a KEY, never a brush (VMs hold no Avalonia types).
    /// Restored stays in the subtle reading colour because it is the expected state; Assumed takes the accent,
    /// which draws the eye without claiming anything is wrong — it is a "worth a look", not a warning.</summary>
    public string OriginBrushKey => Origin switch
    {
        ValueOrigin.Restored => "SubtleForegroundBrush",
        ValueOrigin.Assumed => "AccentBrush",
        _ => string.Empty,
    };

    /// <summary>Whether the value rests on an assumption — the view's cue to give the marker its stronger
    /// weight, so Restored and Assumed differ by colour AND emphasis rather than by tooltip alone.</summary>
    public bool IsAssumed => Origin == ValueOrigin.Assumed;

    /// <summary>Why this row is marked, in the user's words.</summary>
    public string OriginTooltip => Origin switch
    {
        ValueOrigin.Restored => UiStrings.LaunchValueRestoredTooltip,
        ValueOrigin.Assumed => UiStrings.LaunchValueAssumedTooltip,
        _ => string.Empty,
    };

    // Any edit makes the value the user's own, so the marker goes. Without this a row would keep claiming it
    // was filled in automatically after the user had replaced the value — the same small untruth this whole
    // convention exists to remove. The value setters below run BEFORE an origin is assigned by the mechanisms
    // that fill a row, so those assign their origin last (see ApplyHistoryValue).
    partial void OnTextValueChanged(string value) => Origin = ValueOrigin.Entered;
    partial void OnNumericValueChanged(decimal? value) => Origin = ValueOrigin.Entered;
    partial void OnDateValueChanged(DateTime? value) => Origin = ValueOrigin.Entered;
    partial void OnTimeValueChanged(TimeSpan? value) => Origin = ValueOrigin.Entered;
    partial void OnBoolValueChanged(bool value) => Origin = ValueOrigin.Entered;
    partial void OnIsNullChanged(bool value) => Origin = ValueOrigin.Entered;

    // True when the current TimeText can't be parsed — drives the red border and blocks OK.
    [ObservableProperty] private bool _hasTimeError;

    // Per-kind control visibility.
    public bool IsSingleLineTextKind => Kind == ExecuteParamKind.Text;
    public bool IsMultilineKind => Kind is ExecuteParamKind.BlobText or ExecuteParamKind.BlobBinary;
    public bool IsNumericKind => Kind == ExecuteParamKind.Numeric;
    public bool IsDateKind => Kind is ExecuteParamKind.Date or ExecuteParamKind.Timestamp;
    public bool IsTimeKind => Kind is ExecuteParamKind.Time or ExecuteParamKind.Timestamp;
    public bool IsDateTimeKind => IsDateKind || IsTimeKind;
    public bool IsBooleanKind => Kind == ExecuteParamKind.Boolean;

    /// <summary>Parses <see cref="TimeText"/> into <see cref="TimeValue"/>, normalizing to
    /// HH:mm:ss on success. On a NULL row (time irrelevant) or a non-time kind this is a
    /// no-op that clears any error. Returns true when the row is valid to execute.</summary>
    public bool CommitTime()
    {
        if (!IsTimeKind || IsNull)
        {
            HasTimeError = false;
            return true;
        }

        if (TryParseTime(TimeText, out var parsed))
        {
            TimeValue = parsed;
            TimeText = FormatTime(parsed);
            HasTimeError = false;
            return true;
        }

        HasTimeError = true;
        return false;
    }

    /// <summary>The bound CLR value (null for NULL), typed per <see cref="Kind"/>.</summary>
    public object? Resolve()
    {
        if (IsNull) return null;
        return Kind switch
        {
            ExecuteParamKind.Boolean => BoolValue,
            ExecuteParamKind.Numeric => NumericValue,
            ExecuteParamKind.Date => DateValue?.Date,
            ExecuteParamKind.Time => TimeValue,
            ExecuteParamKind.Timestamp =>
                DateValue is { } d ? d.Date + (TimeValue ?? TimeSpan.Zero) : (object?)null,
            _ => string.IsNullOrEmpty(TextValue) ? null : TextValue,
        };
    }

    // ─── History serialization (round-trippable invariant strings) ───────────

    /// <summary>Snapshots this row for the persistent history — a NULL flag plus a
    /// canonical invariant-culture string (TIMESTAMP keeps sub-second precision), and the
    /// declared type the value was entered under, which is what lets a later restore prove
    /// the value still fits (see <see cref="ApplyHistoryValue"/>).</summary>
    internal ParameterValue ToHistoryValue()
    {
        if (IsNull) return new ParameterValue { Name = Name, IsNull = true, Text = null, TypeText = TypeText };
        var text = Kind switch
        {
            ExecuteParamKind.Boolean => BoolValue ? "true" : "false",
            ExecuteParamKind.Numeric => NumericValue?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ExecuteParamKind.Date => DateValue?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            ExecuteParamKind.Time => FormatTime(TimeValue ?? TimeSpan.Zero),
            ExecuteParamKind.Timestamp =>
                (DateValue?.Date + (TimeValue ?? TimeSpan.Zero))?
                    .ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) ?? string.Empty,
            _ => TextValue,
        };
        return new ParameterValue { Name = Name, IsNull = false, Text = text, TypeText = TypeText };
    }

    /// <summary>Restores a previously-used value into this row — but <b>only when the value can be proven
    /// still to fit</b>. The stored value carries the type it was entered under; it is applied only if that
    /// type classifies to the same <see cref="ExecuteParamKind"/> as this row's, so a value entered for an
    /// <c>INTEGER</c> parameter never lands in one that has since become <c>VARCHAR</c>. No conversion is ever
    /// attempted: what cannot be proven is left for the user to decide, and the row stays fresh.
    /// <para>A value stored before the type was recorded (legacy history) cannot be proven and is therefore not
    /// applied. In practice only entered values are affected — a stored <c>NULL</c> would restore the state the
    /// row already starts in.</para>
    /// <para>The row is mutated only once the value has actually materialised: a text that matches the kind but
    /// does not parse (corrupt history) leaves the row untouched, rather than un-checking NULL over a
    /// constructor default — which would show a value nobody entered.</para>
    /// <returns>Whether the value was applied.</returns>
    /// <param name="value">The stored value.</param>
    /// <param name="origin">What the row should report about where its value came from — the caller is the
    /// mechanism, so it is the one that knows (history and same-name carry-over are both
    /// <see cref="ValueOrigin.Restored"/>; the sole-remaining-pair rule is <see cref="ValueOrigin.Assumed"/>).
    /// Assigned last, because writing the value itself marks the row as the user's own.</param></summary>
    /// <param name="requireProvenType">
    /// ⭐⭐ Czy wymagać DOWODU zgodności typu (<see cref="IsProvablyCompatible"/>). <c>true</c> dla
    /// zastosowania AUTOMATYCZNEGO — wtedy nikt nie prosił o tę wartość, więc niedowiedzionej nie
    /// wstawiamy. <c>false</c> dla JAWNEGO wyboru użytkownika: wskazał ten wpis i widzi jego wartości na
    /// etykiecie, więc ciche nic-nierobienie jest błędem, a nie ostrożnością (§19.8).
    /// ⚠ Zniesienie dowodu NIE znosi zabezpieczenia — parsowanie niżej dalej odrzuca wartość, której nie
    /// da się wczytać w typ tego wiersza.
    /// </param>
    internal bool ApplyHistoryValue(
        ParameterValue value, ValueOrigin origin = ValueOrigin.Restored, bool requireProvenType = true)
    {
        if (requireProvenType && !IsProvablyCompatible(value)) return false;

        if (value.IsNull)
        {
            IsNull = true;
            Origin = origin;
            return true;
        }

        var text = value.Text ?? string.Empty;
        switch (Kind)
        {
            case ExecuteParamKind.Boolean:
                if (!bool.TryParse(text, out var b)) return false;
                BoolValue = b;
                break;
            case ExecuteParamKind.Numeric:
                if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return false;
                NumericValue = d;
                break;
            case ExecuteParamKind.Date:
                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd)) return false;
                DateValue = dd.Date;
                break;
            case ExecuteParamKind.Timestamp:
                if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts)) return false;
                DateValue = ts.Date;
                TimeValue = ts.TimeOfDay;
                TimeText = FormatTime(ts.TimeOfDay);
                HasTimeError = false;
                break;
            case ExecuteParamKind.Time:
                if (!TryParseTime(text, out var t)) return false;
                TimeValue = t;
                TimeText = FormatTime(t);
                HasTimeError = false;
                break;
            default:
                TextValue = text;
                break;
        }
        IsNull = false; // only now — the value exists
        Origin = origin; // last: every value setter above resets it to Entered
        return true;
    }

    /// <summary>Whether <paramref name="value"/> is provably compatible with this row: it records the type it
    /// was entered under, and that type classifies to this row's kind. The raw type text is re-classified here
    /// rather than a stored classification being trusted, so the proof always follows the current classifier.
    /// A value with no recorded type (legacy history) is never provable.</summary>
    private bool IsProvablyCompatible(ParameterValue value)
        => value.TypeText is { Length: > 0 } stored && ClassifyKind(stored) == Kind;

    internal static string FormatTime(TimeSpan t) => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    /// <summary>Tolerant time parse: accepts "8", "8:30", "8:30:15" (H / H:mm / H:mm:ss),
    /// empty → 00:00:00. Ranges enforced (0–23 h, 0–59 m/s). Returns false on garbage.</summary>
    internal static bool TryParseTime(string? input, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var s = (input ?? string.Empty).Trim();
        if (s.Length == 0) return true; // empty = midnight

        var parts = s.Split(':');
        if (parts.Length is < 1 or > 3) return false;

        int hh = 0, mm = 0, ss = 0;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hh)) return false;
        if (parts.Length >= 2 && !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out mm)) return false;
        if (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out ss)) return false;

        if (hh is < 0 or > 23 || mm is < 0 or > 59 || ss is < 0 or > 59) return false;

        value = new TimeSpan(hh, mm, ss);
        return true;
    }

    internal static ExecuteParamKind ClassifyKind(string? typeText)
    {
        var t = (typeText ?? string.Empty).TrimStart().ToUpperInvariant();
        if (t.StartsWith("BOOLEAN", StringComparison.Ordinal)) return ExecuteParamKind.Boolean;
        if (t.StartsWith("TIMESTAMP", StringComparison.Ordinal)) return ExecuteParamKind.Timestamp;
        if (t.StartsWith("DATE", StringComparison.Ordinal)) return ExecuteParamKind.Date;
        if (t.StartsWith("TIME", StringComparison.Ordinal)) return ExecuteParamKind.Time;
        if (t.StartsWith("SMALLINT", StringComparison.Ordinal)
            || t.StartsWith("INTEGER", StringComparison.Ordinal)
            || t.StartsWith("INT", StringComparison.Ordinal)
            || t.StartsWith("BIGINT", StringComparison.Ordinal)
            || t.StartsWith("NUMERIC", StringComparison.Ordinal)
            || t.StartsWith("DECIMAL", StringComparison.Ordinal)
            || t.StartsWith("FLOAT", StringComparison.Ordinal)
            || t.StartsWith("DOUBLE", StringComparison.Ordinal))
        {
            return ExecuteParamKind.Numeric;
        }
        if (t.StartsWith("BLOB", StringComparison.Ordinal))
        {
            return (t.Contains("SUB_TYPE 1", StringComparison.Ordinal) || t.Contains("TEXT", StringComparison.Ordinal))
                ? ExecuteParamKind.BlobText
                : ExecuteParamKind.BlobBinary;
        }
        return ExecuteParamKind.Text;
    }

    /// <summary>Pure type-family conversion of a typed string (kept for the Text
    /// path + unit tests). Empty → null; unparseable numerics/dates fall back to
    /// the raw string so Firebird can coerce or report the error.</summary>
    internal static object? ConvertByType(string? typeText, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value!.Trim();
        return ClassifyKind(typeText) switch
        {
            ExecuteParamKind.Numeric =>
                long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l
                : decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d
                : (object)v,
            ExecuteParamKind.Date or ExecuteParamKind.Time or ExecuteParamKind.Timestamp =>
                DateTime.TryParse(v, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt) ? dt : v,
            ExecuteParamKind.Boolean => bool.TryParse(v, out var b) ? b : v,
            _ => v,
        };
    }
}

/// <summary>One saved parameter set shown in the history dropdown. Exposes a two-line
/// display (timestamp + a compact "name=value, …" preview) so the user recognises the
/// set before loading it.</summary>
public sealed class ParameterHistorySnapshotViewModel
{
    private const int PreviewMaxLength = 90;

    public ParameterHistorySnapshotViewModel(ParameterSet set)
    {
        Set = set;
        // ⚠ The reader's own date/time format, not a hard-coded ISO shape (P5, 2026-08-07). This label is
        // pure presentation — it says WHEN a parameter set was last used, and nothing parses it back.
        TimestampText = DateTimeDisplay.DateAndTime(set.ExecutedAt);
        PreviewText = BuildPreview(set.Values);
    }

    public ParameterSet Set { get; }
    public string TimestampText { get; }
    public string PreviewText { get; }

    private static string BuildPreview(IReadOnlyList<ParameterValue> values)
    {
        var sb = new StringBuilder();
        foreach (var v in values)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(v.Name).Append('=').Append(v.IsNull ? "NULL" : (v.Text ?? string.Empty));
            if (sb.Length > PreviewMaxLength) { sb.Length = PreviewMaxLength; sb.Append('…'); break; }
        }
        return sb.ToString();
    }
}

/// <summary>
/// Modal for collecting Execute Procedure / Execute Function input values with
/// type-appropriate controls. Returns the ordered bound values (null entry = SQL NULL)
/// via <see cref="Result"/>, or null on Cancel. Persists and restores per-object
/// parameter history via <see cref="ParameterHistoryStore"/>.
/// </summary>
public partial class ExecuteProcedureDialogViewModel : ObservableObject
{
    private readonly string? _objectName;
    private readonly string? _connectionId;
    private readonly string _objectKind;
    private readonly ParameterHistoryStore? _historyStore;
    private bool _applyingHistory;

    public ExecuteProcedureDialogViewModel(
        IEnumerable<ProcedureParamRowViewModel> inputs,
        string? objectName = null,
        string? connectionId = null,
        string objectKind = "Procedure",
        ParameterHistoryStore? historyStore = null)
        : this(inputs.Select(p => (p.Name, p.TypeText)), objectName, connectionId, objectKind, historyStore)
    {
    }

    // Lightweight (name + type) overload — used by Smart SQL Parameters (F5 on a statement with
    // :name / @name placeholders), where the params come from a scanner + catalog/Unknown typing
    // rather than a full ProcedureParamRowViewModel.
    public ExecuteProcedureDialogViewModel(
        IEnumerable<(string Name, string TypeText)> inputs,
        string? objectName = null,
        string? connectionId = null,
        string objectKind = "Procedure",
        ParameterHistoryStore? historyStore = null)
    {
        _objectName = objectName;
        _connectionId = connectionId;
        _objectKind = objectKind;
        _historyStore = historyStore;

        Params = new ObservableCollection<ExecuteProcedureParamRowViewModel>();
        foreach (var p in inputs)
        {
            Params.Add(new ExecuteProcedureParamRowViewModel(p.Name, p.TypeText));
        }

        History = new ObservableCollection<ParameterHistorySnapshotViewModel>();
        if (_historyStore is not null)
        {
            foreach (var set in _historyStore.Get(_connectionId, _objectKind, _objectName))
            {
                History.Add(new ParameterHistorySnapshotViewModel(set));
            }
        }

        // Auto-load the most recent set ("last run") so re-opening the dialog shows the
        // values used last time — the common re-run case needs zero interaction.
        //
        // ⚠⚠ To jest ta SAMA ścieżka, co ręczny wybór z listy (`OnSelectedHistoryChanged`), więc bez
        // znacznika nie dałoby się ich rozróżnić — a różnią się zasadniczo: tutaj NIKT o tę wartość nie
        // prosił, tam użytkownik wskazał konkretny wpis. Dowód zgodności typu (C3) obowiązuje wyłącznie
        // w tym pierwszym przypadku (§19.8).
        if (History.Count > 0)
        {
            _seedingHistory = true;
            try { SelectedHistory = History[0]; }
            finally { _seedingHistory = false; }
        }
    }

    // Prawda WYŁĄCZNIE w czasie zasiewu z konstruktora — patrz komentarz wyżej.
    private bool _seedingHistory;

    public ObservableCollection<ExecuteProcedureParamRowViewModel> Params { get; }

    public ObservableCollection<ParameterHistorySnapshotViewModel> History { get; }

    public bool HasHistory => History.Count > 0;

    // The chosen history entry; selecting one loads its values into the parameter grid.
    [ObservableProperty]
    private ParameterHistorySnapshotViewModel? _selectedHistory;

    partial void OnSelectedHistoryChanged(ParameterHistorySnapshotViewModel? value)
    {
        if (value is null || _applyingHistory) return;
        _applyingHistory = true;
        try
        {
            foreach (var pv in value.Set.Values)
            {
                var row = Params.FirstOrDefault(
                    r => string.Equals(r.Name, pv.Name, StringComparison.OrdinalIgnoreCase));
                // ⭐ Dowód typu obowiązuje TYLKO przy zasiewie z konstruktora. Przy jawnym wyborze
                // użytkownika wpis bez zapisanego typu (historia sprzed C3) też ma się przywrócić —
                // parsowanie w `ApplyHistoryValue` pozostaje zabezpieczeniem (§19.8).
                row?.ApplyHistoryValue(pv, ValueOrigin.Restored, requireProvenType: _seedingHistory);
            }
        }
        finally { _applyingHistory = false; }
    }

    // True when any time-typed, non-null row has an unparseable TimeText — blocks OK.
    [ObservableProperty]
    private bool _hasValidationError;

    /// <summary>Ordered bound values once accepted; null on cancel.</summary>
    public IReadOnlyList<object?>? Result { get; private set; }

    public event Action? RequestClose;

    [RelayCommand]
    private void Accept()
    {
        // Validate + normalize all time fields first; a bad time blocks execution.
        bool valid = true;
        foreach (var p in Params)
        {
            if (!p.CommitTime()) valid = false;
        }
        HasValidationError = !valid;
        if (!valid) return;

        var values = Params.Select(p => p.Resolve()).ToList();
        Result = values;

        _historyStore?.Record(
            _connectionId, _objectKind, _objectName,
            Params.Select(p => p.ToHistoryValue()).ToList());

        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }
}

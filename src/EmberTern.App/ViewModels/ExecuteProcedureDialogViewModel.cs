using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    /// canonical invariant-culture string (TIMESTAMP keeps sub-second precision).</summary>
    internal ParameterValue ToHistoryValue()
    {
        if (IsNull) return new ParameterValue { Name = Name, IsNull = true, Text = null };
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
        return new ParameterValue { Name = Name, IsNull = false, Text = text };
    }

    /// <summary>Restores a previously-used value (from the persistent history) into the
    /// matching typed holder; a NULL entry restores the NULL state. Unparseable values
    /// are ignored so a schema/type change never crashes the restore.</summary>
    internal void ApplyHistoryValue(ParameterValue value)
    {
        if (value.IsNull)
        {
            IsNull = true;
            return;
        }
        IsNull = false;
        var text = value.Text ?? string.Empty;
        switch (Kind)
        {
            case ExecuteParamKind.Boolean:
                if (bool.TryParse(text, out var b)) BoolValue = b;
                break;
            case ExecuteParamKind.Numeric:
                if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                    NumericValue = d;
                break;
            case ExecuteParamKind.Date:
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd))
                    DateValue = dd.Date;
                break;
            case ExecuteParamKind.Timestamp:
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
                {
                    DateValue = ts.Date;
                    TimeValue = ts.TimeOfDay;
                    TimeText = FormatTime(ts.TimeOfDay);
                    HasTimeError = false;
                }
                break;
            case ExecuteParamKind.Time:
                if (TryParseTime(text, out var t))
                {
                    TimeValue = t;
                    TimeText = FormatTime(t);
                    HasTimeError = false;
                }
                break;
            default:
                TextValue = text;
                break;
        }
    }

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
        TimestampText = set.ExecutedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
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
        if (History.Count > 0)
        {
            SelectedHistory = History[0];
        }
    }

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
                row?.ApplyHistoryValue(pv);
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

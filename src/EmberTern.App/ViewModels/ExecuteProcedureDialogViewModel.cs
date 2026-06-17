using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EmberTern.App.ViewModels;

/// <summary>Which input control the Execute dialog shows for a parameter, by its
/// Firebird type family.</summary>
public enum ExecuteParamKind
{
    Text,        // CHAR / VARCHAR / unknown → TextBox
    Numeric,     // SMALLINT / INTEGER / BIGINT / NUMERIC / DECIMAL / FLOAT / DOUBLE → NumericUpDown
    Date,        // DATE → CalendarDatePicker
    Time,        // TIME → TimePicker
    Timestamp,   // TIMESTAMP → CalendarDatePicker + TimePicker
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
            case ExecuteParamKind.Date: DateValue = now.Date; break;
            case ExecuteParamKind.Timestamp: DateValue = now.Date; TimeValue = now.TimeOfDay; break;
            case ExecuteParamKind.Time: TimeValue = now.TimeOfDay; break;
            case ExecuteParamKind.Numeric: NumericValue = 0m; break;
            case ExecuteParamKind.Boolean: BoolValue = false; break;
            // Text/Blob default to the empty string (TextValue's initial value).
        }
    }

    /// <summary>Restores a previously-used value (from the in-memory history) into
    /// the matching typed holder; null restores the NULL state.</summary>
    internal void ApplyHistoryValue(object? value)
    {
        if (value is null) { IsNull = true; return; }
        IsNull = false;
        switch (Kind)
        {
            case ExecuteParamKind.Boolean: BoolValue = value is bool b && b; break;
            case ExecuteParamKind.Numeric:
                try { NumericValue = Convert.ToDecimal(value, CultureInfo.InvariantCulture); } catch { }
                break;
            case ExecuteParamKind.Date:
                if (value is DateTime d1) DateValue = d1.Date;
                break;
            case ExecuteParamKind.Timestamp:
                if (value is DateTime d2) { DateValue = d2.Date; TimeValue = d2.TimeOfDay; }
                break;
            case ExecuteParamKind.Time:
                if (value is TimeSpan ts) TimeValue = ts;
                else if (value is DateTime d3) TimeValue = d3.TimeOfDay;
                break;
            default: TextValue = value.ToString() ?? string.Empty; break;
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
    // property (CalendarDatePicker.SelectedDate is DateTime?, TimePicker.SelectedTime
    // is TimeSpan?, NumericUpDown.Value is decimal? — gotcha #51).
    [ObservableProperty] private string _textValue = string.Empty;
    [ObservableProperty] private decimal? _numericValue;
    [ObservableProperty] private DateTime? _dateValue;
    [ObservableProperty] private TimeSpan? _timeValue;
    [ObservableProperty] private bool _boolValue;

    // Per-kind control visibility.
    public bool IsSingleLineTextKind => Kind == ExecuteParamKind.Text;
    public bool IsMultilineKind => Kind is ExecuteParamKind.BlobText or ExecuteParamKind.BlobBinary;
    public bool IsNumericKind => Kind == ExecuteParamKind.Numeric;
    public bool IsDateKind => Kind is ExecuteParamKind.Date or ExecuteParamKind.Timestamp;
    public bool IsTimeKind => Kind is ExecuteParamKind.Time or ExecuteParamKind.Timestamp;
    public bool IsDateTimeKind => IsDateKind || IsTimeKind;
    public bool IsBooleanKind => Kind == ExecuteParamKind.Boolean;

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

/// <summary>
/// Modal for collecting Execute Procedure input values with type-appropriate
/// controls. Returns the ordered bound values (null entry = SQL NULL) via
/// <see cref="Result"/>, or null on Cancel.
/// </summary>
public partial class ExecuteProcedureDialogViewModel : ObservableObject
{
    private readonly string? _procedureName;

    public ExecuteProcedureDialogViewModel(IEnumerable<ProcedureParamRowViewModel> inputs, string? procedureName = null)
    {
        _procedureName = procedureName;
        Params = new ObservableCollection<ExecuteProcedureParamRowViewModel>();
        foreach (var p in inputs)
        {
            Params.Add(new ExecuteProcedureParamRowViewModel(p.Name, p.TypeText));
        }

        // Restore the last-used values for this procedure (in-memory, app-lifetime).
        if (procedureName is not null && ExecuteProcedureHistory.Get(procedureName) is { } history)
        {
            for (int k = 0; k < Params.Count && k < history.Count; k++)
            {
                Params[k].ApplyHistoryValue(history[k]);
            }
        }
    }

    public ObservableCollection<ExecuteProcedureParamRowViewModel> Params { get; }

    /// <summary>Ordered bound values once accepted; null on cancel.</summary>
    public IReadOnlyList<object?>? Result { get; private set; }

    public event Action? RequestClose;

    [RelayCommand]
    private void Accept()
    {
        var values = Params.Select(p => p.Resolve()).ToList();
        Result = values;
        if (_procedureName is not null) ExecuteProcedureHistory.Set(_procedureName, values);
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        RequestClose?.Invoke();
    }
}

/// <summary>
/// Process-lifetime store of the last Execute Procedure parameter values, keyed by
/// procedure name. In-memory only (no persistence) — cleared when the app closes.
/// Matches IBExpert's per-session parameter history.
/// </summary>
internal static class ExecuteProcedureHistory
{
    private static readonly Dictionary<string, IReadOnlyList<object?>> Store =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<object?>? Get(string procedureName)
        => Store.TryGetValue(procedureName, out var v) ? v : null;

    public static void Set(string procedureName, IReadOnlyList<object?> values)
        => Store[procedureName] = values;
}

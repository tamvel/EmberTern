using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Import;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One row of the converted preview: the values as they would reach the database, or — when the row cannot get
/// there — the RAW values with the offending cell marked.
/// <para>
/// ⚠ <b>A failed row has no converted values, by construction.</b> <c>ImportPipeline</c> stops a row at its
/// first bad value, because the row is not going in either way and one clear reason beats four. So showing raw
/// values for such a row is not a fallback — it is the only truthful thing there is to show, and it happens to
/// be exactly what the user needs in order to fix the file (§3.6).
/// </para>
/// </summary>
public sealed class ImportConvertedRowViewModel
{
    public ImportConvertedRowViewModel(int sourceRowNumber, object?[] values)
    {
        SourceRowNumber = sourceRowNumber;
        Values = values ?? Array.Empty<object?>();
    }

    public int SourceRowNumber { get; }

    /// <summary>Values positionally aligned to <see cref="ImportConvertedPreviewViewModel.Columns"/>.</summary>
    public object?[] Values { get; }

    /// <summary>Set when this row would be rejected. The row then shows its raw values.</summary>
    public ImportProblemRowViewModel? Problem { get; init; }

    public bool IsFailed => Problem is not null;

    /// <summary>0-based index of the column at fault, or <c>-1</c> — so the grid can mark the cell rather than
    /// the whole row, which is what makes the marker actionable.</summary>
    public int FailedColumnIndex { get; init; } = -1;

    public object? ValueAt(int index) => index >= 0 && index < Values.Length ? Values[index] : null;
}

/// <summary>
/// The <b>Podgląd po konwersji</b> panel (§3.6): the first N rows <em>after conversion</em> — "exactly what will
/// reach the database".
/// <para>
/// ⭐ <b>It runs the real import.</b> The grid is filled by <c>ImportPipeline</c> with a
/// <c>BoundedImportProvider</c> in front and a <c>PreviewImportWriter</c> behind: the same converter, the same
/// validator, the same mapping, the same culture. That is the only way the panel's promise can be true, and it
/// is the same discipline that makes "Validate" a different argument rather than a different mode. A private
/// "convert for display" routine would be a second path, and a second path drifts.
/// </para>
/// <para>
/// Deliberately NOT wired: the shared filter panel and the aggregation bar. Filtering a preview would suggest it
/// changes what is imported; filtering data before import is its own planned feature (§9.5). A boundary, not an
/// oversight.
/// </para>
/// </summary>
public sealed partial class ImportConvertedPreviewViewModel : ViewModelBase
{
    /// <summary>Rows shown. A preview is a diagnostic, not the data.</summary>
    public const int MaxRows = 100;

    public ImportConvertedPreviewViewModel()
    {
        Rows = new ObservableCollection<ImportConvertedRowViewModel>();
        Columns = new ObservableCollection<string>();
        Problems = new ObservableCollection<ImportProblemRowViewModel>();
    }

    public ObservableCollection<ImportConvertedRowViewModel> Rows { get; }

    /// <summary>Mapped target column names, in the order the values are aligned to.</summary>
    public ObservableCollection<string> Columns { get; }

    /// <summary>Every problem the preview found, for the <b>Errors</b> bottom tab. Same rows as the markers in
    /// the grid — one computation, two surfaces.</summary>
    public ObservableCollection<ImportProblemRowViewModel> Problems { get; }

    /// <summary>Raised when the column set changed, so the view can rebuild its dynamic columns.</summary>
    public event EventHandler? SchemaChanged;

    [ObservableProperty] private string _headline = string.Empty;

    [ObservableProperty] private bool _isBusy;

    public bool HasRows => Rows.Count > 0;

    public bool HasProblems => Problems.Count > 0;

    /// <summary>Tab caption for the Errors tab — carries the count, because a tab that hides how much it has to
    /// say has to be opened to find out.</summary>
    public string ProblemsTabHeader => Problems.Count == 0
        ? UiStrings.ImportErrorsTab
        : string.Format(CultureInfo.CurrentCulture, UiStrings.ImportErrorsTabCountFormat, Problems.Count);

    /// <summary>
    /// Publishes one bounded pipeline run.
    /// </summary>
    /// <param name="columns">Mapped target columns, in value order.</param>
    /// <param name="written">Rows the pipeline converted and validated successfully.</param>
    /// <param name="outcome">The run, for its errors — each already carrying a SOURCE row number.</param>
    /// <param name="rawFor">Raw values for a failed row, projected to <paramref name="columns"/>. Supplied by
    /// the coordinator, which already holds the source records the grid above shows.</param>
    public void Publish(
        IReadOnlyList<string> columns,
        IReadOnlyList<ImportRow> written,
        ImportOutcome outcome,
        Func<int, object?[]?> rawFor)
    {
        if (columns is null) throw new ArgumentNullException(nameof(columns));
        if (written is null) throw new ArgumentNullException(nameof(written));
        if (outcome is null) throw new ArgumentNullException(nameof(outcome));
        if (rawFor is null) throw new ArgumentNullException(nameof(rawFor));

        var columnsChanged = !SameColumns(columns);
        if (columnsChanged)
        {
            Columns.Clear();
            foreach (var column in columns) Columns.Add(column);
        }

        Rows.Clear();
        Problems.Clear();

        foreach (var row in written)
        {
            Rows.Add(new ImportConvertedRowViewModel(row.SourceRowNumber, row.Values));
        }

        foreach (var error in outcome.Errors)
        {
            var problem = new ImportProblemRowViewModel(error);
            Problems.Add(problem);

            Rows.Add(new ImportConvertedRowViewModel(
                error.SourceRowNumber,
                rawFor(error.SourceRowNumber) ?? Array.Empty<object?>())
            {
                Problem = problem,
                FailedColumnIndex = IndexOf(columns, error.ColumnName),
            });
        }

        // Source order, so the preview reads like the file rather than like the order faults were found.
        SortRowsBySourceRow();

        Headline = BuildHeadline(Rows.Count, Problems.Count);
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemsTabHeader));

        if (columnsChanged) SchemaChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Empties the panel — there is nothing to convert (no source, no target, or nothing mapped).</summary>
    public void Clear()
    {
        var hadColumns = Columns.Count > 0;

        Rows.Clear();
        Problems.Clear();
        Columns.Clear();
        Headline = string.Empty;

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(ProblemsTabHeader));

        if (hadColumns) SchemaChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static string BuildHeadline(int rows, int problems) => problems == 0
        ? string.Format(CultureInfo.CurrentCulture, UiStrings.ImportPreviewHeadlineFormat, rows)
        : string.Format(CultureInfo.CurrentCulture, UiStrings.ImportPreviewHeadlineProblemsFormat, rows, problems);

    private void SortRowsBySourceRow()
    {
        var ordered = new List<ImportConvertedRowViewModel>(Rows);
        ordered.Sort((a, b) => a.SourceRowNumber.CompareTo(b.SourceRowNumber));

        Rows.Clear();
        foreach (var row in ordered) Rows.Add(row);
    }

    private bool SameColumns(IReadOnlyList<string> columns)
    {
        if (columns.Count != Columns.Count) return false;
        for (var i = 0; i < columns.Count; i++)
        {
            if (!string.Equals(columns[i], Columns[i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static int IndexOf(IReadOnlyList<string> columns, string? name)
    {
        if (string.IsNullOrEmpty(name)) return -1;
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }
}

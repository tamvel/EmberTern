using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Import;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One thing that went wrong with one row, in the four terms the report shows: <b>row · column · value ·
/// reason</b> (§3.7).
/// <para>
/// The same type serves the <b>Errors</b> tab (problems found in the converted preview, before anything is run)
/// and the <b>Report</b> tab (what a run actually hit). They are different questions asked at different moments,
/// but a problem reads the same either way — so there is one row type and one grid template, two instances.
/// </para>
/// </summary>
public sealed class ImportProblemRowViewModel
{
    public ImportProblemRowViewModel(ImportRowError error, bool isWarning = false)
    {
        if (error is null) throw new ArgumentNullException(nameof(error));

        SourceRowNumber = error.SourceRowNumber;
        ColumnName = error.ColumnName ?? string.Empty;
        RawValue = error.RawValue ?? string.Empty;
        Reason = Describe(error);
        IsWarning = isWarning;
    }

    /// <summary>The row as the user sees it in their file — never a batch index (the pipeline already translated
    /// that, and the report must never learn there was one).</summary>
    public int SourceRowNumber { get; }

    public string ColumnName { get; }

    /// <summary>The value as it appeared in the SOURCE. §0.2/§0.6: showing a post-conversion approximation would
    /// mean telling the user to go and fix something they do not have.</summary>
    public string RawValue { get; }

    public string Reason { get; }

    /// <summary>True for a row that <b>went in</b> with a value shortened to fit (§0.2). Not an error — which is
    /// exactly why it is marked rather than mixed in: counting it as a failure would say a written row was
    /// rejected.</summary>
    public bool IsWarning { get; }

    public string SeverityBrushKey => IsWarning ? "WarningBrush" : "ErrorBrush";

    /// <summary>
    /// ⭐ The ONE place an <see cref="ImportErrorKind"/> becomes a sentence (rule #6 — Core carries codes, never
    /// text). A second table anywhere would be a second vocabulary for the same fault.
    /// <para>
    /// Where the engine reported numbers, they are used: I0 measured that the truncation GDS vector carries the
    /// limit and the actual length as integers, so "26 characters, limit 20" comes from Firebird rather than
    /// from parsing its message.
    /// </para>
    /// </summary>
    public static string Describe(ImportRowError error)
    {
        if (error is null) throw new ArgumentNullException(nameof(error));

        if (error.Kind == ImportErrorKind.ValueTooLong && error.Limit is { } limit)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportErrorValueTooLongMeasuredFormat,
                error.ActualLength ?? error.RawValue?.Length ?? 0,
                limit);
        }

        var text = DescribeKind(error.Kind);

        // The server's own words are appended, never replaced: leaving the user with less information than
        // Firebird gave is its own kind of losing information.
        return error.ServerMessage is { Length: > 0 } server ? text + " — " + server : text;
    }

    public static string DescribeKind(ImportErrorKind kind) => kind switch
    {
        ImportErrorKind.NotAnInteger => UiStrings.ImportErrorNotAnInteger,
        ImportErrorKind.NotANumber => UiStrings.ImportErrorNotANumber,
        ImportErrorKind.NotADateTime => UiStrings.ImportErrorNotADateTime,
        ImportErrorKind.NotABoolean => UiStrings.ImportErrorNotABoolean,
        ImportErrorKind.ValueTooLong => UiStrings.ImportErrorValueTooLong,
        ImportErrorKind.ValueOutOfRange => UiStrings.ImportErrorValueOutOfRange,
        ImportErrorKind.PrecisionWouldBeLost => UiStrings.ImportErrorPrecisionWouldBeLost,
        ImportErrorKind.UnsupportedTargetType => UiStrings.ImportErrorUnsupportedTargetType,
        ImportErrorKind.NullNotAllowed => UiStrings.ImportErrorNullNotAllowed,
        ImportErrorKind.NotRepresentableInConnectionCharset => UiStrings.ImportErrorNotRepresentable,
        ImportErrorKind.SourceErrorValue => UiStrings.ImportErrorSourceErrorValue,
        ImportErrorKind.ServerNullViolation => UiStrings.ImportErrorServerNullViolation,
        ImportErrorKind.ServerUniqueViolation => UiStrings.ImportErrorServerUniqueViolation,
        ImportErrorKind.ServerCheckViolation => UiStrings.ImportErrorServerCheckViolation,
        ImportErrorKind.ServerForeignKeyViolation => UiStrings.ImportErrorServerForeignKeyViolation,
        ImportErrorKind.ServerStringTruncation => UiStrings.ImportErrorServerStringTruncation,
        ImportErrorKind.ServerNumericOverflow => UiStrings.ImportErrorServerNumericOverflow,
        ImportErrorKind.ServerTransliterationFailed => UiStrings.ImportErrorServerTransliteration,
        _ => UiStrings.ImportErrorServerError,
    };
}

/// <summary>
/// What a run did — the <b>Report</b> tab (§3.7).
/// <para>
/// ⭐ <b>§0.6: the report does not lie.</b> "Imported N" means N rows the server accepted. While the transaction
/// is still open the headline says so and offers the decision, instead of calling an unpersisted import a
/// success. A cancelled run says it was cancelled and still accounts for the rows already written. A validation
/// says it validated and wrote nothing.
/// </para>
/// <para>
/// It is a pure projection of <see cref="ImportOutcome"/> — every number comes from the pipeline, which counted
/// them against source row numbers. Nothing here recomputes anything.
/// </para>
/// </summary>
public sealed partial class ImportRunReportViewModel : ViewModelBase
{
    public ImportRunReportViewModel()
    {
        Problems = new ObservableCollection<ImportProblemRowViewModel>();
    }

    /// <summary>Errors first, then the shortened-value warnings — both in source-row order within their group.</summary>
    public ObservableCollection<ImportProblemRowViewModel> Problems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    private string _headline = string.Empty;

    public bool HasReport => Headline.Length > 0;

    /// <summary>A note under the headline when the collected list was capped, or when rows were shortened —
    /// the report admitting what it left out rather than implying the list is complete.</summary>
    [ObservableProperty] private string _note = string.Empty;

    public bool HasNote => Note.Length > 0;

    partial void OnNoteChanged(string value) => OnPropertyChanged(nameof(HasNote));

    /// <summary>True while the user still owes a Commit or a Rollback. Drives the two buttons that live HERE,
    /// beside the numbers, and not only in the global transaction bar — the decision is taken where the
    /// evidence is (§3.7).</summary>
    [ObservableProperty] private bool _transactionLeftOpen;

    /// <summary>Severity of the headline, mapped through the shared <c>MessageBanner</c> table so the report
    /// and every other surface describe the same state with the same colour (§9.3).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SeverityBrushKey))]
    private Controls.MessageSeverity _severity = Controls.MessageSeverity.Info;

    /// <summary>The brush key for <see cref="Severity"/>, from the SHARED map — not a second table. A brush is
    /// then looked up WITH the theme variant in the view (gotcha #250).</summary>
    public string SeverityBrushKey => Controls.MessageBanner.BrushKeyFor(Severity);

    public bool HasProblems => Problems.Count > 0;

    /// <summary>
    /// Publishes one outcome.
    /// </summary>
    /// <param name="outcome">What the pipeline counted.</param>
    /// <param name="validation">True when this was a dry run — which wrote nothing and therefore left nothing to
    /// commit, and must say so plainly rather than borrowing the vocabulary of a real import.</param>
    /// <param name="elapsed">Wall time of the run.</param>
    /// <param name="rowsCommitted">Rows already committed by <c>Batched</c>, which a Rollback can no longer
    /// undo (§0.5). Zero in every other mode.</param>
    public void Publish(ImportOutcome outcome, bool validation, TimeSpan elapsed, long rowsCommitted = 0)
    {
        if (outcome is null) throw new ArgumentNullException(nameof(outcome));

        Problems.Clear();
        foreach (var error in outcome.Errors) Problems.Add(new ImportProblemRowViewModel(error));
        foreach (var warning in outcome.Warnings) Problems.Add(new ImportProblemRowViewModel(warning, isWarning: true));
        OnPropertyChanged(nameof(HasProblems));

        TransactionLeftOpen = outcome.TransactionLeftOpen;
        Headline = BuildHeadline(outcome, validation, elapsed);
        Note = BuildNote(outcome, rowsCommitted);
        Severity = ResolveSeverity(outcome, validation);
    }

    /// <summary>Clears the report — a new run has started, and last run's numbers beside a running one would be
    /// read as this run's.</summary>
    public void Clear()
    {
        Problems.Clear();
        OnPropertyChanged(nameof(HasProblems));
        Headline = string.Empty;
        Note = string.Empty;
        TransactionLeftOpen = false;
        Severity = Controls.MessageSeverity.Info;
    }

    internal static string BuildHeadline(ImportOutcome outcome, bool validation, TimeSpan elapsed)
    {
        var time = ExecutionTimer.Format(elapsed);

        if (validation)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                outcome.Cancelled
                    ? UiStrings.ImportReportValidatedCancelledFormat
                    : UiStrings.ImportReportValidatedFormat,
                outcome.RowsWritten, outcome.RowsRead, outcome.RowsFailed, time);
        }

        var format = outcome.Cancelled
            ? UiStrings.ImportReportCancelledFormat
            : UiStrings.ImportReportImportedFormat;

        var headline = string.Format(
            CultureInfo.CurrentCulture, format,
            outcome.RowsWritten, outcome.RowsRead, outcome.RowsFailed, time);

        // ⭐ §0.6. Not "import succeeded": nothing is persisted until the user says so, and the sentence that
        // omits that is the exact lie the rule names.
        return outcome.TransactionLeftOpen
            ? headline + "  " + UiStrings.ImportReportTransactionOpen
            : headline;
    }

    internal static string BuildNote(ImportOutcome outcome, long rowsCommitted)
    {
        var parts = new List<string>(4);

        if (!string.IsNullOrEmpty(outcome.CreatedTable))
        {
            // ⭐ §0.5 / gotcha #213. The table was committed on the Ddl lane before the first row, so it
            // outlives a Rollback — and the report is the last place that fact can still be said. It is stated
            // whether the run succeeded or not: a table left behind by a failed import is exactly the thing
            // the user needs told.
            parts.Add(string.Format(
                CultureInfo.CurrentCulture, UiStrings.ImportReportCreatedTableFormat, outcome.CreatedTable));
        }

        if (rowsCommitted > 0)
        {
            // §0.5 — the one thing a Rollback will not take back in this mode.
            parts.Add(string.Format(
                CultureInfo.CurrentCulture, UiStrings.ImportReportRowsCommittedFormat, rowsCommitted));
        }

        if (outcome.Warnings.Count > 0)
        {
            parts.Add(string.Format(
                CultureInfo.CurrentCulture, UiStrings.ImportReportShortenedFormat, outcome.Warnings.Count));
        }

        if (outcome.ErrorsTruncated || outcome.WarningsTruncated)
        {
            parts.Add(string.Format(
                CultureInfo.CurrentCulture, UiStrings.ImportReportListTruncatedFormat,
                ImportOutcome.MaxCollectedErrors));
        }

        return string.Join(" · ", parts);
    }

    private static Controls.MessageSeverity ResolveSeverity(ImportOutcome outcome, bool validation)
    {
        if (outcome.RowsFailed > 0) return Controls.MessageSeverity.Error;
        if (outcome.Cancelled || outcome.Warnings.Count > 0) return Controls.MessageSeverity.Warning;
        return validation || !outcome.TransactionLeftOpen
            ? Controls.MessageSeverity.Success
            : Controls.MessageSeverity.Info;
    }

    /// <summary>The report as text, for the Copy button — the same four columns the grid shows, tab-separated so
    /// it pastes into a spreadsheet.</summary>
    public string ToClipboardText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Headline);
        if (Note.Length > 0) sb.AppendLine(Note);
        if (Problems.Count == 0) return sb.ToString();

        sb.AppendLine();
        sb.Append(UiStrings.ImportReportColumnRow).Append('\t')
          .Append(UiStrings.ImportReportColumnColumn).Append('\t')
          .Append(UiStrings.ImportReportColumnValue).Append('\t')
          .AppendLine(UiStrings.ImportReportColumnReason);

        foreach (var problem in Problems)
        {
            sb.Append(problem.SourceRowNumber.ToString(CultureInfo.CurrentCulture)).Append('\t')
              .Append(problem.ColumnName).Append('\t')
              .Append(problem.RawValue).Append('\t')
              .AppendLine(problem.Reason);
        }

        return sb.ToString();
    }
}

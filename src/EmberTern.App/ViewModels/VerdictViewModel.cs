using System.Globalization;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of a <see cref="PerformanceVerdict"/> for the verdict bar
/// (zone ①). Immutable — rebuilt per report.</summary>
public sealed class VerdictViewModel
{
    public VerdictViewModel(PerformanceVerdict verdict)
    {
        Verdict = verdict;
    }

    public PerformanceVerdict Verdict { get; }

    public PerformanceGrade Grade => Verdict.Grade;

    public string GradeText => Verdict.Grade switch
    {
        PerformanceGrade.Fast => "Fast",
        PerformanceGrade.Acceptable => "Acceptable",
        PerformanceGrade.NeedsAttention => "Needs attention",
        PerformanceGrade.Slow => "Slow",
        _ => "Analyzed",
    };

    /// <summary>Theme brush key for the grade dot, resolved via DynamicResource in XAML.</summary>
    public string GradeBrushKey => Verdict.Grade switch
    {
        PerformanceGrade.Fast => "SuccessIconBrush",
        PerformanceGrade.Acceptable => "InfoIconBrush",
        PerformanceGrade.NeedsAttention => "WarningBrush",
        PerformanceGrade.Slow => "DangerIconBrush",
        _ => "SubtleForegroundBrush",
    };

    public string Headline => Verdict.Headline;

    public string DurationText => FormatDuration(Verdict.Duration.TotalMilliseconds);

    // A SELECT reports rows returned; a DML / procedure reports rows changed (returned is 0).
    public string RowsText
    {
        get
        {
            if (Verdict.HasResultSet)
            {
                return Verdict.RowsReturned == 1
                    ? "1 row"
                    : Verdict.RowsReturned.ToString("N0", CultureInfo.CurrentCulture) + " rows";
            }
            return Verdict.RowsChanged == 1
                ? "1 row changed"
                : Verdict.RowsChanged.ToString("N0", CultureInfo.CurrentCulture) + " rows changed";
        }
    }

    public bool HasRowsRead => Verdict.RowsRead is not null;

    public string RowsReadText => Verdict.RowsRead is { } read
        ? read.ToString("N0", CultureInfo.CurrentCulture) + " read"
        : string.Empty;

    public bool HasAmplification => Verdict.Amplification is not null;

    public string AmplificationText => Verdict.Amplification is { } amp
        ? amp.ToString("0.#", CultureInfo.CurrentCulture) + "×"
        : string.Empty;

    private static string FormatDuration(double ms) => ms < 1000
        ? ms.ToString("0", CultureInfo.CurrentCulture) + " ms"
        : (ms / 1000.0).ToString("0.00", CultureInfo.CurrentCulture) + " s";
}

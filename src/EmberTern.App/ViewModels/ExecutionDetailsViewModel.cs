using System.Globalization;
using System.Text;
using EmberTern.Core.Performance;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of <see cref="ExecutionDetails"/> for the expert drawer
/// (zone ④). All strings selectable/copyable in the view. Immutable — rebuilt per report.</summary>
public sealed class ExecutionDetailsViewModel
{
    public ExecutionDetailsViewModel(ExecutionDetails details)
    {
        Details = details;
    }

    public ExecutionDetails Details { get; }

    public bool HasTimings => Details.Timings is not null;

    public string TimingsText
    {
        get
        {
            if (Details.Timings is not { } t)
            {
                return "—";
            }
            var sb = new StringBuilder();
            if (t.Prepare is { } prep)
            {
                sb.Append(UiStrings.ExecutionTimingsPrepare).Append(Ms(prep.TotalMilliseconds)).Append(" · ");
            }
            sb.Append(UiStrings.ExecutionTimingsExecute).Append(Ms(t.Execute.TotalMilliseconds));
            if (t.Fetch is { } fetch)
            {
                sb.Append(UiStrings.ExecutionTimingsFetch).Append(Ms(fetch.TotalMilliseconds));
            }
            return sb.ToString();
        }
    }

    public bool HasPlanText => !string.IsNullOrWhiteSpace(Details.RawPlanText);

    public string RawPlanText => Details.RawPlanText ?? string.Empty;

    public string CaptureMethodText => Details.Method switch
    {
        CaptureMethod.PlanOnly => UiStrings.ExecutionCapturePlanOnly,
        CaptureMethod.MonAttachmentDelta => UiStrings.ExecutionCaptureMonAttachment,
        CaptureMethod.MonStatement => UiStrings.ExecutionCaptureMonStatement,
        CaptureMethod.Trace => UiStrings.ExecutionCaptureTrace,
        _ => "—",
    };

    public string PlanDialectText => Details.PlanDialect switch
    {
        PlanDialect.Explain => UiStrings.ExecutionPlanDialectExplain,
        PlanDialect.Legacy => UiStrings.ExecutionPlanDialectLegacy,
        _ => "—",
    };

    /// <summary>The full copy-to-clipboard payload for the expert drawer.</summary>
    public string CopyText
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(UiStrings.ExecutionCopyTimingsLabel).AppendLine(TimingsText);
            sb.Append(UiStrings.ExecutionCopyCaptureLabel).AppendLine(CaptureMethodText);
            if (HasPlanText)
            {
                sb.AppendLine(string.Format(CultureInfo.CurrentCulture, UiStrings.ExecutionCopyPlanLabelFormat, PlanDialectText));
                sb.AppendLine(RawPlanText);
            }
            return sb.ToString();
        }
    }

    private static string Ms(double ms) => ms < 1000
        ? ms.ToString("0", CultureInfo.CurrentCulture) + " ms"
        : (ms / 1000.0).ToString("0.00", CultureInfo.CurrentCulture) + " s";
}

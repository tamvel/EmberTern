using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.App.Controls;
using EmberTern.Core.Import;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One line of the readiness strip — a pure projection of a Core <see cref="ReadinessItem"/>.
/// <para>
/// It decides nothing. The severity, the blocking flag and the owning section all come from Core; this only
/// turns a code into a sentence (rule #6 — Core holds no UI strings) and a severity into the theme keys the
/// strip paints with.
/// </para>
/// </summary>
public sealed class ImportReadinessItemViewModel
{
    public ImportReadinessItemViewModel(ReadinessItem item)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Severity = ToBannerSeverity(item.Severity);
        Message = Describe(item);
    }

    public ReadinessItem Item { get; }

    /// <summary>Which section to expand and focus when this row is clicked.</summary>
    public ImportSection Section => Item.Section;

    public bool IsBlocking => Item.IsBlocking;

    /// <summary>The published <c>IMP####</c> code — shown in the tooltip so a report can be talked about.</summary>
    public string Code => Item.CodeText;

    public string Message { get; }

    /// <summary>⭐ The SHARED severity vocabulary. Mapping Core's severity onto the banner's here — once — is
    /// what keeps the strip and a <see cref="MessageBanner"/> from describing the same idea differently
    /// (§9.3). A second brush table would be the drift.</summary>
    public MessageSeverity Severity { get; }

    public string BrushKey => MessageBanner.BrushKeyFor(Severity);

    public string GeometryKey => MessageBanner.GeometryKeyFor(Severity);

    public static MessageSeverity ToBannerSeverity(ImportSeverity severity) => severity switch
    {
        ImportSeverity.Error => MessageSeverity.Error,
        ImportSeverity.Warning => MessageSeverity.Warning,
        _ => MessageSeverity.Info,
    };

    /// <summary>
    /// Code → sentence. The one place import diagnostics become English (rule #6), mirroring the convention
    /// <c>ExportUnavailableReason</c> established.
    /// </summary>
    public static string Describe(ReadinessItem item)
    {
        var subject = item.Subject ?? string.Empty;
        var count = item.Count ?? 0;

        return item.Code switch
        {
            ImportDiagnosticCode.NoSource => UiStrings.ImportReadyNoSource,
            ImportDiagnosticCode.SourceMissing => Format(UiStrings.ImportReadySourceMissingFormat, subject),
            ImportDiagnosticCode.SourceUnreadable => UiStrings.ImportReadySourceUnreadable,
            ImportDiagnosticCode.SourceHasNoFields => UiStrings.ImportReadySourceHasNoFields,
            ImportDiagnosticCode.SourceOptionsMismatch => UiStrings.ImportReadySourceOptionsMismatch,

            ImportDiagnosticCode.NoTarget => UiStrings.ImportReadyNoTarget,
            ImportDiagnosticCode.TargetNotFound => Format(UiStrings.ImportReadyTargetNotFoundFormat, subject),
            ImportDiagnosticCode.NewTableHasNoColumns => UiStrings.ImportReadyNewTableHasNoColumns,
            ImportDiagnosticCode.NewTableWillBeCommitted => Format(UiStrings.ImportReadyNewTableWillBeCommittedFormat, subject),
            ImportDiagnosticCode.TargetHasBeforeInsertTriggers => Count(UiStrings.ImportReadyBeforeInsertTriggersFormat, count),
            ImportDiagnosticCode.TargetWillBeEmptied => Format(UiStrings.ImportReadyTargetWillBeEmptiedFormat, subject),

            ImportDiagnosticCode.NothingMapped => UiStrings.ImportReadyNothingMapped,
            ImportDiagnosticCode.RequiredColumnNotMapped => Format(UiStrings.ImportReadyRequiredColumnNotMappedFormat, subject),
            ImportDiagnosticCode.UnsupportedColumnType => Format(UiStrings.ImportReadyUnsupportedColumnTypeFormat, subject),
            ImportDiagnosticCode.TargetColumnNotMapped => Count(UiStrings.ImportReadyColumnsNotMappedFormat, count),
            ImportDiagnosticCode.SourceFieldUnused => Count(UiStrings.ImportReadyFieldsUnusedFormat, count),
            ImportDiagnosticCode.AmbiguousNameMatch => Format(UiStrings.ImportReadyAmbiguousNameFormat, subject),
            ImportDiagnosticCode.MappingDropped => Format(UiStrings.ImportReadyMappingDroppedFormat, subject),
            ImportDiagnosticCode.ColumnNotWritable => Format(UiStrings.ImportReadyColumnNotWritableFormat, subject),
            ImportDiagnosticCode.IdentityOverrideRequired => Format(UiStrings.ImportReadyIdentityOverrideFormat, subject),
            ImportDiagnosticCode.PairingAssumed => Format(UiStrings.ImportReadyPairingAssumedFormat, subject),

            ImportDiagnosticCode.NotConnected => UiStrings.ImportReadyNotConnected,
            ImportDiagnosticCode.UserTransactionOpen => UiStrings.ImportReadyUserTransactionOpen,
            ImportDiagnosticCode.BatchedIsNotAtomic => Count(UiStrings.ImportReadyBatchedNotAtomicFormat, count),
            ImportDiagnosticCode.TrimmingEnabled => UiStrings.ImportReadyTrimmingEnabled,
            ImportDiagnosticCode.LongTransactionRisk => Count(UiStrings.ImportReadyLongTransactionFormat, count),
            ImportDiagnosticCode.NotRepresentableInConnectionCharset =>
                Count(UiStrings.ImportReadyNotRepresentableFormat, count),

            _ => item.CodeText,
        };
    }

    private static string Format(string format, string subject)
        => string.Format(CultureInfo.CurrentCulture, format, subject);

    private static string Count(string format, int count)
        => string.Format(CultureInfo.CurrentCulture, format, count);
}

/// <summary>
/// The readiness strip (§3.2) — the surface's answer to "what is missing", and the only thing that gates the
/// run.
/// <para>
/// ⭐ It is a <b>view of <see cref="ImportReadiness"/></b>, not a second opinion. Every decision (blocking vs
/// warning, which section is at fault, whether the import may start) is computed in Core and merely rendered
/// here — the same move that put <c>DebugPreflightItem.BannerSeverity</c> in the model rather than the XAML.
/// </para>
/// <para>
/// Its advantage over a wizard's "Next" button is that it shows EVERY gap at once and each row leads to the
/// section that caused it. A greyed-out button with no reason is a UX defect (§9.1 point 3).
/// </para>
/// </summary>
public sealed partial class ImportReadinessViewModel : ViewModelBase
{
    public ImportReadinessViewModel()
    {
        Items = new ObservableCollection<ImportReadinessItemViewModel>();
        Sections = new ObservableCollection<ImportReadinessSectionViewModel>();
        Update(ImportReadinessReport.Empty, 0);
    }

    /// <summary>Every finding, in the order Core produced them.</summary>
    public ObservableCollection<ImportReadinessItemViewModel> Items { get; }

    /// <summary>One chip per section — the compact ✓/⚠/✖ row the user reads first.</summary>
    public ObservableCollection<ImportReadinessSectionViewModel> Sections { get; }

    /// <summary>True when nothing blocks the run.</summary>
    [ObservableProperty] private bool _canRun;

    /// <summary>The "✓ Ready to import — N rows" line shown when everything is green.</summary>
    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>True when there is at least one finding worth listing under the chips.</summary>
    [ObservableProperty] private bool _hasItems;

    public void Update(ImportReadinessReport report, long rowsKnown)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));

        Items.Clear();
        foreach (var item in report.Items) Items.Add(new ImportReadinessItemViewModel(item));

        Sections.Clear();
        foreach (var section in AllSections)
        {
            Sections.Add(new ImportReadinessSectionViewModel(section, report.SeverityFor(section)));
        }

        CanRun = report.CanRun;
        HasItems = Items.Count > 0;
        Summary = CanRun
            ? (rowsKnown > 0
                ? string.Format(CultureInfo.CurrentCulture, UiStrings.ImportReadySummaryWithRowsFormat, rowsKnown)
                : UiStrings.ImportReadySummary)
            : UiStrings.ImportReadyBlocked;
    }

    private static readonly ImportSection[] AllSections =
    {
        ImportSection.Source,
        ImportSection.Format,
        ImportSection.Target,
        ImportSection.Mapping,
        ImportSection.Transaction,
    };
}

/// <summary>One chip of the strip: a section and how it currently reads.</summary>
public sealed class ImportReadinessSectionViewModel
{
    public ImportReadinessSectionViewModel(ImportSection section, ImportSeverity? severity)
    {
        Section = section;
        Title = TitleFor(section);

        // No finding at all is the ✓ state — deliberately Success rather than Info, because "nothing to say
        // about this section" is exactly what the user wants confirmed.
        Severity = severity is null
            ? MessageSeverity.Success
            : ImportReadinessItemViewModel.ToBannerSeverity(severity.Value);
    }

    public ImportSection Section { get; }
    public string Title { get; }
    public MessageSeverity Severity { get; }
    public string BrushKey => MessageBanner.BrushKeyFor(Severity);
    public string GeometryKey => MessageBanner.GeometryKeyFor(Severity);

    public static string TitleFor(ImportSection section) => section switch
    {
        ImportSection.Source => UiStrings.ImportSectionSource,
        ImportSection.Format => UiStrings.ImportSectionFormat,
        ImportSection.Target => UiStrings.ImportSectionTarget,
        ImportSection.Mapping => UiStrings.ImportSectionMapping,
        _ => UiStrings.ImportSectionTransaction,
    };
}

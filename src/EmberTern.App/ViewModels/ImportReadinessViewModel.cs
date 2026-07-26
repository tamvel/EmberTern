using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    /// <summary>
    /// How many findings the strip shows before it stops growing (U6).
    /// <para>
    /// ⭐ The reason there is a ceiling at all: an uncapped list takes the most vertical space at exactly the
    /// moment the user has the most to fix, i.e. when they most need to see the data they are fixing it
    /// against. The <b>chips</b> keep §3.2's promise — every section's state is visible at once, in one line,
    /// by colour — so the cap costs no information, only immediacy of the wording, and one click restores
    /// that too.
    /// </para>
    /// </summary>
    public const int CollapsedItemLimit = 3;

    public ImportReadinessViewModel()
    {
        Items = new ObservableCollection<ImportReadinessItemViewModel>();
        VisibleItems = new ObservableCollection<ImportReadinessItemViewModel>();
        Sections = new ObservableCollection<ImportReadinessSectionViewModel>();
        Update(ImportReadinessReport.Empty, 0);
    }

    /// <summary>Every finding, in the order Core produced them.</summary>
    public ObservableCollection<ImportReadinessItemViewModel> Items { get; }

    /// <summary>What the strip actually renders — <see cref="Items"/> when expanded, its head when not.</summary>
    public ObservableCollection<ImportReadinessItemViewModel> VisibleItems { get; }

    /// <summary>One chip per section — the compact ✓/⚠/✖ row the user reads first.</summary>
    public ObservableCollection<ImportReadinessSectionViewModel> Sections { get; }

    /// <summary>True when nothing blocks the run.</summary>
    [ObservableProperty] private bool _canRun;

    /// <summary>True when nothing blocks "Validate" — weaker than <see cref="CanRun"/>, because a dry run writes
    /// nowhere and therefore does not care about the working transaction. Both values come straight from Core's
    /// report; the strip never decides either of them.</summary>
    [ObservableProperty] private bool _canValidate;

    /// <summary>The "✓ Ready to import — N rows" line shown when everything is green.</summary>
    [ObservableProperty] private string _summary = string.Empty;

    /// <summary>True when there is at least one finding worth listing under the chips.</summary>
    [ObservableProperty] private bool _hasItems;

    /// <summary>True while the full list is shown instead of its capped head.</summary>
    [ObservableProperty] private bool _isExpanded;

    /// <summary>"… and 2 more problems" — the line that admits the list was cut. Empty when nothing is hidden.</summary>
    [ObservableProperty] private string _moreText = string.Empty;

    /// <summary>True when there is anything the cap is currently hiding.</summary>
    [ObservableProperty] private bool _hasHiddenItems;

    /// <summary>Show the whole list, or fold it back to the cap. The state survives a refresh so the strip
    /// does not snap shut under a user who opened it and is reading it.</summary>
    [RelayCommand]
    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
        PublishVisibleItems();
    }

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
        CanValidate = report.CanValidate;
        HasItems = Items.Count > 0;
        PublishVisibleItems();

        Summary = CanRun
            ? (rowsKnown > 0
                ? string.Format(CultureInfo.CurrentCulture, UiStrings.ImportReadySummaryWithRowsFormat, rowsKnown)
                : UiStrings.ImportReadySummary)
            : UiStrings.ImportReadyBlocked;
    }

    /// <summary>
    /// Projects <see cref="Items"/> onto <see cref="VisibleItems"/> under the cap.
    /// <para>
    /// The findings keep Core's order, so the ones that survive the cut are the ones Core put first — the cap
    /// never re-ranks anything, because a second ordering here is exactly how a strip and a report start
    /// disagreeing about which problem matters most.
    /// </para>
    /// </summary>
    private void PublishVisibleItems()
    {
        var limit = IsExpanded ? Items.Count : Math.Min(CollapsedItemLimit, Items.Count);

        VisibleItems.Clear();
        for (var i = 0; i < limit; i++) VisibleItems.Add(Items[i]);

        var hidden = Items.Count - limit;
        HasHiddenItems = hidden > 0 || (IsExpanded && Items.Count > CollapsedItemLimit);
        MoreText = hidden > 0
            ? string.Format(CultureInfo.CurrentCulture, UiStrings.ImportReadyMoreItemsFormat, hidden)
            : (IsExpanded ? UiStrings.ImportReadyShowFewer : string.Empty);
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

    /// <summary>
    /// What clicking this chip does, in words. The strip is both a status light and a way in (§3.2), and a
    /// control that is both has to say which — otherwise the user is left guessing whether it is a filter, a
    /// tab, a shortcut or a status indicator.
    /// </summary>
    public string FocusHint => Section == ImportSection.Format
        ? UiStrings.ImportReadyChipFormatHint
        : string.Format(CultureInfo.CurrentCulture, UiStrings.ImportReadyChipHintFormat, Title);
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

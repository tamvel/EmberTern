using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Controls;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;

namespace EmberTern.App.ViewModels;

/// <summary>One raw record in the source-preview grid — the provider's own values, before conversion.</summary>
public sealed class ImportSourceRecordRowViewModel
{
    public ImportSourceRecordRowViewModel(RawRecord record)
    {
        SourceRowNumber = record.SourceRowNumber;
        Values = record.Values;
    }

    public int SourceRowNumber { get; }
    public object?[] Values { get; }

    /// <summary>
    /// True when this record's field count differs from what the rest of the file does — the instant tell for
    /// a wrong separator or a stray quote, shown as a gutter marker so it is seen rather than counted (§3.6).
    /// <para>
    /// Stamped after the whole preview is read, against the MOST COMMON width — deliberately not against the
    /// schema's, which is the WIDEST record so that every column stays mappable. Comparing to the widest would
    /// invert the signal: one row with an extra field would set the width and mark all two hundred good rows
    /// as odd.
    /// </para>
    /// </summary>
    public bool IsRagged { get; internal set; }

    public object? ValueAt(int index) => index >= 0 && index < Values.Length ? Values[index] : null;
}

/// <summary>
/// The Data Import working surface (§3) — <b>one surface, not a wizard</b> (decision D7).
/// <para>
/// ⭐ <b>This VM is the single owner of <see cref="ImportConfiguration"/>.</b> Section VMs read their slice and
/// produce a new one; the ONLY place UI state becomes the record (and back) is
/// <see cref="BuildConfiguration"/> / <see cref="ApplyConfiguration"/>. That is §4.8.6, and it is what lets
/// named profiles arrive in I11 as pure UI over an existing store instead of a two-way mapper over forty
/// scattered properties — the rebuild the design exists to avoid.
/// </para>
/// <para>
/// <b>Recalculation (§4.7)</b> is one chain, re-run after any change, lazy and cancellable: a newer edit
/// cancels the in-flight schema read (the CTS idiom the editor's language service uses). Readiness is cheap
/// and computed synchronously; reading the source is not, and goes to a background thread.
/// </para>
/// <para>
/// <b>Etap I5 scope:</b> the frame, the readiness strip and the Source-and-format section. Target, mapping,
/// preview-after-conversion and the run itself are I6/I7 — until then readiness honestly reports "no target",
/// which is the correct answer rather than a placeholder.
/// </para>
/// </summary>
public sealed partial class DataImportTabViewModel : ViewModelBase
{
    /// <summary>Records held for the source preview. A million-row file must not become a million rows in
    /// memory (design R8); the preview is a diagnostic, not the data.</summary>
    public const int SourcePreviewRows = 200;

    private readonly IImportProvider _delimitedProvider = new DelimitedTextImportProvider();
    private readonly Func<bool> _isConnected;
    private readonly Func<bool> _hasOpenUserTransaction;
    private readonly Func<string> _connectionName;

    /// <summary>Reads the table list from the METADATA lane. A delegate, not a service reference, for the same
    /// reason the environment facts are: it keeps the VM testable without a database, and keeps every Firebird
    /// type out of App's ViewModels.</summary>
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>>? _listTablesAsync;

    private readonly Func<string, CancellationToken, Task<ImportTarget?>>? _readTargetAsync;

    private ImportConfiguration _configuration = ImportConfiguration.Empty;
    private SourceSchema? _schema;
    private ImportTarget? _target;
    private bool _sourceExists = true;
    private bool _sourceReadable = true;
    private bool _tablesLoaded;
    private CancellationTokenSource? _recalculation;

    /// <summary>
    /// True once the user has expanded the format options by hand, which suspends auto-collapse (U11) until
    /// they collapse them again. An automat that closes a panel the user just opened is worse than no automat
    /// at all (§2.2 point 2).
    /// </summary>
    private bool _formatOptionsHeldOpen;

    public DataImportTabViewModel(
        Func<bool> isConnected,
        Func<bool> hasOpenUserTransaction,
        Func<string> connectionName,
        Func<CancellationToken, Task<IReadOnlyList<string>>>? listTablesAsync = null,
        Func<string, CancellationToken, Task<ImportTarget?>>? readTargetAsync = null)
    {
        _isConnected = isConnected ?? throw new ArgumentNullException(nameof(isConnected));
        _hasOpenUserTransaction = hasOpenUserTransaction ?? throw new ArgumentNullException(nameof(hasOpenUserTransaction));
        _connectionName = connectionName ?? throw new ArgumentNullException(nameof(connectionName));
        _listTablesAsync = listTablesAsync;
        _readTargetAsync = readTargetAsync;

        Source = new ImportSourceSectionViewModel();
        Source.Changed += (_, _) => QueueRecalculate();

        Target = new ImportTargetSectionViewModel();
        Target.Changed += (_, _) => QueueRecalculate();

        Mapping = new ImportMappingPanelViewModel();
        Mapping.Changed += (_, _) => OnMappingEdited();
        Mapping.StrategyRequested += (_, strategy) => ApplyMappingStrategy(strategy);

        Readiness = new ImportReadinessViewModel();
        PreviewRows = new ObservableCollection<ImportSourceRecordRowViewModel>();
        PreviewFields = new ObservableCollection<SourceField>();

        Recalculate();
    }

    /// <summary>The Source and format section.</summary>
    public ImportSourceSectionViewModel Source { get; }

    /// <summary>The Target section (§3.4) — existing table; the new-table variant is etap I8.</summary>
    public ImportTargetSectionViewModel Target { get; }

    /// <summary>The Mapping panel (§3.5).</summary>
    public ImportMappingPanelViewModel Mapping { get; }

    /// <summary>The readiness strip (§3.2).</summary>
    public ImportReadinessViewModel Readiness { get; }

    /// <summary>Raw records as the provider produced them — the "Source preview" bottom tab.</summary>
    public ObservableCollection<ImportSourceRecordRowViewModel> PreviewRows { get; }

    /// <summary>The fields the preview grid builds its columns from.</summary>
    public ObservableCollection<SourceField> PreviewFields { get; }

    /// <summary>Raised when the preview's shape changed, so the view can rebuild its dynamic columns.</summary>
    public event EventHandler? PreviewSchemaChanged;

    /// <summary>The view supplies a file picker; the VM never touches a dialog type (rule #1).</summary>
    public event Func<Task<string?>>? FilePickRequested;

    /// <summary>The view supplies the clipboard text. App owns Avalonia's clipboard, Core gets a string —
    /// which is exactly why the clipboard is not a second parser (§1.5).</summary>
    public event Func<Task<string?>>? ClipboardReadRequested;

    /// <summary>Asks the view to expand and focus a section (a readiness chip was clicked).</summary>
    public event EventHandler<ImportSection>? SectionFocusRequested;

    // ── Band C: the one message surface ─────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = string.Empty;

    [ObservableProperty] private MessageSeverity _statusSeverity = MessageSeverity.Info;

    public bool HasStatusMessage => StatusMessage.Length > 0;

    // ── Band H: the surface status line — numbers, never adjectives (§9.1 point 4) ──────────────────────

    [ObservableProperty] private string _surfaceStatus = string.Empty;

    /// <summary>Where the rows are going: the active connection and the lane that carries them (U9).</summary>
    [ObservableProperty] private string _destinationStatus = string.Empty;

    /// <summary>True while a source read is in flight.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Bottom panel collapse state (§3.1 band G).</summary>
    [ObservableProperty] private bool _isBottomPanelCollapsed;

    /// <summary>
    /// Remembered height of the bottom panel, in pixels. It lives on the VM rather than in the view because
    /// the import tab is transient — the view is gone before the workspace is written — and it is persisted
    /// globally (<c>WorkspaceState.ImportPreviewPanelHeight</c>), the way the SQL editor's results panel is.
    /// </summary>
    [ObservableProperty] private double _bottomPanelHeight = 190;

    [RelayCommand]
    private void ToggleBottomPanel() => IsBottomPanelCollapsed = !IsBottomPanelCollapsed;

    /// <summary>
    /// Manual expand/collapse of the format options. A manual toggle always wins over any automatic
    /// collapsing — an automat that fights the user is worse than none (§2.2 point 2) — so opening them by
    /// hand pins them open until they are closed again.
    /// </summary>
    [RelayCommand]
    private void ToggleFormatOptions()
    {
        Source.IsExpanded = !Source.IsExpanded;
        _formatOptionsHeldOpen = Source.IsExpanded;
    }

    // ── Source commands ─────────────────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (FilePickRequested is null) return;

        var path = await FilePickRequested.Invoke().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;

        Source.UseFile = true;
        Source.FilePath = path;   // raises Changed → recalculation
    }

    [RelayCommand]
    private async Task UseClipboardAsync()
    {
        if (ClipboardReadRequested is null) return;

        var text = await ClipboardReadRequested.Invoke().ConfigureAwait(true);
        Source.ClipboardText = text ?? string.Empty;
        Source.UseFile = false;
    }

    /// <summary>A readiness chip was clicked — expand and focus the section that caused it. The advantage
    /// over a wizard: every gap is visible AND reachable in one click (§3.2).</summary>
    [RelayCommand]
    private void FocusSection(ImportSection section)
    {
        // Only the FORMAT chip opens something: the source and target pickers are always live, so those
        // findings have nothing to expand — they just need the caret put in the picker.
        if (section is ImportSection.Format)
        {
            Source.IsExpanded = true;
            _formatOptionsHeldOpen = true;
        }
        else if (section is ImportSection.Mapping)
        {
            // A mapping finding is about a specific column; showing only the unmapped ones puts the user in
            // front of exactly the rows the strip is complaining about.
            Mapping.ShowOnlyUnmapped = true;
        }

        SectionFocusRequested?.Invoke(this, section);
    }

    /// <summary>
    /// ⭐ U11 — the format options collapse themselves once they are settled, which is what makes the repeat
    /// import cheap (§2.2): the picker stays live, so the next file is one click and <c>F5</c>, and the
    /// options the user set months ago do not occupy the surface for the rest of the session.
    /// <para>
    /// Deliberately conservative about when it may act: only after a source has actually been read (fields
    /// exist), and never when the user has opened the options by hand.
    /// </para>
    /// </summary>
    private void AutoCollapseFormatOptionsIfSettled()
    {
        if (_formatOptionsHeldOpen) return;
        if (_schema is null || _schema.Fields.Count == 0) return;

        Source.IsExpanded = false;
    }

    // ── The one translation point (§4.8.6) ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Assembles the current UI state into the ONE record.
    /// <para>
    /// Sections that do not exist yet (Target, Mapping, Behavior — etaps I6/I7) pass their part through from
    /// the held configuration unchanged, so a restored profile keeps decisions this build cannot yet edit
    /// rather than silently dropping them.
    /// </para>
    /// </summary>
    public ImportConfiguration BuildConfiguration()
    {
        var descriptor = Source.BuildSource(ResolveFileKind(Source.FilePath));
        var isSpreadsheet = descriptor.Kind is ImportSourceKind.Xlsx or ImportSourceKind.Xls;

        return _configuration with
        {
            Source = descriptor,
            // Exactly one options block is set, matching the source kind — the invariant
            // ImportConfiguration.MatchesSourceKind checks and readiness reports.
            Delimited = isSpreadsheet ? null : Source.BuildDelimited(),
            Spreadsheet = isSpreadsheet ? _configuration.Spreadsheet ?? new SpreadsheetOptions() : null,
            Culture = Source.BuildCulture(),
            Target = Target.BuildTarget(_configuration.Target),
            // The grid is authoritative once it has rows; before that the held mapping passes through, so a
            // restored profile keeps its pairing until the target it refers to has actually been read.
            Mapping = Mapping.Rows.Count > 0 ? Mapping.BuildMapping() : _configuration.Mapping,
            Behavior = Target.BuildBehavior(_configuration.Behavior),
        };
    }

    /// <summary>Loads a configuration into the surface — the path a restored profile takes (§4.8.5).</summary>
    public void ApplyConfiguration(ImportConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        Source.Apply(_configuration);
        Target.Apply(_configuration);
        Recalculate();
    }

    /// <summary>The configuration as it currently stands. Test seam and the future profile-save source.</summary>
    public ImportConfiguration CurrentConfiguration => _configuration;

    // ── The recalculation chain (§4.7) ──────────────────────────────────────────────────────────────────

    private void QueueRecalculate() => Recalculate();

    private void Recalculate()
    {
        _configuration = BuildConfiguration();

        // A newer change cancels the in-flight read rather than racing it — the CTS idiom the editor's
        // language service uses.
        _recalculation?.Cancel();
        _recalculation?.Dispose();
        var cts = new CancellationTokenSource();
        _recalculation = cts;

        UpdateFileFacts();
        PendingRecalculation = RunChainAsync(cts.Token);
    }

    /// <summary>
    /// The chain of §4.7, in the one order that is not a guess resting on a guess: <b>source → target →
    /// mapping → readiness</b>. Each link consumes what the previous one established, and every link is
    /// cancellable, so a newer edit supersedes the whole tail rather than racing it.
    /// </summary>
    private async Task RunChainAsync(CancellationToken cancellationToken)
    {
        await EnsureTablesLoadedAsync(cancellationToken).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested) return;

        await ReadSourceAsync(cancellationToken).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested) return;

        await ReadTargetAsync(cancellationToken).ConfigureAwait(true);
        if (cancellationToken.IsCancellationRequested) return;

        PlanMapping();
        PublishReadiness();
    }

    /// <summary>Loads the table list once per tab. The list is a fact about the database, not a user decision,
    /// so re-reading it on every keystroke would be work nobody asked for.</summary>
    private async Task EnsureTablesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_tablesLoaded || _listTablesAsync is null || !_isConnected()) return;

        try
        {
            var tables = await _listTablesAsync(cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested) return;

            Target.ShowTables(tables);
            _tablesLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // Superseded — not a failure.
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            SetStatus(ex.Message, MessageSeverity.Error);
        }
    }

    /// <summary>
    /// Reads the chosen table's columns and BEFORE INSERT triggers. Refusing with a reason is §0-compliant;
    /// pretending a table exists is not, so a target that cannot be read becomes a null target and readiness
    /// says so.
    /// </summary>
    private async Task ReadTargetAsync(CancellationToken cancellationToken)
    {
        var tableName = _configuration.Target.TableName;

        if (_readTargetAsync is null || string.IsNullOrWhiteSpace(tableName))
        {
            _target = null;
            Target.ShowFacts(null);
            return;
        }

        Target.IsBusy = true;
        try
        {
            _target = await _readTargetAsync(tableName, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested) return;

            Target.ShowFacts(_target);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
        {
            _target = null;
            Target.ShowFacts(null);
            SetStatus(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) Target.IsBusy = false;
        }
    }

    /// <summary>
    /// Re-plans the mapping through <see cref="ImportMappingPlanner"/> — the ONE owner of "which field feeds
    /// which column". The previous mapping is handed in so everything provably still correct survives; a
    /// changed TARGET hands in nothing, because a different table is a different identity (§4.7).
    /// </summary>
    private void PlanMapping()
    {
        if (_target is null || _schema is null)
        {
            // ⚠ Clear the GRID, never the record. There is nothing to show without both sides, but the held
            // mapping is a set of user decisions: dropping it here would mean a restored profile loses its
            // pairing merely because the target has not been read back yet — the "an older build quietly
            // robbed the profile" defect §4.8.6 exists to prevent. Re-choosing the same table restores it.
            Mapping.Update(null, _schema, ImportMappingPlan.Empty);
            return;
        }

        var previous = string.Equals(_mappedTable, _target.TableName, StringComparison.OrdinalIgnoreCase)
            ? _configuration.Mapping
            : null;

        var plan = ImportMappingPlanner.Plan(_target, _schema, previous);
        _mappedTable = _target.TableName;

        Mapping.Update(_target, _schema, plan);
        _configuration = _configuration with { Mapping = Mapping.BuildMapping() };
    }

    /// <summary>The table the current grid belongs to — the fact that decides whether a previous mapping may
    /// be carried over at all.</summary>
    private string? _mappedTable;

    /// <summary>A grid edit changes the record and the readiness, but nothing upstream: the source has not
    /// moved, so re-reading it would be work the user did not ask for.</summary>
    private void OnMappingEdited()
    {
        _configuration = BuildConfiguration();
        PublishReadiness();
    }

    /// <summary>Re-plans with a different strategy, then adopts the result into the existing rows so the grid
    /// does not jump under the user.</summary>
    private void ApplyMappingStrategy(ImportMappingStrategy strategy)
    {
        if (_target is null) return;

        var plan = strategy switch
        {
            ImportMappingStrategy.ByPosition when _schema is not null
                => ImportMappingPlanner.MatchByPosition(_target, _schema),
            ImportMappingStrategy.Clear => ImportMappingPlanner.Clear(_target),
            _ => null,
        };

        if (plan is null) return;

        Mapping.AdoptPlan(plan);
        _configuration = BuildConfiguration();
        PublishReadiness();
    }

    /// <summary>
    /// The in-flight recalculation, so a test can await the chain instead of sleeping on it.
    /// <para>
    /// Kept as a real field rather than fire-and-forget because "start work and hope" is untestable, and an
    /// untestable path is one nobody has tested (gotcha #251).
    /// </para>
    /// </summary>
    internal Task? PendingRecalculation { get; private set; }

    private async Task ReadSourceAsync(CancellationToken cancellationToken)
    {
        var configuration = _configuration;
        var source = TryCreateSource(configuration);

        if (source is null)
        {
            _schema = null;
            _sourceReadable = true;   // nothing to read is not "unreadable"
            return;                   // the chain still runs on — a target can be chosen before a file is
        }

        IsBusy = true;
        try
        {
            // Detection runs FIRST and writes into the declared values, because those are what the reader
            // then uses (§0.4 — the detector proposes, it does not maintain a second hidden setting).
            using (Source.SuspendChangeNotifications())
            {
                await RunDetectionAsync(source, cancellationToken).ConfigureAwait(true);
            }
            if (cancellationToken.IsCancellationRequested) return;

            // Re-assemble with whatever detection settled on, so schema and preview are read exactly the way
            // the user now sees the section configured.
            _configuration = BuildConfiguration();
            configuration = _configuration;

            var schema = await _delimitedProvider
                .ReadSchemaAsync(source, configuration, cancellationToken)
                .ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested) return;

            _schema = schema;
            _sourceReadable = true;
            SetStatus(string.Empty, MessageSeverity.Info);
            await LoadPreviewAsync(source, configuration, schema, cancellationToken).ConfigureAwait(true);
            if (!cancellationToken.IsCancellationRequested) AutoCollapseFormatOptionsIfSettled();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer change — not a failure, and nothing to report.
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _schema = null;
            _sourceReadable = false;
            SetStatus(ex.Message, MessageSeverity.Error);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested) IsBusy = false;
        }
    }

    private async Task LoadPreviewAsync(
        IImportSource source,
        ImportConfiguration configuration,
        SourceSchema schema,
        CancellationToken cancellationToken)
    {
        PreviewRows.Clear();
        PreviewFields.Clear();
        foreach (var field in schema.Fields) PreviewFields.Add(field);
        PreviewSchemaChanged?.Invoke(this, EventArgs.Empty);

        var taken = 0;
        await foreach (var record in _delimitedProvider
                           .ReadRecordsAsync(source, configuration, cancellationToken)
                           .ConfigureAwait(true))
        {
            PreviewRows.Add(new ImportSourceRecordRowViewModel(record));
            if (++taken >= SourcePreviewRows) break;
        }

        MarkRaggedRows(PreviewRows);
    }

    /// <summary>
    /// Flags the records that disagree with the rest of the file about how many fields there are.
    /// <para>
    /// The reference is the MOST COMMON width, not the schema's. The schema reports the WIDEST record so that
    /// every column the file contains stays mappable — but using that as the reference here would invert the
    /// signal: a single row with one extra field would set the width and mark every other row as the odd one.
    /// The useful statement is "this row disagrees with the others", and the majority is what "the others"
    /// means.
    /// </para>
    /// </summary>
    internal static void MarkRaggedRows(IReadOnlyList<ImportSourceRecordRowViewModel> rows)
    {
        if (rows.Count == 0) return;

        var counts = new Dictionary<int, int>();
        foreach (var row in rows)
        {
            counts.TryGetValue(row.Values.Length, out var seen);
            counts[row.Values.Length] = seen + 1;
        }

        var common = 0;
        var best = -1;
        foreach (var pair in counts)
        {
            // Ties go to the wider shape: if half the file has 3 fields and half has 2, the 2-field rows are
            // the ones missing something.
            if (pair.Value > best || (pair.Value == best && pair.Key > common))
            {
                best = pair.Value;
                common = pair.Key;
            }
        }

        foreach (var row in rows) row.IsRagged = row.Values.Length != common;
    }

    /// <summary>
    /// Runs the detectors and writes their proposals into the DECLARED values, with the evidence beside them.
    /// A proposal that cannot say why it was made is a silent decision, which §0.4 forbids.
    /// <para>
    /// Order matters and is not incidental: the ENCODING is proposed from raw bytes first, because the
    /// delimiter can only be looked for in text that has already been decoded — proposing a delimiter from
    /// mis-decoded bytes would be a guess resting on a guess.
    /// </para>
    /// </summary>
    private async Task RunDetectionAsync(IImportSource source, CancellationToken cancellationToken)
    {
        var delimited = _configuration.Delimited;
        if (delimited is null) return;

        if (delimited.AutoDetectEncoding && source is FileImportSource file)
        {
            var proposal = EncodingDetector.Propose(file.ReadDetectionSample());
            Source.ApplyEncodingProposal(proposal.CharsetName, DescribeEncodingBasis(proposal));
            delimited = Source.BuildDelimited();
        }

        if (!delimited.AutoDetectDelimiter) return;

        var sample = await ReadTextSampleAsync(source, delimited, cancellationToken).ConfigureAwait(true);
        if (sample.Length == 0) return;

        var delimiterProposal = DelimiterDetector.Propose(sample, delimited);
        if (delimiterProposal is null) return;

        Source.ApplyDelimiterProposal(
            delimiterProposal.Delimiter,
            string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportDelimiterEvidenceFormat,
                delimiterProposal.ConsistentRecords,
                delimiterProposal.SampledRecords,
                delimiterProposal.FieldCount));
    }

    /// <summary>Reads a bounded head of the source as text, for the delimiter detector. Bounded because a
    /// detector needs a sample, not a file (design R8).</summary>
    private static async Task<string> ReadTextSampleAsync(
        IImportSource source, DelimitedOptions options, CancellationToken cancellationToken)
    {
        using var reader = await source
            .OpenTextAsync(EmberTern.Core.Connections.CharsetCatalog.Resolve(options.EncodingName), cancellationToken)
            .ConfigureAwait(true);

        var buffer = new char[DetectionSampleChars];
        var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length).ConfigureAwait(true);
        return read <= 0 ? string.Empty : new string(buffer, 0, read);
    }

    /// <summary>Characters of the source handed to the delimiter detector.</summary>
    private const int DetectionSampleChars = 64 * 1024;

    private static string DescribeEncodingBasis(EncodingProposal proposal) => proposal.Basis switch
    {
        EncodingDetectionBasis.ByteOrderMark => UiStrings.ImportEncodingEvidenceBom,
        EncodingDetectionBasis.AsciiOnly => UiStrings.ImportEncodingEvidenceAscii,
        _ => UiStrings.ImportEncodingEvidenceHeuristic,
    };

    private void PublishReadiness()
    {
        var input = new ImportReadinessInput
        {
            Configuration = _configuration,
            Schema = _schema,
            SourceExists = _sourceExists,
            SourceReadable = _sourceReadable,
            Target = _target,
            IsConnected = _isConnected(),
            HasOpenUserTransaction = _hasOpenUserTransaction(),
        };

        Readiness.Update(ImportReadiness.Evaluate(input), PreviewRows.Count);
        UpdateSurfaceStatus();
    }

    private void UpdateSurfaceStatus()
    {
        UpdateDestinationStatus();

        if (_schema is null || _schema.Fields.Count == 0)
        {
            SurfaceStatus = UiStrings.ImportSurfaceStatusNoSource;
            return;
        }

        SurfaceStatus = string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.ImportSurfaceStatusFormat,
            _schema.Fields.Count,
            PreviewRows.Count,
            PreviewRows.Count >= SourcePreviewRows ? UiStrings.ImportSurfaceStatusMore : string.Empty);
    }

    /// <summary>
    /// ⭐ Band H's left half — <b>where the rows are going and on which connection lane</b> (U9). It used to
    /// sit in a header band that otherwise only repeated the tab's own title; moved here because this is the
    /// line that answers "where does this land", and in I7 the transaction mode joins it.
    /// <para>
    /// The lane is a constant on purpose: rows always go to the <b>Data</b> lane as the ONE user working
    /// transaction (§4.5). Saying so out loud is the point — a module that writes to a database should not
    /// make the user guess which transaction it joins.
    /// </para>
    /// </summary>
    private void UpdateDestinationStatus()
    {
        var connection = _connectionName();

        DestinationStatus = connection.Length == 0
            ? UiStrings.ImportDestinationNotConnected
            : string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportDestinationFormat,
                connection,
                UiStrings.ImportDestinationDataLane);
    }

    private void UpdateFileFacts()
    {
        if (!Source.UseFile || Source.FilePath.Length == 0)
        {
            _sourceExists = true;
            Source.FileFacts = string.Empty;
            return;
        }

        try
        {
            var info = new FileInfo(Source.FilePath);
            _sourceExists = info.Exists;
            Source.FileFacts = info.Exists
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    UiStrings.ImportFileFactsFormat,
                    info.Length / 1024d,
                    info.LastWriteTime)
                : UiStrings.ImportFileMissing;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _sourceExists = false;
            Source.FileFacts = UiStrings.ImportFileMissing;
        }
    }

    private IImportSource? TryCreateSource(ImportConfiguration configuration)
    {
        if (configuration.Source.Kind == ImportSourceKind.Clipboard)
        {
            return Source.ClipboardText.Length == 0 ? null : new TextImportSource(Source.ClipboardText);
        }

        var path = configuration.Source.Path;
        if (string.IsNullOrWhiteSpace(path) || !_sourceExists) return null;

        // A spreadsheet has no provider until etap I9. Refusing with a reason is §0-compliant; pretending to
        // read it would not be.
        if (configuration.Source.Kind is ImportSourceKind.Xlsx or ImportSourceKind.Xls)
        {
            SetStatus(
                string.Format(CultureInfo.CurrentCulture, UiStrings.ImportFormatNotYetSupportedFormat, Path.GetExtension(path)),
                MessageSeverity.Warning);
            _sourceReadable = false;
            return null;
        }

        return new FileImportSource(path);
    }

    private void SetStatus(string message, MessageSeverity severity)
    {
        StatusMessage = message;
        StatusSeverity = severity;
    }

    /// <summary>Extension → source kind. The picker shows the resolved kind, so an automatic decision is
    /// visible and overridable rather than silent.</summary>
    internal static ImportSourceKind ResolveFileKind(string? path)
    {
        var extension = string.IsNullOrWhiteSpace(path) ? string.Empty : Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" => ImportSourceKind.Xlsx,
            ".xls" => ImportSourceKind.Xls,
            ".txt" => ImportSourceKind.Text,
            _ => ImportSourceKind.Csv,
        };
    }
}

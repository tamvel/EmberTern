using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Export;
using EmberTern.App.Sql;
using EmberTern.Core.Export;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The one shared Export dialog. Format-driven progressive disclosure: CSV/Text reveal
/// delimiter + encoding + culture + header and prompt a Save-file dialog; Clipboard shows only the
/// header toggle and copies straight to the clipboard (no location prompt — the format IS the
/// destination). Streams via <see cref="ExportService"/> with a live row counter + Cancel; on
/// success returns an <see cref="ExportOutcome"/> and closes.
/// </summary>
public sealed partial class ExportDialogViewModel : ViewModelBase
{
    private readonly IExportDataSource _source;
    private readonly ExportService _exportService = new();
    private readonly IReadOnlyList<DelimiterOption> _delimiterOptions;
    private CancellationTokenSource? _cts;

    public ExportDialogViewModel(IExportDataSource source, ExportScope defaultScope)
    {
        _source = source;

        _delimiterOptions = new[]
        {
            new DelimiterOption(';', UiStrings.ExportDelimiterSemicolon),
            new DelimiterOption(',', UiStrings.ExportDelimiterComma),
            new DelimiterOption('|', UiStrings.ExportDelimiterPipe),
            new DelimiterOption('\t', UiStrings.ExportDelimiterTab),
        };

        ScopeOptions = source.Capabilities.Scopes
            .Select(s => new ExportScopeOptionViewModel(this, s, ScopeLabel(s)))
            .ToList();

        SelectedScope = source.Capabilities.Supports(defaultScope)
            ? defaultScope
            : source.Capabilities.Scopes.Count > 0 ? source.Capabilities.Scopes[0] : ExportScope.AllRows;

        // Default format = CSV; seed its idiomatic options (';' + BOM). OnSelectedFormatChanged keeps
        // them in step when the user switches format.
        _selectedDelimiterOption = _delimiterOptions.First(d => d.Value == ';');
        _useBom = true;
    }

    /// <summary>Set by the window: opens a Save-file picker (file formats), returns the path or null.</summary>
    public Func<SaveFileRequest, Task<string?>>? RequestSavePath { get; set; }

    /// <summary>Set by the window: writes the given text to the clipboard (Clipboard format).</summary>
    public Func<string, Task>? WriteClipboard { get; set; }

    /// <summary>The completed export, or null when the dialog was cancelled/dismissed.</summary>
    public ExportOutcome? Result { get; private set; }

    public event Action? RequestClose;

    public string DialogTitle => UiStrings.ExportDialogTitle;

    public IReadOnlyList<ExportScopeOptionViewModel> ScopeOptions { get; }

    public IReadOnlyList<DelimiterOption> DelimiterOptions => _delimiterOptions;

    // ── Format (radio group) ───────────────────────────────────────────────
    [ObservableProperty]
    private ExportFormat _selectedFormat = ExportFormat.Csv;

    public bool IsFormatCsv
    {
        get => SelectedFormat == ExportFormat.Csv;
        set { if (value) SelectedFormat = ExportFormat.Csv; }
    }

    public bool IsFormatText
    {
        get => SelectedFormat == ExportFormat.Text;
        set { if (value) SelectedFormat = ExportFormat.Text; }
    }

    public bool IsFormatClipboard
    {
        get => SelectedFormat == ExportFormat.Clipboard;
        set { if (value) SelectedFormat = ExportFormat.Clipboard; }
    }

    partial void OnSelectedFormatChanged(ExportFormat value)
    {
        OnPropertyChanged(nameof(IsFormatCsv));
        OnPropertyChanged(nameof(IsFormatText));
        OnPropertyChanged(nameof(IsFormatClipboard));
        OnPropertyChanged(nameof(ShowDelimitedOptions));
        OnPropertyChanged(nameof(ShowEncodingOption));

        // Each format carries its idiomatic defaults (CSV → ';' + BOM for Excel; Text → TAB, no BOM).
        switch (value)
        {
            case ExportFormat.Csv:
                SelectedDelimiterOption = _delimiterOptions.First(d => d.Value == ';');
                UseBom = true;
                break;
            case ExportFormat.Text:
                SelectedDelimiterOption = _delimiterOptions.First(d => d.Value == '\t');
                UseBom = false;
                break;
        }
    }

    // ── Scope (radio group) ────────────────────────────────────────────────
    [ObservableProperty]
    private ExportScope _selectedScope;

    partial void OnSelectedScopeChanged(ExportScope value)
    {
        foreach (var o in ScopeOptions) o.NotifySelectionChanged();
    }

    // ── Options ────────────────────────────────────────────────────────────
    [ObservableProperty]
    private DelimiterOption _selectedDelimiterOption;

    [ObservableProperty]
    private bool _includeHeader = true;

    [ObservableProperty]
    private bool _useBom;

    [ObservableProperty]
    private bool _useInvariantCulture;

    // CSV / Text reveal the delimiter + culture; Clipboard shows only the header toggle.
    public bool ShowDelimitedOptions => SelectedFormat is ExportFormat.Csv or ExportFormat.Text;

    // Encoding (BOM) only matters for a written file.
    public bool ShowEncodingOption => SelectedFormat is ExportFormat.Csv or ExportFormat.Text;

    // ── Progress / state ───────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfiguring))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private bool _isExporting;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool IsConfiguring => !IsExporting;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public bool CanExport => !IsExporting;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        ErrorMessage = null;
        var request = BuildRequest();

        string? path = null;
        Encoding? encoding = null;
        if (SelectedFormat is ExportFormat.Csv or ExportFormat.Text)
        {
            if (RequestSavePath is not { } askPath) return;
            var ext = FileExtension;
            path = await askPath(new SaveFileRequest(
                UiStrings.ExportDialogTitle,
                _source.Capabilities.DefaultBaseFileName + ext,
                FileFilterName,
                ext)).ConfigureAwait(true);
            if (string.IsNullOrEmpty(path)) return; // picker cancelled → stay in the options view
            encoding = BuildEncoding();
        }

        _cts = new CancellationTokenSource();
        IsExporting = true;
        ProgressText = UiStrings.ExportPreparing;
        var progress = new Progress<long>(n =>
            ProgressText = string.Format(CultureInfo.CurrentCulture, UiStrings.ExportProgressFormat, n));

        try
        {
            long rows;
            if (SelectedFormat == ExportFormat.Clipboard)
            {
                var (count, text) = await _exportService
                    .ExportToClipboardTextAsync(_source, request, progress, _cts.Token)
                    .ConfigureAwait(true);
                if (WriteClipboard is { } write) await write(text).ConfigureAwait(true);
                rows = count;
            }
            else
            {
                rows = await _exportService
                    .ExportToFileAsync(_source, request, path!, encoding!, progress, _cts.Token)
                    .ConfigureAwait(true);
            }

            Result = new ExportOutcome(SelectedFormat, SelectedScope, rows, path);
            RequestClose?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // Keep the dialog open in its options view so the user can retry or cancel out.
            ProgressText = string.Empty;
            IsExporting = false;
        }
        catch (Exception ex) when (ex is QueryExecutionException or IOException or UnauthorizedAccessException)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, UiStrings.ExportErrorFormat, ex.Message);
            IsExporting = false;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsExporting)
        {
            _cts?.Cancel();
            return;
        }
        Result = null;
        RequestClose?.Invoke();
    }

    private string FileExtension => SelectedFormat == ExportFormat.Text ? ".txt" : ".csv";

    private string FileFilterName => SelectedFormat == ExportFormat.Text
        ? UiStrings.ExportTextFilterName
        : UiStrings.ExportCsvFilterName;

    private Encoding BuildEncoding()
        => UseBom ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true) : SqlFileWriter.Utf8NoBom;

    private ExportRequest BuildRequest() => new()
    {
        Format = SelectedFormat,
        Scope = SelectedScope,
        Delimited = SelectedFormat is ExportFormat.Csv or ExportFormat.Text
            ? new DelimitedTextOptions(SelectedDelimiterOption.Value, IncludeHeader, UseInvariantCulture)
            : null,
        IncludeHeader = IncludeHeader,
    };

    private string ScopeLabel(ExportScope scope)
    {
        var baseLabel = scope switch
        {
            ExportScope.CurrentView => UiStrings.ExportScopeCurrentView,
            ExportScope.AllRows => UiStrings.ExportScopeAllRows,
            ExportScope.SelectedRows => UiStrings.ExportScopeSelected,
            _ => scope.ToString(),
        };
        var est = _source.Capabilities.EstimateFor(scope);
        if (est.Count is long c)
        {
            var fmt = est.IsApproximate ? UiStrings.ExportScopeCountApproxFormat : UiStrings.ExportScopeCountFormat;
            return baseLabel + " " + string.Format(CultureInfo.CurrentCulture, fmt, c);
        }
        return baseLabel;
    }
}

/// <summary>A CSV/TXT delimiter choice (value + human label).</summary>
public sealed record DelimiterOption(char Value, string Label);

/// <summary>One scope radio in the Export dialog; <see cref="IsSelected"/> is two-way bound and drives
/// the owner's <see cref="ExportDialogViewModel.SelectedScope"/>.</summary>
public sealed class ExportScopeOptionViewModel : ViewModelBase
{
    private readonly ExportDialogViewModel _owner;

    public ExportScopeOptionViewModel(ExportDialogViewModel owner, ExportScope scope, string label)
    {
        _owner = owner;
        Scope = scope;
        Label = label;
    }

    public ExportScope Scope { get; }
    public string Label { get; }

    public bool IsSelected
    {
        get => _owner.SelectedScope == Scope;
        set { if (value) _owner.SelectedScope = Scope; }
    }

    internal void NotifySelectionChanged() => OnPropertyChanged(nameof(IsSelected));
}

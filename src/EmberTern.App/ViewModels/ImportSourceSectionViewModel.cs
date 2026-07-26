using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Import;

namespace EmberTern.App.ViewModels;

/// <summary>One entry of a character-valued picker (delimiter, quote, separators). <c>null</c> means "none".</summary>
public sealed record ImportCharOption(string Display, char? Value);

/// <summary>One entry of the file-encoding picker. The vocabulary is Firebird's charset names, because
/// <c>CharsetCatalog</c> is the codebase's one owner of "charset name → Encoding" and the connection profile
/// already speaks it.</summary>
public sealed record ImportEncodingOption(string Display, string CharsetName);

/// <summary>One entry of an enum-valued picker.</summary>
public sealed record ImportChoiceOption<T>(string Display, T Value);

/// <summary>
/// The <b>Source and format</b> section (§3.3): where the data comes from and how the text is read.
/// <para>
/// ⭐ <b>It does not own the configuration.</b> <see cref="DataImportTabViewModel"/> holds the one
/// <see cref="ImportConfiguration"/>; this VM reads its own slice out of it (<see cref="Apply"/>) and produces
/// a new slice on demand (<see cref="BuildDelimited"/> / <see cref="BuildSource"/> / <see cref="BuildCulture"/>).
/// That is §4.8.6's rule, and it is the reason named profiles can arrive in I11 as pure UI: a setting that
/// lives only here, in an <c>[ObservableProperty]</c>, would be invisible to a saved profile — which is exactly
/// the rebuild the design refuses to sign up for.
/// </para>
/// <para>
/// <b>Auto-detection PROPOSES, never decides</b> (§0.4). The detectors fill the declared value and publish the
/// evidence behind it ("240/240 records have the same field count"), so an automatic choice can be read and
/// overruled rather than merely obeyed.
/// </para>
/// </summary>
public sealed partial class ImportSourceSectionViewModel : ViewModelBase
{
    private bool _suspendChangeNotification;

    public ImportSourceSectionViewModel()
    {
        Apply(ImportConfiguration.Empty);
    }

    /// <summary>Raised whenever a user decision here changes, so the coordinator can re-run the chain
    /// (§4.7). Deliberately one event for the whole section: the coordinator recomputes everything to the
    /// right of the change anyway, so a per-property signal would buy nothing and could drift.</summary>
    public event EventHandler? Changed;

    // ── Where the data comes from ───────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileSource))]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private bool _useFile = true;

    /// <summary>True when the source is a file; false means the clipboard.</summary>
    public bool IsFileSource => UseFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    [NotifyPropertyChangedFor(nameof(FileDisplayName))]
    private string _filePath = string.Empty;

    /// <summary>File name for the collapsed summary — never the whole path, which would swamp the line.</summary>
    public string FileDisplayName
        => string.IsNullOrWhiteSpace(FilePath) ? string.Empty : Path.GetFileName(FilePath);

    /// <summary>Size + last-write line shown beside the picker; empty when the file is unreachable.</summary>
    [ObservableProperty] private string _fileFacts = string.Empty;

    /// <summary>Clipboard text, held only while the surface is open — it is NEVER part of the configuration
    /// (§4.8.2: a profile stores decisions, not data).</summary>
    [ObservableProperty] private string _clipboardText = string.Empty;

    // ── Parsing (delimited variant) ─────────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private ImportCharOption _delimiter = DefaultDelimiter;

    [ObservableProperty] private bool _autoDetectDelimiter = true;

    /// <summary>What the detector saw, in the user's own terms. An automatic decision that cannot explain
    /// itself builds no trust (§8 point 11).</summary>
    [ObservableProperty] private string _delimiterEvidence = string.Empty;

    [ObservableProperty] private ImportCharOption _quote = DefaultQuote;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private ImportEncodingOption _encoding = DefaultEncoding;

    [ObservableProperty] private bool _autoDetectEncoding = true;

    [ObservableProperty] private string _encodingEvidence = string.Empty;

    [ObservableProperty] private ImportChoiceOption<LineEndingMode> _lineEnding = LineEndingOptions[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private bool _hasHeader = true;

    [ObservableProperty] private int _firstDataRow = 2;

    /// <summary>Empty means "to the end". <b>Never</b> <c>2147483647</c> — an implementation detail in the UI
    /// is exactly what §8 point 7 criticises in the tool being replaced.</summary>
    [ObservableProperty] private string _lastRowText = string.Empty;

    [ObservableProperty] private bool _trimWhitespace;

    [ObservableProperty] private string _nullToken = string.Empty;

    // ── Culture ─────────────────────────────────────────────────────────────────────────────────────────

    [ObservableProperty] private ImportCharOption _decimalSeparator = DecimalSeparatorOptions[0];
    [ObservableProperty] private ImportCharOption _thousandsSeparator = ThousandsSeparatorOptions[0];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryText))]
    private ImportChoiceOption<DateFieldOrder> _dateOrder = DateOrderOptions[0];

    [ObservableProperty] private ImportCharOption _dateSeparator = DateSeparatorOptions[0];
    [ObservableProperty] private ImportCharOption _timeSeparator = TimeSeparatorOptions[0];

    // ── Presentation state ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Expanded while the section is incomplete; a manual toggle always wins over the automatic
    /// one, because an automat that fights the user is worse than none (§2.2 point 2).</summary>
    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>The collapsed one-liner: <c>Fantomy.xlsx · WIN1250 · ";" · DMY</c>. The whole point of the
    /// surface — an expert reads the entire configuration without opening anything (§2.2 point 1).</summary>
    public string SummaryText
    {
        get
        {
            var parts = new List<string>(5);
            parts.Add(UseFile
                ? (FileDisplayName.Length > 0 ? FileDisplayName : UiStrings.ImportSourceNoFile)
                : UiStrings.ImportSourceClipboard);

            if (UseFile) parts.Add(Encoding.Display);
            parts.Add(string.Format(CultureInfo.CurrentCulture, UiStrings.ImportSummaryDelimiterFormat, Delimiter.Display));
            parts.Add(DateOrder.Display);
            if (!HasHeader) parts.Add(UiStrings.ImportSummaryNoHeader);

            return string.Join(" · ", parts);
        }
    }

    // ── Option lists ────────────────────────────────────────────────────────────────────────────────────

    public static ImportCharOption DefaultDelimiter { get; } = new(";", ';');
    public static ImportCharOption DefaultQuote { get; } = new("\"", '"');
    public static ImportEncodingOption DefaultEncoding { get; } = new("WIN1250", "WIN1250");

    public IReadOnlyList<ImportCharOption> DelimiterOptions { get; } = new[]
    {
        DefaultDelimiter,
        new ImportCharOption(",", ','),
        new ImportCharOption(UiStrings.ImportDelimiterTab, '\t'),
        new ImportCharOption("|", '|'),
    };

    public IReadOnlyList<ImportCharOption> QuoteOptions { get; } = new[]
    {
        DefaultQuote,
        new ImportCharOption("'", '\''),
    };

    /// <summary>UTF-16 is offered because a FILE's byte-order mark can legitimately say so — those names are
    /// not Firebird CONNECTION charsets, which is why <c>CharsetCatalog.Supported</c> does not list them and
    /// this list does.</summary>
    public IReadOnlyList<ImportEncodingOption> EncodingOptions { get; } = new[]
    {
        DefaultEncoding,
        new ImportEncodingOption("UTF8", "UTF8"),
        new ImportEncodingOption("WIN1252", "WIN1252"),
        new ImportEncodingOption("ISO8859_1", "ISO8859_1"),
        new ImportEncodingOption("ISO8859_2", "ISO8859_2"),
        new ImportEncodingOption("UTF-16 LE", "UTF16LE"),
        new ImportEncodingOption("UTF-16 BE", "UTF16BE"),
    };

    public static IReadOnlyList<ImportChoiceOption<LineEndingMode>> LineEndingOptions { get; } = new[]
    {
        new ImportChoiceOption<LineEndingMode>(UiStrings.ImportLineEndingAuto, LineEndingMode.Auto),
        new ImportChoiceOption<LineEndingMode>("CRLF", LineEndingMode.Crlf),
        new ImportChoiceOption<LineEndingMode>("LF", LineEndingMode.Lf),
        new ImportChoiceOption<LineEndingMode>("CR", LineEndingMode.Cr),
    };

    public static IReadOnlyList<ImportChoiceOption<DateFieldOrder>> DateOrderOptions { get; } = new[]
    {
        new ImportChoiceOption<DateFieldOrder>("DMY", DateFieldOrder.Dmy),
        new ImportChoiceOption<DateFieldOrder>("MDY", DateFieldOrder.Mdy),
        new ImportChoiceOption<DateFieldOrder>("YMD", DateFieldOrder.Ymd),
        new ImportChoiceOption<DateFieldOrder>("ISO (yyyy-MM-dd)", DateFieldOrder.Iso),
    };

    public static IReadOnlyList<ImportCharOption> DecimalSeparatorOptions { get; } = new[]
    {
        new ImportCharOption(",", ','),
        new ImportCharOption(".", '.'),
    };

    public static IReadOnlyList<ImportCharOption> ThousandsSeparatorOptions { get; } = new[]
    {
        new ImportCharOption(UiStrings.ImportSeparatorNone, null),
        new ImportCharOption(UiStrings.ImportSeparatorSpace, ' '),
        new ImportCharOption(".", '.'),
        new ImportCharOption(",", ','),
    };

    public static IReadOnlyList<ImportCharOption> DateSeparatorOptions { get; } = new[]
    {
        new ImportCharOption(".", '.'),
        new ImportCharOption("-", '-'),
        new ImportCharOption("/", '/'),
    };

    public static IReadOnlyList<ImportCharOption> TimeSeparatorOptions { get; } = new[]
    {
        new ImportCharOption(":", ':'),
        new ImportCharOption(".", '.'),
    };

    // ── The one translation point (§4.8.6) ──────────────────────────────────────────────────────────────

    /// <summary>This section's slice of the source descriptor.</summary>
    public SourceDescriptor BuildSource(ImportSourceKind fileKind)
        => UseFile && FilePath.Length > 0
            ? SourceDescriptor.File(fileKind, FilePath)
            : SourceDescriptor.Clipboard();

    public DelimitedOptions BuildDelimited() => new()
    {
        Delimiter = Delimiter.Value ?? ';',
        AutoDetectDelimiter = AutoDetectDelimiter,
        Quote = Quote.Value ?? '"',
        EncodingName = Encoding.CharsetName,
        AutoDetectEncoding = AutoDetectEncoding,
        LineEnding = LineEnding.Value,
        HasHeader = HasHeader,
        FirstDataRow = Math.Max(1, FirstDataRow),
        LastRow = ParseLastRow(LastRowText),
        TrimWhitespace = TrimWhitespace,
        NullToken = NullToken,
    };

    public ImportCultureOptions BuildCulture() => new()
    {
        DecimalSeparator = DecimalSeparator.Value ?? ',',
        ThousandsSeparator = ThousandsSeparator.Value,
        DateOrder = DateOrder.Value,
        DateSeparator = DateSeparator.Value ?? '.',
        TimeSeparator = TimeSeparator.Value ?? ':',
    };

    /// <summary>
    /// Loads this section from a configuration — the other half of the round trip, and the path a restored
    /// profile takes (§4.8.5).
    /// <para>
    /// Notifications are suspended for the duration: applying a stored configuration is not a user decision,
    /// and letting each assignment fire <see cref="Changed"/> would kick off a dozen recalculation chains for
    /// one load.
    /// </para>
    /// </summary>
    public void Apply(ImportConfiguration configuration)
    {
        if (configuration is null) throw new ArgumentNullException(nameof(configuration));

        _suspendChangeNotification = true;
        try
        {
            UseFile = configuration.Source.Kind != ImportSourceKind.Clipboard;
            FilePath = configuration.Source.Path ?? string.Empty;

            var delimited = configuration.Delimited ?? new DelimitedOptions();
            Delimiter = Match(DelimiterOptions, delimited.Delimiter, DefaultDelimiter);
            AutoDetectDelimiter = delimited.AutoDetectDelimiter;
            Quote = Match(QuoteOptions, delimited.Quote, DefaultQuote);
            Encoding = MatchEncoding(delimited.EncodingName);
            AutoDetectEncoding = delimited.AutoDetectEncoding;
            LineEnding = Match(LineEndingOptions, delimited.LineEnding);
            HasHeader = delimited.HasHeader;
            FirstDataRow = delimited.FirstDataRow;
            LastRowText = delimited.LastRow?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
            TrimWhitespace = delimited.TrimWhitespace;
            NullToken = delimited.NullToken;

            var culture = configuration.Culture;
            DecimalSeparator = Match(DecimalSeparatorOptions, culture.DecimalSeparator, DecimalSeparatorOptions[0]);
            ThousandsSeparator = MatchNullable(ThousandsSeparatorOptions, culture.ThousandsSeparator);
            DateOrder = Match(DateOrderOptions, culture.DateOrder);
            DateSeparator = Match(DateSeparatorOptions, culture.DateSeparator, DateSeparatorOptions[0]);
            TimeSeparator = Match(TimeSeparatorOptions, culture.TimeSeparator, TimeSeparatorOptions[0]);
        }
        finally
        {
            _suspendChangeNotification = false;
        }

        OnPropertyChanged(nameof(SummaryText));
    }

    /// <summary>
    /// Silences <see cref="Changed"/> for the duration of the returned scope.
    /// <para>
    /// Needed because a detector writes its proposal into the DECLARED values, and a declared value changing
    /// is normally a user decision that restarts the recalculation chain — which, during a recalculation,
    /// would be an infinite loop. Suspending says what is true: this assignment came from the chain, not from
    /// the user.
    /// </para>
    /// </summary>
    public IDisposable SuspendChangeNotifications()
    {
        _suspendChangeNotification = true;
        return new NotificationScope(this);
    }

    private sealed class NotificationScope : IDisposable
    {
        private readonly ImportSourceSectionViewModel _owner;
        public NotificationScope(ImportSourceSectionViewModel owner) => _owner = owner;
        public void Dispose() => _owner._suspendChangeNotification = false;
    }

    /// <summary>Writes a detector's proposal into the declared value and records the evidence. The proposal
    /// becomes the value the reader uses — there is no second, hidden "detected" setting (§0.4).</summary>
    public void ApplyDelimiterProposal(char delimiter, string evidence)
    {
        Delimiter = Match(DelimiterOptions, delimiter, DefaultDelimiter);
        DelimiterEvidence = evidence;
    }

    public void ApplyEncodingProposal(string charsetName, string evidence)
    {
        Encoding = MatchEncoding(charsetName);
        EncodingEvidence = evidence;
    }

    /// <summary>Nullable row bound: blank / unparsable / non-positive all mean "to the end".</summary>
    internal static int? ParseLastRow(string text)
        => int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value > 0
            ? value
            : null;

    // Every observable property routes here (CommunityToolkit generates the partial hook), so one method
    // decides what counts as a user decision. Assignments made by Apply are suspended.
    partial void OnUseFileChanged(bool value) => RaiseChanged();
    partial void OnFilePathChanged(string value) => RaiseChanged();
    partial void OnClipboardTextChanged(string value) => RaiseChanged();
    partial void OnDelimiterChanged(ImportCharOption value) => RaiseChanged();
    partial void OnAutoDetectDelimiterChanged(bool value) => RaiseChanged();
    partial void OnQuoteChanged(ImportCharOption value) => RaiseChanged();
    partial void OnEncodingChanged(ImportEncodingOption value) => RaiseChanged();
    partial void OnAutoDetectEncodingChanged(bool value) => RaiseChanged();
    partial void OnLineEndingChanged(ImportChoiceOption<LineEndingMode> value) => RaiseChanged();
    partial void OnHasHeaderChanged(bool value) => RaiseChanged();
    partial void OnFirstDataRowChanged(int value) => RaiseChanged();
    partial void OnLastRowTextChanged(string value) => RaiseChanged();
    partial void OnTrimWhitespaceChanged(bool value) => RaiseChanged();
    partial void OnNullTokenChanged(string value) => RaiseChanged();
    partial void OnDecimalSeparatorChanged(ImportCharOption value) => RaiseChanged();
    partial void OnThousandsSeparatorChanged(ImportCharOption value) => RaiseChanged();
    partial void OnDateOrderChanged(ImportChoiceOption<DateFieldOrder> value) => RaiseChanged();
    partial void OnDateSeparatorChanged(ImportCharOption value) => RaiseChanged();
    partial void OnTimeSeparatorChanged(ImportCharOption value) => RaiseChanged();

    private void RaiseChanged()
    {
        if (_suspendChangeNotification) return;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static ImportCharOption Match(
        IReadOnlyList<ImportCharOption> options, char value, ImportCharOption fallback)
    {
        foreach (var option in options)
        {
            if (option.Value == value) return option;
        }
        return fallback;
    }

    private static ImportCharOption MatchNullable(IReadOnlyList<ImportCharOption> options, char? value)
    {
        foreach (var option in options)
        {
            if (option.Value == value) return option;
        }
        return options[0];
    }

    private static ImportChoiceOption<T> Match<T>(IReadOnlyList<ImportChoiceOption<T>> options, T value)
        where T : struct
    {
        foreach (var option in options)
        {
            if (EqualityComparer<T>.Default.Equals(option.Value, value)) return option;
        }
        return options[0];
    }

    private ImportEncodingOption MatchEncoding(string? charsetName)
    {
        foreach (var option in EncodingOptions)
        {
            if (string.Equals(option.CharsetName, charsetName, StringComparison.OrdinalIgnoreCase)) return option;
        }
        return DefaultEncoding;
    }
}

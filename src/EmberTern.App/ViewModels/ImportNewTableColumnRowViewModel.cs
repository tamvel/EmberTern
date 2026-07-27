using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Export.Sql;
using EmberTern.Core.Import;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One row of the new-table type grid (§3.4): a column that is about to be CREATED.
/// <para>
/// ⭐ <b>Editable, not merely shown.</b> The inference is a proposal, and §0.3 requires the result to be
/// <em>shown and editable before the DDL runs</em> — because once the <c>CREATE</c> has run it is committed and
/// a Rollback cannot take it back (§0.5 / gotcha #213). This grid is the last moment a wrong type costs
/// nothing, which is precisely why the correction has to be possible here rather than afterwards.
/// </para>
/// <para>
/// <b>"Basis" is not decoration.</b> It is the answer to "where did that come from" — the number of rows
/// analysed, the longest value seen, and for a column that fell back to text, the value that decided it. R19
/// measured that mixed columns are the norm rather than the exception, so the fallback needs a reason the user
/// can check against their own file rather than looking arbitrary.
/// </para>
/// <para>
/// Size/Scale enablement comes from the shared <see cref="FieldTypeRules"/> — the same rules every other field
/// grid in the application uses, so "does VARCHAR take a size" has one answer (§4.6).
/// </para>
/// </summary>
public sealed partial class ImportNewTableColumnRowViewModel : ViewModelBase
{
    private readonly Action? _onChanged;
    private bool _suspend;

    /// <summary>The types offered. Deliberately the set <see cref="ImportTargetType"/> can write, and no more:
    /// offering a type the import would then refuse to fill would be a menu item that cannot work.</summary>
    public static IReadOnlyList<string> AvailableTypes { get; } = new[]
    {
        "VARCHAR", "CHAR", "INTEGER", "BIGINT", "SMALLINT", "NUMERIC", "DECIMAL",
        "DOUBLE PRECISION", "DATE", "TIME", "TIMESTAMP", "BOOLEAN", "BLOB",
    };

    /// <summary>The same list, reachable from a row's own DataContext — a compiled binding resolves instance
    /// members, so the grid's picker needs an instance door onto the shared list.</summary>
    public IReadOnlyList<string> TypeOptions => AvailableTypes;

    public ImportNewTableColumnRowViewModel(InferredColumn inferred, Action? onChanged)
        : this(
            (inferred ?? throw new ArgumentNullException(nameof(inferred))).Definition,
            DescribeBasis(inferred.Evidence),
            onChanged)
    {
    }

    public ImportNewTableColumnRowViewModel(ImportColumnDefinition definition, string basis, Action? onChanged)
    {
        if (definition is null) throw new ArgumentNullException(nameof(definition));

        _onChanged = onChanged;
        _suspend = true;
        try
        {
            _name = definition.Name;
            _type = string.IsNullOrWhiteSpace(definition.BasicType) ? "VARCHAR" : definition.BasicType;
            _size = definition.Size;
            _scale = definition.Scale;
            _blobSubType = definition.BlobSubType;
            _isNullable = !definition.NotNull;
        }
        finally
        {
            _suspend = false;
        }

        Basis = basis;
    }

    /// <summary>Why this row has the type it has. Empty for a row the user added by hand — there is no evidence
    /// behind a decision the user made, and inventing one would be worse than saying nothing.</summary>
    public string Basis { get; }

    [ObservableProperty] private string _name = string.Empty;

    private bool _settingNameUpper;

    partial void OnNameChanged(string value)
    {
        // Identifiers live in catalog UPPERCASE, exactly as the New Table grid coerces them — otherwise the
        // quoted CREATE would build a lower-case column the catalog then reports under a different name than
        // the mapping expects. Re-entrancy guard, same shape as NewTableFieldRowViewModel.
        if (_settingNameUpper) return;

        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, StringComparison.Ordinal))
        {
            _settingNameUpper = true;
            try { Name = upper; } finally { _settingNameUpper = false; }
            return;
        }

        Raise();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSizeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsScaleEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSubTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(TypeText))]
    private string _type = "VARCHAR";

    partial void OnTypeChanged(string value)
    {
        // Changing to a type that carries no argument clears the now-meaningless cell, so a stale 20 cannot
        // linger behind an INTEGER and reappear if the user switches back. Same fix as the New Table grid.
        if (!FieldTypeRules.UsesSize(value) && Size is not null) Size = null;
        if (!FieldTypeRules.UsesScale(value) && Scale is not null) Scale = null;
        if (!FieldTypeRules.UsesSubType(value) && BlobSubType is not null) BlobSubType = null;

        // A text type with no length is not a type — give the user the smallest usable default rather than
        // emitting a DDL Firebird refuses.
        if (FieldTypeRules.UsesSize(value) && Size is null) Size = DefaultSizeFor(value);
        if (FieldTypeRules.UsesSubType(value) && BlobSubType is null) BlobSubType = 1;

        Raise();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeText))]
    private int? _size;

    partial void OnSizeChanged(int? value) => Raise();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeText))]
    private int? _scale;

    partial void OnScaleChanged(int? value) => Raise();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TypeText))]
    private int? _blobSubType;

    partial void OnBlobSubTypeChanged(int? value) => Raise();

    /// <summary>Shown as "NULL" rather than "NOT NULL" because that is the column caption in §3.4's sketch, and
    /// because a ticked box reading "this may be empty" is the way round the user thinks about a file.</summary>
    [ObservableProperty] private bool _isNullable = true;

    partial void OnIsNullableChanged(bool value) => Raise();

    public bool IsSizeEnabled => FieldTypeRules.UsesSize(Type);

    public bool IsScaleEnabled => FieldTypeRules.UsesScale(Type);

    public bool IsSubTypeEnabled => FieldTypeRules.UsesSubType(Type);

    /// <summary>The declared type as it will appear in the DDL — rendered by the ONE owner, so the row cannot
    /// show one thing and the CREATE emit another.</summary>
    public string TypeText => ImportNewTable.TypeText(Build());

    /// <summary>This row as the record's own shape. The grid is presentation; <see cref="ImportColumnDefinition"/>
    /// is the decision, and it reaches <c>ImportConfiguration</c> through the coordinator's one translation
    /// point (§4.8.6).</summary>
    public ImportColumnDefinition Build() => new()
    {
        Name = Name.Trim(),
        BasicType = string.IsNullOrWhiteSpace(Type) ? "VARCHAR" : Type,
        Size = Size,
        Scale = Scale,
        BlobSubType = BlobSubType,
        NotNull = !IsNullable,
    };

    private void Raise()
    {
        if (_suspend) return;
        OnPropertyChanged(nameof(TypeText));
        _onChanged?.Invoke();
    }

    private static int? DefaultSizeFor(string type)
        => FieldTypeRules.UsesScale(type) ? 18 : 50;

    // ── The basis sentence ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Turns the evidence into the "Basis" cell. Core produced codes and numbers; the sentence is App's job
    /// (rule #6), and it is composed here so every row phrases it identically.
    /// </summary>
    internal static string DescribeBasis(ColumnInferenceEvidence evidence)
    {
        if (evidence is null) return string.Empty;

        if (evidence.ValuesSeen == 0)
        {
            return UiStrings.ImportNewTableBasisNoValues;
        }

        if (evidence.IsMixed)
        {
            // ⭐ R19. The value that ended the candidate, and the row it is on — so the user can open their
            // file at that line rather than take the module's word for it (§0.6).
            return string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportNewTableBasisMixedFormat,
                DescribeKind(evidence.RejectedKind),
                evidence.RejectedAtRow,
                Shorten(evidence.RejectedByValue),
                evidence.MaxTextLength);
        }

        if (evidence.ChosenKind is SqlValueKind.Text or SqlValueKind.TextBlob)
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.ImportNewTableBasisTextFormat,
                evidence.ValuesSeen,
                evidence.MaxTextLength);
        }

        return string.Format(
            CultureInfo.CurrentCulture,
            UiStrings.ImportNewTableBasisMatchedFormat,
            evidence.ValuesSeen,
            DescribeKind(evidence.ChosenKind));
    }

    private static string DescribeKind(SqlValueKind kind) => kind switch
    {
        SqlValueKind.Integer => UiStrings.ImportNewTableKindInteger,
        SqlValueKind.Decimal => UiStrings.ImportNewTableKindDecimal,
        SqlValueKind.Date => UiStrings.ImportNewTableKindDate,
        SqlValueKind.Timestamp => UiStrings.ImportNewTableKindTimestamp,
        SqlValueKind.Time => UiStrings.ImportNewTableKindTime,
        SqlValueKind.Boolean => UiStrings.ImportNewTableKindBoolean,
        _ => UiStrings.ImportNewTableKindText,
    };

    /// <summary>Keeps one wild value from pushing the rest of the sentence off the row.</summary>
    private static string Shorten(string? value)
    {
        const int max = 24;
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value.Substring(0, max) + "…";
    }
}

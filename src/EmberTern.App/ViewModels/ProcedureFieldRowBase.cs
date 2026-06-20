using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Shared rich field-definition row for a procedure's Input / Output parameter and
/// Variable grids (Easy mode). Reuses the table field-definition infrastructure — the
/// same <see cref="DomainSpec"/> list, <see cref="BasicTypes"/>, and
/// <see cref="DdlGenerator.FormatTypeOrDomain"/> — so there is no second type system.
///
/// The structured editors (Type combo, Domain combo, TYPE OF, Size, Scale, Sub Type,
/// Charset, Collate) compose the canonical <see cref="TypeText"/>. <see cref="TypeText"/>
/// is the round-trip source of truth: it stays exactly as loaded until the user edits a
/// structured field, at which point it is recomposed — so any Firebird type form (incl.
/// ones the structured editors can't fully model) survives a load with no information loss.
/// </summary>
public abstract partial class ProcedureFieldRowBase : ObservableObject
{
    private readonly IFieldRowOwner? _owner;
    private bool _suppressCompose;
    // Guards the "mirror the domain's resolved type into the Type/Size/Scale cells for
    // display" writes so the BaseType setter doesn't treat them as the user picking a
    // plain base type (which would clear the domain).
    private bool _syncingType;

    protected ProcedureFieldRowBase(IFieldRowOwner? owner)
    {
        _owner = owner;
        if (_owner is not null)
        {
            _owner.AvailableDomains.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(SelectedDomainSpec));
                // Domains load asynchronously after the rows are built — once the list
                // arrives, resolve a domain-typed row's Type cell so it isn't blank.
                if (!string.IsNullOrEmpty(DomainName)) SyncTypeDisplayFromDomain(DomainName!);
            };
        }
    }

    // ─── Name (catalog UPPERCASE) ─────────────────────────────────────────

    [ObservableProperty] private string _name = string.Empty;

    private bool _settingNameUpper;
    partial void OnNameChanged(string value)
    {
        if (_settingNameUpper) return;
        var upper = value?.ToUpperInvariant() ?? string.Empty;
        if (!string.Equals(value, upper, StringComparison.Ordinal))
        {
            _settingNameUpper = true;
            try { Name = upper; } finally { _settingNameUpper = false; }
        }
    }

    // ─── Canonical type text (round-trip source of truth) ─────────────────

    [ObservableProperty] private string _typeText = "INTEGER";

    // ─── Structured type editors (compose into TypeText) ──────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTypeItem))]
    private string? _baseType = "INTEGER";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDomainSpec))]
    [NotifyPropertyChangedFor(nameof(HasDomain))]
    [NotifyPropertyChangedFor(nameof(IsTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsTypeOfEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSizeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsScaleEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSubTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCharsetEnabled))]
    private string? _domainName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTypeOf))]
    [NotifyPropertyChangedFor(nameof(IsTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSizeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsScaleEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSubTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCharsetEnabled))]
    private string _typeOf = string.Empty;

    [ObservableProperty] private int? _size;
    [ObservableProperty] private int? _scale;
    [ObservableProperty] private string _subType = string.Empty;
    [ObservableProperty] private string _charset = string.Empty;
    [ObservableProperty] private string _collate = string.Empty;

    // ─── Per-cell enable gates (#4) ───────────────────────────────────────
    // A domain (or TYPE OF) governs the type, so the type-construction cells
    // are disabled — kept visible (no empty columns), matching the table editor.
    public bool HasDomain => !string.IsNullOrWhiteSpace(DomainName);
    public bool HasTypeOf => !string.IsNullOrWhiteSpace(TypeOf);
    public bool IsTypeEnabled => !HasDomain && !HasTypeOf;
    public bool IsTypeOfEnabled => !HasDomain;
    public bool IsSizeEnabled => !HasDomain && !HasTypeOf;
    public bool IsScaleEnabled => !HasDomain && !HasTypeOf;
    public bool IsSubTypeEnabled => !HasDomain && !HasTypeOf;
    public bool IsCharsetEnabled => !HasDomain && !HasTypeOf;

    [ObservableProperty] private bool _notNull;
    [ObservableProperty] private string _defaultValue = string.Empty;
    [ObservableProperty] private string _description = string.Empty;

    partial void OnBaseTypeChanged(string? value)
    {
        // _syncingType means WE set the base type from the selected domain (for display);
        // the user did NOT pick a plain type, so don't clear the domain.
        if (_suppressCompose || _syncingType) return;
        if (!string.IsNullOrWhiteSpace(value)) { _domainName = null; _typeOf = string.Empty; OnPropertyChanged(nameof(SelectedDomainSpec)); OnPropertyChanged(nameof(TypeOf)); }
        Recompose();
    }

    partial void OnDomainNameChanged(string? value)
    {
        OnPropertyChanged(nameof(SelectedDomainSpec));
        if (_suppressCompose) return;
        if (!string.IsNullOrWhiteSpace(value))
        {
            _typeOf = string.Empty;
            OnPropertyChanged(nameof(TypeOf));
            // Mirror the domain's resolved type into the Type/Size/Scale cells so the
            // Type column shows the effective type instead of going blank. ComposeType
            // still returns the domain NAME (DomainName wins), so the canonical type is
            // the domain — the display sync is informational only.
            SyncTypeDisplayFromDomain(value!);
        }
        Recompose();
    }

    partial void OnTypeOfChanged(string value)
    {
        if (_suppressCompose || _syncingType) return;
        if (!string.IsNullOrWhiteSpace(value)) { _baseType = null; _domainName = null; OnPropertyChanged(nameof(BaseType)); OnPropertyChanged(nameof(SelectedDomainSpec)); }
        Recompose();
    }

    partial void OnSizeChanged(int? value) { if (!_suppressCompose && !_syncingType) Recompose(); }
    partial void OnScaleChanged(int? value) { if (!_suppressCompose && !_syncingType) Recompose(); }
    partial void OnSubTypeChanged(string value) { if (!_suppressCompose && !_syncingType) Recompose(); }
    partial void OnCharsetChanged(string value) { if (!_suppressCompose && !_syncingType) Recompose(); }
    partial void OnCollateChanged(string value) { if (!_suppressCompose && !_syncingType) Recompose(); }

    private void Recompose() => TypeText = ComposeType();

    // Fills the Type / Size / Scale cells from the selected domain's resolved type
    // (e.g. domain T_KODPOCZ → VARCHAR(6)) for display, WITHOUT clearing DomainName.
    private void SyncTypeDisplayFromDomain(string domain)
    {
        string? resolved = null;
        foreach (var d in AvailableDomains)
        {
            if (string.Equals(d.Name, domain, StringComparison.OrdinalIgnoreCase)) { resolved = d.Type; break; }
        }
        if (string.IsNullOrWhiteSpace(resolved)) return; // domain list not loaded yet
        var (b, a1, a2) = SplitBaseAndArgs(resolved!.Trim());
        _syncingType = true;
        try
        {
            BaseType = string.IsNullOrEmpty(b) ? null : b.ToUpperInvariant();
            Size = a1;
            Scale = a2;
        }
        finally { _syncingType = false; }
    }

    /// <summary>Builds the full Firebird type spec from the structured fields.</summary>
    private string ComposeType()
    {
        string core;
        if (!string.IsNullOrWhiteSpace(DomainName))
        {
            core = DomainName!.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(TypeOf))
        {
            core = "TYPE OF " + TypeOf.Trim();
        }
        else
        {
            var b = (BaseType ?? string.Empty).Trim().ToUpperInvariant();
            BlobSubType? blob = null;
            if (b == "BLOB" && int.TryParse(SubType.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var st))
                blob = (BlobSubType)st;
            core = DdlGenerator.FormatTypeOrDomain(new FieldDefinition
            {
                BasicType = b,
                Size = b is "CHAR" or "VARCHAR" or "CSTRING" ? Size : null,
                Precision = b is "NUMERIC" or "DECIMAL" ? Size : null,
                Scale = b is "NUMERIC" or "DECIMAL" ? Scale : null,
                BlobSubType = blob,
            });
        }

        var sb = new StringBuilder(core);
        if (!string.IsNullOrWhiteSpace(Charset) && string.IsNullOrWhiteSpace(DomainName) && string.IsNullOrWhiteSpace(TypeOf))
            sb.Append(" CHARACTER SET ").Append(Charset.Trim());
        if (!string.IsNullOrWhiteSpace(Collate))
            sb.Append(" COLLATE ").Append(Collate.Trim());
        return sb.ToString();
    }

    /// <summary>Loads a raw type spec into the structured editors for display WITHOUT
    /// recomposing (so <see cref="TypeText"/> keeps the exact loaded text — no loss).</summary>
    protected void LoadType(string? raw)
    {
        _suppressCompose = true;
        try
        {
            var t = (raw ?? string.Empty).Trim();
            TypeText = t;
            BaseType = null; DomainName = null; TypeOf = string.Empty;
            Size = null; Scale = null; SubType = string.Empty; Charset = string.Empty; Collate = string.Empty;

            // Trailing COLLATE.
            var collateIx = IndexOfWord(t, "COLLATE");
            if (collateIx >= 0) { Collate = t.Substring(collateIx + 7).Trim(); t = t.Substring(0, collateIx).Trim(); }
            // CHARACTER SET.
            var csIx = IndexOfWord(t, "CHARACTER SET");
            if (csIx >= 0) { Charset = t.Substring(csIx + 13).Trim(); t = t.Substring(0, csIx).Trim(); }

            if (StartsWithWord(t, "TYPE OF"))
            {
                TypeOf = t.Substring(7).Trim();
                return;
            }

            // BASE, BASE(a), BASE(a,b), or BLOB SUB_TYPE n.
            var (baseTok, a, b) = SplitBaseAndArgs(t);
            var up = baseTok.ToUpperInvariant();
            if (up == "BLOB")
            {
                BaseType = "BLOB";
                var stIx = IndexOfWord(t, "SUB_TYPE");
                if (stIx >= 0) SubType = FirstToken(t.Substring(stIx + 8));
            }
            else if (IsKnownBasicType(up))
            {
                BaseType = up;
                if (up is "NUMERIC" or "DECIMAL") { Size = a; Scale = b; }
                else if (up is "CHAR" or "VARCHAR" or "CSTRING") Size = a;
            }
            else
            {
                // Unknown base — most likely a domain. Show it in the Domain column;
                // ComposeType returns it verbatim, so it round-trips.
                DomainName = baseTok;
            }
        }
        finally
        {
            _suppressCompose = false;
        }
    }

    // ─── Type combo wrapper (null-safe for the filtering picker) ──────────
    // The Type picker's items (BasicTypes) carry base type names only. Binding
    // SelectedItem straight to BaseType lets a partial-typed filter (no exact
    // match yet) write null back and clear the type. This wrapper ignores null,
    // so typing-to-filter never corrupts the value.
    public string? SelectedTypeItem
    {
        get
        {
            if (string.IsNullOrEmpty(BaseType)) return null;
            foreach (var t in BasicTypes)
                if (string.Equals(t, BaseType, StringComparison.OrdinalIgnoreCase)) return t;
            return null;
        }
        set
        {
            if (string.IsNullOrEmpty(value)) return; // filter-in-progress clobber — ignore
            if (string.Equals(value, BaseType, StringComparison.OrdinalIgnoreCase)) return;
            BaseType = value;
        }
    }

    // ─── Domain combo wrapper (Avalonia 12 has no SelectedValueBinding) ────

    public ObservableCollection<DomainSpec> AvailableDomains => _owner?.AvailableDomains ?? FallbackDomains;
    public IReadOnlyList<string> BasicTypes => _owner?.BasicTypes ?? FallbackBasicTypes;

    public DomainSpec? SelectedDomainSpec
    {
        get
        {
            if (string.IsNullOrEmpty(DomainName)) return FindNoneSentinel();
            foreach (var d in AvailableDomains)
                if (string.Equals(d.Name, DomainName, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }
        set
        {
            if (value is null) return; // load-time clobber — ignore
            DomainName = string.Equals(value.Name, UiStrings.DomainNoneOption, StringComparison.Ordinal)
                ? null
                : value.Name;
        }
    }

    private DomainSpec? FindNoneSentinel()
    {
        foreach (var d in AvailableDomains)
            if (string.Equals(d.Name, UiStrings.DomainNoneOption, StringComparison.Ordinal)) return d;
        return null;
    }

    // ─── Parse helpers ─────────────────────────────────────────────────────

    private static bool IsKnownBasicType(string upper) => upper is
        "SMALLINT" or "INTEGER" or "BIGINT" or "FLOAT" or "DOUBLE PRECISION" or "DOUBLE"
        or "NUMERIC" or "DECIMAL" or "CHAR" or "VARCHAR" or "CSTRING"
        or "DATE" or "TIME" or "TIMESTAMP" or "BOOLEAN";

    private static (string Base, int? A, int? B) SplitBaseAndArgs(string t)
    {
        var open = t.IndexOf('(');
        if (open < 0) return (t.Trim(), null, null);
        var close = t.IndexOf(')', open + 1);
        var baseTok = t.Substring(0, open).Trim();
        if (close < 0) return (baseTok, null, null);
        var inner = t.Substring(open + 1, close - open - 1);
        var parts = inner.Split(',');
        int? a = null, b = null;
        if (parts.Length > 0 && int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pa)) a = pa;
        if (parts.Length > 1 && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pb)) b = pb;
        return (baseTok, a, b);
    }

    private static int IndexOfWord(string s, string word)
        => s.IndexOf(word, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithWord(string s, string word)
        => s.TrimStart().StartsWith(word, StringComparison.OrdinalIgnoreCase);

    private static string FirstToken(string s)
    {
        s = s.Trim();
        int i = 0;
        while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
        return s.Substring(0, i);
    }

    protected string EffectiveDefault => string.IsNullOrWhiteSpace(DefaultValue) ? string.Empty : DefaultValue.Trim();

    private static readonly ObservableCollection<DomainSpec> FallbackDomains = new();
    private static readonly IReadOnlyList<string> FallbackBasicTypes = new[]
    {
        "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR",
        "DATE", "TIME", "TIMESTAMP", "BLOB", "BOOLEAN",
    };
}

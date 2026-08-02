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
public abstract partial class ProcedureFieldRowBase : ObservableObject, ITypeSourceRow
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
                OnPropertyChanged(nameof(SelectedTypeSource));
                // Domains load asynchronously after the rows are built — once the list
                // arrives, resolve a domain-typed row's Type cell so it isn't blank.
                if (!string.IsNullOrEmpty(DomainName)) SyncTypeDisplayFromDomain(DomainName!, adoptNotNull: false);
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
    [NotifyPropertyChangedFor(nameof(SelectedTypeSource))]
    [NotifyPropertyChangedFor(nameof(TypeSourceDisplay))]
    [NotifyPropertyChangedFor(nameof(HasDomain))]
    [NotifyPropertyChangedFor(nameof(IsTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsTypeOfEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSizeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsScaleEnabled))]
    [NotifyPropertyChangedFor(nameof(IsSubTypeEnabled))]
    [NotifyPropertyChangedFor(nameof(IsCharsetEnabled))]
    private string? _domainName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedTypeSource))]
    [NotifyPropertyChangedFor(nameof(TypeSourceDisplay))]
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
        // Drop args that don't apply to the new base type (e.g. VARCHAR→SMALLINT clears Size).
        if (!FieldTypeRules.UsesSize(value) && Size is not null) Size = null;
        if (!FieldTypeRules.UsesScale(value) && Scale is not null) Scale = null;
        if (!FieldTypeRules.UsesSubType(value) && !string.IsNullOrEmpty(SubType)) SubType = string.Empty;
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
            SyncTypeDisplayFromDomain(value!, adoptNotNull: true);
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

    // Fills the Type / Size / Scale / Sub Type / Charset / Not Null cells from the
    // selected domain's definition (e.g. domain T_KODPOCZ → VARCHAR(6)) for display,
    // WITHOUT clearing DomainName. ComposeType still returns the domain NAME (domain
    // wins), so this is informational only and never corrupts the generated DDL.
    /// <param name="adoptNotNull">
    /// ⚠⚠ Czy przejąć również <c>NOT NULL</c> domeny. <c>true</c> tylko wtedy, gdy użytkownik WŁAŚNIE
    /// WYBRAŁ domenę — wtedy przyjmuje jej atrybuty w komplecie. <c>false</c> przy WCZYTYWANIU
    /// istniejącej deklaracji: tam `NOT NULL` zostało już sparsowane z samej deklaracji i bywa własnym
    /// ustawieniem zmiennej, niezależnym od domeny. Nadpisanie go zmieniłoby zapisany kod użytkownika
    /// przy samym otwarciu edytora — a to reguła #11, nie kosmetyka (§19.8).
    /// </param>
    private void SyncTypeDisplayFromDomain(string domain, bool adoptNotNull)
    {
        DomainSpec? d = null;
        foreach (var x in AvailableDomains)
        {
            if (string.Equals(x.Name, domain, StringComparison.OrdinalIgnoreCase)) { d = x; break; }
        }
        if (d is null) return; // domain list not loaded yet
        _syncingType = true;
        try
        {
            BaseType = string.IsNullOrEmpty(d.BaseType) ? null : d.BaseType.ToUpperInvariant();
            Size = d.Size;
            Scale = d.Scale;
            SubType = ExtractSubType(d.Type);
            Charset = d.Charset ?? string.Empty;
            if (adoptNotNull) NotNull = d.NotNull;
        }
        finally { _syncingType = false; }
    }

    // "BLOB SUB_TYPE 1" → "1"; otherwise empty.
    private static string ExtractSubType(string type)
    {
        var ix = type.IndexOf("SUB_TYPE", StringComparison.OrdinalIgnoreCase);
        if (ix < 0) return string.Empty;
        var rest = type[(ix + 8)..].Trim();
        int i = 0;
        while (i < rest.Length && (char.IsLetterOrDigit(rest[i]) || rest[i] == '_')) i++;
        return rest[..i];
    }

    /// <summary>Builds the full Firebird type spec from the structured fields.</summary>
    private string ComposeType()
    {
        string core;
        if (!string.IsNullOrWhiteSpace(DomainName))
        {
            // Generated-DDL identifier style: a picked domain shows UPPERCASE (bare), even if the
            // catalog stored it lower-case — §0-safe (regular identifiers only; see PresentIdentifier).
            core = DdlGenerator.PresentIdentifier(DomainName);
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

                // ⭐⭐ …i od razu rozwiąż typ bazowy domeny do kolumn Type/Size/Scale/SubType/Charset.
                // ⚠ Bez tej linii kolumna Type zostawała PUSTA, a Size/Scale wyglądały jak brakujące
                // dane (§19.8). Mechanizm istniał w dwóch miejscach, ale żadne tu nie sięgało:
                // `OnDomainNameChanged` wychodzi na `_suppressCompose`, które `LoadType` właśnie trzyma,
                // a subskrypcja `AvailableDomains.CollectionChanged` ratowała sytuację TYLKO wtedy, gdy
                // lista domen dojeżdżała PO zbudowaniu wierszy. Przy połączeniu, w którym domeny były
                // już wczytane, kolekcja się nie zmieniała i nie odpalało się nic.
                // ⚠ `adoptNotNull: false` — to WCZYTANIE, nie wybór: `NOT NULL` pochodzi z deklaracji.
                SyncTypeDisplayFromDomain(baseTok, adoptNotNull: false);
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

    // ─── Merged Domain/Column picker (Faza 4) ─────────────────────────────
    // One "Domain / Column" cell replaces the separate Domain + TYPE OF columns.
    // Its two tabs commit either a DomainSpec (→ DomainName) or a ColumnRef (→ TypeOf,
    // which ComposeType emits as TYPE OF COLUMN). Domain and TYPE OF are mutually
    // exclusive — picking one clears the other (handled by the existing change hooks).

    /// <summary>Bound to the merged picker's SelectedItem (TwoWay). Reads/writes whichever
    /// of <see cref="DomainName"/> / <see cref="TypeOf"/> is active. The getter returns a
    /// non-null value whenever either is set so the picker's ✕ clear stays visible; a
    /// string fallback covers a TYPE OF form that isn't a column ref (never written back —
    /// the control only commits a DomainSpec/ColumnRef on pick, or null on clear).</summary>
    public object? SelectedTypeSource
    {
        get
        {
            if (!string.IsNullOrEmpty(DomainName)) return SelectedDomainSpec ?? (object?)DomainName;
            if (!string.IsNullOrWhiteSpace(TypeOf)) return ColumnRef.Parse(TypeOf) ?? (object)TypeOf;
            return null;
        }
        set
        {
            switch (value)
            {
                case DomainSpec d:
                    DomainName = d.Name;          // OnDomainNameChanged clears TypeOf
                    break;
                case ColumnRef c:
                    TypeOf = c.TypeOfClause;       // OnTypeOfChanged clears DomainName/BaseType
                    break;
                case null:
                    // ✕ clear → drop both type sources.
                    if (!string.IsNullOrEmpty(DomainName)) DomainName = null;
                    if (!string.IsNullOrWhiteSpace(TypeOf)) TypeOf = string.Empty;
                    break;
            }
        }
    }

    /// <summary>Closed-box text for the merged picker: the domain name, the column
    /// reference (TABLE.COLUMN, "COLUMN " prefix stripped), or empty.</summary>
    public string TypeSourceDisplay
    {
        get
        {
            if (!string.IsNullOrEmpty(DomainName)) return DdlGenerator.PresentIdentifier(DomainName);
            if (!string.IsNullOrWhiteSpace(TypeOf)) return ColumnRef.StripColumnPrefix(TypeOf);
            return string.Empty;
        }
    }

    /// <summary>Live table list + lazy column loader for the picker's "Table column" tab,
    /// forwarded from the owning editor (so columns are loaded on demand, never eagerly).</summary>
    public ObservableCollection<string> AvailableTables => _owner?.AvailableTables ?? FallbackTables;
    public IColumnsLoader? ColumnsLoader => _owner?.ColumnsLoader;

    public DomainSpec? SelectedDomainSpec
    {
        get
        {
            // Empty = no domain → empty field (SearchableComboBox shows the watermark).
            if (string.IsNullOrEmpty(DomainName)) return null;
            foreach (var d in AvailableDomains)
                if (string.Equals(d.Name, DomainName, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }
        // SearchableComboBox commits only on an explicit pick/clear, so null = the user
        // cleared (✕) → drop the domain; non-null = picked a domain.
        set => DomainName = value?.Name;
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
    private static readonly ObservableCollection<string> FallbackTables = new();
    private static readonly IReadOnlyList<string> FallbackBasicTypes = new[]
    {
        "SMALLINT", "INTEGER", "BIGINT", "FLOAT", "DOUBLE PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR",
        "DATE", "TIME", "TIMESTAMP", "BLOB", "BOOLEAN",
    };
}

using EmberTern.App.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.App.Settings;
using EmberTern.Core.Formatting;
using EmberTern.Core.Settings;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One legal value of an enumerated preference, ready to be rendered as a radio button or a ComboBox item.
/// <para>⚠ The <see cref="Key"/> is Core's (persisted, validated); the <see cref="Label"/> is
/// <c>UiStrings</c>'. Nothing here invents either — both come from the catalog.</para>
/// </summary>
public sealed partial class PreferenceOptionViewModel : ObservableObject
{
    private readonly PreferenceSettingViewModel _owner;

    internal PreferenceOptionViewModel(PreferenceSettingViewModel owner, string key)
    {
        _owner = owner;
        Key = key;
    }

    public string Key { get; }

    /// <summary>
    /// ⚠ Computed from the owner's descriptor rather than captured, for the same reason every other settings
    /// text is: an option label is a word, and a word moves with the language. "Dark"/"Light" are as visible
    /// on the General page as the row that offers them.
    /// </summary>
    public string Label => _owner.LabelFor(Key);

    internal void RefreshLocalizedText() => OnPropertyChanged(nameof(Label));

    /// <summary>Two-way for a <c>RadioButton</c>. ⚠ Only a transition to <c>true</c> is a user decision: a
    /// radio group unchecks its siblings, so acting on <c>false</c> as well would fight the group.</summary>
    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _owner.Value = Key;
        }
    }
}

/// <summary>
/// What every row on a settings page has in common: a caption, a sentence, a search haystack, and whether the
/// current search matched it.
/// <para>⭐ It exists because etap 5b added a row that is a <b>command</b> rather than a value (Import / export),
/// and search reads the whole catalog — so both kinds must be filterable by exactly the same rule. The
/// alternative was giving the action row a fake value.</para>
/// </summary>
public abstract partial class SettingRowViewModel : ObservableObject
{
    protected SettingRowViewModel(SettingDescriptor descriptor, string categoryTitle)
    {
        Id = descriptor.Id;
        CategoryId = descriptor.CategoryId;
    }

    public string Id { get; }

    public string CategoryId { get; }

    /// <summary>
    /// This row's own descriptor, in the language being rendered right now.
    ///
    /// <para>⚠⚠ <b>Resolved on every read, never captured.</b> The three texts below used to be assigned in the
    /// constructor, which froze this window's whole vocabulary at the language in force when it opened — and,
    /// because the catalog behind it froze at type-init, closing and reopening the window did not help either.
    /// The user saw it as a General page whose heading said "Ogólne" while every row under it stayed
    /// English.</para>
    /// </summary>
    private SettingDescriptor Descriptor => SettingsCatalog.Descriptor(Id);

    public string Label => Descriptor.Label;

    /// <summary>
    /// ⭐ <c>KeepNumbersWhole</c> is applied HERE, in the one place every row's description passes through, so
    /// it covers the texts that exist and the ones nobody has written yet — a description with a grouped number
    /// cannot wrap in the middle of it. See <c>ProseNumbers</c>: a space between two digits is a separator,
    /// never a word gap.
    /// </summary>
    public string Description => ProseNumbers.KeepNumbersWhole(Descriptor.Description);

    /// <summary>Hidden by a search that does not match it. The row stays in place; only its visibility
    /// changes, so nothing is rebuilt while the user types.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// Searching matches what is DISPLAYED plus the keywords that lead to it — and the category's own title,
    /// so typing "general" keeps the whole page rather than emptying it.
    ///
    /// <para>⚠ Built from the RAW description on purpose: the displayed one carries non-breaking spaces, so a
    /// user typing "1 000 000" with ordinary spaces would stop matching a row that plainly contains it.</para>
    /// </summary>
    internal string Haystack
    {
        get
        {
            var descriptor = Descriptor;
            return string.Join('\n',
                descriptor.Label,
                descriptor.Description,
                SettingsCatalog.Category(CategoryId).Title,
                string.Join(' ', descriptor.Keywords));
        }
    }

    /// <summary>Tells the row's bindings to re-read after a language change. Every text it shows is computed,
    /// so a blanket notification is the whole of it — except for a row that owns child view models of its own
    /// (see <see cref="PreferenceSettingViewModel"/>).</summary>
    internal virtual void RefreshLocalizedText() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// A row that offers commands instead of a value — today only Import / export settings.
/// <para>It holds no value, is never persisted, and deliberately has no arm in
/// <c>SettingsCenterViewModel.ValueOf</c> or <c>Compose</c>: apply-on-change is a property of preferences, and
/// an export is a deliberate action with its own dialog.</para>
/// </summary>
public sealed class SettingActionViewModel : SettingRowViewModel
{
    internal SettingActionViewModel(SettingDescriptor descriptor, string categoryTitle)
        : base(descriptor, categoryTitle)
    {
    }
}

/// <summary>
/// An on/off preference row, rendered as a checkbox.
/// <para>A checkbox is a <b>discrete</b> control, so it commits the moment it is clicked — the same rule
/// <see cref="PreferenceSettingViewModel"/> follows, and the reason neither needs the blur-or-Enter path that
/// <see cref="NumericSettingViewModel"/> does (design §5.5.1).</para>
/// <para>⚠ It is deliberately not a two-value <c>PreferenceOptionSet</c> rendered as radios. "On"/"Off" as
/// persisted option keys would put a second vocabulary for <c>true</c>/<c>false</c> into the settings file, and
/// a pair of radio buttons for a yes/no question is worse UX than the checkbox everyone already reads.</para>
/// </summary>
public sealed partial class BooleanSettingViewModel : SettingRowViewModel
{
    private bool _value;

    internal BooleanSettingViewModel(SettingDescriptor descriptor, string categoryTitle, bool value)
        : base(descriptor, categoryTitle)
    {
        _value = value;
    }

    /// <summary>Two-way for a <c>CheckBox</c>. Setting it is the user's decision, so it is settled.</summary>
    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when the user settled on a new value — the page's cue to persist.</summary>
    public event EventHandler? ValueChanged;
}

/// <summary>
/// A numeric preference row, rendered as a plain text field bounded by its Core
/// <see cref="PreferenceRange"/>.
///
/// <para>⭐ <b>This is the class §16.8 recorded as etap 6's debt: the blur-or-Enter commit path (design
/// §5.5.1).</b> <see cref="EditText"/> follows every keystroke and commits <b>nothing</b>; the view calls
/// <see cref="Commit"/> on lost focus and on Enter. The reason is not performance: every save does a full
/// read + decrypt + deserialize of <c>settings.dat</c> before rewriting it, and <c>TryAtomicWrite</c> keeps exactly
/// <b>one</b> generation of <c>settings.dat.bak</c> — so typing <c>5000</c> per-keystroke would roll the single
/// hand-recovery backup through four generations at precisely the moment someone is editing settings.</para>
///
/// <para>⚠ <b>Out of range clamps and the field shows the clamped value.</b> The store would clamp it anyway
/// (<see cref="PreferenceRange.Normalize"/>), so echoing the stored number back is the only honest option — a
/// field that kept displaying <c>50000000</c> over a stored <c>1000000</c> would be lying, and a validation
/// error on a page that applies on change has nowhere to live. Unparseable text reverts to the current
/// value.</para>
/// </summary>
public sealed partial class NumericSettingViewModel : SettingRowViewModel
{
    private readonly PreferenceRange _range;
    private int _value;
    private string _editText;

    internal NumericSettingViewModel(
        SettingDescriptor descriptor, string categoryTitle, PreferenceRange range, int value)
        : base(descriptor, categoryTitle)
    {
        _range = range;
        _value = range.Normalize(value);
        _editText = _value.ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>The settled value — what <c>Compose</c> reads. Never out of range.</summary>
    public int Value => _value;

    public int Minimum => _range.Minimum;

    public int Maximum => _range.Maximum;

    /// <summary>
    /// What the field currently shows — and the ONE gate on what may reach it.
    ///
    /// <para>⚠ Bound two-way per keystroke and deliberately NOT a commit: <see cref="Commit"/> is. Typing moves
    /// this; only blur or Enter settles it.</para>
    ///
    /// <para>⭐ <b>Text that could never be a number is refused here rather than tolerated and undone later.</b>
    /// Letting a letter land and silently reverting the whole entry on commit is the weaker behaviour: the user
    /// loses the digits they had already typed and is told nothing about why. Refusing the keystroke is the
    /// answer a numeric field is expected to give.</para>
    ///
    /// <para>⚠⚠ <b>It is deliberately TOLERANT — the refusal lives in the view, at the input boundary.</b>
    /// Vetoing here was tried and measured to fail on both halves: Avalonia's two-way binding ignores a
    /// <c>PropertyChanged</c> raised while it is pushing target → source, so the rejected text stayed on screen
    /// with the model disagreeing; and a veto would have made <b>paste</b> strictly worse, because
    /// <see cref="Commit"/> then finds the model already correct, changes nothing, notifies nothing, and the
    /// pasted junk never leaves the field. The property that the control writes to cannot also be the property
    /// that refuses the write.</para>
    /// </summary>
    public string EditText
    {
        get => _editText;
        set
        {
            var candidate = value ?? string.Empty;
            if (string.Equals(_editText, candidate, StringComparison.Ordinal)) return;

            _editText = candidate;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> may stand in the field — i.e. whether it already is, or could still
    /// become, a number this row can hold.
    ///
    /// <para>⭐ <b>The ONE definition of "acceptable text", and it lives on the row because the row owns the
    /// range.</b> The view enforces it at the input boundary (see <c>SettingsWindow</c>); nothing else decides
    /// what a numeric field may contain.</para>
    ///
    /// <para>⚠ It judges a <b>partial</b> entry, not a final one, which is why it is not simply
    /// <c>int.TryParse</c>: an empty field is legitimate (the user is retyping), and so is a lone
    /// <c>-</c> where the range admits negatives. <b>Range is NOT checked here</b> — typing <c>1</c> on the way
    /// to <c>1000</c> would fail a minimum of <c>10</c>, and blocking it would make the field impossible to
    /// use. Bounds are <see cref="Commit"/>'s job, where they clamp (§17.1).</para>
    ///
    /// <para>⚠⚠ <b>The length cap is <c>int</c>'s width, NOT the range's</b>, and the difference matters. Capping
    /// at <see cref="Maximum"/>'s digit count was the first attempt and it quietly broke the promise §17.1
    /// makes: typing <c>50000000</c> into a field whose maximum is <c>1 000 000</c> is the user saying "as many
    /// as possible", and clamping it is the documented answer — but an 8-digit cap would have refused the 8th
    /// keystroke, which reads as a broken field. So over-range entry stays possible and still clamps; only
    /// lengths no <c>int</c> could hold are refused.</para>
    ///
    /// <para>⚠ The sign branch reads <see cref="PreferenceRange.Minimum"/> rather than assuming the positive
    /// ranges this build happens to ship — a predicate that silently refuses half of a future range's legal
    /// values would be found the hard way, by a field nobody could type into.</para>
    /// </summary>
    public bool AcceptsText(string candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Length == 0) return true;

        var digitsFrom = 0;
        if (candidate[0] == '-')
        {
            if (_range.Minimum >= 0) return false;
            if (candidate.Length == 1) return true;
            digitsFrom = 1;
        }

        if (candidate.Length - digitsFrom > MaxDigits) return false;

        for (var i = digitsFrom; i < candidate.Length; i++)
        {
            // ⚠ ASCII digits only, never char.IsDigit — that accepts every Unicode decimal digit, and a field
            // holding Arabic-Indic digits would pass this gate and then fail to parse at commit.
            if (candidate[i] is < '0' or > '9') return false;
        }

        return true;
    }

    /// <summary>The widest run of digits worth accepting — <c>int</c>'s own, so a long-enough entry can still
    /// be over the range (and clamp) but never so long that it means nothing.</summary>
    private static readonly int MaxDigits =
        int.MaxValue.ToString(CultureInfo.InvariantCulture).Length;

    /// <summary>Raised when the user settled on a new value — the page's cue to persist.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>
    /// Settles whatever is in the field: parse, clamp, echo the result back, and report a change if there was
    /// one. Called by the view on blur and on Enter — the two moments a free-text value is settled.
    /// </summary>
    public void Commit()
    {
        // ⚠ Parsed as a LONG, then squeezed into int before the range clamps it. AcceptsText admits up to
        // int.MaxValue's DIGIT COUNT, which lets through a handful of 10-digit values above int.MaxValue
        // ("9999999999") — parsing those as int would fail and revert the entry, when what the user typed
        // plainly means "the maximum". One widening keeps "type a big number, get the maximum" true for every
        // length the field accepts.
        //
        // ⚠ The unparseable branch is therefore unreachable from the keyboard, and is kept for the routes that
        // do not pass through AcceptsText: an empty field (legitimate while retyping, and "" is not a number)
        // and a value set programmatically.
        var settled = long.TryParse(EditText, NumberStyles.Integer, CultureInfo.CurrentCulture, out var parsed)
            ? _range.Normalize((int)Math.Clamp(parsed, int.MinValue, int.MaxValue))
            : _value;   // unparseable → keep what we had; the echo below puts it back in the field

        var text = settled.ToString(CultureInfo.CurrentCulture);
        if (!string.Equals(EditText, text, StringComparison.Ordinal))
        {
            EditText = text;
        }

        if (settled == _value) return;

        _value = settled;
        OnPropertyChanged(nameof(Value));
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// One preference row on a settings page: its caption, its sentence, its legal values and its current value.
///
/// <para>⭐ <b>The values come from the Core option set, never from XAML</b> (design §5.2.2). A hand-typed
/// list in the view would be a second copy of the legal set, and the drift is silent in the dangerous
/// direction: the user picks an option the validator rejects, it appears to work, and it reverts on the next
/// load with nothing failing anywhere.</para>
/// </summary>
public sealed partial class PreferenceSettingViewModel : SettingRowViewModel
{
    private readonly List<PreferenceOptionViewModel> _options = new();
    private bool _syncing;
    private string _value = string.Empty;

    internal PreferenceSettingViewModel(SettingDescriptor descriptor, string categoryTitle, string value)
        : base(descriptor, categoryTitle)
    {
        if (descriptor.Options is { } options)
        {
            foreach (var key in options.Values)
            {
                _options.Add(new PreferenceOptionViewModel(this, key));
            }
        }

        SetValue(value, notify: false);
    }

    public IReadOnlyList<PreferenceOptionViewModel> Options => _options;

    /// <summary>The label this row's catalog entry gives an option key, in the current language.</summary>
    internal string LabelFor(string optionKey)
        => SettingsCatalog.Descriptor(Id).OptionLabels?[optionKey] ?? optionKey;

    /// <summary>⚠ The options are separate view models, so the base's blanket notification cannot reach them —
    /// their labels are what the user actually clicks ("Dark" / "Light", "lower case" / "UPPER CASE").</summary>
    internal override void RefreshLocalizedText()
    {
        base.RefreshLocalizedText();
        foreach (var option in _options)
        {
            option.RefreshLocalizedText();
        }
    }

    /// <summary>The stored key of the current value. Setting it is what "apply on change" means for a
    /// discrete control: the user selected, so the value is settled.</summary>
    public string Value
    {
        get => _value;
        set => SetValue(value, notify: true);
    }

    /// <summary>Two-way for a <c>ComboBox</c>. Same underlying <see cref="Value"/>, so a page may render a
    /// setting as radios or as a list without either becoming a second source.</summary>
    public PreferenceOptionViewModel? SelectedOption
    {
        get => _options.FirstOrDefault(o => string.Equals(o.Key, _value, StringComparison.Ordinal));
        set
        {
            if (value is not null)
            {
                Value = value.Key;
            }
        }
    }

    /// <summary>Raised when the user settled on a new value — the page's cue to persist.</summary>
    public event EventHandler? ValueChanged;

    private void SetValue(string value, bool notify)
    {
        if (string.Equals(_value, value, StringComparison.Ordinal) || _syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            _value = value;
            foreach (var option in _options)
            {
                option.IsSelected = string.Equals(option.Key, value, StringComparison.Ordinal);
            }
        }
        finally
        {
            _syncing = false;
        }

        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(SelectedOption));

        if (notify)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>
/// One entry in the category list.
///
/// <para>⚠⚠ <b>Observable, and <see cref="Title"/> is computed</b> — this list is the exact place the frozen
/// language showed itself: the page heading beside it is a live <c>{app:Loc}</c> binding and said "Ogólne"
/// while the list item, bound to a captured <c>Title</c>, still said "General".</para>
/// </summary>
public sealed partial class SettingsCategoryViewModel : ObservableObject
{
    internal SettingsCategoryViewModel(SettingsCategoryDescriptor descriptor)
    {
        Id = descriptor.Id;
        IconKey = descriptor.IconKey;
    }

    public string Id { get; }

    public string Title => SettingsCatalog.Category(Id).Title;

    /// <summary>The category's icon as a geometry KEY — a string, never a <c>Geometry</c> or a brush
    /// (architecture rule #1). Resolved in the view by <c>IconGeometryConverter</c>. ⚠ Captured deliberately:
    /// an icon key is not a word and does not move with the language.</summary>
    public string IconKey { get; }

    internal void RefreshLocalizedText() => OnPropertyChanged(nameof(Title));
}

/// <summary>
/// Settings Center's content: a projection of <see cref="SettingsCatalog"/> over the app's one
/// <see cref="PreferencesService"/>.
///
/// <para><b>Apply on change, no OK/Cancel</b> (ratified Q8). Every discrete control commits the moment it is
/// selected; there is no pending state and nothing to confirm. A free-text or numeric setting will commit on
/// blur or Enter when one arrives (design §5.5.1) — the view is what decides when a control's value is
/// settled, which is why nothing here streams keystrokes.</para>
///
/// <para>⚠ <b>The store can refuse to write, silently, and this is the one surface that must say so</b>
/// (design §2.5 / §5.5). <see cref="PreferencesService.Apply"/> reports it; <see cref="ShowSaveRefusal"/>
/// carries it to the shared <c>MessageBanner</c>. The change still holds for the session — a refusal means
/// the FILE cannot be written, not that the choice was wrong.</para>
///
/// <para>⚠ <b>No Avalonia type appears here</b> (architecture rule #1). The theme travels as a string; turning
/// it into a <c>ThemeVariant</c> and painting the app is <see cref="ThemePreference"/>'s job, driven by
/// <see cref="PreferencesService.Changed"/> — which is also what keeps the titlebar toggle and this window
/// from being two ways to apply a theme.</para>
/// </summary>
public sealed partial class SettingsCenterViewModel : ObservableObject
{
    private readonly PreferencesService _preferences;
    private readonly SettingsPortability _portability;
    private readonly IReadOnlyList<SettingsCategoryViewModel> _allCategories;
    private readonly Dictionary<string, SettingRowViewModel> _settings = new(StringComparer.Ordinal);

    public SettingsCenterViewModel(PreferencesService preferences, SettingsPortability portability)
    {
        _preferences = preferences;
        _portability = portability;
        var current = preferences.Current;

        _allCategories = SettingsCatalog.Categories
            .Select(c => new SettingsCategoryViewModel(c))
            .ToArray();

        foreach (var category in SettingsCatalog.Categories)
        {
            foreach (var descriptor in SettingsCatalog.SettingsIn(category.Id))
            {
                // ⚠ The kinds diverge HERE and nowhere else. An action row is never handed to a value mapping,
                // which is what keeps those mappings statements about preferences only — each still throws for an
                // id it does not know, and that is the guard, not an inconvenience.
                if (descriptor.Kind == SettingKind.Action)
                {
                    _settings.Add(descriptor.Id, new SettingActionViewModel(descriptor, category.Title));
                    continue;
                }

                SettingRowViewModel row = descriptor.ValueKind switch
                {
                    SettingValueKind.Toggle => Wire(
                        new BooleanSettingViewModel(descriptor, category.Title, FlagOf(descriptor.Id, current))),

                    // ⚠ A Number row must carry a range, and the `!` rests on the catalog pairing the two at the
                    // one place both are declared — SettingsCatalog passes `valueKind: Number` and `range:`
                    // together, so a row with one and not the other would have to be written deliberately.
                    // ⚠ There is deliberately NO test named here: an earlier draft of this comment cited a
                    // guard that was never written, which is worse than no claim at all — it tells the next
                    // reader a net exists. If a Number row ever ships without a range this throws at
                    // construction, loudly and immediately, which is the behaviour a missing range deserves.
                    SettingValueKind.Number => Wire(
                        new NumericSettingViewModel(
                            descriptor, category.Title, descriptor.Range!, NumberOf(descriptor.Id, current))),

                    _ => Wire(
                        new PreferenceSettingViewModel(
                            descriptor, category.Title, ValueOf(descriptor.Id, current))),
                };

                _settings.Add(descriptor.Id, row);
            }
        }

        Categories = new ObservableCollection<SettingsCategoryViewModel>(_allCategories);
        SelectedCategory = Categories.FirstOrDefault();

        // ⚠ This window is where the language is CHANGED, so it is the one surface guaranteed to be on screen
        // when the change happens. Everything it shows is computed now, so the handler only has to say
        // "re-read" — see RefreshLocalizedText.
        Localization.Loc.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// ⚠⚠ <b>Unsubscribes from the static <see cref="Localization.Loc.LanguageChanged"/>.</b> Unlike
    /// <c>MainWindowViewModel</c>, which lives as long as the process and may simply subscribe, this view model
    /// is created per window opening — an unremoved handler would keep every previously closed Settings window's
    /// view model alive for the rest of the session, and each would answer the next language change.
    /// </summary>
    public void Dispose() => Localization.Loc.LanguageChanged -= OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLocalizedText();

    /// <summary>
    /// Re-renders every caption this window owns in the current language.
    ///
    /// <para>⭐ Three groups, because they are three kinds of holder: what this view model computes, the
    /// category LIST (its own view models), and the ROWS (theirs, plus each option row's children). None of
    /// them stores text any more, so each only needs telling to ask again.</para>
    ///
    /// <para>⚠ The filter is re-applied last, and that is not tidiness: <c>Haystack</c> is now in the new
    /// language, so leaving the previous language's match results in place would show rows the current search
    /// term no longer matches — and hide ones it does.</para>
    /// </summary>
    internal void RefreshLocalizedText()
    {
        OnPropertyChanged(string.Empty);

        foreach (var category in _allCategories)
        {
            category.RefreshLocalizedText();
        }

        foreach (var row in _settings.Values)
        {
            row.RefreshLocalizedText();
        }

        ApplyFilter(SearchText);
    }

    // One subscription point for all three value-carrying row kinds, so "a settled value persists" is stated
    // once rather than once per kind — the place a future fourth kind would otherwise be forgotten.
    private T Wire<T>(T row) where T : SettingRowViewModel
    {
        switch (row)
        {
            case PreferenceSettingViewModel option: option.ValueChanged += (_, _) => Commit(); break;
            case BooleanSettingViewModel toggle: toggle.ValueChanged += (_, _) => Commit(); break;
            case NumericSettingViewModel number: number.ValueChanged += (_, _) => Commit(); break;
        }

        return row;
    }

    /// <summary>
    /// Opens the export dialog. Supplied by the view, because a file picker and a modal owner are view things —
    /// the same request/callback shape <c>ExportDialogViewModel</c> already uses for the data export.
    /// </summary>
    public Func<Task>? RequestExport { get; set; }

    /// <summary>Opens the import dialog.</summary>
    public Func<Task>? RequestImport { get; set; }

    /// <summary>Reveals <see cref="SettingsPortability.SettingsFolder"/> in the shell.</summary>
    public Func<string, Task>? RequestRevealFolder { get; set; }

    public ObservableCollection<SettingsCategoryViewModel> Categories { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsEditorPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsGridPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsTabsPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsDebuggerPageVisible))]
    [NotifyPropertyChangedFor(nameof(IsFormatterPageVisible))]
    private SettingsCategoryViewModel? _selectedCategory;

    /// <summary>Live filter over every setting's label, sentence, keywords and category title.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The refusal text for the docked banner.</summary>
    [ObservableProperty]
    private string _saveRefusalMessage = string.Empty;

    /// <summary>True while the last change did not reach the file.</summary>
    [ObservableProperty]
    private bool _showSaveRefusal;

    public PreferenceSettingViewModel Theme => Preference(SettingsCatalog.SettingTheme);

    public PreferenceSettingViewModel Language => Preference(SettingsCatalog.SettingLanguage);

    public BooleanSettingViewModel RestoreWorkspace => Toggle(SettingsCatalog.SettingRestoreWorkspace);

    public BooleanSettingViewModel ProcedureEasyMode => Toggle(SettingsCatalog.SettingProcedureEasyMode);

    public BooleanSettingViewModel ViewEasyMode => Toggle(SettingsCatalog.SettingViewEasyMode);

    public BooleanSettingViewModel TriggerEasyMode => Toggle(SettingsCatalog.SettingTriggerEasyMode);

    public BooleanSettingViewModel FunctionEasyMode => Toggle(SettingsCatalog.SettingFunctionEasyMode);

    public NumericSettingViewModel PreviewRowLimit => Number(SettingsCatalog.SettingPreviewRowLimit);

    public NumericSettingViewModel FullLoadPromptThreshold
        => Number(SettingsCatalog.SettingFullLoadPromptThreshold);

    public NumericSettingViewModel DataPageSize => Number(SettingsCatalog.SettingDataPageSize);

    public BooleanSettingViewModel GridAutoFitColumns => Toggle(SettingsCatalog.SettingGridAutoFit);

    public PreferenceSettingViewModel TabStripMode => Preference(SettingsCatalog.SettingTabStripMode);

    public NumericSettingViewModel TabStripMaxRows => Number(SettingsCatalog.SettingTabStripMaxRows);

    /// <summary>
    /// Whether <see cref="TabStripMaxRows"/> is shown at all — it is not, in single-row layout.
    ///
    /// <para>⭐ <b>Ratified by the user (2026-08-03), and it OVERTURNS this etap's first decision.</b> M3.3b
    /// deliberately kept the row visible, reasoning that the value survives a mode round trip so hiding it
    /// might suggest the number had been lost. The user's rule is better and simpler: <i>the interface does
    /// not show settings that do nothing in the current mode.</i> The value is still kept — it is the ROW that
    /// disappears, not the number.</para>
    ///
    /// <para>⚠ It is an AND with the row's own <c>IsVisible</c>, which is the search filter's property. Two
    /// independent reasons to hide one row, so neither may overwrite the other: writing the mode's answer
    /// into <c>IsVisible</c> would make a search for "rows" resurrect a row that does not apply, or a mode
    /// switch resurrect a row the filter had excluded.</para>
    /// </summary>
    public bool ShowTabStripMaxRows
        => TabStripMaxRows.IsVisible
           && string.Equals(
               TabStripMode.Value,
               PreferenceOptions.TabStripModeMultiRow,
               StringComparison.Ordinal);

    /// <summary>
    /// Whether the Easy-mode card is shown at all — it is not, when the search excludes all four of its rows.
    ///
    /// <para>⭐ <b>This is presentation only, and deliberately so.</b> The four flags stay four independent
    /// catalog rows with four independent ids, four haystacks and four stored values; nothing about
    /// <see cref="SettingsCatalog"/> or the meaning of a category changes. What changes is that the view draws
    /// ONE card around them, because they are one subject — "which mode does an object editor open in" — and
    /// four equal cards said four subjects.</para>
    ///
    /// <para>⚠ It is an OR, and each checkbox keeps its OWN <c>IsVisible</c> inside the card. So searching
    /// "procedure" shows the card with one row in it, not the card with four: the filter's meaning is
    /// unchanged, only its container is. Same shape as <see cref="ShowTabStripMaxRows"/>.</para>
    /// </summary>
    public bool ShowEasyModeGroup
        => ProcedureEasyMode.IsVisible
           || ViewEasyMode.IsVisible
           || TriggerEasyMode.IsVisible
           || FunctionEasyMode.IsVisible;

    public PreferenceSettingViewModel DebuggerIsolation
        => Preference(SettingsCatalog.SettingDebuggerIsolation);

    public PreferenceSettingViewModel FormatterKeywordCase
        => Preference(SettingsCatalog.SettingFormatterKeywordCase);

    public PreferenceSettingViewModel FormatterIdentifierCase
        => Preference(SettingsCatalog.SettingFormatterIdentifierCase);

    /// <summary>The Import / export row — an action row, so it exposes visibility and words but no value.</summary>
    public SettingActionViewModel ImportExport
        => (SettingActionViewModel)_settings[SettingsCatalog.SettingImportExport];

    private PreferenceSettingViewModel Preference(string id) => (PreferenceSettingViewModel)_settings[id];

    private BooleanSettingViewModel Toggle(string id) => (BooleanSettingViewModel)_settings[id];

    private NumericSettingViewModel Number(string id) => (NumericSettingViewModel)_settings[id];

    /// <summary>The folder the <i>Open settings folder</i> button reveals.</summary>
    public string SettingsFolder => _portability.SettingsFolder;

    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        if (RequestExport is { } request) await request();
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        if (RequestImport is { } request) await request();
    }

    [RelayCommand]
    private async Task OpenSettingsFolderAsync()
    {
        if (RequestRevealFolder is { } reveal) await reveal(_portability.SettingsFolder);
    }

    /// <summary>
    /// Which page the right pane shows. One property per category, deliberately: with a handful of pages this
    /// is one line each and every binding is compiled and typed, whereas a generic page host would be an
    /// abstraction built for pages that do not exist yet (§9.1).
    /// </summary>
    public bool IsGeneralPageVisible
        => string.Equals(SelectedCategory?.Id, SettingsCatalog.CategoryGeneral, StringComparison.Ordinal);

    /// <inheritdoc cref="IsGeneralPageVisible"/>
    public bool IsEditorPageVisible
        => string.Equals(SelectedCategory?.Id, SettingsCatalog.CategoryEditor, StringComparison.Ordinal);

    /// <inheritdoc cref="IsGeneralPageVisible"/>
    public bool IsGridPageVisible
        => string.Equals(SelectedCategory?.Id, SettingsCatalog.CategoryGrid, StringComparison.Ordinal);

    /// <inheritdoc cref="IsGeneralPageVisible"/>
    public bool IsTabsPageVisible
        => string.Equals(SelectedCategory?.Id, SettingsCatalog.CategoryTabs, StringComparison.Ordinal);

    /// <inheritdoc cref="IsGeneralPageVisible"/>
    public bool IsDebuggerPageVisible
        => string.Equals(SelectedCategory?.Id, SettingsCatalog.CategoryDebugger, StringComparison.Ordinal);

    /// <inheritdoc cref="IsGeneralPageVisible"/>
    public bool IsFormatterPageVisible
        => string.Equals(SelectedCategory?.Id, SettingsCatalog.CategoryFormatter, StringComparison.Ordinal);

    /// <summary>False when the search matches nothing — the cue for an explained empty state rather than an
    /// empty window.</summary>
    public bool HasMatches => Categories.Count > 0;

    partial void OnSearchTextChanged(string value) => ApplyFilter(value);

    private void ApplyFilter(string search)
    {
        foreach (var setting in _settings.Values)
        {
            setting.IsVisible = SettingsCatalog.Matches(setting.Haystack, search);
        }

        var visible = _allCategories
            .Where(c => _settings.Values.Any(s =>
                s.IsVisible && string.Equals(s.CategoryId, c.Id, StringComparison.Ordinal)))
            .ToArray();

        var previous = SelectedCategory;

        Categories.Clear();
        foreach (var category in visible) Categories.Add(category);

        // Keep the user where they were when it survives the filter; otherwise land on the first page that
        // does, so the right pane is never blank while the left list has entries.
        SelectedCategory = visible.Contains(previous) ? previous : visible.FirstOrDefault();

        OnPropertyChanged(nameof(HasMatches));
        // ⚠ Filtr jest DRUGIM powodem ukrycia wiersza „Maximum rows" (pierwszym jest tryb), więc obie strony
        //   muszą ogłaszać zmianę — inaczej wpisanie frazy w wyszukiwarkę zostawiłoby wiersz w poprzednim
        //   stanie widoczności.
        OnPropertyChanged(nameof(ShowTabStripMaxRows));
        // ⚠ Ten sam powód co wiersz wyżej: karta Easy-mode jest widoczna, dopóki filtr zostawia w niej
        //   choć jeden wiersz, więc jej widoczność musi być ogłaszana przy każdej zmianie filtra.
        OnPropertyChanged(nameof(ShowEasyModeGroup));
    }

    /// <summary>
    /// Reads one setting's value out of the stored preferences.
    /// <para>⚠ This and <see cref="Compose"/> are the two halves of ONE mapping between a catalog id and a
    /// <see cref="Preferences"/> property, and a new setting has to appear in both. They are deliberately
    /// explicit rather than reflective: a reflective mapping would bind a UI row to a property name, which is
    /// exactly the kind of link that breaks silently on a rename.</para>
    /// </summary>
    private static string ValueOf(string settingId, Preferences preferences) => settingId switch
    {
        SettingsCatalog.SettingTheme => preferences.Theme,
        SettingsCatalog.SettingLanguage => preferences.Language,
        SettingsCatalog.SettingTabStripMode => preferences.TabStripMode,
        SettingsCatalog.SettingDebuggerIsolation => preferences.DebuggerIsolation,
        SettingsCatalog.SettingFormatterKeywordCase => preferences.FormatterKeywordCase,
        SettingsCatalog.SettingFormatterIdentifierCase => preferences.FormatterIdentifierCase,
        _ => throw new ArgumentOutOfRangeException(nameof(settingId), settingId, "No such setting in the catalog."),
    };

    /// <summary>
    /// <see cref="ValueOf"/> for a <see cref="SettingValueKind.Toggle"/> row.
    /// <para>⚠ One mapping method per value SHAPE rather than one returning <c>object</c>: three small total
    /// functions each throw for an id they do not know, which is what makes a new row fail loudly instead of
    /// silently reading the wrong kind of value.</para>
    /// </summary>
    private static bool FlagOf(string settingId, Preferences preferences) => settingId switch
    {
        SettingsCatalog.SettingRestoreWorkspace => preferences.RestoreWorkspaceOnStartup,
        SettingsCatalog.SettingProcedureEasyMode => preferences.ProcedureEasyModeDefault,
        SettingsCatalog.SettingViewEasyMode => preferences.ViewEasyModeDefault,
        SettingsCatalog.SettingTriggerEasyMode => preferences.TriggerEasyModeDefault,
        SettingsCatalog.SettingFunctionEasyMode => preferences.FunctionEasyModeDefault,
        SettingsCatalog.SettingGridAutoFit => preferences.GridAutoFitColumns,
        _ => throw new ArgumentOutOfRangeException(nameof(settingId), settingId, "No such setting in the catalog."),
    };

    /// <inheritdoc cref="FlagOf"/>
    private static int NumberOf(string settingId, Preferences preferences) => settingId switch
    {
        SettingsCatalog.SettingPreviewRowLimit => preferences.PreviewRowLimit,
        SettingsCatalog.SettingFullLoadPromptThreshold => preferences.FullLoadPromptThreshold,
        SettingsCatalog.SettingDataPageSize => preferences.DataPageSize,
        SettingsCatalog.SettingTabStripMaxRows => preferences.TabStripMaxRows,
        _ => throw new ArgumentOutOfRangeException(nameof(settingId), settingId, "No such setting in the catalog."),
    };

    /// <summary>
    /// The current page state as a whole <see cref="Preferences"/>.
    /// <para>⭐ Built with <c>with</c> on the live value, never a fresh instance, so a preference this window
    /// does not render passes through untouched instead of being reset to its default. Same reasoning as
    /// <c>PreferencesStore.Validate</c>: a fresh instance silently loses whatever nobody remembered to list,
    /// which turns "I added a preference" into "that preference never persists".</para>
    /// <para>⚠ <b>As of etap 4 every preference IS rendered here</b>, so <c>with</c> currently has no unrendered
    /// subject to protect — which is exactly when someone deletes it as redundant. Keep it: the next preference
    /// added to <see cref="Preferences"/> is unrendered until its row exists, and
    /// <c>EveryPreference_IsRenderedOrRecordedAsHidden</c> is what makes that gap a failing test instead of a
    /// silent reset.</para>
    /// </summary>
    private Preferences Compose() => _preferences.Current with
    {
        // ⚠ Action rows (Import / export) are absent here by design, not omission — they carry no value. The
        // SettingKind split is what makes that a typed fact rather than a convention.
        Theme = Theme.Value,
        Language = Language.Value,
        RestoreWorkspaceOnStartup = RestoreWorkspace.Value,
        ProcedureEasyModeDefault = ProcedureEasyMode.Value,
        ViewEasyModeDefault = ViewEasyMode.Value,
        TriggerEasyModeDefault = TriggerEasyMode.Value,
        FunctionEasyModeDefault = FunctionEasyMode.Value,
        PreviewRowLimit = PreviewRowLimit.Value,
        FullLoadPromptThreshold = FullLoadPromptThreshold.Value,
        DataPageSize = DataPageSize.Value,
        GridAutoFitColumns = GridAutoFitColumns.Value,
        TabStripMode = TabStripMode.Value,
        TabStripMaxRows = TabStripMaxRows.Value,
        DebuggerIsolation = DebuggerIsolation.Value,
        FormatterKeywordCase = FormatterKeywordCase.Value,
        FormatterIdentifierCase = FormatterIdentifierCase.Value,
    };

    private void Commit()
    {
        // ⚠ Wołane po KAŻDEJ ustalonej wartości, więc jest jedynym miejscem, w którym trzeba ogłosić, że
        //   zmiana trybu paska zakładek mogła ukryć albo pokazać wiersz „Maximum rows". Stoi tutaj, a nie
        //   przy samym wierszu trybu, bo `Wire` jest JEDNYM punktem subskrypcji dla wszystkich rodzajów
        //   wierszy — dopisanie tego przy jednym z nich byłoby drugą ścieżką powiadomień.
        OnPropertyChanged(nameof(ShowTabStripMaxRows));

        var persisted = _preferences.Apply(Compose());

        ShowSaveRefusal = !persisted;
        SaveRefusalMessage = persisted
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.SettingsSaveRefusedFormat,
                // ⭐ Composed AFTER PreferencesService.Apply raised Changed, which is what applies a new language —
                // so this line already renders in the language the user just chose. Pinned by a test, because the
                // ordering is what makes it correct and a refactor could reverse it silently.
                _preferences.LastSaveMessage is { } m ? Loc.Format(m) : string.Empty);
    }
}

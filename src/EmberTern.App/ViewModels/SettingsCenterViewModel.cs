using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.App.Settings;
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

    internal PreferenceOptionViewModel(PreferenceSettingViewModel owner, string key, string label)
    {
        _owner = owner;
        Key = key;
        Label = label;
    }

    public string Key { get; }

    public string Label { get; }

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
/// One row on a settings page: its caption, its sentence, its legal values and its current value.
///
/// <para>⭐ <b>The values come from the Core option set, never from XAML</b> (design §5.2.2). A hand-typed
/// list in the view would be a second copy of the legal set, and the drift is silent in the dangerous
/// direction: the user picks an option the validator rejects, it appears to work, and it reverts on the next
/// load with nothing failing anywhere.</para>
/// </summary>
public sealed partial class PreferenceSettingViewModel : ObservableObject
{
    private readonly List<PreferenceOptionViewModel> _options = new();
    private bool _syncing;
    private string _value = string.Empty;

    internal PreferenceSettingViewModel(SettingDescriptor descriptor, string categoryTitle, string value)
    {
        Id = descriptor.Id;
        CategoryId = descriptor.CategoryId;
        Label = descriptor.Label;
        Description = descriptor.Description;

        if (descriptor.Options is { } options && descriptor.OptionLabels is { } labels)
        {
            foreach (var key in options.Values)
            {
                _options.Add(new PreferenceOptionViewModel(this, key, labels[key]));
            }
        }

        // Searching matches what is DISPLAYED plus the keywords that lead to it — and the category's own
        // title, so typing "general" keeps the whole page rather than emptying it.
        Haystack = string.Join('\n',
            Label, Description, categoryTitle, string.Join(' ', descriptor.Keywords));

        SetValue(value, notify: false);
    }

    public string Id { get; }

    public string CategoryId { get; }

    public string Label { get; }

    public string Description { get; }

    public IReadOnlyList<PreferenceOptionViewModel> Options => _options;

    /// <summary>Hidden by a search that does not match it. The row stays in place; only its visibility
    /// changes, so nothing is rebuilt while the user types.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    internal string Haystack { get; }

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

/// <summary>One entry in the category list.</summary>
public sealed class SettingsCategoryViewModel
{
    internal SettingsCategoryViewModel(SettingsCategoryDescriptor descriptor)
    {
        Id = descriptor.Id;
        Title = descriptor.Title;
    }

    public string Id { get; }

    public string Title { get; }
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
    private readonly IReadOnlyList<SettingsCategoryViewModel> _allCategories;
    private readonly Dictionary<string, PreferenceSettingViewModel> _settings = new(StringComparer.Ordinal);

    public SettingsCenterViewModel(PreferencesService preferences)
    {
        _preferences = preferences;
        var current = preferences.Current;

        _allCategories = SettingsCatalog.Categories
            .Select(c => new SettingsCategoryViewModel(c))
            .ToArray();

        foreach (var category in SettingsCatalog.Categories)
        {
            foreach (var descriptor in SettingsCatalog.SettingsIn(category.Id))
            {
                var setting = new PreferenceSettingViewModel(descriptor, category.Title, ValueOf(descriptor.Id, current));
                setting.ValueChanged += (_, _) => Commit();
                _settings.Add(descriptor.Id, setting);
            }
        }

        Categories = new ObservableCollection<SettingsCategoryViewModel>(_allCategories);
        SelectedCategory = Categories.FirstOrDefault();
    }

    public ObservableCollection<SettingsCategoryViewModel> Categories { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralPageVisible))]
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

    public PreferenceSettingViewModel Theme => _settings[SettingsCatalog.SettingTheme];

    public PreferenceSettingViewModel Language => _settings[SettingsCatalog.SettingLanguage];

    public PreferenceSettingViewModel FormatterKeywordCase
        => _settings[SettingsCatalog.SettingFormatterKeywordCase];

    public PreferenceSettingViewModel FormatterIdentifierCase
        => _settings[SettingsCatalog.SettingFormatterIdentifierCase];

    /// <summary>
    /// Which page the right pane shows. One property per category, deliberately: with a handful of pages this
    /// is one line each and every binding is compiled and typed, whereas a generic page host would be an
    /// abstraction built for pages that do not exist yet (§9.1).
    /// </summary>
    public bool IsGeneralPageVisible
        => string.Equals(SelectedCategory?.Id, SettingsCatalog.CategoryGeneral, StringComparison.Ordinal);

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
        SettingsCatalog.SettingFormatterKeywordCase => preferences.FormatterKeywordCase,
        SettingsCatalog.SettingFormatterIdentifierCase => preferences.FormatterIdentifierCase,
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
        Theme = Theme.Value,
        Language = Language.Value,
        FormatterKeywordCase = FormatterKeywordCase.Value,
        FormatterIdentifierCase = FormatterIdentifierCase.Value,
    };

    private void Commit()
    {
        var persisted = _preferences.Apply(Compose());

        ShowSaveRefusal = !persisted;
        SaveRefusalMessage = persisted
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                UiStrings.SettingsSaveRefusedFormat,
                _preferences.LastSaveDiagnostic ?? string.Empty);
    }
}

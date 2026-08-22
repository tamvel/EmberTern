using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Email;
using EmberTern.LicenseManager.Services;
using EmberTern.LicenseManager.Settings;
using EmberTern.LicenseManager.ViewModels;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>An option's identity is its VALUE; a word is never part of it.</b>
///
/// <para>Every picker in this application binds <c>ComboBox.SelectedItem</c> to a <c>record</c>, and a
/// record compares by every positional member. So a label carried in the primary constructor puts the
/// CURRENT LANGUAGE inside the option's identity: rebuild the list in another language and the operator's
/// selection equals nothing in its own list, and the picker silently blanks. ⚠ Three of the affected
/// pickers gate consequential work — the transport a password travels over, the reason written into an
/// append-only column, and which of the two restore modes runs.</para>
///
/// <para>⚠⚠ <b>The failure is silent in both directions and that is why these guards are structural.</b>
/// A blanked <c>SelectedItem</c> raises no binding error; and the worst instance found here was not a
/// language problem at all — <c>StorageViewModel</c> built its DEFAULT restore mode as a second,
/// independent <c>new(false, "Restore to another location")</c>, so the safe default was only the offered
/// option for as long as two literals stayed byte-identical.</para>
///
/// <para>⭐ Written for L8.0/prep, BEFORE the localization mechanism exists. Once it does, the behavioural
/// half of this claim ("a picker keeps its selection across a language change") becomes measurable
/// directly and gets its own guard; these stay, because they key on the DECLARATION rather than on the
/// value, which is the only thing that distinguishes a correct option from one that merely happens to
/// render the same text today (gotcha #284).</para>
/// </summary>
public sealed class OptionIdentityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private readonly ManagerFixture _manager = new(Now);
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "etlm-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// A parameter name that carries WORDS rather than a value.
    /// </summary>
    /// <remarks>
    /// ⚠ Names, not types: a <c>string</c> parameter is perfectly legitimate as an identity — the licence
    /// status and the language code are both strings and both belong in the identity. What must not appear
    /// is a parameter whose job is to be READ.
    /// </remarks>
    private static readonly string[] WordShapedParameters =
        ["label", "explanation", "description", "caption", "title", "hint", "summary", "text"];

    /// <summary>
    /// ⛔ Discovered by the sweep, and deliberately NOT judged by it — each with its reason.
    /// </summary>
    /// <remarks>
    /// ⚠ A type listed here is one somebody decided about; a type in neither list is one nobody has.
    /// ⚠⚠ Every entry is a record built from <c>required … { get; init; }</c> properties rather than from
    /// a primary constructor, so its identity is every public property and the constructor check below
    /// would report nothing about it — a silent pass, which is worse than an exemption that says so.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> RowsNotJudgedHere =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(ArtifactListItem)] =
                "A presentation ROW, not a picker option — it legitimately holds words (Reason, Ordinal, "
                + "Standing), because presenting them is its whole job. ⭐ Its list is already rebuilt "
                + "identity-safely: ArtifactHistoryViewModel re-selects by Artifact.ArtifactId, never by "
                + "object equality. ⏭ Whether that stays true once the words follow a language is L8's "
                + "question, not prep's.",

            [nameof(CustomerRecord)] =
                "The REGISTER's own DTO, reached here only because the shell both lists customers and "
                + "holds a selected one. ⛔ Its equality is a data fact and is not this stage's to change; "
                + "it carries no localized word — every member is something the operator typed or the "
                + "register stored.",

            [nameof(LicenseRecord)] =
                "The register's licence DTO, and the same case as CustomerRecord: a stored row the shell "
                + "lists and selects from. ⛔ Nothing on it is our vocabulary — Status holds one of "
                + "LicenseStatuses, which is a persisted value and must never move with a language.",
        };

    // ── Guard 1 · the declaration ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// No record offered by a picker declares a word in its primary constructor.
    /// </summary>
    /// <remarks>
    /// ⭐ The subject is DISCOVERED, not listed: an option type is one a view model exposes as a list AND
    /// holds a single selected value of. That is the exact shape <c>SelectedItem</c> equality depends on,
    /// so the guard arms itself for a picker nobody thought to add here (the dead-list trap, #233).
    /// </remarks>
    [Fact]
    public void NoOptionRecord_CarriesAWordInItsIdentity()
    {
        var offenders = new List<string>();

        foreach (var option in DiscoverOptionTypes().Where(t => !RowsNotJudgedHere.ContainsKey(t.Name)))
        {
            foreach (var parameter in PrimaryConstructorParameters(option))
            {
                if (WordShapedParameters.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
                {
                    offenders.Add($"{option.Name}.{parameter.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "An option record must not carry a word in its identity — a record compares by every "
            + "positional member, so the current language would decide whether SelectedItem still "
            + "matches its own list. Make it a computed property instead: "
            + string.Join(", ", offenders));
    }

    /// <summary>⭐ The discovery itself is asserted, or an empty sweep would pass for the wrong reason.</summary>
    [Fact]
    public void TheOptionTypes_AreActuallyFound()
    {
        var found = DiscoverOptionTypes().Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal);

        // ⚠ The three filters are `FilterOption` subclasses reached through their own lists; the four
        //   others are direct. A type LEAVING this list is as interesting as one arriving — it means a
        //   picker stopped being discoverable, and guard 1 would then be sweeping less than it says.
        // ⚠⚠ The two ROWS are here because the sweep genuinely finds them: a selected row's equality is
        //   the same mechanism as a selected option's. They are exempted from the judgement above, with
        //   their reasons, rather than filtered out of the discovery — an exemption that is visible is
        //   the only kind that can be revisited.
        Assert.Equal(
            [
                nameof(ArtifactListItem),
                nameof(CustomerRecord),
                nameof(ExpiryFilter),
                nameof(IssueReasonOption),
                nameof(IssuingFilter),
                nameof(LanguageOption),
                nameof(LicenseRecord),
                nameof(RestoreModeOption),
                nameof(SmtpSecurityOption),
                nameof(StatusFilter),
            ],
            found);
    }

    /// <summary>Every exemption names a type the sweep actually finds — an entry that goes stale fails.</summary>
    [Fact]
    public void EveryExemption_NamesADiscoveredType()
    {
        var discovered = DiscoverOptionTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var exempt in RowsNotJudgedHere.Keys)
        {
            Assert.True(
                discovered.Contains(exempt),
                $"'{exempt}' is exempted from the option-identity judgement but is no longer discovered "
                + "as a selectable type. Remove the exemption — a stale one reads as coverage.");
        }
    }

    // ── Guard 2 · the behaviour that declaration buys ────────────────────────────────────────────────

    /// <summary>
    /// Two options built from the same value are EQUAL, so a rebuilt list still contains the selection.
    /// </summary>
    /// <remarks>
    /// ⭐ This is the half that will still hold when the labels become lookups: rebuilding a list is
    /// exactly what a language change does, and equality by value is what makes the selection survive it.
    /// </remarks>
    [Fact]
    public void AnOptionRebuilt_EqualsTheOneItReplaces()
    {
        Assert.Equal(new SmtpSecurityOption(SmtpSecurity.StartTls), new SmtpSecurityOption(SmtpSecurity.StartTls));
        Assert.Equal(new LanguageOption(ApplicationLanguages.Polish), new LanguageOption(ApplicationLanguages.Polish));
        Assert.Equal(new IssueReasonOption(IssueReasons.Renewal), new IssueReasonOption(IssueReasons.Renewal));
        Assert.Equal(new RestoreModeOption(true), new RestoreModeOption(true));
        Assert.Equal(new StatusFilter(LicenseStatuses.Blocked), new StatusFilter(LicenseStatuses.Blocked));
        Assert.Equal(new ExpiryFilter(WithinDays: 30), new ExpiryFilter(WithinDays: 30));
        Assert.Equal(new IssuingFilter(true), new IssuingFilter(true));

        // ⛔ And options that mean different things stay different — an identity of "nothing" would make
        //    every option equal and the picker would select the first row whatever was chosen.
        Assert.NotEqual(new RestoreModeOption(true), new RestoreModeOption(false));
        Assert.NotEqual(new ExpiryFilter(WithinDays: 30), new ExpiryFilter(WithinDays: 90));
        Assert.NotEqual(new ExpiryFilter(), new ExpiryFilter(Expired: true));
    }

    /// <summary>
    /// ⭐⭐ Every default selection is one of the options actually OFFERED — never a second instance.
    /// </summary>
    /// <remarks>
    /// ⚠ This is the guard that fails against the defect as it stood: <c>StorageViewModel</c>'s default
    /// restore mode was constructed separately from <c>RestoreModes[0]</c>, so the two were equal only
    /// while their two label literals agreed.
    /// </remarks>
    [Fact]
    public void EveryDefaultSelection_IsOneOfTheOfferedOptions()
    {
        var browser = new LicenseBrowserViewModel(_manager.Register, () => Now);
        Assert.Contains(browser.SelectedStatus, browser.StatusFilters);
        Assert.Contains(browser.SelectedExpiry, browser.ExpiryFilters);
        Assert.Contains(browser.SelectedIssuing, browser.IssuingFilters);

        Directory.CreateDirectory(_folder);
        var storage = new StorageViewModel(
            _manager.Register, _manager.Paths, SigningKeyFacts.Of(_manager.Session), () => Now);
        Assert.Contains(storage.SelectedRestoreMode, storage.RestoreModes);

        // ⭐ And it is the SAFE one — the mode that cannot touch the working register is the one an
        //   operator must choose to leave.
        Assert.False(storage.SelectedRestoreMode.ReplacesActiveRegister);

        var settings = new SettingsViewModel(
            new SmtpSettingsStore(Path.Combine(_folder, "smtp.dat")));
        Assert.Contains(settings.SelectedSecurity, settings.SecurityOptions);
        Assert.Contains(settings.MessageLanguage, settings.MessageLanguageOptions);
        Assert.Contains(settings.ApplicationLanguage, settings.ApplicationLanguageOptions);
    }

    // ── Guard 3 · the two language catalogs are two ──────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ The interface language and the message language are read from DIFFERENT catalogs.
    /// </summary>
    /// <remarks>
    /// <para>⚠ Both pickers used to be built from <c>MessageLanguages.All</c>. That was invisible while
    /// the interface picker was a disabled placeholder, and it means that adding a language for a CUSTOMER
    /// would have added an interface language for the OPERATOR — one with no translation behind it.</para>
    /// <para>⭐ <b>The assertion can genuinely fail today</b>, which is what makes it worth writing: the
    /// two catalogs hold the same two codes in the OPPOSITE order, because each leads with its own
    /// default. A sequence comparison therefore discriminates; a set comparison would not.</para>
    /// </remarks>
    [Fact]
    public void TheApplicationAndMessageLanguages_ComeFromTheirOwnCatalogs()
    {
        Assert.Equal(
            ApplicationLanguages.All,
            LanguageOption.ForApplication().Select(o => o.Code).ToArray());

        Assert.Equal(
            MessageLanguages.All,
            LanguageOption.ForMessages().Select(o => o.Code).ToArray());

        // The distinguishing fact the comparison above relies on, stated so that a future change making
        // the two orders identical fails HERE — with a message saying why — rather than quietly
        // weakening the guard above into a tautology.
        Assert.NotEqual(ApplicationLanguages.All, MessageLanguages.All);
    }

    /// <summary>
    /// The two defaults are decided separately: English for the operator (D‑3), Polish for the customer (D‑9).
    /// </summary>
    [Fact]
    public void TheTwoDefaults_AreDecidedSeparately()
    {
        Assert.Equal(ApplicationLanguages.English, ApplicationLanguages.Default);
        Assert.Equal(MessageLanguages.Polish, MessageLanguages.Default);
        Assert.NotEqual(ApplicationLanguages.Default, MessageLanguages.Default);
    }

    /// <summary>An unknown or missing interface language resolves to the default rather than throwing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("not-a-culture")]
    public void AnUnusableInterfaceLanguage_ResolvesToTheDefault(string? stored) =>
        Assert.Equal(ApplicationLanguages.Default, ApplicationLanguages.Resolve(stored));

    /// <summary>A known code resolves to itself, whatever its casing.</summary>
    [Theory]
    [InlineData("en", "en")]
    [InlineData("PL", "pl")]
    public void AKnownInterfaceLanguage_ResolvesToItself(string stored, string expected) =>
        Assert.Equal(expected, ApplicationLanguages.Resolve(stored));

    // ── Discovery ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every record a view model both LISTS and holds a single SELECTED value of.
    /// </summary>
    /// <remarks>
    /// ⚠ Matched on the shape rather than on a name: <c>SmtpSecurityOption</c>, <c>StatusFilter</c> and
    /// <c>RestoreModeOption</c> share no naming convention, and a convention is what a future picker would
    /// fail to follow.
    /// </remarks>
    private static IEnumerable<Type> DiscoverOptionTypes()
    {
        var assembly = typeof(ShellViewModel).Assembly;
        var found = new HashSet<Type>();

        foreach (var viewModel in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
        {
            var properties = viewModel
                .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            var listed = properties
                .Select(p => ElementOf(p.PropertyType))
                .Where(t => t is not null && IsRecord(t))
                .Select(t => t!)
                .ToHashSet();

            foreach (var property in properties)
            {
                var selected = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                if (!IsRecord(selected))
                {
                    continue;
                }

                // A picker's list may be typed as the base (the three filters share `FilterOption` in
                // XAML) or as the concrete type; either direction counts as "this is what is selected
                // from that list".
                if (listed.Any(l => l == selected || l.IsAssignableFrom(selected) || selected.IsAssignableFrom(l)))
                {
                    found.Add(selected);
                }
            }
        }

        return found;
    }

    /// <summary>The element type of a list-shaped property, or <see langword="null"/>.</summary>
    private static Type? ElementOf(Type type)
    {
        if (!type.IsGenericType || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return null;
        }

        var arguments = type.GetGenericArguments();
        return arguments.Length == 1 ? arguments[0] : null;
    }

    /// <summary>
    /// Whether <paramref name="type"/> is a record.
    /// </summary>
    /// <remarks>
    /// ⚠ There is no <c>IsRecord</c> in reflection. The compiler-generated clone method is the durable
    /// marker — its name is not a legal C# identifier, so nothing else can declare it.
    /// </remarks>
    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            is not null;

    /// <summary>
    /// The parameters that make up a record's identity.
    /// </summary>
    /// <remarks>
    /// ⚠ The primary constructor is the widest PUBLIC one: a record's copy constructor is protected, and
    /// the compiler declares no other public overload unless the author wrote it.
    /// </remarks>
    private static IReadOnlyList<ParameterInfo> PrimaryConstructorParameters(Type type)
    {
        var constructor = type
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();

        return constructor?.GetParameters() ?? [];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _manager.Dispose();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary folder is not worth failing a test over.
        }
    }
}

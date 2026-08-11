using System.Globalization;
using System.Resources;
using Avalonia.Controls;
using Avalonia.Headless;
using EmberTern.App;
using EmberTern.App.Localization;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>The one measurement the whole live-switching design stands on: does a bound string actually
/// re-read when the language changes?</b>
///
/// <para>Everything else in the localization stage is mechanical — migrating members, migrating XAML — and
/// all of it is wasted if the notification does not reach a realized control. The claim is easy to state and
/// easy to get wrong in a way that looks fine: <c>{app:Loc}</c> binds to an INDEXER, and an indexer binding
/// listens for the property name <c>"Item[]"</c>, not for the key. Raise the wrong name and every binding
/// silently keeps the first language it saw, with a green build and a correct-looking screen in English.</para>
///
/// <para>⚠ It cannot be measured with one shipped language: with only English, a live binding and a frozen
/// one render identical text. So the catalog is swapped for a two-culture one that lives HERE, in the test
/// assembly — no pseudo-language ships. That is <see cref="Loc.UseCatalogForVerification"/>'s only purpose.</para>
///
/// <para>⚠ Constructs Avalonia controls, so this class joins the headless collection — never its own class
/// fixture (gotchas #94 / #226 / #286).</para>
///
/// <para>⚠⚠ <b>And it must RUN them on the session's dispatcher, which is what every other class in the
/// collection does and this one did not.</b> Creating the control on xunit's own thread made the binding's
/// initial delivery a POST rather than a synchronous write, so the assertion on the next line read
/// <c>block.Text == null</c> — intermittently, roughly one run in six. The failure looked like a liveness
/// defect (it named the two tests that switch languages) and was purely a test-harness one: both failed on
/// their FIRST assertion, before any language change had happened. ⛔ Do not "fix" a future flake here by
/// relaxing an assertion; the subject is a real binding on a real control, and it has to be exercised on the
/// thread the product binds on.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class LocalizationLivenessTests
{
    private readonly HeadlessUnitTestSession _session;

    public LocalizationLivenessTests(HeadlessSessionFixture fixture) => _session = fixture.Session;

    // A two-culture catalog built in memory: the neutral set is English, "qps-ploc" (a real pseudo-locale
    // Windows recognises, so CultureInfo accepts it) carries a distinguishable value for the same key.
    private const string Key = "SidebarPlaceholderEmpty";
    private const string English = "Add a connection to get started.";
    private const string Other = "[[translated]]";

    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    private sealed class TwoLanguageCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture)
            => Equals(culture, Pseudo) ? Other : English;
    }

    [Fact]
    public async System.Threading.Tasks.Task ABoundString_RereadsWhenTheLanguageChanges()
    {
        await _session.Dispatch(() =>
        {
            try
            {
                Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);

                var block = new TextBlock();
                block.Bind(TextBlock.TextProperty, new LocExtension(Key).ProvideValue());

                Assert.Equal(English, block.Text);

                // The whole mechanism, exercised the way the product exercises it.
                Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);

                Assert.Equal(Other, block.Text);
            }
            finally
            {
                Loc.UseCatalogForVerification(null, null);
            }
        }, default);
    }

    /// <summary>
    /// ⭐⭐ <b>The same claim, now against the SHIPPED Polish instead of a substitute catalog</b> — a real
    /// <c>TextBlock</c>, the real <c>{app:Loc}</c> binding, and the production entry point
    /// (<c>Loc.Apply</c>, which is what <c>App</c> calls when the language preference changes). Until the PL
    /// stage this could not exist: with one shipped language a live binding and a frozen one render the same
    /// text, which is why the substitute catalog above was built in the first place.
    ///
    /// <para>⚠ The expected values are READ from the two resource sets, never typed here. Transcribing
    /// "Anuluj" would make this a test of today's wording — it would go red the day a translator improves a
    /// label, for a reason that has nothing to do with the mechanism it is named after (#333).</para>
    /// </summary>
    [Fact]
    public async System.Threading.Tasks.Task ABoundString_SwitchesToTheShippedPolish_AndBack()
    {
        const string ProbeKey = nameof(UiStrings.DialogCancel);
        var catalog = new ResourceManager("EmberTern.App.Localization.Strings", typeof(UiStrings).Assembly);
        var english = catalog.GetString(ProbeKey, CultureInfo.InvariantCulture);
        var polish = catalog.GetString(ProbeKey, CultureInfo.GetCultureInfo("pl"));

        // If these ever coincide the test proves nothing — say so rather than passing vacuously.
        Assert.False(string.IsNullOrEmpty(polish), $"{ProbeKey} has no Polish entry.");
        Assert.NotEqual(english, polish);

        await _session.Dispatch(() =>
        {
            var previous = Loc.Culture;
            try
            {
                Loc.Apply(PreferenceOptions.LanguageEnglish);

                var block = new TextBlock();
                block.Bind(TextBlock.TextProperty, new LocExtension(ProbeKey).ProvideValue());
                Assert.Equal(english, block.Text);

                Loc.Apply(PreferenceOptions.LanguagePolish);
                Assert.Equal(polish, block.Text);

                // Back again — a one-way switch would still satisfy the assertion above.
                Loc.Apply(PreferenceOptions.LanguageEnglish);
                Assert.Equal(english, block.Text);
            }
            finally
            {
                Loc.Apply(previous.Name.Length == 0 ? PreferenceOptions.LanguageEnglish : previous.Name);
            }
        }, default);
    }

    /// <summary>
    /// The C# half of the same claim: a <c>UiStrings</c> member is a PROPERTY, so a read after the change
    /// returns the new language. A <c>const</c> or a <c>static readonly</c> field would fail this — which is
    /// exactly why neither form is allowed for a localized member.
    /// </summary>
    [Fact]
    public void AUiStringsMember_ReadsTheCurrentLanguage()
    {
        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);
            Assert.Equal(English, UiStrings.SidebarPlaceholderEmpty);

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
            Assert.Equal(Other, UiStrings.SidebarPlaceholderEmpty);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// The seam for consumers a binding cannot reach — anything that captured text once.
    /// ⚠ Pinned because it is the only mechanism those surfaces have, and a missing notification there is
    /// invisible: the screen keeps working, in the previous language.
    /// </summary>
    [Fact]
    public void LanguageChanged_FiresForCaptureOnceConsumers()
    {
        var fired = 0;
        void Handler(object? sender, System.EventArgs e) => fired++;

        Loc.LanguageChanged += Handler;
        try
        {
            // ⚠ The first Apply is a REAL change (at rest the culture is Invariant, not "en"), so it fires.
            // The assertion that matters is the SECOND one: re-applying the same language must be silent,
            // because every PreferencesService.Changed notification reaches Loc.Apply — a theme toggle would
            // otherwise make every capture-once surface rebuild and discard user state.
            Loc.Apply("en");
            var afterFirst = fired;
            Loc.Apply("en");
            var afterRepeat = fired;

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);

            Assert.Equal(1, afterFirst);
            Assert.Equal(1, afterRepeat);
            Assert.Equal(2, fired);
        }
        finally
        {
            Loc.LanguageChanged -= Handler;
            Loc.UseCatalogForVerification(null, null);
        }
    }
}

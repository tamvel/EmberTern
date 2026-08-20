using System;
using System.Globalization;
using System.Resources;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using EmberTern.LicenseManager;
using EmberTern.LicenseManager.Localization;
using EmberTern.LicenseManager.Settings;
using Xunit;

namespace EmberTern.LicenseManager.Tests;

/// <summary>
/// ⭐⭐ <b>The one measurement the whole live-switching design stands on: does a bound string actually
/// re-read when the language changes?</b>
///
/// <para>Everything else in L8 is mechanical — migrating members, migrating XAML — and all of it is wasted
/// if the notification never reaches a realised control. The claim is easy to state and easy to get wrong
/// in a way that looks fine: EmberTern's first version bound an INDEXER, which delivers its initial value
/// correctly and then never updates, so the screen renders right on load and freezes. ⛔ That is why this
/// application binds a plain property on a small per-key object, and why this test exists rather than a
/// reading of the code.</para>
///
/// <para>⚠⚠ <b>It cannot be measured with one shipped language.</b> With only English, a live binding and a
/// frozen one render identical text. So the catalog is swapped for a two-culture one defined HERE, in the
/// test assembly — ⛔ no pseudo-language ships, and <see cref="Loc.UseCatalogForVerification"/> has no other
/// purpose and no production caller.</para>
///
/// <para>⚠⚠ <b>Every test here RETURNS its <c>Task</c></b> (gotcha #374). <c>Dispatch</c> returns one, and
/// the expression-bodied <c>void</c> form compiles while discarding it — xUnit then never awaits, and no
/// assertion in the body can fail the test.</para>
///
/// <para>⚠ Constructs Avalonia controls, so it joins <see cref="ManagerHeadlessCollection"/> — never its own
/// class fixture (#94 / #226 / #286).</para>
/// </summary>
[Collection(ManagerHeadlessCollection.Name)]
public sealed class LocalizationLivenessTests
{
    private readonly HeadlessUnitTestSession _session;

    public LocalizationLivenessTests(ManagerHeadlessSessionFixture fixture) => _session = fixture.Session;

    private const string Key = "Settings.WindowTitle";
    private const string English = "Settings";
    private const string Other = "[[translated]]";

    // ⚠ A real pseudo-locale Windows recognises, so CultureInfo accepts it without a custom culture.
    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    /// <summary>A catalog whose answer depends on the culture — the instrument this file needs.</summary>
    private sealed class TwoLanguageCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture)
            => Equals(culture, Pseudo) ? Other : English;
    }

    /// <summary>
    /// ⭐⭐ <b>THE measurement.</b> A realised <c>TextBlock</c> bound with <c>{lm:Loc}</c> shows the new
    /// language after a change, without being rebuilt.
    /// </summary>
    [Fact]
    public Task ABoundString_RereadsWhenTheLanguageChanges() =>
        _session.Dispatch(() =>
        {
            using var isolated = Loc.IsolateSubscribersForVerification();

            try
            {
                Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);

                var block = new TextBlock();
                block[!TextBlock.TextProperty] = new LocExtension(Key).ProvideValue();

                // ⚠ The window is what realises the control and delivers the binding synchronously; a
                //   TextBlock nobody arranged answers about nothing.
                var window = new Window { Content = block };
                window.Show();
                window.UpdateLayout();

                Assert.Equal(English, block.Text);

                Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
                window.UpdateLayout();

                // ⭐ The control was NOT rebuilt, re-bound or re-created. If this line reads the old value,
                //   the mechanism is frozen — and the screen would have looked perfectly correct until the
                //   moment somebody switched languages.
                Assert.Equal(Other, block.Text);

                window.Close();
                return true;
            }
            finally
            {
                Loc.UseCatalogForVerification(null, null);
            }
        }, default);

    /// <summary>The same claim for a C# read: a catalog PROPERTY follows the language too.</summary>
    /// <remarks>
    /// ⭐ The other half of the mechanism. XAML is served by the binding; C# consumers are served by the
    /// property shape, and a <c>static readonly</c> would pass the binding test and fail this one.
    /// </remarks>
    [Fact]
    public void ACatalogMember_ReadsTheCurrentLanguage()
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);
            Assert.Equal(English, ManagerSettingsCatalog.WindowTitle);

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
            Assert.Equal(Other, ManagerSettingsCatalog.WindowTitle);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// <see cref="Loc.LanguageChanged"/> fires for capture-once consumers — and only on a REAL change.
    /// </summary>
    /// <remarks>
    /// ⚠ The second half is what stops an unrelated preference write from rebuilding a surface: applying
    /// the same language must be silent, or every save would look like a language change.
    /// </remarks>
    [Fact]
    public void LanguageChanged_FiresOnlyForARealChange()
    {
        using var isolated = Loc.IsolateSubscribersForVerification();

        try
        {
            Loc.Apply(ApplicationLanguages.English);

            var fired = 0;
            void Handler(object? sender, EventArgs e) => fired++;

            Loc.LanguageChanged += Handler;
            try
            {
                Loc.Apply(ApplicationLanguages.English);
                Assert.Equal(0, fired);

                Loc.Apply(ApplicationLanguages.Polish);
                Assert.Equal(1, fired);

                Loc.Apply(ApplicationLanguages.Polish);
                Assert.Equal(1, fired);
            }
            finally
            {
                Loc.LanguageChanged -= Handler;
            }
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// ⭐ The composition root's path works end to end: a stored preference reaches a bound control.
    /// </summary>
    /// <remarks>
    /// ⚠ It exercises the REAL production sequence — read <c>ui.json</c>, hand the code to
    /// <see cref="Loc.Apply"/> — rather than calling <c>Apply</c> with a literal, because the interesting
    /// question is whether the stored value survives the trip.
    /// </remarks>
    [Fact]
    public Task AStoredPreference_ReachesTheInterface() =>
        _session.Dispatch(() =>
        {
            using var isolated = Loc.IsolateSubscribersForVerification();

            var folder = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "etlm-tests", Guid.NewGuid().ToString("N"));

            try
            {
                var store = new ManagerPreferencesStore(System.IO.Path.Combine(folder, "ui.json"));
                Assert.True(store.Save(new ManagerPreferences { Language = ApplicationLanguages.Polish }));

                Loc.Apply(store.Load().Language);
                Assert.Equal(ApplicationLanguages.Polish, Loc.Culture.TwoLetterISOLanguageName);

                // ⚠ Polish has no translation yet — L8.5 introduces it — so the words are still English.
                //   ⭐ That is the correct answer and worth pinning: an untranslated key must fall back
                //   neutral-ward rather than render blank or throw.
                Assert.Equal("Settings", ManagerSettingsCatalog.WindowTitle);

                return true;
            }
            finally
            {
                Loc.Apply(ApplicationLanguages.Default);
                try
                {
                    System.IO.Directory.Delete(folder, recursive: true);
                }
                catch (System.IO.IOException)
                {
                    // A leftover temporary folder is not worth failing a test over.
                }
            }
        }, default);
}

using System;
using System.Globalization;
using System.Resources;
using EmberTern.App.Localization;
using EmberTern.Core.Connections;
using EmberTern.Core.Localization;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The third Core/Firebird producer on decision <b>D‑3</b>, and the textbook case for the boundary the
/// decision draws: <b>our sentence is the key, the server's sentence is an argument.</b>
///
/// <para>⭐ These guards exist mostly to protect the <i>seam</i>, not the words. The interesting failure is
/// not "a label went untranslated" — it is a raw server message quietly becoming a translated one, or the
/// <c>Legacy_Auth</c> recognition starting to read EmberTern's text instead of Firebird's, which would be
/// invisible in English and would break on the day a second language ships.</para>
///
/// <para>⚠ Joins the headless collection: it swaps <c>Loc</c>'s catalog, which is process-global state
/// (localization.md §5.4).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class FirebirdConnectionLocalizationTests
{
    private static ConnectionProfile Profile() => new()
    {
        Name = "lab",
        Host = "localhost",
        Port = 3050,
        Username = "SYSDBA",
    };

    // What a server refusal actually looks like: the driver concatenates the whole GDS vector, so the text is
    // long, English, and none of it is ours.
    private const string RawServerText =
        "Your user name and password are not defined. Ask your database administrator to set up a Firebird login.";

    private const string LegacyAuthText =
        "Error occurred during login, please check server firebird.log for details. " +
        "Not supported plugin 'Legacy_Auth'";

    // ── The boundary: our sentence is the key, the server's is data ──────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The server's message travels as an ARGUMENT, verbatim.</b> Not paraphrased, not keyed, not
    /// re-wrapped — the exact characters Firebird produced, so the user is never left with less information
    /// than the engine gave, in any language.
    /// </summary>
    [Fact]
    public void TheServersOwnMessage_TravelsAsDataAndReachesTheUserVerbatim()
    {
        var localized = FirebirdConnectionService.MapError(new InvalidOperationException(RawServerText), Profile());

        Assert.Equal(FirebirdConnectionMessages.Failed, localized.Key);
        Assert.Equal("localhost:3050", localized.Arguments[0]);
        Assert.Equal(RawServerText, localized.Arguments[1]); // byte-for-byte, not a rendering of it

        Assert.Contains(RawServerText, Loc.Format(localized), StringComparison.Ordinal);
    }

    /// <summary>
    /// The anti-drift guard for the exception's two descriptions. <c>Message</c> stays English for logs and
    /// for any catch-all nobody enumerated; <see cref="ConnectionFailedException.Localized"/> is what the UI
    /// resolves. ⚠ Two copies of one sentence is a real cost, and this is what stops it becoming a defect:
    /// edit the resource entry alone and the log would keep speaking an older wording than the screen.
    /// </summary>
    [Theory]
    [InlineData(RawServerText)]
    [InlineData(LegacyAuthText)]
    public void TheEnglishFallback_SaysExactlyWhatTheLocalizedFormResolvesTo(string serverText)
    {
        var profile = Profile();
        var ex = new InvalidOperationException(serverText);

        var fallback = FirebirdConnectionService.MapErrorMessage(ex, profile);
        var resolved = Loc.Format(FirebirdConnectionService.MapError(ex, profile));

        Assert.Equal(fallback, resolved);
    }

    [Theory]
    [InlineData("WI-V2.5.9.27139 Firebird 2.5")]
    [InlineData(null)]
    [InlineData("   ")]
    public void TheUnsupportedServerRefusal_AlsoAgreesWithItsFallback(string? version)
        => Assert.Equal(
            FirebirdConnectionService.UnsupportedServerMessage(version),
            Loc.Format(FirebirdConnectionService.UnsupportedServer(version)));

    // ── Legacy_Auth recognition ──────────────────────────────────────────────────────────────────────────

    private static readonly CultureInfo Pseudo = CultureInfo.GetCultureInfo("qps-ploc");

    private sealed class TwoLanguageCatalog : ResourceManager
    {
        public override string GetString(string name, CultureInfo? culture)
            => Equals(culture, Pseudo) ? "[[" + name + " {0} {1}]]" : "EN " + name + " {0} {1}";
    }

    /// <summary>
    /// ⛔⛔ <b>The one that matters most in this etap: recognition reads FIREBIRD's text, never ours.</b>
    /// The refusal carries no SQLSTATE and no GDS code, so its message is the only signal — but it is the
    /// engine's message, and the engine does not speak the user's language.
    ///
    /// <para>⭐ Proven by running the recognition <i>while the application is in another language</i>: if
    /// anyone ever rewires it to match against a resolved EmberTern string, the comparison becomes
    /// English-vs-translation and this test goes red. In English alone the defect would be invisible, which is
    /// precisely why the assertion is made under a language switch rather than beside it.</para>
    /// </summary>
    [Fact]
    public void LegacyAuthIsRecognisedFromTheServerText_WhateverLanguageTheAppIsIn()
    {
        var profile = Profile();
        var ex = new InvalidOperationException(LegacyAuthText);

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);

            var localized = FirebirdConnectionService.MapError(ex, profile);

            Assert.Equal(FirebirdConnectionMessages.SrpAuthentication, localized.Key);
            Assert.Equal("localhost:3050", Assert.Single(localized.Arguments));
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// The other half of the ratified boundary: the SRP wording <b>replaces</b> the server's text (it is the
    /// single sanctioned rewrite), while every other failure <b>passes it through</b>. ⚠ Pinned because the
    /// tempting "simplification" is to always pass the raw text and append the hint — which would restore the
    /// unreadable `Legacy_Auth` line the rewrite exists to hide.
    /// </summary>
    [Fact]
    public void OnlyTheSrpCase_ReplacesTheServerText()
    {
        var profile = Profile();

        var srp = FirebirdConnectionService.MapError(new InvalidOperationException(LegacyAuthText), profile);
        Assert.DoesNotContain("Legacy_Auth", Loc.Format(srp), StringComparison.OrdinalIgnoreCase);

        var other = FirebirdConnectionService.MapError(new InvalidOperationException(RawServerText), profile);
        Assert.Contains(RawServerText, Loc.Format(other), StringComparison.Ordinal);
    }

    // ── Live switching ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ A failure captured in one language must read in whatever language is current when it is SHOWN. The
    /// exception is built once — as it is in production, at the moment the connection fails — and resolved
    /// twice.
    ///
    /// <para>⚠ This is why the exception carries a <see cref="LocalizableMessage"/> rather than finished text:
    /// resolving in the producer would freeze the sentence, and the freeze would be invisible until someone
    /// switched language with a failure banner already on screen.</para>
    /// </summary>
    [Fact]
    public void AFailureRaisedBeforeTheSwitch_ReadsInTheNewLanguage()
    {
        var profile = Profile();
        var thrown = new ConnectionFailedException(
            FirebirdConnectionService.MapError(new InvalidOperationException(RawServerText), profile),
            FirebirdConnectionService.MapErrorMessage(new InvalidOperationException(RawServerText), profile));

        try
        {
            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), CultureInfo.InvariantCulture);
            Assert.StartsWith("EN Firebird.Connection.Failed", Loc.Format(thrown.Localized), StringComparison.Ordinal);

            Loc.UseCatalogForVerification(new TwoLanguageCatalog(), Pseudo);
            Assert.StartsWith("[[Firebird.Connection.Failed", Loc.Format(thrown.Localized), StringComparison.Ordinal);

            // ⚠ And through all of it the English fallback is untouched — it is a different job.
            Assert.Contains(RawServerText, thrown.Message, StringComparison.Ordinal);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }
}

using System;
using System.Globalization;
using System.Resources;
using EmberTern.App;
using EmberTern.App.Localization;
using EmberTern.Core.Localization;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The charset guard as a decision <b>D‑3</b> producer: <b>our sentence is the key, the user's data are the
/// arguments</b> — and the whole sentence reaches the reader in the reader's language.
///
/// <para>⭐⭐ <b>These exist because the defect actually shipped and was caught by hand, not by the suite.</b>
/// Phase 5 built both resource entries correctly and then never read them: the refusal is WRAPPED into
/// <c>QueryExecutionException</c> on its way out, and the display site read <c>ex.Message</c> — the English
/// fallback. A Polish user got a fully English paragraph, with a green build, 8 844 green tests, and a
/// perfectly good Polish entry in <c>Strings.pl.resx</c> that nothing ever resolved. So the load-bearing
/// assertion here is not "the words exist" but <b>"the words arrive through the wrapping"</b>
/// (<see cref="TheRefusal_ResolvesThroughTheCatalog_EvenAfterItIsWrappedForTheEditor"/>).</para>
///
/// <para>⚠⚠ <b>Read this before adding a test here: this class must never mutate the process-global language
/// state — neither the culture nor the catalog.</b> Most <c>UiStrings</c>-reading tests
/// (<c>TerminologyTests</c>, <c>DebuggerTabVmTests</c>, <c>DataImportNewTableTests</c>, …) are NOT in the
/// headless collection, so they run in PARALLEL and would start reading whatever this swapped in. Measured
/// twice while writing this file, on a suite that had been green six runs running: a draft using the culture
/// switch produced <b>3 failures in 3 unrelated classes</b>, and a draft using the catalog seam produced
/// <b>2 more</b> — roughly one run in three, every one of them a flake manufactured by the test rather than a
/// defect in the product.</para>
///
/// <para>⭐ So the wording is asserted against the resource sets <b>directly</b>, and the resolution mechanism
/// is proved by giving the wrapper a message that DIFFERS from the refusal's — which separates "read the
/// wrapper" from "read the inner localized form" without any language switching at all. The class still joins
/// the headless collection, but now only to protect ITSELF from the classes that do swap.</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CharsetGuardLocalizationTests
{
    private static readonly ResourceManager Catalog =
        new("EmberTern.App.Localization.Strings", typeof(UiStrings).Assembly);

    private const string StatementKey = "Charset.Unrepresentable.Statement";
    private const string ParameterKey = "Charset.Unrepresentable.Parameter";

    private static readonly CultureInfo PolishCulture = CultureInfo.GetCultureInfo("pl");

    private static string English(string key) => Catalog.GetString(key, CultureInfo.InvariantCulture)!;
    private static string Polish(string key) => Catalog.GetString(key, PolishCulture)!;

    // Never opened — the guard is entirely client-side. Port 1 so an accidental Open() fails loudly.
    private static FbConnection Connection(string charset) => new(new FbConnectionStringBuilder
    {
        DataSource = "127.0.0.1", Port = 1, Database = "loc-tests.fdb",
        UserID = "SYSDBA", Password = "x", Charset = charset, Dialect = 3, Pooling = false,
    }.ToString());

    private static CharsetRepresentationException StatementRefusal()
    {
        using var connection = Connection("WIN1250");
        return Assert.Throws<CharsetRepresentationException>(
            () => connection.CreateGuardedCommand("SELECT 'Ж' FROM RDB$DATABASE"));
    }

    private static CharsetRepresentationException ParameterRefusal()
    {
        using var connection = Connection("WIN1250");
        using var cmd = connection.CreateGuardedCommand("SELECT * FROM T WHERE C = @p");
        return Assert.Throws<CharsetRepresentationException>(() => cmd.AddGuardedParameter("@p", "Ж"));
    }

    /// <summary>Renders a message against one resource set, exactly as <c>Loc.Format</c> would.</summary>
    private static string Render(LocalizableMessage message, CultureInfo culture)
    {
        var format = Catalog.GetString(message.Key.Value, culture)!;
        return string.Format(culture, format, message.Arguments is { Count: > 0 }
            ? System.Linq.Enumerable.ToArray(message.Arguments)
            : Array.Empty<object?>());
    }

    // ── The premise ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Both keys must exist in both sets and genuinely differ, or every assertion below would pass on an
    /// untranslated entry. ⛔ The Polish is never transcribed into this file — that would turn a mechanism
    /// test into a test of today's wording (#333).
    /// </summary>
    [Theory]
    [InlineData(StatementKey)]
    [InlineData(ParameterKey)]
    public void BothMessages_ExistInBothLanguages_AndAreActuallyTranslated(string key)
    {
        Assert.False(string.IsNullOrWhiteSpace(English(key)));
        Assert.False(string.IsNullOrWhiteSpace(Polish(key)));
        Assert.NotEqual(English(key), Polish(key));
    }

    // ── Polish ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b>The whole statement refusal reads in Polish and still carries every fact.</b> The producer's real
    /// message (key + arguments) is rendered against the Polish set, so this measures what a Polish user sees.
    /// </summary>
    [Fact]
    public void TheStatementRefusal_ReadsEntirelyInPolish_WithAllItsData()
    {
        var shown = Render(StatementRefusal().Localized, PolishCulture);

        // Every dynamic fact survives the translation — the half a re-wording could silently drop.
        Assert.Contains("Ж", shown, StringComparison.Ordinal);        // the character
        Assert.Contains("U+0416", shown, StringComparison.Ordinal);   // its code point
        Assert.Contains("8", shown, StringComparison.Ordinal);        // its position
        Assert.Contains("WIN1250", shown, StringComparison.Ordinal);  // the connection charset

        // ⛔ And nothing English leaked: no placeholder survived, and none of the fallback's phrases appear.
        Assert.DoesNotContain("{0}", shown, StringComparison.Ordinal);
        AssertNoEnglishFragments(shown);
    }

    /// <summary>The parameter refusal, same guarantee — including the parameter's own name.</summary>
    [Fact]
    public void TheParameterRefusal_ReadsEntirelyInPolish_WithAllItsData()
    {
        var shown = Render(ParameterRefusal().Localized, PolishCulture);

        Assert.Contains("@p", shown, StringComparison.Ordinal);
        Assert.Contains("Ж", shown, StringComparison.Ordinal);
        Assert.Contains("U+0416", shown, StringComparison.Ordinal);
        Assert.Contains("WIN1250", shown, StringComparison.Ordinal);

        Assert.DoesNotContain("{0}", shown, StringComparison.Ordinal);
        AssertNoEnglishFragments(shown);
    }

    /// <summary>The phrases that would betray a half-translated sentence — rule 12's "no English fragment
    /// concatenated onto a catalog sentence".</summary>
    private static void AssertNoEnglishFragments(string shown)
    {
        foreach (var fragment in new[]
                 {
                     "The statement contains", "Parameter", "cannot represent",
                     "nothing was sent", "would have changed it silently", "connection character set",
                     "or remove the character",
                 })
        {
            Assert.DoesNotContain(fragment, shown, StringComparison.Ordinal);
        }
    }

    // ── English ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheStatementRefusal_ReadsEntirelyInEnglish_WithAllItsData()
    {
        var shown = Render(StatementRefusal().Localized, CultureInfo.InvariantCulture);

        Assert.Contains("Ж", shown, StringComparison.Ordinal);
        Assert.Contains("U+0416", shown, StringComparison.Ordinal);
        Assert.Contains("8", shown, StringComparison.Ordinal);
        Assert.Contains("WIN1250", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void TheParameterRefusal_ReadsEntirelyInEnglish_WithAllItsData()
    {
        var shown = Render(ParameterRefusal().Localized, CultureInfo.InvariantCulture);

        Assert.Contains("@p", shown, StringComparison.Ordinal);
        Assert.Contains("Ж", shown, StringComparison.Ordinal);
        Assert.Contains("U+0416", shown, StringComparison.Ordinal);
        Assert.Contains("WIN1250", shown, StringComparison.Ordinal);
        Assert.DoesNotContain("{0}", shown, StringComparison.Ordinal);
    }

    /// <summary>
    /// The anti-drift guard for the exception's two descriptions, matching
    /// <c>FirebirdConnectionLocalizationTests</c>: <c>Message</c> stays English for logs and for any catch-all
    /// nobody enumerated, <c>Localized</c> is what the UI resolves. ⚠ Two copies of one sentence is a real
    /// cost, and this is what stops it becoming a defect — edit the resource entry alone and the log would
    /// keep speaking an older wording than the screen.
    /// </summary>
    [Fact]
    public void TheEnglishFallback_SaysExactlyWhatTheEnglishEntryResolvesTo()
    {
        var statement = StatementRefusal();
        var parameter = ParameterRefusal();

        Assert.Equal(statement.Message, Render(statement.Localized, CultureInfo.InvariantCulture));
        Assert.Equal(parameter.Message, Render(parameter.Localized, CultureInfo.InvariantCulture));
    }

    // ── The wrapping — the defect that was actually shipped ───────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The regression guard for the reported defect.</b> A refusal reaches the SQL editor, the object
    /// editors and the grid editor <i>wrapped</i> in a domain exception whose <c>Message</c> is the English
    /// fallback. The display path must still resolve the localized form — which it does only because
    /// <see cref="ErrorText"/> walks the <c>InnerException</c> chain instead of reading the outermost message.
    ///
    /// <para>⭐ <b>The wrapper is given a DIFFERENT message on purpose.</b> In production it copies the inner
    /// one, which would make "read the wrapper" and "read the inner" indistinguishable in English — the test
    /// would pass against the very bug it is meant to catch. A distinct wrapper message separates them with no
    /// language switching at all, which is what keeps this class free of process-global state (see the class
    /// remarks). ⚠ Asserted at TWO depths: "works when wrapped once" was already true of a design that broke
    /// the moment anything re-wrapped it. Verified RED against the pre-fix behaviour before being accepted.</para>
    /// </summary>
    [Fact]
    public void TheRefusal_ResolvesTheLocalizedForm_EvenAfterItIsWrappedForTheEditor()
    {
        const string WrapperText = "wrapper text that is not the refusal";

        var refusal = StatementRefusal();
        var wrappedOnce = new QueryExecutionException(WrapperText, refusal);
        var wrappedTwice = new DdlExecutionException(WrapperText, wrappedOnce);

        var expected = Loc.Format(refusal.Localized);

        Assert.Equal(expected, ErrorText.Of(refusal));
        Assert.Equal(expected, ErrorText.Of(wrappedOnce));
        Assert.Equal(expected, ErrorText.Of(wrappedTwice));

        // The thing that used to happen: the display site read the WRAPPER's message.
        Assert.NotEqual(WrapperText, ErrorText.Of(wrappedOnce));
        Assert.NotEqual(WrapperText, ErrorText.Of(wrappedTwice));

        // The user's data still reach the sentence after the unwrapping.
        Assert.Contains("Ж", ErrorText.Of(wrappedTwice), StringComparison.Ordinal);
        Assert.Contains("WIN1250", ErrorText.Of(wrappedTwice), StringComparison.Ordinal);
    }

    /// <summary>
    /// ⛔ The other half of the boundary: a failure that is NOT ours passes through untouched. A raw Firebird
    /// error has no localized form, so <see cref="ErrorText"/> returns the server's own words — never a
    /// translation of them, and never an empty string.
    /// </summary>
    [Fact]
    public void AFailureThatIsNotOurs_KeepsTheServersOwnWords()
    {
        const string ServerText = "unsuccessful metadata update -- object TBL is in use";

        Assert.Equal(ServerText, ErrorText.Of(new DdlExecutionException(ServerText)));
        Assert.Equal(ServerText, ErrorText.Of(new QueryExecutionException(ServerText)));
        Assert.Equal(string.Empty, ErrorText.Of(null));
    }

    // ⛔ There is deliberately NO "reads in the language current when shown" test here. The liveness of a
    // LocalizableMessage is a property of the MECHANISM, already pinned once by
    // FirebirdConnectionLocalizationTests.AFailureRaisedBeforeTheSwitch_ReadsInTheNewLanguage — and pinning it
    // a second time would require swapping the process-global catalog, which is exactly what this class must
    // not do (class remarks). A duplicate guard is not worth manufacturing a flake for.
}

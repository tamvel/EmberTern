using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App.Controls;
using EmberTern.App.Localization;
using EmberTern.App.ViewModels;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Core.Localization;
using EmberTern.Office;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Etap C8 — the two workbook readers on decision <b>D‑3</b>, and the first producer outside Core/Firebird.
///
/// <para>⭐⭐ <b>These guards carry more weight than their siblings in earlier etaps, and that is a measurement
/// rather than a claim.</b> C4a could prove zero change in wording by leaving ~20 existing tests untouched;
/// C6 could do it with <c>ExecutionSummaryTests</c>. Here the pre-migration pins were <b>two
/// <c>Assert.Contains</c> calls on one of the two sentences</b> and nothing at all on the other, so no
/// existing test could carry that proof. The dual-form check below is therefore the ONLY machine check that
/// the English these providers speak has not moved — which is exactly why it reads the sentence off a REAL
/// thrown exception rather than off a re-derived string.</para>
///
/// <para>⚠ Joins the headless collection: <c>Loc</c>'s catalog is process-global state, and a test that
/// resolves against it must not race one that swaps it (localization.md §5.4).</para>
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ImportSourceLocalizationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "embertern-c8-" + Guid.NewGuid().ToString("N"));

    public ImportSourceLocalizationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    // The two signatures that make a file "not what its name claims", and the whole reason these refusals
    // exist: a ZIP header is an OOXML package, an OLE2 header is a BIFF workbook.
    private static readonly byte[] OoxmlHeader = { 0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0 };
    private static readonly byte[] BiffHeader = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    // ── The anti-drift guard for the two descriptions ────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <b><c>Message</c> must say exactly what <see cref="ImportSourceException.Localized"/> resolves to in
    /// English.</b> Two copies of one sentence is a real cost; this is what stops it becoming a defect — edit
    /// the resource entry alone and every catch-all reading <c>ex.Message</c> would keep speaking the older
    /// wording, silently, with the screen already showing the newer one.
    ///
    /// <para>⚠ It drives the REAL providers over REAL malformed files rather than constructing the exception,
    /// so it also pins that each provider passes the key its own format belongs to. A copy-paste that gave the
    /// <c>.xls</c> reader the <c>.xlsx</c> key would produce a perfectly grammatical sentence advising the user
    /// to rename the file to the extension it already has.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(BothRefusals))]
    public async Task TheEnglishFallback_SaysExactlyWhatTheLocalizedFormResolvesTo(
        string fileName, byte[] header, string _)
    {
        var thrown = await RefusalFor(fileName, header);

        Assert.Equal(thrown.Message, Loc.Format(thrown.Localized));
    }

    /// <summary>
    /// The two sentences are not interchangeable: each names the format the file CLAIMS to be and recommends
    /// the other one, so a provider handed the wrong key would advise renaming a file to the extension it
    /// already has.
    ///
    /// <para>⚠⚠ <b>The rationale first written here was FALSE and the plant disproved it.</b> It claimed the
    /// dual-form check could not see a key swap because "both halves would still agree with each other".
    /// Measured: swapping the keys turns BOTH this test and
    /// <see cref="TheEnglishFallback_SaysExactlyWhatTheLocalizedFormResolvesTo"/> red — the English literal
    /// stays at the producer while the resolved entry moves, so they disagree. The dual-form check sees it
    /// only because these two sentences happen to differ in English; that is a property of today's wording,
    /// not of the mechanism.</para>
    ///
    /// <para>⭐ So what this test earns is not visibility but a <b>correct diagnosis</b>: it fails with
    /// "expected Import.Source.NotReadableXls", naming the defect, where its sibling fails with an English
    /// mismatch and sends the reader to the resource file — the one place that is innocent.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(BothRefusals))]
    public async Task EachProvider_RaisesItsOwnKey(string fileName, byte[] header, string expectedKey)
    {
        var thrown = await RefusalFor(fileName, header);

        Assert.Equal(expectedKey, thrown.Localized.Key.Value);
        Assert.Equal(new object?[] { fileName }, thrown.Localized.Arguments);
    }

    /// <summary>
    /// ⭐⭐ <b>The library's own words never reach the user, and that is the point of the whole refusal.</b>
    /// Handed an intact old workbook, <c>DocumentFormat.OpenXml</c> answers <i>File contains corrupted data</i>
    /// — a sentence that is not merely unhelpful but FALSE. So unlike <c>FirebirdConnectionMessages</c>, where
    /// the server's text travels as an argument because it is authoritative, here it is kept on
    /// <c>InnerException</c> for a developer and is deliberately absent from both user-facing forms.
    ///
    /// <para>⚠ The inner exception is still asserted PRESENT: dropping it would make the same diagnosis
    /// impossible to reach a second time.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(BothRefusals))]
    public async Task TheReaderLibrarysOwnMessage_StaysOnTheInnerExceptionAndReachesNoUserForm(
        string fileName, byte[] header, string _)
    {
        var thrown = await RefusalFor(fileName, header);

        Assert.NotNull(thrown.InnerException);

        var libraryText = thrown.InnerException!.Message;
        Assert.DoesNotContain(libraryText, thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(libraryText, Loc.Format(thrown.Localized), StringComparison.Ordinal);
    }

    /// <summary>
    /// The file name is DATA and travels verbatim — the one thing in these sentences that is not language.
    /// </summary>
    [Theory]
    [MemberData(nameof(BothRefusals))]
    public async Task TheFileName_TravelsAsDataAndReachesTheUserVerbatim(
        string fileName, byte[] header, string _)
    {
        var thrown = await RefusalFor(fileName, header);

        Assert.Contains(fileName, Loc.Format(thrown.Localized), StringComparison.Ordinal);
    }

    // ── The App half: does the localized form actually reach the screen? ─────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The one test that proves the migration reached the user, and it drives the REAL surface.</b>
    /// Everything above pins the producer; this points <c>DataImportTabViewModel</c> at a BIFF workbook wearing
    /// an <c>.xlsx</c> name and reads the banner it publishes — through the real recalculation chain, the real
    /// catch-all, and the real <c>Describe</c>.
    ///
    /// <para>⚠ Why a producer-side pin was not enough: etap C2's finding (#355) was exactly this gap. The
    /// <c>string</c> → <c>MessageKey</c> change compiled cleanly at a site that CONCATENATED the value, so the
    /// key itself would have been rendered on screen while every key-side guard stayed green — because they all
    /// resolve the key themselves and therefore test the catalog, not the screen.</para>
    ///
    /// <para>⚠⚠ <b>It runs against a SWAPPED catalog, and the first version — which did not — was green for
    /// two reasons and pinned neither.</b> English is the only shipped language, so
    /// <c>Loc.Format(localized)</c> and <c>ex.Message</c> render the same characters by the dual form's own
    /// guarantee. Measured: with the consumer reverted to <c>SetStatus(ex.Message, …)</c> the earlier version
    /// stayed <b>green</b>. A catalog that answers differently is the only way to tell "the App resolved the
    /// key" from "the App printed the English fallback" — the same shape as #357, and as C6's
    /// <c>SwitchingLanguage_…</c>.</para>
    ///
    /// <para>⚠ It asserts EQUALITY, not <c>Contains</c>: a <c>Contains</c> check passes for a banner that
    /// appends a stack trace or the raw library text, both of which this etap exists to prevent.</para>
    ///
    /// <para>⛔ This is what makes the class's membership of <see cref="HeadlessCollection"/> load-bearing
    /// rather than precautionary — <c>Loc</c>'s catalog is process-global and the swap is undone in
    /// <c>finally</c>.</para>
    /// </summary>
    [Fact]
    public async Task TheRefusalReachesTheImportSurface_AsResolvedTextAndNothingElse()
    {
        var path = Path.Combine(_directory, "przebrany.xlsx");
        await File.WriteAllBytesAsync(path, BiffHeader);

        try
        {
            Loc.UseCatalogForVerification(new TranslatingCatalog(), CultureInfo.InvariantCulture);

            var vm = new DataImportTabViewModel(new DataImportEnvironment(() => false, () => "—"))
            {
                PreviewDebounce = TimeSpan.Zero,
            };

            await SettleAsync(vm);
            vm.Source.FilePath = path;
            await SettleAsync(vm);

            Assert.Equal(MessageSeverity.Error, vm.StatusSeverity);
            Assert.Equal(TranslatingCatalog.Translated, vm.StatusMessage);
        }
        finally
        {
            Loc.UseCatalogForVerification(null, null);
        }
    }

    /// <summary>
    /// A catalog whose answer no producer's English literal can accidentally match. ⛔ Deliberately not a
    /// pseudo-language that ships — it exists only so a resolved key is distinguishable from a fallback
    /// (the purpose <c>Loc.UseCatalogForVerification</c> documents for itself).
    /// </summary>
    private sealed class TranslatingCatalog : ResourceManager
    {
        internal const string Translated = "[[nieczytelny skoroszyt]]";

        public override string GetString(string name, CultureInfo? culture) => Translated;
    }

    /// <summary>Drains the recalculation chain — the shape every Data Import view-model test uses.</summary>
    private static async Task SettleAsync(DataImportTabViewModel vm)
    {
        for (var i = 0; i < 10; i++)
        {
            var pending = vm.PendingRecalculation;
            if (pending is null) return;
            await pending.ConfigureAwait(false);
            if (ReferenceEquals(pending, vm.PendingRecalculation)) return;
        }
    }

    public static IEnumerable<object[]> BothRefusals() => new[]
    {
        new object[] { "przebrany.xlsx", BiffHeader, "Import.Source.NotReadableXlsx" },
        new object[] { "przebrany.xls", OoxmlHeader, "Import.Source.NotReadableXls" },
    };

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes a file whose bytes contradict its extension and returns the refusal the matching provider
    /// raises. ⭐ The provider is chosen the way <c>DataImportTabViewModel.ProviderFor</c> chooses it — by the
    /// source KIND — so the pairing under test is the one the product actually makes.
    /// </summary>
    private async Task<ImportSourceException> RefusalFor(string fileName, byte[] header)
    {
        var path = Path.Combine(_directory, fileName);
        await File.WriteAllBytesAsync(path, header);

        var kind = Path.GetExtension(fileName) == ".xlsx" ? ImportSourceKind.Xlsx : ImportSourceKind.Xls;
        IImportProvider provider = kind == ImportSourceKind.Xlsx
            ? new XlsxImportProvider()
            : new XlsImportProvider();

        var configuration = ImportConfiguration.Empty with
        {
            Source = SourceDescriptor.File(kind, fileName),
            Delimited = null,
            Spreadsheet = new SpreadsheetOptions { FirstDataRow = 2 },
        };

        return await Assert.ThrowsAsync<ImportSourceException>(
            () => provider.ReadSchemaAsync(
                new FileImportSource(path), configuration, CancellationToken.None));
    }
}

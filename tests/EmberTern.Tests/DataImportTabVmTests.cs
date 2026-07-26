using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Import;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I5: the working surface's coordinator.
/// <para>
/// The load-bearing test here is <see cref="Configuration_SurvivesABuildApplyRoundTrip"/>. Design §4.8.6 makes
/// one promise — that named profiles can arrive later as pure UI over an existing store — and that promise is
/// only true while every user decision passes through the ONE record. A setting added straight to a section
/// VM would be invisible to a saved profile, and the defect would surface in etap I11 as "please rebuild the
/// surface". This test is the thing that fails first instead.
/// </para>
/// </summary>
public class DataImportTabVmTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "et-import-" + Guid.NewGuid().ToString("N"));

    public DataImportTabVmTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static DataImportTabViewModel Vm(
        bool connected = true, bool transactionOpen = false, string connectionName = "LAB")
        => new(() => connected, () => transactionOpen, () => connectionName);

    private static async Task SettleAsync(DataImportTabViewModel vm)
    {
        // The chain is lazy and cancellable, and a change supersedes the previous read — so settle by
        // awaiting until no new work was queued while the last batch ran.
        for (var i = 0; i < 10; i++)
        {
            var pending = vm.PendingRecalculation;
            if (pending is null) return;
            await pending.ConfigureAwait(false);
            if (ReferenceEquals(pending, vm.PendingRecalculation)) return;
        }
    }

    // ── The one representation (§4.8.1 / §4.8.6) ────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Every decision the surface offers must survive being written to the record and read back. This is the
    /// mechanism §4.8.6 asks for at the App layer — the Core half is already pinned by
    /// <c>ImportConfigurationRoundTripTests</c>.
    /// </summary>
    [Fact]
    public async Task Configuration_SurvivesABuildApplyRoundTrip()
    {
        var vm = Vm();
        var path = WriteFile("in.csv", "A;B\n1;x\n");

        vm.Source.UseFile = true;
        vm.Source.FilePath = path;
        vm.Source.HasHeader = false;
        vm.Source.FirstDataRow = 3;
        vm.Source.LastRowText = "99";
        vm.Source.TrimWhitespace = true;
        vm.Source.NullToken = "NULL";
        vm.Source.AutoDetectDelimiter = false;
        vm.Source.AutoDetectEncoding = false;
        vm.Source.Delimiter = vm.Source.DelimiterOptions.Single(o => o.Value == ',');
        vm.Source.Encoding = vm.Source.EncodingOptions.Single(o => o.CharsetName == "UTF8");
        vm.Source.LineEnding = ImportSourceSectionViewModel.LineEndingOptions.Single(o => o.Value == LineEndingMode.Lf);
        vm.Source.DecimalSeparator = ImportSourceSectionViewModel.DecimalSeparatorOptions.Single(o => o.Value == '.');
        vm.Source.ThousandsSeparator = ImportSourceSectionViewModel.ThousandsSeparatorOptions.Single(o => o.Value == ' ');
        vm.Source.DateOrder = ImportSourceSectionViewModel.DateOrderOptions.Single(o => o.Value == DateFieldOrder.Iso);
        await SettleAsync(vm);

        var built = vm.BuildConfiguration();

        // Round-trip it through a fresh surface — the path a restored profile takes (§4.8.5).
        var restored = Vm();
        restored.ApplyConfiguration(built);
        await SettleAsync(restored);
        var rebuilt = restored.BuildConfiguration();

        Assert.Equal(built.Source, rebuilt.Source);
        Assert.Equal(built.Delimited, rebuilt.Delimited);
        Assert.Equal(built.Culture, rebuilt.Culture);
    }

    /// <summary>Decisions this build cannot yet edit (target, mapping, behaviour — I6/I7) must be carried
    /// through unchanged rather than silently dropped, or a profile written by a later build would be
    /// quietly degraded by an earlier one.</summary>
    [Fact]
    public async Task Configuration_CarriesThroughTheSectionsThisEtapDoesNotEditYet()
    {
        var vm = Vm();
        var original = ImportConfiguration.Empty with
        {
            Target = TargetDescriptor.Existing("ORDERS"),
            Mapping = new[] { new ColumnMapping { TargetColumnName = "A", SourceFieldIndex = 0 } },
            Behavior = new ImportBehaviorOptions { TrimTooLongValues = true },
            ErrorPolicy = ImportErrorPolicy.SkipInvalidRows,
            Transaction = ImportTransactionMode.Batched,
        };

        vm.ApplyConfiguration(original);
        await SettleAsync(vm);
        var rebuilt = vm.BuildConfiguration();

        Assert.Equal("ORDERS", rebuilt.Target.TableName);
        Assert.Single(rebuilt.Mapping);
        Assert.True(rebuilt.Behavior.TrimTooLongValues);
        Assert.Equal(ImportErrorPolicy.SkipInvalidRows, rebuilt.ErrorPolicy);
        Assert.Equal(ImportTransactionMode.Batched, rebuilt.Transaction);
    }

    // ── Reading a source ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChoosingAFile_ReadsItsShapeAndFillsTheSourcePreview()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteFile("in.csv", "Indeks;Nazwa\n1;abc\n2;def\n");
        await SettleAsync(vm);

        Assert.Equal(new[] { "Indeks", "Nazwa" }, vm.PreviewFields.Select(f => f.Name));
        Assert.Equal(2, vm.PreviewRows.Count);
        // The source's OWN numbering — the number every error message will quote (§0.6).
        Assert.Equal(2, vm.PreviewRows[0].SourceRowNumber);
        Assert.Equal("abc", vm.PreviewRows[0].ValueAt(1));
    }

    /// <summary>
    /// ⭐ A record that disagrees with the rest of the file about its field count is the instant tell for a
    /// wrong separator — marking it beats making the user count columns (§3.6).
    /// <para>
    /// The reference is the MAJORITY width, and the difference is not cosmetic: the schema reports the WIDEST
    /// record (so every column stays mappable), and marking against that would invert the signal — the one
    /// row with an extra field would set the width and every good row would be flagged instead.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheRecordThatDisagreesWithTheRest_IsTheOneMarked()
    {
        var vm = Vm();
        vm.Source.AutoDetectDelimiter = false;
        vm.Source.FilePath = WriteFile("ragged.csv", "A;B\n1;x\n2;y\n3;z;EXTRA\n4;w\n");
        await SettleAsync(vm);

        // Three rows have two fields, one has three — the odd one out is the third.
        Assert.Equal(new[] { false, false, true, false }, vm.PreviewRows.Select(r => r.IsRagged));
    }

    [Fact]
    public async Task AUniformFile_MarksNothing()
    {
        var vm = Vm();
        vm.Source.AutoDetectDelimiter = false;
        vm.Source.FilePath = WriteFile("clean.csv", "A;B\n1;x\n2;y\n3;z\n");
        await SettleAsync(vm);

        Assert.All(vm.PreviewRows, r => Assert.False(r.IsRagged));
    }

    /// <summary>⭐ Detection PROPOSES and publishes its evidence; the proposal becomes the DECLARED value the
    /// reader then uses, so there is no second hidden setting (§0.4).</summary>
    [Fact]
    public async Task AutoDetection_WritesTheDeclaredValue_AndSaysWhy()
    {
        var vm = Vm();
        vm.Source.Delimiter = vm.Source.DelimiterOptions.Single(o => o.Value == ';');
        vm.Source.FilePath = WriteFile("commas.csv", "A,B,C\n1,2,3\n4,5,6\n");
        await SettleAsync(vm);

        Assert.Equal(',', vm.Source.Delimiter.Value);
        Assert.NotEqual(string.Empty, vm.Source.DelimiterEvidence);
        Assert.Equal(3, vm.PreviewFields.Count);
    }

    [Fact]
    public async Task TurningAutoDetectionOff_LeavesTheDeclaredValueAlone()
    {
        var vm = Vm();
        vm.Source.AutoDetectDelimiter = false;
        vm.Source.Delimiter = vm.Source.DelimiterOptions.Single(o => o.Value == ';');
        vm.Source.FilePath = WriteFile("commas.csv", "A,B,C\n1,2,3\n");
        await SettleAsync(vm);

        Assert.Equal(';', vm.Source.Delimiter.Value);
    }

    [Fact]
    public async Task AMissingFile_IsReportedWithoutThrowing()
    {
        var vm = Vm();
        vm.Source.FilePath = Path.Combine(_dir, "does-not-exist.csv");
        await SettleAsync(vm);

        Assert.Contains(vm.Readiness.Items, i => i.Item.Code == ImportDiagnosticCode.SourceMissing);
        Assert.False(vm.Readiness.CanRun);
    }

    /// <summary>A format with no provider yet is REFUSED WITH A REASON. Pretending to read it, or hiding it
    /// from the file filter and leaving the user guessing, would both be worse (§0 / decision D2).</summary>
    [Fact]
    public async Task ASpreadsheet_IsRefusedWithAReason_NotSilentlyIgnored()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteFile("book.xlsx", "not really a workbook");
        await SettleAsync(vm);

        Assert.True(vm.HasStatusMessage);
        Assert.Equal(MessageSeverity.Warning, vm.StatusSeverity);
        Assert.Contains(".xlsx", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("a.csv", ImportSourceKind.Csv)]
    [InlineData("a.txt", ImportSourceKind.Text)]
    [InlineData("a.xlsx", ImportSourceKind.Xlsx)]
    [InlineData("a.xls", ImportSourceKind.Xls)]
    [InlineData("a.dat", ImportSourceKind.Csv)]
    public void FileKind_IsResolvedFromTheExtension(string name, ImportSourceKind expected)
        => Assert.Equal(expected, DataImportTabViewModel.ResolveFileKind(name));

    // ── The readiness strip ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Environment facts are read as DELEGATES, not snapshotted at open time, so the strip reflects
    /// the connection and transaction as they are NOW.</summary>
    [Fact]
    public async Task AnOpenWorkingTransaction_BlocksTheRun()
    {
        var vm = Vm(transactionOpen: true);
        vm.Source.FilePath = WriteFile("in.csv", "A;B\n1;x\n");
        await SettleAsync(vm);

        Assert.False(vm.Readiness.CanRun);
        var item = vm.Readiness.Items.Single(i => i.Item.Code == ImportDiagnosticCode.UserTransactionOpen);
        Assert.True(item.IsBlocking);
        Assert.Equal(ImportSection.Transaction, item.Section);
    }

    [Fact]
    public async Task NoConnection_BlocksTheRun()
    {
        var vm = Vm(connected: false);
        vm.Source.FilePath = WriteFile("in.csv", "A;B\n1;x\n");
        await SettleAsync(vm);

        Assert.Contains(vm.Readiness.Items, i => i.Item.Code == ImportDiagnosticCode.NotConnected);
    }

    /// <summary>Etap I5 has no Target section yet, and "no target" is the honest answer rather than a
    /// placeholder that pretends the surface is ready.</summary>
    [Fact]
    public async Task WithNoTargetSectionYet_TheStripSaysSo()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteFile("in.csv", "A;B\n1;x\n");
        await SettleAsync(vm);

        Assert.Contains(vm.Readiness.Items, i => i.Item.Code == ImportDiagnosticCode.NoTarget);
        Assert.False(vm.Readiness.CanRun);
    }

    /// <summary>⭐ The strip and a <c>MessageBanner</c> share ONE severity vocabulary (§9.3), and every chip
    /// resolves both a brush key and a geometry key — a row that resolves neither paints nothing while every
    /// observable state looks healthy (gotcha #250).</summary>
    [Fact]
    public async Task EverySectionChip_CarriesResolvableThemeKeys()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteFile("in.csv", "A;B\n1;x\n");
        await SettleAsync(vm);

        Assert.Equal(5, vm.Readiness.Sections.Count);
        Assert.All(vm.Readiness.Sections, s =>
        {
            Assert.False(string.IsNullOrEmpty(s.BrushKey));
            Assert.False(string.IsNullOrEmpty(s.GeometryKey));
            Assert.False(string.IsNullOrEmpty(s.Title));
        });

        // A section with nothing to say reads as Success — "nothing wrong here" is what the ✓ means.
        var source = vm.Readiness.Sections.Single(s => s.Section == ImportSection.Source);
        Assert.Equal(MessageSeverity.Success, source.Severity);
    }

    /// <summary>Every code the engine can emit must produce a sentence — a strip row showing a bare
    /// <c>IMP0007</c> would be Core's vocabulary leaking into the user's (rule #6).</summary>
    [Fact]
    public void EveryDiagnosticCode_HasAMessage()
    {
        foreach (ImportDiagnosticCode code in Enum.GetValues<ImportDiagnosticCode>())
        {
            if (code == ImportDiagnosticCode.None) continue;

            var item = new ReadinessItem(code, ImportSeverity.Warning, false, ImportSection.Source, "X", 1);
            var message = ImportReadinessItemViewModel.Describe(item);

            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.NotEqual(item.CodeText, message);   // not merely echoing the code back
        }
    }

    // ── Presentation state ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClickingAReadinessChip_ExpandsTheSectionItBlames()
    {
        var vm = Vm();
        vm.Source.IsExpanded = false;

        vm.FocusSectionCommand.Execute(ImportSection.Format);

        Assert.True(vm.Source.IsExpanded);
    }

    [Fact]
    public void TheBottomPanelToggles()
    {
        var vm = Vm();
        Assert.False(vm.IsBottomPanelCollapsed);

        vm.ToggleBottomPanelCommand.Execute(null);
        Assert.True(vm.IsBottomPanelCollapsed);
    }

    /// <summary>
    /// The collapsed summary describes only what actually folded away — how the text is read.
    /// <para>
    /// ⭐ The file name is deliberately absent (U1): the picker stays live at all times now, so repeating the
    /// name here would state twice what is already on screen once. If someone "restores" it, this fails and
    /// says why.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheCollapsedSummary_DescribesTheFormat_NotTheFileTheUserCanAlreadySee()
    {
        var vm = Vm();
        vm.Source.AutoDetectDelimiter = false;
        vm.Source.AutoDetectEncoding = false;
        vm.Source.FilePath = WriteFile("fantomy.csv", "A;B\n1;x\n");
        await SettleAsync(vm);

        var summary = vm.Source.SummaryText;
        Assert.Contains("WIN1250", summary, StringComparison.Ordinal);
        Assert.Contains(";", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("fantomy.csv", summary, StringComparison.Ordinal);
    }

    // ── U11: the format options settle themselves ───────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ What makes a repeat import cheap (§2.2 / §1.2): once a source has actually been read, the options
    /// that produced it fold away on their own. The picker stays live, so the next file costs one click.
    /// </summary>
    [Fact]
    public async Task FormatOptions_CollapseThemselves_OnceASourceHasBeenRead()
    {
        var vm = Vm();
        vm.Source.IsExpanded = true;

        vm.Source.FilePath = WriteFile("in.csv", "A;B\n1;x\n2;y\n");
        await SettleAsync(vm);

        Assert.False(vm.Source.IsExpanded);
    }

    /// <summary>
    /// ⭐ …but an automat that closes a panel the user just opened is worse than no automat at all
    /// (§2.2 point 2). A manual expand pins it open across later reads.
    /// </summary>
    [Fact]
    public async Task FormatOptions_OpenedByHand_SurviveTheNextRead()
    {
        var vm = Vm();

        // The options start EXPANDED (first use — §3.3), so reaching the "user opened them by hand" state
        // means closing them first. Toggling straight from the default would be testing the opposite case.
        vm.ToggleFormatOptionsCommand.Execute(null);
        Assert.False(vm.Source.IsExpanded);

        vm.ToggleFormatOptionsCommand.Execute(null);
        Assert.True(vm.Source.IsExpanded);

        vm.Source.FilePath = WriteFile("in.csv", "A;B\n1;x\n");
        await SettleAsync(vm);

        Assert.True(vm.Source.IsExpanded);
    }

    /// <summary>Nothing readable yet means nothing settled — the options must not fold on an empty surface,
    /// which is exactly when the user needs them.</summary>
    [Fact]
    public async Task FormatOptions_StayOpen_WhileThereIsNothingToRead()
    {
        var vm = Vm();
        vm.Source.IsExpanded = true;

        vm.Source.FilePath = Path.Combine(_dir, "does-not-exist.csv");
        await SettleAsync(vm);

        Assert.True(vm.Source.IsExpanded);
    }

    // ── U6: the readiness strip has a ceiling ───────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The strip must not take the most space at the moment the user has the most to fix. The chips still
    /// carry §3.2's "every gap at once" — this only caps how many findings are spelled out.
    /// </summary>
    [Fact]
    public void Readiness_CapsTheSpelledOutFindings_AndSaysHowManyItHid()
    {
        var vm = Vm(connected: false, transactionOpen: true);
        var readiness = vm.Readiness;

        Assert.True(readiness.Items.Count > ImportReadinessViewModel.CollapsedItemLimit);
        Assert.Equal(ImportReadinessViewModel.CollapsedItemLimit, readiness.VisibleItems.Count);
        Assert.True(readiness.HasHiddenItems);
        Assert.Contains(
            (readiness.Items.Count - ImportReadinessViewModel.CollapsedItemLimit).ToString(CultureInfo.CurrentCulture),
            readiness.MoreText,
            StringComparison.Ordinal);

        // Every chip stays, whatever the cap did — that is the half that keeps the §3.2 promise.
        Assert.Equal(5, readiness.Sections.Count);
    }

    /// <summary>Expanding gives the whole list back, and the cap never re-orders anything: the survivors are
    /// Core's own first findings, so the strip and a report cannot disagree about what matters most.</summary>
    [Fact]
    public void Readiness_ExpandsToTheWholeList_InCoresOrder()
    {
        var vm = Vm(connected: false, transactionOpen: true);
        var readiness = vm.Readiness;

        Assert.Equal(
            readiness.Items.Take(ImportReadinessViewModel.CollapsedItemLimit).Select(i => i.Code),
            readiness.VisibleItems.Select(i => i.Code));

        readiness.ToggleExpandedCommand.Execute(null);

        Assert.Equal(readiness.Items.Count, readiness.VisibleItems.Count);
        Assert.Equal(readiness.Items.Select(i => i.Code), readiness.VisibleItems.Select(i => i.Code));
    }

    // ── U9: band H says where the rows land ─────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The fact the removed header band was supposed to carry. The lane is stated out loud because a module
    /// that writes to a database must not make the user guess which transaction it joins (§4.5).
    /// </summary>
    [Fact]
    public void BandH_NamesTheConnectionAndTheLane()
    {
        var vm = Vm(connectionName: "SZKOLENIE");

        Assert.Contains("SZKOLENIE", vm.DestinationStatus, StringComparison.Ordinal);
        Assert.Contains(UiStrings.ImportDestinationDataLane, vm.DestinationStatus, StringComparison.Ordinal);
    }

    /// <summary>Read as a DELEGATE, not snapshotted — the line states where things stand now.</summary>
    [Fact]
    public void BandH_SaysSoWhenThereIsNoConnection()
    {
        var vm = Vm(connected: false, connectionName: "");

        Assert.Equal(UiStrings.ImportDestinationNotConnected, vm.DestinationStatus);
    }

    // ── U2: the bottom panel's remembered height ────────────────────────────────────────────────────────

    /// <summary>
    /// The height lives on the VM because the import tab is transient — a value remembered inside the view
    /// would be gone before the workspace is written. <c>MainWindow</c> persists it globally, like the SQL
    /// editor's results panel.
    /// </summary>
    [Fact]
    public void BottomPanelHeight_IsCarriedByTheViewModel()
    {
        var vm = Vm();
        var seen = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DataImportTabViewModel.BottomPanelHeight)) seen++;
        };

        vm.BottomPanelHeight = 260;

        Assert.Equal(260, vm.BottomPanelHeight);
        Assert.Equal(1, seen);
    }
}

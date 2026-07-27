using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EmberTern.App;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
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
        bool connected = true,
        string connectionName = "LAB",
        ImportTarget? target = null,
        params ImportTarget[] moreTargets)
        => new(Environment(connected, connectionName, target, moreTargets))
        {
            // The converted preview waits ~150 ms so that changing a separator does not re-read the file on
            // every keystroke. That is a delay, not a decision — zeroing it keeps the suite fast without
            // changing a single thing the tests are actually about.
            PreviewDebounce = TimeSpan.Zero,
        };

    /// <summary>
    /// The surface's whole outside world, as delegates (etap I7 replaced five positional ones with this bundle).
    /// The run-time collaborators are left unset here on purpose: a test that needs a writer, a commit or a
    /// profile store supplies its own, so every other test keeps proving that a surface with nothing behind it
    /// refuses to run rather than throwing.
    /// </summary>
    private static DataImportEnvironment Environment(
        bool connected = true,
        string connectionName = "LAB",
        ImportTarget? target = null,
        ImportTarget[]? moreTargets = null)
    {
        var all = target is null
            ? Array.Empty<ImportTarget>()
            : new[] { target }.Concat(moreTargets ?? Array.Empty<ImportTarget>()).ToArray();

        return new DataImportEnvironment(() => connected, () => connectionName)
        {
            ListTablesAsync = all.Length == 0
                ? null
                : _ => Task.FromResult<IReadOnlyList<string>>(all.Select(t => t.TableName).ToList()),
            ReadTargetAsync = all.Length == 0
                ? null
                : (name, _) => Task.FromResult(
                    all.FirstOrDefault(t => string.Equals(t.TableName, name, StringComparison.OrdinalIgnoreCase))),
        };
    }

    /// <summary>
    /// A surface whose clipboard answers with whatever <paramref name="clipboard"/> returns — a function, not a
    /// string, so a test can prove a <b>re-read</b> happened (by counting calls) rather than merely that the text
    /// arrived once.
    /// <para>
    /// ⚠ The clipboard is supplied through the <b>environment</b>, not wired afterwards, and that is the point of
    /// the seam: the recalculation chain reads the clipboard, so it must be answerable from the constructor's
    /// first chain run. A test that attached it later could never exercise "the tab opened on a clipboard
    /// configuration and read the clipboard by itself".
    /// </para>
    /// </summary>
    private static DataImportTabViewModel ClipboardVm(
        Func<string?> clipboard,
        ImportTarget? target = null,
        ImportConfiguration? lastUsed = null)
    {
        var environment = new DataImportEnvironment(() => true, () => "LAB")
        {
            ReadClipboardAsync = () => Task.FromResult(clipboard()),
            ListTablesAsync = target is null
                ? null
                : _ => Task.FromResult<IReadOnlyList<string>>(new[] { target.TableName }),
            ReadTargetAsync = target is null
                ? null
                : (name, _) => Task.FromResult(
                    string.Equals(name, target.TableName, StringComparison.OrdinalIgnoreCase) ? target : null),
            LoadLastUsed = lastUsed is null ? null : () => lastUsed,
        };

        return new DataImportTabViewModel(environment) { PreviewDebounce = TimeSpan.Zero };
    }

    /// <summary>A small target table: a required column, an optional one, a COMPUTED one and an identity
    /// ALWAYS — one of each kind the mapping grid has to treat differently.</summary>
    private static ImportTarget LabTarget() => new(
        "IMP_LAB",
        new[]
        {
            new ColumnSpec("ID", "INTEGER") { Identity = IdentityKind.Always },
            new ColumnSpec("KOD", "VARCHAR(20)", NotNull: true),
            new ColumnSpec("NAZWA", "VARCHAR(100)"),
            new ColumnSpec("SUMA", "NUMERIC(15,2)") { IsComputed = true },
        },
        Array.Empty<string>());

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
    /// ⭐ Etap I9 — the same surface, the same code, a workbook. This is the test that pins the ONE thing App
    /// had to learn: choosing a reader from the source kind. Everything after that point — schema, preview, row
    /// numbers — is the path CSV already used, which is the pillar §1.4 claims and I9 exists to check.
    /// <para>
    /// Note the values arrive NATIVE (a <see cref="double"/>, not "42"), which is what lets I8's type inference
    /// work on a sheet without a single change.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ChoosingAWorkbook_ReadsItThroughTheSameSurfaceAsText()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteWorkbook("in.xlsx");
        await SettleAsync(vm);

        Assert.False(vm.HasStatusMessage); // no "format not supported" refusal any more
        Assert.Equal(new[] { "Indeks", "Nazwa" }, vm.PreviewFields.Select(f => f.Name));
        Assert.Equal(2, vm.PreviewRows.Count);
        Assert.Equal(2, vm.PreviewRows[0].SourceRowNumber);
        Assert.Equal("abc", vm.PreviewRows[0].ValueAt(1));
        Assert.Equal(1d, vm.PreviewRows[0].ValueAt(0));
    }

    /// <summary>
    /// ⭐ §3.3: the Format section follows the PROVIDER's capabilities, not the file extension. A workbook
    /// carries its own encoding and has no separators, so those controls are not shown at all — rather than
    /// shown and quietly ignored, which is the state that makes a user think they changed something.
    /// </summary>
    [Fact]
    public async Task TheFormatSection_FollowsTheProvidersCapabilities_NotTheFileExtension()
    {
        var vm = Vm();

        vm.Source.FilePath = WriteFile("in.csv", "Indeks;Nazwa\n1;abc\n");
        await SettleAsync(vm);
        Assert.True(vm.Source.SupportsDelimiters);
        Assert.True(vm.Source.SupportsEncoding);
        Assert.False(vm.Source.SupportsSheets);
        Assert.Empty(vm.Source.Sheets);

        vm.Source.FilePath = WriteWorkbook("in.xlsx");
        await SettleAsync(vm);
        Assert.False(vm.Source.SupportsDelimiters);
        Assert.False(vm.Source.SupportsEncoding);
        Assert.True(vm.Source.SupportsSheets);
        Assert.Equal("Arkusz1", Assert.Single(vm.Source.Sheets).Name);
    }

    /// <summary>The sheet choice is a DECISION, so it must survive the round trip through the record — a
    /// setting that lived only on the surface would be missing from a saved profile (§4.8.6).</summary>
    [Fact]
    public async Task TheSheetChoiceAndDateHandling_SurviveTheConfigurationRoundTrip()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteWorkbook("in.xlsx");
        await SettleAsync(vm);

        vm.Source.DatesAsDates = false;
        var configuration = vm.BuildConfiguration();

        Assert.Null(configuration.Delimited); // exactly one options block, matching the source kind
        Assert.NotNull(configuration.Spreadsheet);
        Assert.Equal(0, configuration.Spreadsheet!.SheetIndex);
        Assert.Equal("Arkusz1", configuration.Spreadsheet.SheetName);
        Assert.False(configuration.Spreadsheet.DatesAsDates);

        var reloaded = Vm();
        reloaded.ApplyConfiguration(configuration);
        Assert.False(reloaded.Source.DatesAsDates);
        Assert.Equal(0, reloaded.Source.SelectedSheet?.Index);
    }

    /// <summary>A two-column, two-row workbook with a header — the spreadsheet twin of the CSV above.</summary>
    private string WriteWorkbook(string name)
    {
        var path = Path.Combine(_dir, name);
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

        static Cell Text(string reference, string value) => new()
        {
            CellReference = reference,
            DataType = CellValues.InlineString,
            InlineString = new InlineString(new Text(value)),
        };
        static Cell Number(string reference, string value) => new()
        {
            CellReference = reference,
            CellValue = new CellValue(value),
        };
        static Row Line(uint index, params Cell[] cells)
        {
            var row = new Row { RowIndex = index };
            foreach (var cell in cells) row.Append(cell);
            return row;
        }

        var sheetData = new SheetData();
        sheetData.Append(Line(1, Text("A1", "Indeks"), Text("B1", "Nazwa")));
        sheetData.Append(Line(2, Number("A2", "1"), Text("B2", "abc")));
        sheetData.Append(Line(3, Number("A3", "2"), Text("B3", "def")));

        worksheetPart.Worksheet = new Worksheet(sheetData);
        workbookPart.Workbook.AppendChild(new Sheets(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1U,
            Name = "Arkusz1",
        }));
        workbookPart.Workbook.Save();
        return path;
    }

    /// <summary>The same two rows as <see cref="WriteWorkbook"/>, in the legacy BIFF8 container — so the two
    /// spreadsheet formats are asked literally the same question and any difference in the answer is the
    /// provider's, not the fixture's. NPOI writes it; nothing in <c>src/</c> uses NPOI.</summary>
    private string WriteLegacyWorkbook(string name)
    {
        var path = Path.Combine(_dir, name);

        var workbook = new NPOI.HSSF.UserModel.HSSFWorkbook();
        var sheet = workbook.CreateSheet("Arkusz1");

        var header = sheet.CreateRow(0);
        header.CreateCell(0).SetCellValue("Indeks");
        header.CreateCell(1).SetCellValue("Nazwa");

        var first = sheet.CreateRow(1);
        first.CreateCell(0).SetCellValue(1d);
        first.CreateCell(1).SetCellValue("abc");

        var second = sheet.CreateRow(2);
        second.CreateCell(0).SetCellValue(2d);
        second.CreateCell(1).SetCellValue("def");

        using var output = File.Create(path);
        workbook.Write(output, leaveOpen: true);
        return path;
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

    /// <summary>
    /// ⭐ Etap I10 — the legacy format joins on the same terms .xlsx did in I9: a third reader behind the ONE
    /// factory, and nothing else on the surface knows. Values arrive NATIVE here too.
    /// <para>
    /// ⚠ This test REPLACED one asserting the opposite. Until I10 a <c>.xls</c> was refused with a reason,
    /// which was the correct behaviour while the format had no provider — the refusal was narrowed in I9 rather
    /// than deleted, and now that BIFF8 is genuinely readable the refusal itself is gone. What survives is the
    /// case below: a file that is not what its extension claims still gets an honest answer.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ChoosingALegacyWorkbook_ReadsItThroughTheSameSurface()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteLegacyWorkbook("in.xls");
        await SettleAsync(vm);

        Assert.False(vm.HasStatusMessage);
        Assert.Equal(new[] { "Indeks", "Nazwa" }, vm.PreviewFields.Select(f => f.Name));
        Assert.Equal(2, vm.PreviewRows.Count);
        Assert.Equal(2, vm.PreviewRows[0].SourceRowNumber);
        Assert.Equal(1d, vm.PreviewRows[0].ValueAt(0));
        Assert.Equal("abc", vm.PreviewRows[0].ValueAt(1));
    }

    /// <summary>The legacy reader answers the same capability question the .xlsx one does — which is why the
    /// Format section needed no change at all for a second spreadsheet format (§3.3).</summary>
    [Fact]
    public async Task ALegacyWorkbook_OffersSheets_AndNoSeparators()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteLegacyWorkbook("in.xls");
        await SettleAsync(vm);

        Assert.False(vm.Source.SupportsDelimiters);
        Assert.False(vm.Source.SupportsEncoding);
        Assert.True(vm.Source.SupportsSheets);
        Assert.Equal("Arkusz1", Assert.Single(vm.Source.Sheets).Name);
    }

    /// <summary>
    /// A file that is not what its extension claims is REFUSED WITH A REASON — never read as something else and
    /// never passed through in silence (§0). The message has to name the file, because "invalid file signature"
    /// on its own is not something a user can act on.
    /// </summary>
    [Fact]
    public async Task AFileThatIsNotReallyAWorkbook_IsRefusedWithAReason_NotSilentlyIgnored()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteFile("book.xls", "not really a workbook");
        await SettleAsync(vm);

        Assert.True(vm.HasStatusMessage);
        Assert.Contains("book.xls", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ⭐ §1.5 — the clipboard is not a second parser, it is a different ORIGIN for the one text reader. Pasting
    /// from Excel produces TAB-separated text, which is the realistic case this etap is about: the surface reads
    /// it with no file on disk, and the separator is DETECTED rather than assumed (§0.4 — detection proposes).
    /// </summary>
    [Fact]
    public async Task PastingFromExcel_ImportsWithoutAFileOnDisk()
    {
        var vm = ClipboardVm(() => "Indeks\tNazwa\r\n1\tabc\r\n2\tdef\r\n");

        vm.UseClipboardCommand.Execute(null);
        await SettleAsync(vm);

        Assert.False(vm.Source.UseFile);
        Assert.Equal(new[] { "Indeks", "Nazwa" }, vm.PreviewFields.Select(f => f.Name));
        Assert.Equal(2, vm.PreviewRows.Count);
        Assert.Equal("abc", vm.PreviewRows[0].ValueAt(1));
        Assert.Equal(ImportSourceKind.Clipboard, vm.BuildConfiguration().Source.Kind);
    }

    /// <summary>
    /// ⚠ The clipboard's TEXT is never part of the configuration (§4.8.2): a profile stores DECISIONS, not the
    /// data they were made about. Saving the pasted rows into a profile would quietly turn a reusable setup
    /// into a snapshot of one afternoon's clipboard.
    /// </summary>
    [Fact]
    public async Task TheClipboardsText_IsNotPartOfTheConfiguration()
    {
        var vm = ClipboardVm(() => "Indeks\tNazwa\r\n1\tabc\r\n");

        vm.UseClipboardCommand.Execute(null);
        await SettleAsync(vm);

        var configuration = vm.BuildConfiguration();
        Assert.Null(configuration.Source.Path);

        // A second surface given the same configuration has no rows until it looks at the clipboard itself —
        // which is what the read link now does, and which is why this one is given no clipboard at all.
        var reloaded = Vm();
        reloaded.ApplyConfiguration(configuration);
        Assert.Equal(string.Empty, reloaded.Source.ClipboardText);
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

    /// <summary>
    /// ⭐ I7.5 reversed this. The module owns its own transaction now, so whatever the SQL Editor has open is
    /// none of its business: the strip says nothing about it and the run is not blocked by it. The previous
    /// version of this test asserted the opposite — re-asserting it would re-introduce the very entanglement
    /// the decision removed.
    /// </summary>
    [Fact]
    public async Task AnOpenConsoleTransaction_IsNotTheImportsBusiness()
    {
        var vm = Vm();
        vm.Source.FilePath = WriteFile("in.csv", "A;B\n1;x\n");
        await SettleAsync(vm);

        // IMP0021 was UserTransactionOpen. Nothing may raise it, and nothing in the Transaction section may
        // block while the surface is merely connected.
        Assert.All(vm.Readiness.Items, i => Assert.NotEqual("IMP0021", i.Code));
        Assert.DoesNotContain(
            vm.Readiness.Items, i => i.Section == ImportSection.Transaction && i.IsBlocking);
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
        // Four findings, deliberately: disconnected + no source + no target give three, and trimming gives the
        // fourth. I7.5 retired a fifth (the console's transaction), so the cap now needs a real fourth cause
        // rather than a state the module no longer reports.
        var vm = Vm(connected: false);
        vm.ApplyConfiguration(ImportConfiguration.Empty with
        {
            Behavior = new ImportBehaviorOptions { TrimTooLongValues = true },
        });
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
        var vm = Vm(connected: false);
        vm.ApplyConfiguration(ImportConfiguration.Empty with
        {
            Behavior = new ImportBehaviorOptions { TrimTooLongValues = true },
        });
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

    // ── Etap I6: Target + Mapping ───────────────────────────────────────────────────────────────────────

    private async Task<DataImportTabViewModel> MappedVmAsync(string csv = "KOD;NAZWA\nA1;Widget\n")
    {
        var target = LabTarget();
        var vm = Vm(target: target);
        await SettleAsync(vm);

        vm.Source.FilePath = WriteFile("in.csv", csv);
        await SettleAsync(vm);

        vm.Target.SelectedTable = target.TableName;
        await SettleAsync(vm);
        return vm;
    }

    /// <summary>
    /// ⭐ The grid is a projection of the TABLE, not of what happens to be mappable: a column that can never
    /// be written is shown WITH its reason rather than quietly missing (§3.5). A missing row is a question
    /// the user cannot even ask.
    /// </summary>
    [Fact]
    public async Task Mapping_ShowsEveryTargetColumn_IncludingTheOnesThatCanNeverBeWritten()
    {
        var vm = await MappedVmAsync();

        Assert.Equal(4, vm.Mapping.Rows.Count);

        var computed = vm.Mapping.Rows.Single(r => r.TargetColumnName == "SUMA");
        Assert.True(computed.NeverWritable);
        Assert.False(computed.IsPickerEnabled);
        Assert.NotEmpty(computed.LockReason);
    }

    /// <summary>Auto-matching is the planner's answer, merely rendered here — a second matching rule in the
    /// VM is exactly how the grid and the readiness strip start telling the user different things.</summary>
    [Fact]
    public async Task Mapping_MatchesByName_AndMarksTheOriginAsAutomatic()
    {
        var vm = await MappedVmAsync();

        var kod = vm.Mapping.Rows.Single(r => r.TargetColumnName == "KOD");
        Assert.True(kod.IsMapped);
        Assert.True(kod.IsAutomatic);
        Assert.Equal("KOD", kod.SelectedOption.Field!.Name);
    }

    /// <summary>
    /// ⭐ Identity GENERATED ALWAYS is writable only after a deliberate unlock (R10). Firebird refuses an
    /// INSERT naming it without OVERRIDING SYSTEM VALUE, so supplying that silently would decide on the
    /// user's behalf that the server's identity should be overwritten.
    /// </summary>
    [Fact]
    public async Task Mapping_LocksIdentityAlways_UntilItIsExplicitlyUnlocked()
    {
        var vm = await MappedVmAsync();
        var id = vm.Mapping.Rows.Single(r => r.TargetColumnName == "ID");

        Assert.True(id.NeedsIdentityOverride);
        Assert.False(id.IsPickerEnabled);

        id.IsIdentityUnlocked = true;
        Assert.True(id.IsPickerEnabled);
    }

    /// <summary>A user's decision outranks the planner, and the marker must never describe a value the user
    /// has since replaced (the debugger's ValueOrigin rule, C3).</summary>
    [Fact]
    public async Task Mapping_AManualEdit_ClearsTheAutomaticOrigin_AndReachesTheRecord()
    {
        var vm = await MappedVmAsync();
        var nazwa = vm.Mapping.Rows.Single(r => r.TargetColumnName == "NAZWA");

        nazwa.SelectedOption = nazwa.Options.Single(o => o.Field?.Name == "KOD");

        Assert.False(nazwa.IsAutomatic);
        Assert.False(nazwa.IsAssumed);

        var mapping = vm.CurrentConfiguration.Mapping.Single(m => m.TargetColumnName == "NAZWA");
        Assert.Equal("KOD", mapping.SourceFieldName);
        Assert.Equal(MappingOrigin.Manual, mapping.Origin);
    }

    /// <summary>"Do not import" is a DECISION, not an absence — it has to survive into the record as a skip
    /// so a re-read cannot quietly re-map the column.</summary>
    [Fact]
    public async Task Mapping_ChoosingDoNotImport_IsRecordedAsASkip()
    {
        var vm = await MappedVmAsync();
        var kod = vm.Mapping.Rows.Single(r => r.TargetColumnName == "KOD");

        kod.SelectedOption = kod.Options[0];   // "— do not import —"

        var mapping = vm.CurrentConfiguration.Mapping.Single(m => m.TargetColumnName == "KOD");
        Assert.True(mapping.IsSkipped);
        Assert.False(mapping.IsMapped);
    }

    /// <summary>Clearing goes through the planner too — the panel never invents a mapping state of its own.</summary>
    [Fact]
    public async Task Mapping_ClearAndMatchByPosition_GoThroughThePlanner()
    {
        var vm = await MappedVmAsync();

        vm.Mapping.ClearMappingCommand.Execute(null);
        Assert.All(vm.Mapping.Rows, r => Assert.False(r.IsMapped));

        vm.Mapping.MatchByPositionCommand.Execute(null);
        Assert.Contains(vm.Mapping.Rows, r => r.IsMapped);
    }

    /// <summary>
    /// The list of fields nobody consumes is how "I forgot a column" becomes visible BEFORE the import
    /// rather than after it (§3.5).
    /// <para>
    /// ⚠ TWO spare fields on purpose. With exactly one unmatched column and one unused field, Core's
    /// sole-remaining-pair rule fires and there is nothing left over — correct behaviour, but it would make
    /// this test pass or fail for the wrong reason.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Mapping_NamesTheSourceFieldsNobodyUses()
    {
        var vm = await MappedVmAsync("KOD;NAZWA;SPARE1;SPARE2\nA1;Widget;x;y\n");

        Assert.True(vm.Mapping.HasUnusedFields);
        Assert.Contains("SPARE1", vm.Mapping.UnusedFieldsText, StringComparison.Ordinal);
        Assert.Contains("SPARE2", vm.Mapping.UnusedFieldsText, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐ The sole-remaining-pair rule reaches an identity ALWAYS column too — and that is deliberate, not an
    /// oversight: Core treats such a column as mappable and raises <c>IMP0007</c> when it maps one, so the
    /// INSERT will carry <c>OVERRIDING SYSTEM VALUE</c> and the user is told. The row unlocks itself in that
    /// case because the decision has already been made and stated; what stays locked is the user reaching for
    /// it by hand out of nowhere.
    /// </summary>
    [Fact]
    public async Task Mapping_ThePairRuleMayReachAnIdentityColumn_AndSaysSo()
    {
        var vm = await MappedVmAsync("KOD;NAZWA;NIEUZYWANE\nA1;Widget;7\n");

        var id = vm.Mapping.Rows.Single(r => r.TargetColumnName == "ID");
        Assert.True(id.IsMapped);
        Assert.True(id.IsAssumed);
        Assert.True(id.IsIdentityUnlocked);
        Assert.False(vm.Mapping.HasUnusedFields);
    }

    /// <summary>
    /// ⭐ Choosing a DIFFERENT table must not carry the old pairing over: a different table is a different
    /// identity, and silently re-pointing a mapping at columns that merely happen to share a name is the
    /// class of defect §0.1 exists to forbid.
    /// </summary>
    [Fact]
    public async Task Mapping_IsNotCarriedAcrossADifferentTable()
    {
        var other = new ImportTarget(
            "IMP_OTHER",
            new[] { new ColumnSpec("KOD", "VARCHAR(20)") },
            Array.Empty<string>());

        var vm = Vm(target: LabTarget(), moreTargets: other);
        await SettleAsync(vm);
        vm.Source.FilePath = WriteFile("in.csv", "KOD;NAZWA\nA1;Widget\n");
        await SettleAsync(vm);

        vm.Target.SelectedTable = "IMP_LAB";
        await SettleAsync(vm);
        Assert.Contains(vm.Mapping.Rows, r => r.TargetColumnName == "NAZWA" && r.IsMapped);

        vm.Target.SelectedTable = "IMP_OTHER";
        await SettleAsync(vm);

        // The grid is the other table's, and nothing from the first one survived into the record.
        Assert.Single(vm.Mapping.Rows);
        Assert.DoesNotContain(vm.CurrentConfiguration.Mapping, m => m.TargetColumnName == "NAZWA");
    }

    /// <summary>
    /// ⚠ Clearing the TARGET clears the grid but must NOT clear the record's mapping: those are user
    /// decisions, and dropping them because the target has not been read back yet is the "an older build
    /// quietly robbed the profile" defect. Re-choosing the same table brings the pairing back.
    /// </summary>
    [Fact]
    public async Task Mapping_SurvivesTheTargetBeingClearedAndReChosen()
    {
        var vm = await MappedVmAsync();
        Assert.Contains(vm.CurrentConfiguration.Mapping, m => m.TargetColumnName == "KOD" && m.IsMapped);

        vm.Target.SelectedTable = null;
        await SettleAsync(vm);
        Assert.Empty(vm.Mapping.Rows);
        Assert.Contains(vm.CurrentConfiguration.Mapping, m => m.TargetColumnName == "KOD" && m.IsMapped);

        vm.Target.SelectedTable = "IMP_LAB";
        await SettleAsync(vm);
        Assert.Contains(vm.Mapping.Rows, r => r.TargetColumnName == "KOD" && r.IsMapped);
    }

    /// <summary>The target tile states the facts that decide whether an import behaves as expected — and the
    /// triggers are NAMED, because a count says something is there while the names say what will rewrite the
    /// values on the way in (R6).</summary>
    [Fact]
    public async Task Target_StatesColumnsPrimaryKeyAndBeforeInsertTriggers()
    {
        var target = new ImportTarget(
            "IMP_TRIG",
            new[] { new ColumnSpec("ID", "INTEGER") { IsPrimaryKey = true } },
            new[] { "IMP_TRIG_BI" });

        var vm = Vm(target: target);
        await SettleAsync(vm);
        vm.Target.SelectedTable = target.TableName;
        await SettleAsync(vm);

        Assert.Contains("ID", vm.Target.FactsText, StringComparison.Ordinal);
        Assert.Contains("IMP_TRIG_BI", vm.Target.FactsText, StringComparison.Ordinal);
    }

    /// <summary>The target and the emptying option are user decisions, so they must travel in the ONE record
    /// (§4.8.6) — the reflection guard covers the Core half, this covers the App half.</summary>
    [Fact]
    public async Task Target_ReachesTheOneRecord()
    {
        var vm = await MappedVmAsync();
        vm.Target.EmptyBeforeImport = true;
        await SettleAsync(vm);

        var configuration = vm.BuildConfiguration();
        Assert.Equal("IMP_LAB", configuration.Target.TableName);
        Assert.Equal(ImportTargetKind.ExistingTable, configuration.Target.Kind);
        Assert.True(configuration.Behavior.EmptyTargetBeforeImport);
    }

    /// <summary>
    /// ⭐ A build this old cannot edit a NEW-table target (that is etap I8), so it must pass one through
    /// untouched rather than degrade it to an existing-table target — the same "an older build must not rob
    /// a newer profile" promise the section pass-through already makes.
    /// </summary>
    [Fact]
    public void Target_PassesANewTableConfigurationThrough()
    {
        var vm = Vm();
        var newTable = TargetDescriptor.New(
            "IMP_NEW",
            new[] { new ImportColumnDefinition { Name = "A", BasicType = "VARCHAR", Size = 20 } });

        vm.ApplyConfiguration(ImportConfiguration.Empty with { Target = newTable });

        var configuration = vm.BuildConfiguration();
        Assert.Equal(ImportTargetKind.NewTable, configuration.Target.Kind);
        Assert.Equal("IMP_NEW", configuration.Target.TableName);
        Assert.Single(configuration.Target.NewTableColumns);
    }

    /// <summary>Readiness stops saying "no target" the moment there is one — the strip reads the same target
    /// the grid does, because there is only one.</summary>
    [Fact]
    public async Task Readiness_SeesTheChosenTarget()
    {
        var vm = Vm(target: LabTarget());
        await SettleAsync(vm);
        Assert.Contains(vm.Readiness.Items, i => i.Item.Code == ImportDiagnosticCode.NoTarget);

        vm.Target.SelectedTable = "IMP_LAB";
        await SettleAsync(vm);

        Assert.DoesNotContain(vm.Readiness.Items, i => i.Item.Code == ImportDiagnosticCode.NoTarget);
    }

    /// <summary>The "only unmapped" filter is a view over the same rows — it must never drop a row from the
    /// record, only from the display.</summary>
    [Fact]
    public async Task Mapping_OnlyUnmappedFilter_HidesRowsWithoutChangingTheRecord()
    {
        var vm = await MappedVmAsync();
        var before = vm.CurrentConfiguration.Mapping.Count;

        vm.Mapping.ShowOnlyUnmapped = true;

        Assert.True(vm.Mapping.VisibleRows.Count < vm.Mapping.Rows.Count);
        Assert.All(vm.Mapping.VisibleRows, r => Assert.False(r.IsMapped));
        Assert.Equal(before, vm.CurrentConfiguration.Mapping.Count);
    }

    // ══ Refresh — one entry point for re-reading the world (post-I10 ergonomics seam) ════════════════════
    //
    // The user's report was three findings that turned out to be one question: is there ONE path that re-reads
    // the source and recomputes everything downstream of it? There is — Recalculate → RunChainAsync — and these
    // tests pin the two things that were true of it and the three that were not: it never re-read the clipboard
    // or the table list, and there was no way to ask it to run at all.

    /// <summary>
    /// ⭐ Refresh re-reads the file <b>and everything downstream of it</b>. This is the user's third finding in
    /// its most load-bearing form: a source whose FIELDS changed must re-plan the mapping, because a re-read that
    /// leaves the mapping describing the previous file is worse than no re-read — the surface would then look
    /// settled while pointing the wrong column at the wrong place.
    /// </summary>
    [Fact]
    public async Task Refresh_ReReadsTheFile_AndRebuildsTheMapping()
    {
        var target = LabTarget();
        var vm = Vm(target: target);
        var path = WriteFile("in.csv", "A;B\n1;x\n");

        vm.Source.FilePath = path;
        await SettleAsync(vm);
        vm.Target.SelectedTable = target.TableName;
        await SettleAsync(vm);

        // Nothing matches by name yet, so nothing is paired automatically.
        Assert.Equal(new[] { "A", "B" }, vm.PreviewFields.Select(f => f.Name));
        Assert.False(vm.Mapping.Rows.Single(r => r.TargetColumnName == "KOD").IsMapped);

        // The file changes on disk — the case the button exists for. Nothing in the surface has been touched.
        File.WriteAllText(path, "KOD;NAZWA\nA1;Widget\nA2;Gadget\n");
        vm.RefreshCommand.Execute(null);
        await SettleAsync(vm);

        Assert.Equal(new[] { "KOD", "NAZWA" }, vm.PreviewFields.Select(f => f.Name));
        Assert.Equal(2, vm.PreviewRows.Count);

        var kod = vm.Mapping.Rows.Single(r => r.TargetColumnName == "KOD");
        Assert.True(kod.IsMapped);
        Assert.Equal("KOD", kod.SelectedOption.Field!.Name);

        // …and the whole tail ran, not just the read: the converted preview is a real bounded import.
        Assert.Equal(2, vm.ConvertedPreview.Rows.Count);
    }

    /// <summary>
    /// ⭐ Refresh re-reads the TABLE LIST too — the „the table that was blocking my CREATE has been dropped" case
    /// the user named. Until this seam the list was latched behind <c>_tablesLoaded</c> and read exactly once per
    /// tab, so a table added or dropped elsewhere could only be seen by closing and reopening the surface.
    /// </summary>
    [Fact]
    public async Task Refresh_ReReadsTheTableList()
    {
        var tables = new List<string> { "IMP_ONE" };
        var environment = new DataImportEnvironment(() => true, () => "LAB")
        {
            ListTablesAsync = _ => Task.FromResult<IReadOnlyList<string>>(tables.ToList()),
        };
        var vm = new DataImportTabViewModel(environment) { PreviewDebounce = TimeSpan.Zero };
        await SettleAsync(vm);

        Assert.Equal(new[] { "IMP_ONE" }, vm.Target.Tables);

        tables.Add("IMP_TWO");

        // An ordinary decision must NOT re-read it: the catalog is not re-queried on every keystroke.
        vm.Source.NullToken = "NULL";
        await SettleAsync(vm);
        Assert.Equal(new[] { "IMP_ONE" }, vm.Target.Tables);

        vm.RefreshCommand.Execute(null);
        await SettleAsync(vm);
        Assert.Equal(new[] { "IMP_ONE", "IMP_TWO" }, vm.Target.Tables);
    }

    /// <summary>
    /// ⭐ The defect behind the user's „even Ctrl+V did not recompute": the clipboard used to arrive by ASSIGNING a
    /// property, and an assignment that does not change the value raises nothing — so re-reading identical text
    /// recomputed nothing at all. Now the read is a link in the chain and the chain was started explicitly, so
    /// what happens next does not depend on whether the text differs.
    /// </summary>
    [Fact]
    public async Task Refresh_ReReadsTheClipboard_EvenWhenTheTextIsIdentical()
    {
        var reads = 0;
        var vm = ClipboardVm(() =>
        {
            reads++;
            return "Indeks\tNazwa\r\n1\tabc\r\n";
        });

        vm.UseClipboardCommand.Execute(null);
        await SettleAsync(vm);
        Assert.Equal(1, reads);
        Assert.Single(vm.PreviewRows);

        vm.RefreshCommand.Execute(null);
        await SettleAsync(vm);

        Assert.Equal(2, reads);
        Assert.Single(vm.PreviewRows);
    }

    /// <summary>An ordinary edit does not go back to the clipboard: the automatic read happens when the surface
    /// starts using the clipboard, not on every keystroke that re-runs the chain.</summary>
    [Fact]
    public async Task ADecision_DoesNotReReadTheClipboard()
    {
        var reads = 0;
        var vm = ClipboardVm(() =>
        {
            reads++;
            return "Indeks\tNazwa\r\n1\tabc\r\n";
        });

        vm.UseClipboardCommand.Execute(null);
        await SettleAsync(vm);
        Assert.Equal(1, reads);

        vm.Source.TrimWhitespace = true;
        await SettleAsync(vm);

        Assert.Equal(1, reads);
    }

    /// <summary>
    /// ⭐ The user's first ask: a surface that opens on a clipboard configuration reads the clipboard by itself.
    /// It is what makes the clipboard a live source rather than a one-off paste — and it is only possible because
    /// the read is answered by the ENVIRONMENT, i.e. before anything could be wired to the finished tab.
    /// </summary>
    [Fact]
    public async Task OpeningOnAClipboardConfiguration_ReadsTheClipboardByItself()
    {
        var configuration = ImportConfiguration.Empty with { Source = SourceDescriptor.Clipboard() };
        var vm = ClipboardVm(() => "Indeks\tNazwa\r\n1\tabc\r\n2\tdef\r\n", lastUsed: configuration);

        await SettleAsync(vm);

        Assert.False(vm.Source.UseFile);
        Assert.Equal(new[] { "Indeks", "Nazwa" }, vm.PreviewFields.Select(f => f.Name));
        Assert.Equal(2, vm.PreviewRows.Count);
    }

    /// <summary>
    /// ⚠ …but only for content that IS a table. Nobody asked for this read, so filling the surface with a copied
    /// sentence — and running a whole read chain over it — has to be earned. An explicit refresh is a different
    /// question and adopts whatever is there (below).
    /// </summary>
    [Fact]
    public async Task OpeningOnAClipboardConfiguration_IgnoresContentThatIsNotATable()
    {
        var configuration = ImportConfiguration.Empty with { Source = SourceDescriptor.Clipboard() };
        var vm = ClipboardVm(() => "select * from orders where id = 4", lastUsed: configuration);

        await SettleAsync(vm);

        Assert.Equal(string.Empty, vm.Source.ClipboardText);
        Assert.Empty(vm.PreviewRows);
    }

    /// <summary>
    /// The same non-tabular text, but now the user asked for it: „re-read the clipboard" has exactly one honest
    /// meaning, so it is adopted and the surface shows what it found. Refusing an explicit request because the
    /// content looks unpromising would be the module deciding it knows better.
    /// </summary>
    [Fact]
    public async Task Refresh_AdoptsWhateverTheClipboardHolds()
    {
        var reads = 0;
        var vm = ClipboardVm(() => ++reads == 1 ? "A\tB\r\n1\t2\r\n" : "select * from orders where id = 4");

        vm.UseClipboardCommand.Execute(null);
        await SettleAsync(vm);
        Assert.Equal(new[] { "A", "B" }, vm.PreviewFields.Select(f => f.Name));

        // The clipboard now holds something the implicit read would have declined. The user asked, so it lands.
        vm.RefreshCommand.Execute(null);
        await SettleAsync(vm);

        Assert.Equal("select * from orders where id = 4", vm.Source.ClipboardText);

        // Re-read AND re-analysed: one line, one field. (No data rows — with a header expected, the only line
        // there is IS the header. Which is the honest reading of a one-line source, not a failure to read it.)
        Assert.Single(vm.PreviewFields);
        Assert.Empty(vm.PreviewRows);
    }

    /// <summary>Switching away from the clipboard re-arms the automatic read, so coming back looks at it again
    /// without the user having to ask.</summary>
    [Fact]
    public async Task SwitchingAwayFromTheClipboardAndBack_ReadsItAgain()
    {
        var reads = 0;
        var vm = ClipboardVm(() =>
        {
            reads++;
            return "Indeks\tNazwa\r\n1\tabc\r\n";
        });

        vm.UseClipboardCommand.Execute(null);
        await SettleAsync(vm);
        Assert.Equal(1, reads);

        vm.Source.UseFile = true;
        vm.Source.FilePath = WriteFile("in.csv", "A;B\n1;x\n");
        await SettleAsync(vm);
        Assert.Equal(1, reads);

        // Back to the clipboard by the property alone — no command, no refresh.
        vm.Source.UseFile = false;
        await SettleAsync(vm);

        Assert.Equal(2, reads);
        Assert.Equal(new[] { "Indeks", "Nazwa" }, vm.PreviewFields.Select(f => f.Name));
    }

    /// <summary>
    /// „Tabular" is not a new heuristic — the first question goes to <c>DelimiterDetector</c>, the module's one
    /// owner of „is this delimited text". The second is only there because that detector deliberately refuses to
    /// invent a separator for a single column, and a single column pasted out of Excel is still a table.
    /// </summary>
    [Theory]
    [InlineData("", false)]
    [InlineData("   \r\n  ", false)]
    [InlineData("just a sentence", false)]
    [InlineData("A\tB\r\n1\t2\r\n", true)]
    [InlineData("A;B\r\n1;2\r\n", true)]
    [InlineData("1234\r\n5678\r\n", true)]
    [InlineData("1234\r\n", false)]
    public void LooksTabular_AnswersFromTheModulesOwnDetector(string text, bool expected)
        => Assert.Equal(expected, DataImportTabViewModel.LooksTabular(text, new DelimitedOptions()));

    /// <summary>
    /// ⚠ A deliberate boundary, stated as a test so it cannot be „fixed" by accident: a refresh does NOT re-propose
    /// the types of a new table while the source still describes the same fields. Types the user edited are
    /// decisions, and overwriting a decision with a proposal is the one thing rule #11 forbids outright; the
    /// existing rule already re-infers when the fields or the culture move, which is when the ground under those
    /// decisions has actually shifted.
    /// </summary>
    [Fact]
    public async Task Refresh_DoesNotOverwriteTypesTheUserEdited()
    {
        var vm = Vm();
        var path = WriteFile("in.csv", "KOD;ILOSC\nA1;1\n");

        vm.Source.FilePath = path;
        await SettleAsync(vm);

        vm.Target.IsNewTable = true;
        vm.Target.NewTableName = "IMP_NEW";
        await SettleAsync(vm);

        var column = vm.Target.NewColumns.Single(c => c.Name == "ILOSC");
        column.Type = "VARCHAR";
        column.Size = 30;
        await SettleAsync(vm);
        Assert.Equal("VARCHAR(30)", column.TypeText);

        // The same columns, different values — the fields have not moved, so neither has the decision.
        File.WriteAllText(path, "KOD;ILOSC\nA1;1\nA2;2\n");
        vm.RefreshCommand.Execute(null);
        await SettleAsync(vm);

        Assert.Equal("VARCHAR(30)", vm.Target.NewColumns.Single(c => c.Name == "ILOSC").TypeText);
        Assert.Equal(2, vm.PreviewRows.Count);
    }
}

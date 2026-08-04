// DEVELOPER MEASUREMENT TOOL — NOT PART OF THE PRODUCT. See tools/probes/README.md.
//
// Import file-selection cost, measured before M3b.2.
//
// THE QUESTION (user, 2026-08-04): choosing a LARGE file for import visibly freezes the UI until the read
// finishes, and the progress bar only starts afterwards. Which part is synchronous on the UI thread — the
// open, the read, the parse, or the model build?
//
// WHAT THIS MEASURES. The surface's real file-selection chain is DataImportTabViewModel.ReadSourceAsync:
//     ListSheetsAsync → RunDetectionAsync → ReadSchemaAsync → LoadPreviewAsync (bounded head of
//     ReadRecordsAsync)
// and NONE of those four is wrapped in Task.Run — unlike the type inference and the converted preview, which
// are. So the cost of each, plus whether its continuation returns on the CALLING thread, decides whether the
// UI can freeze at all.
//
// HOW THE THREAD CLAIM IS MEASURED, and why it is not a detail: FileImportSource.OpenTextAsync /
// OpenStreamAsync return Task.FromResult(...). An await on an ALREADY-COMPLETED task continues INLINE on the
// same thread regardless of ConfigureAwait — so the provider bodies run wherever the caller ran, i.e. on the
// UI thread. The probe proves that by comparing managed thread ids across each await rather than asserting it
// from the source.
//
// ⚠⚠ THE GENERATED WORKBOOK USES A SHARED STRING TABLE ON PURPOSE. The first draft of this probe wrote inline
// strings, which produce NO sharedStrings.xml — it would have measured a file shape the user does not have and
// reported the table cost as zero. Excel writes shared strings, so the probe must too, and every row gets a
// UNIQUE text because that is what makes the table large.
//
// RUN: dotnet run --project tools\probes\ImportFileOpenProbe -c Release
//      (optional first arg = row count for the generated files; default 300 000)

using System.Diagnostics;
using Avalonia;
using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EmberTern.Core.Import;
using EmberTern.Core.Import.Providers;
using EmberTern.Office;

var rows = args.Length > 0 && int.TryParse(args[0], out var parsed) ? parsed : 300_000;
var dir = Path.Combine(Path.GetTempPath(), "embertern-import-open-probe");
Directory.CreateDirectory(dir);

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine($"=== ImportFileOpenProbe — {rows:N0} wierszy ===");
Console.WriteLine($"katalog: {dir}");
Console.WriteLine($"watek startowy: {Environment.CurrentManagedThreadId}");
Console.WriteLine();

var csvPath = Path.Combine(dir, $"probe-{rows}.csv");
var xlsxPath = Path.Combine(dir, $"probe-{rows}.xlsx");

if (!File.Exists(csvPath)) Stage("generowanie CSV", () => WriteCsv(csvPath, rows), () => Size(csvPath));
else Console.WriteLine($"  CSV już istnieje ({Size(csvPath)}) — pomijam generowanie");

if (!File.Exists(xlsxPath)) Stage("generowanie XLSX", () => WriteXlsx(xlsxPath, rows), () => Size(xlsxPath));
else Console.WriteLine($"  XLSX już istnieje ({Size(xlsxPath)}) — pomijam generowanie");
Console.WriteLine();

// ── CSV ──────────────────────────────────────────────────────────────────────────────────────────────

Console.WriteLine($"── CSV ({Size(csvPath)}) ─────────────────────────────────────────────");
{
    var provider = new DelimitedTextImportProvider();
    var source = new FileImportSource(csvPath);
    var cfg = new ImportConfiguration
    {
        Source = new SourceDescriptor { Kind = ImportSourceKind.Csv, Path = csvPath },
        Delimited = new DelimitedOptions { HasHeader = true },
        Spreadsheet = null,
    };

    await StageAsync("ReadDetectionSample (detekcja kodowania)", () =>
        Task.FromResult($"{source.ReadDetectionSample().Length} B próbki"));

    await StageAsync("ReadSchemaAsync (próbka 200 rekordów)", async () =>
    {
        var before = Environment.CurrentManagedThreadId;
        var schema = await provider.ReadSchemaAsync(source, cfg, CancellationToken.None);
        var after = Environment.CurrentManagedThreadId;
        return $"{schema.Fields.Count} kolumn · EstimatedRows={Describe(schema.EstimatedRows)} · "
             + $"wątek {before}→{after} {Same(before, after)}";
    });

    await StageAsync("ReadRecordsAsync — 100 wierszy podglądu", async () =>
    {
        var before = Environment.CurrentManagedThreadId;
        var taken = 0;
        await foreach (var _ in provider.ReadRecordsAsync(source, cfg, CancellationToken.None))
        {
            if (++taken >= 100) break;
        }
        var after = Environment.CurrentManagedThreadId;
        return $"{taken} wierszy · wątek {before}→{after} {Same(before, after)}";
    });
}

// ── XLSX ─────────────────────────────────────────────────────────────────────────────────────────────

Console.WriteLine();
Console.WriteLine($"── XLSX ({Size(xlsxPath)}) ───────────────────────────────────────────");
{
    var provider = new XlsxImportProvider();
    var source = new FileImportSource(xlsxPath);
    var cfg = new ImportConfiguration
    {
        Source = new SourceDescriptor { Kind = ImportSourceKind.Xlsx, Path = xlsxPath },
        Delimited = null,
        Spreadsheet = new SpreadsheetOptions { HasHeader = true },
    };

    await StageAsync("ListSheetsAsync", async () =>
    {
        var before = Environment.CurrentManagedThreadId;
        var sheets = await provider.ListSheetsAsync(source, CancellationToken.None);
        var after = Environment.CurrentManagedThreadId;
        var first = sheets.Count > 0 ? sheets[0].EstimatedRows : null;
        return $"{sheets.Count} arkuszy · wiersze z <dimension>={Describe(first)} · "
             + $"wątek {before}→{after} {Same(before, after)}";
    });

    await StageAsync("ReadSchemaAsync (próbka wierszy + tablica stringów)", async () =>
    {
        var before = Environment.CurrentManagedThreadId;
        var schema = await provider.ReadSchemaAsync(source, cfg, CancellationToken.None);
        var after = Environment.CurrentManagedThreadId;
        return $"{schema.Fields.Count} kolumn · EstimatedRows={Describe(schema.EstimatedRows)} · "
             + $"wątek {before}→{after} {Same(before, after)}";
    });

    await StageAsync("ReadRecordsAsync — 100 wierszy podglądu", async () =>
    {
        var before = Environment.CurrentManagedThreadId;
        var taken = 0;
        await foreach (var _ in provider.ReadRecordsAsync(source, cfg, CancellationToken.None))
        {
            if (++taken >= 100) break;
        }
        var after = Environment.CurrentManagedThreadId;
        return $"{taken} wierszy · wątek {before}→{after} {Same(before, after)}";
    });

    // Rozbicie kosztu — to jedyny sposób, żeby powiedzieć, CO jest drogie: otwarcie paczki, tablica
    // stringów współdzielonych, czy odczyt próbki wierszy.
    Console.WriteLine();
    Console.WriteLine("  rozbicie kosztu (te same operacje, osobno):");

    Stage("  SpreadsheetDocument.Open + Sheets", () =>
    {
        using var s = File.OpenRead(xlsxPath);
        using var d = SpreadsheetDocument.Open(s, false);
        _ = d.WorkbookPart?.Workbook.Sheets;
    }, () => "paczka otwarta");

    var sharedCount = 0;
    Stage("  odczyt CAŁEJ tablicy stringów współdzielonych", () =>
    {
        using var s = File.OpenRead(xlsxPath);
        using var d = SpreadsheetDocument.Open(s, false);
        var part = d.WorkbookPart?.SharedStringTablePart;
        if (part is null) return;
        using var r = OpenXmlReader.Create(part);
        while (r.Read())
        {
            if (r.ElementType != typeof(SharedStringItem)) continue;
            if (r.LoadCurrentElement() is SharedStringItem) sharedCount++;
        }
    }, () => $"{sharedCount:N0} pozycji");

    // ⭐⭐ IZOLACJA PRAWDZIWEGO KOSZTU. `RowsFromDimension` czyta `worksheetPart.Worksheet` — a to jest
    // akcesor DOM, który materializuje CAŁY arkusz do drzewa obiektów, jeszcze PRZED sprawdzeniem, czy
    // element <dimension> w ogóle istnieje. Ten pomiar oddziela ten jeden dostęp od wszystkiego innego.
    var domNote = string.Empty;
    Stage("  DOSTĘP DO worksheetPart.Worksheet (materializacja DOM)", () =>
    {
        using var s = File.OpenRead(xlsxPath);
        using var d = SpreadsheetDocument.Open(s, false);
        var wb = d.WorkbookPart!;
        var sheet = wb.Workbook.Sheets!.Elements<Sheet>().First();
        var part = (WorksheetPart)wb.GetPartById(sheet.Id!.Value!);
        var dim = part.Worksheet?.SheetDimension?.Reference?.Value;
        domNote = $"<dimension>={(string.IsNullOrEmpty(dim) ? "BRAK" : dim)}";
    }, () => domNote);

    Stage("  strumieniowy odczyt 100 wierszy (OpenXmlReader, bez DOM)", () =>
    {
        using var s = File.OpenRead(xlsxPath);
        using var d = SpreadsheetDocument.Open(s, false);
        var wb = d.WorkbookPart!;
        var sheet = wb.Workbook.Sheets!.Elements<Sheet>().First();
        var part = (WorksheetPart)wb.GetPartById(sheet.Id!.Value!);
        var taken = 0;
        using var r = OpenXmlReader.Create(part);
        while (r.Read() && taken < 100)
        {
            if (r.ElementType != typeof(Row)) continue;
            if (r.LoadCurrentElement() is Row) taken++;
        }
    }, () => "dla porównania z dostępem do DOM");
}

// ── ten sam arkusz, ale Z elementem <dimension> — tak zapisuje Excel ─────────────────────────────────
//
// ⚠⚠ BEZ TEGO POMIAR BY NIE GENERALIZOWAŁ. Plik wygenerowany wyżej nie ma <dimension>, więc można by
// zarzucić, że mierzy wyłącznie ścieżkę awaryjną, której prawdziwy plik z Excela nie dotyka. Ten blok
// sprawdza to wprost, na pliku, który ten element ma.

Console.WriteLine();
var dimPath = Path.Combine(dir, $"probe-{rows}-dim.xlsx");
if (!File.Exists(dimPath)) Stage("generowanie XLSX z <dimension>", () => WriteXlsx(dimPath, rows, withDimension: true), () => Size(dimPath));
else Console.WriteLine($"  XLSX z <dimension> już istnieje ({Size(dimPath)})");

Console.WriteLine($"── XLSX Z <dimension> ({Size(dimPath)}) ──────────────────────────────");
{
    var provider = new XlsxImportProvider();
    var source = new FileImportSource(dimPath);
    var cfg = new ImportConfiguration
    {
        Source = new SourceDescriptor { Kind = ImportSourceKind.Xlsx, Path = dimPath },
        Delimited = null,
        Spreadsheet = new SpreadsheetOptions { HasHeader = true },
    };

    await StageAsync("ListSheetsAsync", async () =>
    {
        var sheets = await provider.ListSheetsAsync(source, CancellationToken.None);
        return $"{sheets.Count} arkuszy · wiersze z <dimension>={Describe(sheets.Count > 0 ? sheets[0].EstimatedRows : null)}";
    });

    await StageAsync("ReadSchemaAsync", async () =>
    {
        var schema = await provider.ReadSchemaAsync(source, cfg, CancellationToken.None);
        return $"{schema.Fields.Count} kolumn · EstimatedRows={Describe(schema.EstimatedRows)}";
    });

    // ⭐⭐ CZY TEN SAM ATRYBUT DA SIĘ ODCZYTAĆ TANIO. Propozycja bez tego pomiaru byłaby zgadywaniem:
    // <dimension> jest pierwszym dzieckiem <worksheet>, PRZED <sheetData>, więc OpenXmlReader powinien
    // dojść do niego natychmiast i zatrzymać się, nie materializując arkusza.
    Console.WriteLine();
    Console.WriteLine("  czy <dimension> da się przeczytać bez DOM:");
    foreach (var (label, path) in new[] { ("plik Z <dimension>", dimPath), ("plik BEZ <dimension>", xlsxPath) })
    {
        var note = string.Empty;
        Stage($"  OpenXmlReader do <dimension> — {label}", () =>
        {
            using var s = File.OpenRead(path);
            using var d = SpreadsheetDocument.Open(s, false);
            var wb = d.WorkbookPart!;
            var sheet = wb.Workbook.Sheets!.Elements<Sheet>().First();
            var part = (WorksheetPart)wb.GetPartById(sheet.Id!.Value!);

            string? reference = null;
            using var r = OpenXmlReader.Create(part);
            while (r.Read())
            {
                if (r.ElementType == typeof(SheetDimension))
                {
                    reference = (r.LoadCurrentElement() as SheetDimension)?.Reference?.Value;
                    break;
                }
                // ⚠ Zatrzymanie na <sheetData> jest tym, co czyni odczyt tanim także dla pliku BEZ
                // <dimension>: bez tego czytelnik przeszedłby przez wszystkie wiersze, szukając elementu,
                // którego nie ma — czyli zamienilibyśmy jeden drogi mechanizm na drugi.
                if (r.ElementType == typeof(SheetData)) break;
            }
            note = reference is null ? "BRAK (zatrzymane na <sheetData>)" : reference;
        }, () => note);
    }
}


// ═════════════════════════════════════════════════════════════════════════════════════════════════════
// CZĘŚĆ 2 — ODCINEK SYNCHRONICZNY OD WYBORU PLIKU DO PIERWSZEJ MOŻLIWEJ KLATKI
//
// Część 1 wyżej wyceniła providera. To NIE JEST odpowiedź na objaw, który zgłasza użytkownik: „po
// wskazaniu pliku aplikacja na kilka sekund zamarza, nie odświeża UI i wygląda, jakby przeskakiwała
// przy odmalowywaniu". Objaw jest o wątku UI, nie o koszcie odczytu.
//
// CO SIĘ MIERZY. `DataImportTabViewModel.Recalculate` startuje łańcuch przypisaniem
// `PendingRecalculation = RunGuardedChainAsync(...)` — metoda `async` biegnie INLINE do pierwszego
// NIEZAKOŃCZONEGO await. Zmierzone w części 1: wszystkie awaity providera kończą się synchronicznie.
// Wniosek do sprawdzenia: całość dzieje się wewnątrz przypisania `Source.FilePath = path`, czyli
// wewnątrz zwykłego settera właściwości, bez oddania sterowania Dispatcherowi.
//
// JAK MIERZONE JEST „PIERWSZE ODMALOWANIE". Przed przypisaniem odkładamy na Dispatcher zadanie
// o priorytecie Render. Jeżeli wykona się dopiero PO powrocie z settera, to znaczy, że w tym czasie
// okno nie miało ani jednej okazji się przemalować — a to jest dokładnie definicja zamrożonego UI.
// ⚠ To jest pomiar OKAZJI do odmalowania, nie samego piksela: sonda nie ma okna. Granica podana wprost,
// bo pomiar bez podanego zakresu wprowadza w błąd bardziej niż jego brak.
// ═════════════════════════════════════════════════════════════════════════════════════════════════════

Console.WriteLine();
Console.WriteLine("═══ CZĘŚĆ 2: odcinek synchroniczny wyboru pliku (prawdziwy Dispatcher Avalonii) ═══");

Avalonia.AppBuilder
    .Configure<ProbeApp>()
    .UsePlatformDetect()
    .SetupWithoutStarting();

foreach (var (label, path) in new[] { ("CSV", csvPath), ("XLSX", xlsxPath) })
{
    Console.WriteLine();
    Console.WriteLine($"── {label} ({Size(path)}) ──");

    var vm = new EmberTern.App.ViewModels.DataImportTabViewModel(
        new EmberTern.App.ViewModels.DataImportEnvironment(() => false, () => "—"));
    // ⚠ `PreviewDebounce` jest `internal` (widoczne dla testów, nie dla sondy), więc podglądu po
    // konwersji nie skracamy. Nie ma to znaczenia dla przedmiotu pomiaru: to OSTATNIE ogniwo łańcucha,
    // za pierwszym oddaniem sterowania, a mierzymy odcinek PRZED nim.

    // Ile razy łańcuch w ogóle startuje — `BrowseAsync` ustawia DWIE właściwości, a każda podnosi Changed.
    var chainStarts = 0;
    vm.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(vm.IsBusy) && vm.IsBusy) chainStarts++;
    };

    var renderRan = false;
    long renderAtMs = -1;
    var sw = System.Diagnostics.Stopwatch.StartNew();

    // Zadanie o priorytecie Render — to samo, którym odmalowuje się okno.
    Avalonia.Threading.Dispatcher.UIThread.Post(
        () => { renderRan = true; renderAtMs = sw.ElapsedMilliseconds; },
        Avalonia.Threading.DispatcherPriority.Render);

    // ⭐ TO JEST TA JEDNA LINIA, KTÓRA JEST PRZEDMIOTEM POMIARU — zwykły setter właściwości.
    var before = System.Diagnostics.Stopwatch.StartNew();
    vm.Source.UseFile = true;
    var afterUseFile = before.ElapsedMilliseconds;
    vm.Source.FilePath = path;
    var afterFilePath = before.ElapsedMilliseconds;

    var span = afterFilePath - afterUseFile;
    Console.WriteLine($"  Source.UseFile = true          {afterUseFile,7:N0} ms   (łańcuch startuje, ale ścieżki jeszcze nie ma)");
    Console.WriteLine($"  Source.FilePath = path         {span,7:N0} ms   ⭐ ODCINEK SYNCHRONICZNY (setter nie wraca)");

    // ⚠⚠ WERDYKT, NIE SAMO „TAK/NIE". Pytanie „czy okno dostało klatkę W TRAKCIE settera" ma sens wyłącznie
    // wtedy, gdy setter w ogóle blokuje. Przy odcinku 0 ms odpowiedź „NIE" jest prawdziwa i zupełnie myląca —
    // klatka nie była potrzebna. Log, który da się przeczytać jako porażkę, jest gorszy niż brak logu.
    const long FrameBudgetMs = 33;   // ~jedna klatka przy 30 fps
    Console.WriteLine(span <= FrameBudgetMs
        ? $"  werdykt: OK — odcinek mieści się w budżecie klatki ({FrameBudgetMs} ms), okno nie miało czego przegapić"
        : renderRan
            ? "  werdykt: OK — okno dostało klatkę W TRAKCIE blokującego odcinka"
            : "  werdykt: ⛔ ZABLOKOWANE — odcinek dłuższy niż klatka i ani jednej okazji na odmalowanie");

    // Dopiero teraz Dispatcher dostaje szansę.
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
    Console.WriteLine($"  zadanie Render wykonane po oddaniu sterowania: {(renderRan ? $"tak, w {renderAtMs:N0} ms od startu" : "nie")}");
    Console.WriteLine($"  ile razy łańcuch wszedł w stan zajętości: {chainStarts}");

    // Reszta pracy (inferencja w Task.Run, podgląd po konwersji) biegnie już poza wątkiem — dajemy jej
    // chwilę, żeby następna iteracja nie mierzyła ogona poprzedniej.
    // ⚠⚠ Tu MUSI być synchroniczny Sleep, nie `await Task.Delay`. Po `SetupWithoutStarting()` bieżący wątek
    // JEST wątkiem UI Avalonii, więc await odłożyłby kontynuację na Dispatcher, którego nikt nie pompuje —
    // sonda zawisłaby na zawsze (sprawdzone: zawisła). To ta sama pułapka, którą sonda mierzy, jeden poziom
    // wyżej: praca na wątku UI bez pętli komunikatów nie ma jak dokończyć.
    Thread.Sleep(400);
    Avalonia.Threading.Dispatcher.UIThread.RunJobs();
}


Console.WriteLine();
Console.WriteLine("=== KONIEC ===");
Console.WriteLine($"Pliki pozostawione w {dir} — usuń je ręcznie.");
return 0;

// ── infrastruktura pomiaru ───────────────────────────────────────────────────────────────────────────

static void Stage(string name, Action work, Func<string> describe)
{
    var sw = Stopwatch.StartNew();
    work();
    sw.Stop();
    Console.WriteLine($"  {name,-52} {sw.ElapsedMilliseconds,7:N0} ms   {describe()}");
}

static async Task StageAsync(string name, Func<Task<string>> work)
{
    var sw = Stopwatch.StartNew();
    var note = await work();
    sw.Stop();
    Console.WriteLine($"  {name,-52} {sw.ElapsedMilliseconds,7:N0} ms   {note}");
}

static string Same(int a, int b) => a == b ? "(TEN SAM — praca zostaje u wołającego)" : "(ZMIANA wątku)";

static string Describe(long? v) => v is null ? "null" : v.Value.ToString("N0", CultureInfo.InvariantCulture);

static string Size(string path) => $"{new FileInfo(path).Length / 1024.0 / 1024.0:N1} MB";

// ── generatory plików ────────────────────────────────────────────────────────────────────────────────

static void WriteCsv(string path, int rows)
{
    using var w = new StreamWriter(path, false, new UTF8Encoding(false));
    w.WriteLine("ID;NAZWA;KOD;KWOTA;DATA");
    for (var i = 1; i <= rows; i++)
    {
        w.Write(i.ToString(CultureInfo.InvariantCulture));
        w.Write(";Kontrahent ");
        w.Write(i.ToString(CultureInfo.InvariantCulture));
        w.Write(";KOD-");
        w.Write((i % 5000).ToString(CultureInfo.InvariantCulture));
        w.Write(';');
        w.Write((i * 1.37).ToString("F2", CultureInfo.InvariantCulture));
        w.Write(";2026-01-");
        w.WriteLine(((i % 28) + 1).ToString("00", CultureInfo.InvariantCulture));
    }
}

// Zapis strumieniowy przez OpenXmlWriter — DOM zjadłby pamięć przy setkach tysięcy wierszy.
// ⚠ Tekst idzie przez TABLICĘ STRINGÓW WSPÓŁDZIELONYCH, bo tak zapisuje Excel i bo właśnie ta tablica
// jest przedmiotem pomiaru; kolumna NAZWA ma wartość unikalną w każdym wierszu, żeby tablica rosła.
static void WriteXlsx(string path, int rows, bool withDimension = false)
{
    if (File.Exists(path)) File.Delete(path);
    using var doc = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
    var workbookPart = doc.AddWorkbookPart();

    // 1) tablica stringów współdzielonych: nagłówki + unikalna nazwa na wiersz + powtarzalne kody
    var sharedPart = workbookPart.AddNewPart<SharedStringTablePart>();
    var headers = new[] { "ID", "NAZWA", "KOD", "KWOTA", "DATA" };
    const int codeCount = 5000;
    using (var w = OpenXmlWriter.Create(sharedPart))
    {
        w.WriteStartElement(new SharedStringTable());
        foreach (var h in headers) WriteSharedItem(w, h);
        for (var i = 1; i <= rows; i++)
        {
            WriteSharedItem(w, "Kontrahent " + i.ToString(CultureInfo.InvariantCulture));
        }
        for (var c = 0; c < codeCount; c++)
        {
            WriteSharedItem(w, "KOD-" + c.ToString(CultureInfo.InvariantCulture));
        }
        w.WriteEndElement();
    }

    // indeksy: 0..4 nagłówki · 5+i-1 nazwa wiersza i · 5+rows+c kod c
    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    using (var w = OpenXmlWriter.Create(worksheetPart))
    {
        w.WriteStartElement(new Worksheet());
        if (withDimension)
        {
            w.WriteElement(new SheetDimension { Reference = "A1:E" + (rows + 1).ToString(CultureInfo.InvariantCulture) });
        }
        w.WriteStartElement(new SheetData());

        w.WriteStartElement(new Row { RowIndex = 1U });
        for (var c = 0; c < headers.Length; c++) WriteSharedCell(w, ColumnName(c) + "1", c);
        w.WriteEndElement();

        for (var i = 1; i <= rows; i++)
        {
            var rowIndex = i + 1;
            var suffix = rowIndex.ToString(CultureInfo.InvariantCulture);
            w.WriteStartElement(new Row { RowIndex = (uint)rowIndex });
            WriteNumberCell(w, "A" + suffix, i);
            WriteSharedCell(w, "B" + suffix, headers.Length + i - 1);
            WriteSharedCell(w, "C" + suffix, headers.Length + rows + (i % codeCount));
            WriteNumberCell(w, "D" + suffix, Math.Round(i * 1.37, 2));
            WriteSharedCell(w, "E" + suffix, headers.Length + rows + (i % codeCount));
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteEndElement();
    }

    workbookPart.Workbook = new Workbook(
        new Sheets(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1U,
            Name = "Dane",
        }));
    workbookPart.Workbook.Save();
}

static void WriteSharedItem(OpenXmlWriter w, string text)
{
    w.WriteStartElement(new SharedStringItem());
    w.WriteElement(new Text(text));
    w.WriteEndElement();
}

static void WriteSharedCell(OpenXmlWriter w, string reference, int sharedIndex)
    => w.WriteElement(new Cell
    {
        CellReference = reference,
        DataType = CellValues.SharedString,
        CellValue = new CellValue(sharedIndex.ToString(CultureInfo.InvariantCulture)),
    });

static void WriteNumberCell(OpenXmlWriter w, string reference, double value)
    => w.WriteElement(new Cell
    {
        CellReference = reference,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture)),
    });

static string ColumnName(int index)
{
    var name = string.Empty;
    for (var i = index; ; i = i / 26 - 1)
    {
        name = (char)('A' + i % 26) + name;
        if (i < 26) break;
    }
    return name;
}

internal sealed class ProbeApp : Avalonia.Application
{
    public override void Initialize()
    {
        // Sonda nie renderuje okna, więc motywy nie są potrzebne — potrzebny jest sam Dispatcher.
    }
}

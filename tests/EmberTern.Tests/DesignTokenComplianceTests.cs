using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SkiaSharp;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The Design Token guard (Product Polish §11). Typography, corner radii and font families belong to the
/// token catalog in <c>Themes/Tokens.axaml</c> and <c>Themes/Typography.axaml</c> — not to individual views.
/// <para>
/// <b>Why this test exists at all.</b> The project has a hard precedent for what happens without a guard: a
/// keyboard shortcut typed by hand into a tooltip survived the command being re-bound from <c>Alt+F</c> to
/// <c>Ctrl+K</c> for an entire sprint, with a green build and green tests (gotcha #284). A value copied by hand
/// goes stale silently. A design system without a guard grows back into 589 local <c>FontSize</c> declarations,
/// which is exactly the state the M0 audit measured.
/// </para>
/// <para>
/// <b>⭐ Why counts and not a plain file list.</b> §11 first described a list of exempted files. The measured
/// starting state showed why that is not enough: 609 <c>FontSize</c> declarations spread over 49 files, with a
/// single file holding 86. A file-level exemption would clear <c>DataImportTabView.axaml</c> wholesale — it
/// could add an 87th with no signal at all, which is precisely the silence this test exists to break. So an
/// exemption is a <i>pair</i>: file → how many declarations it had when the baseline was taken.
/// </para>
/// <para>
/// <b>⭐ The ratchet detects DRIFT — it does not veto DECISIONS</b> (user, on accepting the design, 2026-08-01).
/// A red test does not mean "you did something wrong"; it means "state which of the two things you are doing".
/// If a number changes deliberately and the change is written down in the stage's documentation, updating the
/// baseline here is a <i>correct part of the process</i>, not a way around the guard. What the guard prevents is
/// the third case: a number that moved because nobody noticed.
/// </para>
/// <para>
/// <b>What is deliberately NOT guarded.</b> <c>Margin</c> and <c>Padding</c> are too contextual — placement
/// inside a layout is the host's responsibility, not the chrome's — so a test over them would be either full of
/// holes or a constant nuisance. There the tool is the review in M2c and M5 (§11).
/// </para>
/// <para>
/// <b>Scope.</b> <c>Views/</c> and <c>Controls/</c> only. <c>Themes/</c> is excluded on purpose: that is where
/// the system lives, and a style setter declaring <c>FontSize</c> there is the catalog doing its job.
/// </para>
/// <para>
/// ⚠⚠ <b>ZMIERZONE W M4 I ŚWIADOMIE NIEZAŁATANE — okno licznika NIE WIDZI 29 deklaracji.</b>
/// <c>Themes/PickerTemplates.axaml</c> ma 12 literałów, <c>Completion/*.cs</c> — 16 (karta hover, Quick Info,
/// Parameter Helper, czyli powierzchnie oglądane przy pisaniu każdego zapytania), <c>Sql/</c> — 1.
/// ⭐ Argument o <c>Themes/</c> powyżej był słuszny, gdy go pisano, i przestał być słuszny w M2c iteracji 6:
/// odkąd regex pomija setter czytający katalog, „katalog robiący swoje" i tak jest dla licznika niewidoczny,
/// więc wyłączenie folderu chroni już tylko LITERAŁY w nim. <c>Completion/</c> i <c>Sql/</c> nigdy nie były
/// przedmiotem żadnego argumentu — po prostu leżały poza oknem.
/// </para>
/// <para>
/// ⛔ <b>Poszerzenie okna NIE zostało wykonane w bloku typografii M4 i wymaga osobnej decyzji użytkownika</b>,
/// bo pociąga za sobą także <c>FontFamily</c> (ten sam <c>Measure</c> obsługuje obie własności), a to jest
/// temat czcionki monospace — ratyfikowany jako backlog sprintu UX, nie robota tego bloku.
/// ⭐ Rzecz warta zapamiętania niezależnie od decyzji: <b>wartość ustawiona tam, gdzie licznik nie zagląda,
/// nie jest „czysta" — jest niezmierzona</b>, a liczba, którą etap raportuje, jest wtedy nie tyle duża,
/// co nieprawdziwa. Ten sam kształt co gotcha #332, o jedną własność dalej.
/// </para>
/// </summary>
public class DesignTokenComplianceTests
{
    // A declaration is an assignment (`FontSize="12"` in XAML, `FontSize = 12` in an object initializer, or
    // `label.FontSize = 12`) or a style setter (`<Setter Property="FontSize" …>`). `(?!=)` keeps a C# equality
    // comparison out, and matching on `=` rather than the bare word keeps `new FontFamily("…")` from counting
    // twice for one declaration.
    // ⭐⭐ A VALUE READ FROM THE CATALOG IS NOT COUNTED, and that correction (M2b step 12) is what makes
    // this number mean something. `FontSize="{DynamicResource Text.Status.Size}"` is precisely the state
    // M2c is supposed to arrive at — counting it identically to the `FontSize="12"` it replaced made the
    // stage's exit condition unreachable: a fully migrated view would report the same total as an
    // untouched one. The negative lookahead excludes a resource reference, so what is left is what the
    // name says: LOCAL VALUES. ⚠ The baselines below were re-measured against this rule, so they are NOT
    // comparable with the ones from M2a — that drop is migration already done, not unrecorded progress.
    // ⚠⚠ DRUGA POŁOWA TEJ SAMEJ POPRAWKI — dopisana w M2c iteracji 6 (§18.6), po pomiarze.
    // M2b krok 12 wyłączył z licznika ATRYBUT czytający katalog (`FontSize="{DynamicResource …}"`), ale
    // drugi człon tego wzorca — STYL LOKALNY W WIDOKU (`<Setter Property="FontSize" Value="…" />`) — był
    // liczony bezwarunkowo, więc `Value="{DynamicResource Text.Grid.Size}"` liczyło się dokładnie tak samo
    // jak `Value="11"`. To ten sam defekt i to samo uzasadnienie: licznik ma mierzyć WARTOŚCI LOKALNE,
    // a setter czytający rolę wartością lokalną nie jest — inaczej plik z lokalnym stylem NIE MOŻE
    // osiągnąć stanu docelowego, choćby był zmigrowany w całości.
    // ⭐ Zmiana NIE osłabia strażnika: setter z literałem (`Value="11"`) nadal się liczy, bo lookahead
    // wyklucza wyłącznie odwołanie do zasobu. Przed M2c żaden setter w `Views/`/`Controls/` nie czytał
    // katalogu, więc korekta nie rusza ani jednej bazy poza tymi, które ten etap właśnie migruje.
    private static Regex DeclarationOf(string property) =>
        new($@"\b{property}\s*=(?!=)(?!\s*""{{)|Property\s*=\s*""{property}""(?!\s*Value\s*=\s*""{{)", RegexOptions.Compiled);

    /// <summary>
    /// State measured on 2026-08-01, at the start of M2a — before any migration. A long list here is the
    /// <b>correct</b> state at this point: M2a builds the system, M2b switches it on, and <b>M2c</b> is the
    /// stage whose exit condition is this list reduced to a justified remainder. Until then the numbers are a
    /// ceiling, not an endorsement.
    /// </summary>
    private static readonly Dictionary<string, int> FontSizeBaseline = new(StringComparer.Ordinal)
    {
        // ⭐⭐ OSIEM EDYTORÓW OBIEKTÓW — 141 → 0 (M2c iteracja 5). Pierwsza iteracja BEZ ANI JEDNEGO
        // WYJĄTKU, i to nie przypadek: te widoki są zbudowane z jednego wzorca (formularz + drzewa
        // zależności + podgląd DDL), więc każda wartość miała rolę o tej samej liczbie. 31 usunięć
        // (kontrolki formularza przy 12), 110 na role. Wpisy USUNIĘTE, bo pliki są czyste — straznik
        // wymaga usunięcia wpisu, nie wyzerowania go.
        // Zdjęte: TableDetail 27 · TriggerDetail 22 · ViewDetail 20 · PackageDetail 17 · DomainDetail 16
        // · GeneratorDetail 15 · ExceptionDetail 13 · IndexDetail 11.
        // ⭐ 26 → 4 / 17 → 3 / 17 → 9 (M2c iteracja 6 — monitory). Tu odwrotnie niż w edytorach
        // obiektów: każdy z tych trzech widoków ma WŁASNE decyzje — pasek segmentowy, karty ostrzeżeń,
        // koła stanu, kapsuły postępu, nagłówki paneli szczegółów — i stąd wyjątki. Security Manager
        // trzyma OSIEM nagłówków przy 13 px, dla których katalog ma wyłącznie rolę kodu (§18.0.5/3).
        // ⭐ OGON DIALOGÓW — 28 plików, 78 → 4 (M2c iteracja 8). 8 usunięć, 70 na role, 4 wyjątki:
        // treść `ConfirmDialog`/`ChoiceDialog`/`ForeignKeyDialog` przy 13 px (katalog ma przy 13 wyłącznie
        // rolę kodu — ⚠ trzy z tych czterech zdjęło M4.4, patrz niżej) i podgląd Global Search — edytor
        // w wierszu siatki przy 12 px. 24 wpisy zdjęte
        // w całości. ⭐ `AggregationBarView` oddał swój promień na **`Radius.Chip`** — jedyny prawdziwy
        // chip w aplikacji (§18.0.5/2), wartość i funkcja zgodne, więc bez wyjątku.
        // ⭐ M4.3: Session ZDJĘTY W CAŁOŚCI (3 → 0), Trace 2 → 1, Debugger 4 → 3. Zeszły dwie grupy,
        // obie ratyfikowane na renderze: komunikat pustego stanu (13 → `Text.Application`, bo 13 to rola
        // KODU, a to nie jest kod) i podpisy przy 9 px (→ `Text.Caption` 10, bo to TEKST, a nie znak
        // strojony do kontenera).
        // ⛔ SECURITY MANAGER ZOSTAJE PRZY 9 I TO JEST DECYZJA, NIE DŁUG. Osiem z tych dziewięciu to
        // `FontWeight="Bold" FontSize="13"` bindowane do `PrivilegeStateGlyphConverter`, który zwraca
        // „✓" / „✓+" — czyli GLIFY w przycisku 20×18, strojone do KONTENERA (reguła #10), a nie tekst.
        // ⚠ Dawny komentarz w widoku twierdził „a to jest tekst" i zaliczał je do grupy „TextBlock 13 px"
        // z §18.0.5/3; M4.3 poprawiło POWÓD, nie wartość — bo to powód był nieprawdziwy (R12).
        ["Views/SecurityManagerTabView.axaml"] = 9,
        ["Views/TraceMonitorTabView.axaml"] = 1,
        // ⭐ POWIERZCHNIA TRWAŁA (§0.1) — `MainWindow` 26 → 0 + 1 promień, `BreadcrumbBar` 2 → 0,
        // `MessageBanner` 2 → 0, `TableColumnPicker` 3 → 0 (M2c iteracja 7). Zero wyjątków mimo
        // największej różnorodności ról w jednym pliku: pasek statusu dostał `Text.Status` (cztery
        // elementy — pierwszy realny konsument tej roli poza Data Import), nazwa połączenia
        // `Text.Title`, plakietka DEV MODE `Text.Caption`, dwa edytory `Text.Code`, log komunikatów
        // `Text.Application`, reszta `Text.Compact`.
        // ⭐ M4.4: wpis `ForeignKeyDialog` USUNIĘTY (1 → 0) — uzasadnienie przy zdjętej trójce niżej.
        // ⭐ 41 → 4 i 40 → 4 (M2c iteracja 4). Bliźniaki, migrowane RAZEM — mają tę samą strukturę,
        // więc osobno rozjechałyby się na pierwszej niejednoznacznej roli. Po jednym usunięciu (koszyk A),
        // reszta na role. Cztery wyjątki w każdym, identyczne co do rodzaju: dwa edytory w WIERSZU SIATKI
        // przy 12 px (§18.0.5/3 — gęstość kontenera, nie dryf), znak rodzaju przy 9 px (brak roli)
        // i nagłówek karty 12 px + SemiBold przy roli nagłówka niosącej 11 (rejestr kolizji §18.R).
        ["Views/FunctionDetailTabView.axaml"] = 3,
        ["Views/ProcedureDetailTabView.axaml"] = 3,
        // ⭐ 42 → 6 (M2c iteracja 3). Ten widok wnosi TRZECIĄ postać tego samego konfliktu: rola,
        // która pasuje FUNKCJĄ, niesie inną LICZBĘ. Trzy nagłówki sekcji mają 12 px + SemiBold, a kanoniczna
        // rola nagłówka (`Text.SectionHeader`, tyle co `group-header`) niesie 11 — więc zostają lokalne
        // z powodem, zamiast zostać opisane jako treść. Reszta wyjątków: dwa znaki przy 13 i 9 px oraz
        // jedna linia treści przy 13, gdzie katalog ma wyłącznie rolę kodu.
        ["Views/PerformancePanelView.axaml"] = 3,
        // ⭐ 82 → 4 (M2c iteracja 2). Odwrotność iteracji 1: tu koszyk A był największy w całym etapie —
        // **35 wartości po prostu usunięto**, bo `ComboBox`/`TextBox`/`CheckBox`/`NumericUpDown`/
        // `RadioButton`/`Button` już dostają dokładnie te 12 px ze stylu M2b. 41 przeszło na rolę,
        // 4 zostają: `DataGrid FontSize="12"` przy roli siatki niosącej 11 (powód przy każdej z nich).
        // ⚠ Dwa z pierwotnych 82 nie były wartościami, tylko PROZĄ W KOMENTARZU — strażnik czyta plik
        // regexem i liczy również wzmiankę. Komentarz przeredagowano tak, by nie zapisywał składni
        // atrybutu; to jedyny sposób, żeby licznik mierzył dług, a nie dokumentację.
        ["Views/DataImportTabView.axaml"] = 4,
        // ⭐ 85 → 4 (M2c iteracja 1). Pierwszy widok przepięty na katalog ról w całości: 81 deklaracji czyta
        // dziś rolę z Themes/Typography.axaml, a cztery pozostają lokalne Z POWODEM ZAPISANYM W MIEJSCU —
        // dwa znaki 9 px (katalog nie ma roli o tej wartości) i dwa znaki 12 px dobrane do przycisku 18×18
        // (element układu, nie tekst). To jest kształt, do którego zmierza całe M2c wg reguły R12: nie zero,
        // tylko uzasadniona reszta. ⚠ Koszyk A był tu PUSTY — cały debugger stoi o stopień gęściej (11 px)
        // niż domyślny styl M2b (12), więc żadnej wartości nie dało się po prostu usunąć.
        // ⭐ M4.3: 4 → 3. Znacznik pochodzenia wartości zszedł z 9 na `Text.Caption` (10) — to TEKST
        // („odtworzone" / „założone"). ⛔ Zostają trzy pozycje, wszystkie z tego samego powodu i wszystkie
        // ratyfikowane: marker ▶ przy 9 w kolumnie o stałej szerokości 14 px oraz ★/☆ przy 12 — znaki
        // strojone do KONTENERA, czyli reguła #10, a nie dryf typograficzny.
        ["Views/DebuggerTabView.axaml"] = 3,
        ["Views/GlobalSearchTabView.axaml"] = 1,
        // ⭐⭐ M4.4: `ChoiceDialog`, `ConfirmDialog` i `ForeignKeyDialog` ZDJĘTE (1 + 1 + 1 → 0), czyli
        // grupa „TextBlock 13 px" z §18.0.5/3 przestała istnieć w dialogach. Wszystkie trzy niosły ten sam
        // odziedziczony komentarz („treść komunikatu, a katalog ma przy 13 wyłącznie rolę kodu") i wszystkie
        // trzy odsyłały do bramy §13.3, która ich NIGDY nie podjęła — ten sam sierocy kształt co #340.
        // ⭐ Rozstrzygnięcie jest to samo, które M4.3 podjęło dla pustych stanów Session i Trace: przy 13
        // katalog ma wyłącznie rolę KODU, a to jest proza ⇒ `Text.Application` (13 → 12).
        // ⚠⚠ Trzeci przypadek NIE był tym samym co dwa pierwsze i pomiar odwrócił moją pierwszą diagnozę:
        // `ForeignKeyDialog` pokazuje nazwę tabeli MONOSPACE, więc odziedziczony komentarz był o nim
        // nieprawdziwy, a `Text.Code` (niesie 13, zero zmiany wyglądu) wyglądał na właściwą odpowiedź.
        // Zmierzona rodzina to wykluczyła: wszystkie 25 konsumentów `Text.Code` to pełnowymiarowe EDYTORY,
        // a wśród ~48 elementów monospace spoza edytora ten był JEDYNYM z literałem — reszta czyta role
        // tekstu interfejsu, w tym bliźniaczy `AddFieldDialog` (nazwa typu bazy, monospace, ta sama rola).
        // ⭐ Czyli reguła „`Text.Code` opisuje edytor, monospace poza edytorem bierze rolę tekstu" już
        // w produkcie była; M4.4 ją dokończyło, zamiast wprowadzać nową.
        // 6 → 1 (M2c iteracja 1). Pięć wywołań czyta rolę przez `BindFontSize` (odpowiednik
        // `{DynamicResource}` po stronie C#, bliźniak istniejącego `BindBrush`); zostaje ciało karty Peek —
        // powierzchnia KODU przy 12 px, gdy rola `Text.Code` niesie 13.
        ["Views/DebuggerTabView.axaml.cs"] = 1,
    };

    /// <summary>
    /// Seven divergent monospace strings across the app, six of them in these files.
    /// <para>
    /// ⚠⚠ <b>THIS COMMENT USED TO SAY "M2c should drive this list to empty", AND THE M2c INVENTORY MEASURED
    /// THAT TO BE IMPOSSIBLE</b> (2026-08-02, ratified by the user — <c>product-polish.md</c> §18.0.5/1). The
    /// <c>Font.Code</c> token carries <c>Cascadia <b>Mono</b>, …</c> while 65 of the 81 occurrences are
    /// <c>Cascadia <b>Code</b>, …</c> — <b>not one of the 81 strings is identical to the token</b>, so swapping
    /// any of them changes the typeface in the SQL editor, the debugger, the hover cards and eleven DDL
    /// previews at once. M2c is a de-localization sweep with an unchanged appearance, so <c>FontFamily</c> left
    /// its scope entirely; <c>Cascadia Code</c> (ligatures) vs <c>Cascadia Mono</c> (none) belongs to the
    /// backlogged UX sprint together with collapsing the 7 strings / 95 occurrences / 33 files.
    /// </para>
    /// <para>
    /// ⭐ So this baseline is a <b>ratchet against new drift</b>, not a countdown to zero: nothing may be added,
    /// and the existing entries carry their reason at the token itself (<c>Themes/Typography.axaml</c>).
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, int> FontFamilyBaseline = new(StringComparer.Ordinal)
    {
        ["Views/DebuggerTabView.axaml"] = 17,
        ["Views/AddFieldDialog.axaml"] = 5,
        ["Views/FunctionDetailTabView.axaml"] = 5,
        ["Views/ProcedureDetailTabView.axaml"] = 5,
        ["Views/TraceMonitorTabView.axaml"] = 5,
        ["Views/MainWindow.axaml"] = 4,
        ["Views/PackageDetailTabView.axaml"] = 3,
        ["Views/PerformancePanelView.axaml"] = 3,
        ["Views/ScriptExecutorTabView.axaml"] = 3,
        ["Views/SessionManagerTabView.axaml"] = 3,
        ["Views/TriggerDetailTabView.axaml"] = 3,
        ["Views/ViewDetailTabView.axaml"] = 3,
        ["Views/CheckConstraintDialog.axaml"] = 2,
        ["Views/DataImportTabView.axaml"] = 2,
        ["Views/DebuggerTabView.axaml.cs"] = 2,
        ["Views/DiagnosticsPanelView.axaml"] = 2,
        ["Views/ForeignKeyDialog.axaml"] = 2,
        ["Views/IndexDialog.axaml"] = 2,
        ["Views/BlobEditorWindow.axaml"] = 1,
        ["Views/ConstraintFieldDialog.axaml"] = 1,
        ["Views/DomainDetailTabView.axaml"] = 1,
        ["Views/ExceptionDetailTabView.axaml"] = 1,
        ["Views/GeneratorDetailTabView.axaml"] = 1,
        ["Views/GlobalSearchTabView.axaml"] = 1,
        ["Views/IndexDetailTabView.axaml"] = 1,
        ["Views/NewTableTabView.axaml"] = 1,
        ["Views/TableDetailTabView.axaml"] = 1,
        ["Views/ThirdPartyNoticesWindow.axaml"] = 1,
    };

    /// <summary>
    /// Five values with no rule (audit M‑6). The measurement behind §4.2.2: every 4 / 4.5 / 5 / 6 is a chip, every
    /// 3 is a surface — two roles, <c>Radius.Chip</c> and <c>Radius.Surface</c>, not five numbers to average.
    /// </summary>
    private static readonly Dictionary<string, int> CornerRadiusBaseline = new(StringComparer.Ordinal)
    {
        // M2c iteracja 6: bez zmian — wszystkie dziewięć to GEOMETRIA albo KARTA. Koła (10×10 r=5,
        // 9×9 r=4.5), kapsuła (Height 10, r=5), karty i kontenery przy 4 (`Radius.Surface` niesie 3)
        // oraz jeden setter resetów przy 0. ⛔ Nie tokenizujemy arytmetyki (§18.0.5/2). Powody w miejscu.
        // ⭐⭐ M4.3 / Q1 — Session 9 → 6, Trace 6 → 2, Performance 2 → 1. Siedem promieni `4` zeszło na
        // `Radius.Surface` (3), a chip Session na `Radius.Chip` (4 → 4, wygląd bez zmiany).
        // ⚠ Decyzja ma DWIE POŁOWY i tylko jedna była nowa: trzy KARTY dziedziczą argument B2 z M4.2
        // („rola `Radius.Surface` wymienia «Kartę» we własnym komentarzu"), natomiast cztery RAMKI
        // KONTROLEK — dwa przełączniki segmentowe, jeden w Session, oraz pole filtra Trace — rozstrzygnął
        // argument mocniejszy: `ControlCornerRadius` Fluenta = 3 i jest świadomie NIENADPISANY
        // w `FluentBridge`, więc każda PRAWDZIWA obramowana kontrolka renderuje się przy 3, a te cztery
        // ją tylko udawały. Render postawił obok nich prawdziwy `TextBox` i to on zamknął pytanie.
        // ⛔ Co zostaje i dlaczego: same koła (`10×10` r=5, `9×9` r=4.5), kapsuły (`Height=10` r=5)
        // i dwa resety `Value="0"`. To ARYTMETYKA i RESET, nie role (§18.0.5/2).
        // ⚠ M4.3c: Session 6 → 5, Trace 2 → 1. To NIE jest migracja, tylko PRZENIESIENIE: styl
        // `Button.seg` (z `CornerRadius="0"` — resetem) przeszedł do `Themes/ControlStyles.axaml`,
        // a `Measure` skanuje wyłącznie `Views/` + `Controls/`. ⭐ Wartość istnieje dalej, tyle że poza
        // zasięgiem licznika — i tak trzeba to czytać, żeby spadek nie udawał postępu, którego nie było.
        ["Views/SessionManagerTabView.axaml"] = 5,
        ["Views/TraceMonitorTabView.axaml"] = 1,
        // M2c iteracja 2: 4 → 0. Wszystkie cztery to `CornerRadius="3"` na kontenerach (siatka typów,
        // siatka mapowania, ramka podglądu, ramka podglądu DDL) — czyli dokładnie `Radius.Surface`,
        // jedyna grupa, którą krok 0 dopuścił do migracji (§18.0.5/2). Wpis usunięty.
        // M2c iteracja 3: 4 → 2. Dwa promienie 3 przeszły na `Radius.Surface`; zostają KARTA przy 4
        // (`Radius.Surface` niesie 3 — decyzja produktowa oddana §13.3) i KAPSUŁA przy 6, gdzie promień
        // jest połową wysokości, czyli arytmetyką, a nie rolą (§18.0.5/2).
        ["Views/PerformancePanelView.axaml"] = 1,
        // ⭐ M4.2 / B2: bliźniaki ZDJĘTE (1 + 1 → 0). Karta aktywności wzięła `Radius.Surface` (4 → 3) po
        // decyzji użytkownika podjętej NA RENDERZE (`VisualCandidateProbe -- radius`, oba motywy, 1:1 i ×4).
        // ⚠ Wpis stał tu od M2c iteracji 4 z adnotacją „decyzja należy do przeglądu §13.3" — a przegląd
        // §13.3a NIGDY jej nie podjął i pozycja nie dostała numeru K, więc wypadła między etapami i przeżyła
        // zamknięcie rejestru kolizji „w całości". ⭐ Wariant `Radius.Card` = 4 rozważony i ODRZUCONY:
        // rola `Radius.Surface` wymienia „Kartę" jako pierwszego konsumenta we własnym komentarzu, więc
        // nowa rola legalizowałaby wartość zamiast opisywać element (R12 czytane w drugą stronę).
    };

    /// <summary>
    /// ⭐⭐ <b>ODSTĘPY — STRAŻNIK BEZ MIGRACJI (decyzja użytkownika, 2026-08-08).</b> Te trzy własności nie były
    /// liczone przez NIKOGO do M4.1: liczniki M2c mierzyły <c>FontSize</c>, <c>FontFamily</c> i <c>CornerRadius</c>,
    /// a rozmiar ikony doczekał się licznika dopiero w M4. Zmierzone przy pierwszym spojrzeniu: <b>985 wartości
    /// lokalnych</b>, przy czym <c>Padding</c> i <c>Margin</c> czytają rolę z katalogu <b>dokładnie zero razy</b> —
    /// mimo że katalog ma dla nich siedem stopni skali odstępów i dwanaście ról złożonych od M2a.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⛔ <b>To jest WYŁĄCZNIE zapadka przeciw przyrostowi — nie zobowiązanie do migracji i nie wymuszenie ról.</b>
    /// Odstępy dostaną własny etap, z własnym pomiarem, decyzją projektową i QA; ratyfikowane 2026-08-08 wraz
    /// z jawnym zastrzeżeniem, żeby <b>nie zmieniać żadnej wartości tylko po to, by zadowolić tego strażnika</b>.
    /// Migracja M4.1–M4.4 zostaje przy swoim zakresie (ikony + <c>FontSize</c>).
    /// </para>
    /// <para>
    /// ⭐ Baseline jest PER PLIK, a nie sumą per własność, i to jest różnica merytoryczna: suma przepuściłaby
    /// dodanie pięciu marginesów w jednym widoku, gdyby w innym pięć zniknęło. Zapadka ma pilnować kierunku
    /// w każdym pliku z osobna.
    /// </para>
    /// <para>
    /// ⚠ Liczby pochodzą z tego samego <c>Measure</c>, który egzekwuje regułę — czyli skanują <c>.axaml</c>
    /// <b>i</b> <c>.cs</c> w <c>Views/</c> + <c>Controls/</c> i <b>nie</b> pomijają komentarzy. Wzmianka
    /// o atrybucie w prozie komentarza się liczy; ten sam kwirk jest udokumentowany przy <c>FontSize</c>.
    /// Odczyt roli (<c>Margin="{DynamicResource Margin.FieldGap}"</c>) NIE jest liczony — mierzymy dług,
    /// nie użycie własności.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<string, int> SpacingBaseline = new(StringComparer.Ordinal)
    {
        // Spacing — odstęp między dziećmi `StackPanel`. Zmierzone 2026-08-08: 309 deklaracji lokalnych w 46 plikach.
        ["Views/DebuggerTabView.axaml"] = 35,
        ["Views/DataImportTabView.axaml"] = 29,
        ["Views/SecurityManagerTabView.axaml"] = 27,
        ["Views/SessionManagerTabView.axaml"] = 25,
        ["Views/AddFieldDialog.axaml"] = 18,
        ["Views/MainWindow.axaml"] = 16,
        ["Views/PerformancePanelView.axaml"] = 14,
        ["Views/TraceMonitorTabView.axaml"] = 10,
        ["Views/UserEditDialog.axaml"] = 10,
        ["Views/FunctionDetailTabView.axaml"] = 9,
        ["Views/ExportDialog.axaml"] = 8,
        ["Views/ProcedureDetailTabView.axaml"] = 8,
        ["Views/ForeignKeyDialog.axaml"] = 7,
        ["Views/SettingsWindow.axaml"] = 7,
        ["Views/TriggerDetailTabView.axaml"] = 6,
        ["Views/ConstraintFieldDialog.axaml"] = 5,
        ["Views/IndexDialog.axaml"] = 5,
        ["Views/NewConnectionDialog.axaml"] = 5,
        ["Views/SettingsExportDialog.axaml"] = 5,
        ["Views/SettingsImportDialog.axaml"] = 5,
        ["Views/CheckConstraintDialog.axaml"] = 4,
        ["Views/GlobalSearchDialog.axaml"] = 4,
        ["Views/RecompileDependentsDialog.axaml"] = 4,
        ["Views/ScriptExecutorTabView.axaml"] = 4,
        ["Views/AggregationBarView.axaml"] = 3,
        ["Views/ExecuteProcedureDialog.axaml"] = 3,
        ["Views/FilterPanelView.axaml"] = 3,
        ["Controls/MessageBanner.axaml"] = 2,
        ["Views/AboutWindow.axaml"] = 2,
        ["Views/BatchResultsDialog.axaml"] = 2,
        ["Views/DataImportTabView.axaml.cs"] = 2,
        ["Views/DebuggerTabView.axaml.cs"] = 2,
        ["Views/NewFolderDialog.axaml"] = 2,
        ["Views/NewRoleDialog.axaml"] = 2,
        ["Views/NewTableTabView.axaml"] = 2,
        ["Views/TableDetailTabView.axaml"] = 2,
        ["Views/TextPromptDialog.axaml"] = 2,
        ["Views/ViewDetailTabView.axaml"] = 2,
        ["Views/BlobEditorWindow.axaml"] = 1,
        ["Views/ConfirmDialog.axaml"] = 1,
        ["Views/DomainDetailTabView.axaml"] = 1,
        ["Views/GeneratorDetailTabView.axaml"] = 1,
        ["Views/GlobalSearchTabView.axaml"] = 1,
        ["Views/IndexDetailTabView.axaml"] = 1,
        ["Views/PackageDetailTabView.axaml"] = 1,
        ["Views/SubprogramKindDialog.axaml"] = 1,
    };

    private static readonly Dictionary<string, int> PaddingBaseline = new(StringComparer.Ordinal)
    {
        // Padding — wnętrze kontrolki lub panelu. Zmierzone 2026-08-08: 185 deklaracji lokalnych w 49 plikach.
        //
        // ⭐ M4.2b: `Controls/DependencyTreeView.axaml` = 2 — wpis NOWY i podniesiony ŚWIADOMIE, drogą, którą
        // ten strażnik sam wskazuje („zmiana jest decyzją projektową ⇒ podnieś sufit I zapisz powód").
        // Obie wartości ODWZOROWUJĄ pasek boczny, bo drzewo zależności stoi teraz na tym samym mechanizmie:
        //   • `Padding="2,0"` na wierszu — wcięcie ZAZNACZENIA od krawędzi panelu, nie odstęp treści;
        //     katalog nie ma dla niego roli i ten sam zapis stoi przy pasku bocznym w `MainWindow`.
        //   • `Padding="0"` na przycisku chevronu — RESET, a nie odstęp; rola dałaby tu wartość dodatnią.
        // ⛔ Nie „naprawiać" ich rolą: obie są celowe i obie mają powód zapisany w miejscu.
        ["Controls/DependencyTreeView.axaml"] = 2,
        ["Views/MainWindow.axaml"] = 21,
        ["Views/DebuggerTabView.axaml"] = 14,
        ["Views/DataImportTabView.axaml"] = 10,
        ["Views/FunctionDetailTabView.axaml"] = 9,
        ["Views/ProcedureDetailTabView.axaml"] = 9,
        // ⚠ M4.3c: 8 → 7 (Session) i 7 → 6 (Trace niżej) — PRZENIESIENIE, nie migracja. `Padding` stylu
        // `Button.seg` wyszedł z widoku do `Themes/ControlStyles.axaml`, którego ten licznik nie skanuje.
        ["Views/SessionManagerTabView.axaml"] = 7,
        ["Views/ForeignKeyDialog.axaml"] = 7,
        ["Views/TraceMonitorTabView.axaml"] = 6,
        ["Views/TableDetailTabView.axaml"] = 6,
        ["Views/AddFieldDialog.axaml"] = 5,
        ["Views/ConstraintFieldDialog.axaml"] = 5,
        ["Views/IndexDialog.axaml"] = 5,
        ["Views/NewTableTabView.axaml"] = 5,
        ["Views/PerformancePanelView.axaml"] = 5,
        ["Controls/MessageBanner.axaml"] = 4,
        ["Views/BatchResultsDialog.axaml"] = 4,
        ["Views/CheckConstraintDialog.axaml"] = 4,
        ["Views/AggregationBarView.axaml"] = 3,
        ["Views/NewConnectionDialog.axaml"] = 3,
        ["Views/SecurityManagerTabView.axaml"] = 3,
        ["Views/TriggerDetailTabView.axaml"] = 3,
        ["Views/ViewDetailTabView.axaml"] = 3,
        ["Views/BlobEditorWindow.axaml"] = 2,
        ["Views/ChoiceDialog.axaml"] = 2,
        ["Views/ConfirmDialog.axaml"] = 2,
        ["Views/ExecuteProcedureDialog.axaml"] = 2,
        ["Views/ExportDialog.axaml"] = 2,
        ["Views/GlobalSearchDialog.axaml"] = 2,
        ["Views/NewFolderDialog.axaml"] = 2,
        ["Views/NewRoleDialog.axaml"] = 2,
        ["Views/PackageDetailTabView.axaml"] = 2,
        ["Views/RecompileDependentsDialog.axaml"] = 2,
        ["Views/ScriptExecutorTabView.axaml"] = 2,
        ["Views/SubprogramKindDialog.axaml"] = 2,
        ["Views/TextPromptDialog.axaml"] = 2,
        ["Views/ThirdPartyNoticesWindow.axaml"] = 2,
        ["Views/UserEditDialog.axaml"] = 2,
        ["Controls/BreadcrumbBar.axaml"] = 1,
        ["Controls/SearchableComboBox.cs"] = 1,
        ["Views/DebuggerTabView.axaml.cs"] = 1,
        ["Views/DomainDetailTabView.axaml"] = 1,
        ["Views/ExceptionDetailTabView.axaml"] = 1,
        ["Views/FilterPanelView.axaml"] = 1,
        ["Views/GeneratorDetailTabView.axaml"] = 1,
        ["Views/GlobalSearchTabView.axaml"] = 1,
        ["Views/IndexDetailTabView.axaml"] = 1,
        ["Views/SettingsExportDialog.axaml"] = 1,
        ["Views/SettingsImportDialog.axaml"] = 1,
        ["Views/TableDetailTabView.axaml.cs"] = 1,
    };

    private static readonly Dictionary<string, int> MarginBaseline = new(StringComparer.Ordinal)
    {
        // Margin — odstęp wokół elementu. Zmierzone 2026-08-08: 491 deklaracji lokalnych w 55 plikach.
        ["Views/DebuggerTabView.axaml"] = 49,
        ["Views/DataImportTabView.axaml"] = 40,
        ["Views/MainWindow.axaml"] = 29,
        ["Views/DomainDetailTabView.axaml"] = 21,
        ["Views/FunctionDetailTabView.axaml"] = 19,
        ["Views/IndexDetailTabView.axaml"] = 20,
        ["Views/ProcedureDetailTabView.axaml"] = 18,
        ["Views/SessionManagerTabView.axaml"] = 19,
        ["Views/TraceMonitorTabView.axaml"] = 19,
        ["Views/AddFieldDialog.axaml"] = 16,
        ["Views/PerformancePanelView.axaml"] = 16,
        ["Views/SecurityManagerTabView.axaml"] = 16,
        ["Views/TableDetailTabView.axaml"] = 14,
        ["Views/ViewDetailTabView.axaml"] = 12,
        ["Views/GeneratorDetailTabView.axaml"] = 11,
        ["Views/SettingsWindow.axaml"] = 11,
        ["Views/ScriptExecutorTabView.axaml"] = 10,
        ["Views/ExceptionDetailTabView.axaml"] = 7,
        ["Views/SettingsImportDialog.axaml"] = 9,
        ["Views/TriggerDetailTabView.axaml"] = 6,
        ["Views/AboutWindow.axaml"] = 7,
        ["Views/ExecuteProcedureDialog.axaml"] = 7,
        ["Views/PackageDetailTabView.axaml"] = 4,
        ["Views/TableDetailTabView.axaml.cs"] = 6,
        ["Views/UserEditDialog.axaml"] = 6,
        ["Controls/TableColumnPicker.cs"] = 5,
        ["Views/BatchResultsDialog.axaml"] = 5,
        ["Views/DiagnosticsPanelView.axaml"] = 5,
        ["Views/FilterPanelView.axaml"] = 5,
        ["Views/ForeignKeyDialog.axaml"] = 5,
        ["Views/IndexDialog.axaml"] = 5,
        ["Views/SettingsExportDialog.axaml"] = 5,
        ["Controls/MessageBanner.axaml"] = 4,
        ["Views/DebuggerTabView.axaml.cs"] = 4,
        ["Views/KeyboardShortcutsWindow.axaml"] = 4,
        ["Views/RecompileDependentsDialog.axaml"] = 4,
        ["Views/CheckConstraintDialog.axaml"] = 3,
        ["Views/ConstraintFieldDialog.axaml"] = 3,
        ["Views/ExportDialog.axaml"] = 3,
        ["Views/NewTableTabView.axaml"] = 3,
        ["Views/AggregationBarView.axaml"] = 2,
        ["Views/BlobEditorWindow.axaml"] = 2,
        ["Views/ChoiceDialog.axaml"] = 2,
        ["Views/NewConnectionDialog.axaml"] = 2,
        ["Views/TextPromptDialog.axaml"] = 2,
        ["Controls/BreadcrumbBar.axaml"] = 1,
        ["Controls/SearchableComboBox.cs"] = 1,
        ["Views/ConfirmDialog.axaml"] = 1,
        ["Views/FunctionDetailTabView.axaml.cs"] = 1,
        ["Views/GlobalSearchDialog.axaml"] = 1,
        ["Views/NewFolderDialog.axaml"] = 1,
        ["Views/NewRoleDialog.axaml"] = 1,
        ["Views/ProcedureDetailTabView.axaml.cs"] = 1,
        ["Views/SubprogramKindDialog.axaml"] = 1,
        ["Views/ViewDetailTabView.axaml.cs"] = 1,
    };

    /// <summary>
    /// ⭐⭐ <b>Każda geometria ikony jest WYŚRODKOWANA W PIONIE w swojej siatce 24×24.</b> Powstało ze zgłoszenia
    /// QA (M4.1): strzałki Undo/Redo w pasku narzędzi „wyglądają na nierówno ustawione względem pozostałych
    /// ikon". Zgłoszenie było trafne, a przyczyną okazała się <b>geometria, nie pozycjonowanie</b> — przycisk,
    /// kontener, padding i rozmiar były identyczne jak u sąsiadów, a ikona i tak siedziała 1,83 px niżej.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⭐ <b>Dlaczego to musi być strażnik, a nie jednorazowa poprawka.</b> Szablon <c>SvgIcon</c> to
    /// <c>Viewbox Uniform</c> nad <b>stałym</b> <c>Canvas 24×24</c>, więc położenie ścieżki w tej siatce
    /// przenosi się 1:1 na render i <b>nic go nie normalizuje</b>. Ikona narysowana nisko renderuje się nisko
    /// obok sąsiadów — przy zielonym buildzie i bez żadnego licznika, który by to widział.
    /// </para>
    /// <para>
    /// ⚠ <b>Próg 1,0 jednostki jest ZMIERZONY, nie dobrany.</b> Rozkład w chwili napisania: <b>73 z 86 geometrii
    /// mieszczą się w 0,25</b> jednostki od środka, 82 w 0,5, 83 w 1,0. Próg leży więc powyżej naturalnego
    /// rozrzutu rodziny i poniżej odstępstwa — czyli tam, gdzie jedno odróżnia od drugiego.
    /// </para>
    /// <para>
    /// ⛔⛔ <b>DLACZEGO SKIA, A NIE Avalonia.Media.Geometry — i to jest najważniejsza rzecz w tym teście.</b>
    /// Pierwsza wersja liczyła <c>StreamGeometry.GetRenderBounds</c> w sesji headless testów i zgłosiła
    /// <b>sześć fałszywych znalezisk</b> (<c>Icon.Search</c>, <c>Moon</c>, <c>RotateCw</c>, <c>User</c>,
    /// <c>Index</c>, <c>Cut</c>) — wszystkie zawierające ŁUK. Zmierzone porównawczo: platforma headless
    /// (<c>UseHeadlessDrawing</c>, czyli ta, w której biegną testy) <b>ignoruje wybrzuszenie łuku</b> i liczy
    /// pudełko z punktów końcowych. Dla <c>Icon.Search</c> daje Y 10..22 (środek 16), a Skia — czyli to, co
    /// NAPRAWDĘ się rysuje — Y 2..22 (środek 12, ikona idealnie wyśrodkowana).
    /// ⭐ Strażnik byłby więc czerwony z powodu, którego jego własna nazwa nie opisuje (#315), i kazałby
    /// „poprawić" sześć poprawnych ikon. <b>Narzędzie liczące geometrię musi używać silnika, którym produkt
    /// rysuje</b>; platforma headless nim nie jest.
    /// ⭐ Pomiar Skią wprost ma jeszcze jedną zaletę: nie potrzebuje platformy Avalonii, więc ten test zostaje
    /// w partycji GŁÓWNEJ i nie powiększa kruchej listy klas headless (#94/#226/#286).
    /// </para>
    /// <para>
    /// ⛔ Oś POZIOMA celowo NIE jest pilnowana: <c>Icon.Play</c> jest przesunięty w prawo o 1,5 jednostki i to
    /// jest <b>poprawna korekta optyczna</b> trójkąta, a nie defekt (#288 — pudełko tuszu DIAGNOZUJE wielkość
    /// optyczną, nigdy jej nie DYKTUJE).
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryIconGeometry_IsVerticallyCentredInIts24Grid()
    {
        // Wyjątek z zapisanym POWODEM — reguła R12: celem jest usunięcie odstępstw NIEUZASADNIONYCH, nie
        // wyzerowanie licznika. Wpis tutaj zmusza autora do zadeklarowania, po której stronie stoi.
        var exempt = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Zmierzone +1,62 (1,08 px @16). „Przeskok" nie ma ani podłogi, ani sufitu, w odróżnieniu od
            // rodzeństwa (`Icon.StepInto` ma kreskę podłogi na y=19, `Icon.StepOut` sufit na y=5), więc jego
            // tusz z natury leży niżej. ⛔ Świadomie NIE ruszone w M4.1: to pasek debuggera (M4.3), odchylenie
            // jest o połowę mniejsze od zgłoszonego, a wyrównanie tej TRÓJKI trzeba oceniać razem i na
            // renderze, nie po jednej liczbie.
            ["Icon.StepOver"] = "przeskok bez podlogi/sufitu - do oceny razem z rodzenstwem Step* w M4.3",
        };

        var source = File.ReadAllText(Path.Combine(AppRoot(), "Themes", "IconGeometries.axaml"));
        var matches = Regex.Matches(
            Regex.Replace(source, @"<!--[\s\S]*?-->", string.Empty),
            @"<StreamGeometry x:Key=""(?<key>[^""]+)"">(?<data>[^<]+)</StreamGeometry>");

        // Test, który przechodzi, bo NICZEGO nie dopasował, jest gorszy niż brak testu (R16).
        Assert.True(matches.Count > 50,
            $"W IconGeometries.axaml znaleziono tylko {matches.Count} geometrii — jeżeli zmienił się ich zapis, "
            + "ten strażnik musi pójść za nim, a nie zniknąć.");

        var offenders = new List<string>();
        var stale = new List<string>();

        foreach (Match m in matches)
        {
            var key = m.Groups["key"].Value;
            var deviation = InkCentreY(m.Groups["data"].Value, key) - 12d;

            if (exempt.ContainsKey(key))
            {
                // Wyjątek, który przestał być potrzebny, wprowadza w błąd tak samo jak brakujący.
                if (Math.Abs(deviation) <= 1d) stale.Add($"{key} (odchylenie {deviation:+0.00;-0.00})");
                continue;
            }

            if (Math.Abs(deviation) > 1d)
            {
                offenders.Add($"{key}: środek tuszu {deviation + 12:F2} zamiast 12,00 — odchylenie "
                    + $"{deviation:+0.00;-0.00} jednostki, czyli {deviation * 16 / 24:+0.00;-0.00} px przy renderze 16 px");
            }
        }

        Assert.True(offenders.Count == 0,
            "Geometria ikony nie jest wyśrodkowana w pionie w siatce 24×24:\n  "
            + string.Join("\n  ", offenders)
            + "\n\n`SvgIcon` to Viewbox nad STAŁYM Canvas 24×24, więc ikona narysowana nisko renderuje się\n"
            + "nisko obok sąsiadów i nic tego nie normalizuje. Dwa wyjścia, i sens tego testu polega na tym,\n"
            + "że musisz powiedzieć, które:\n"
            + "  • przesuń geometrię tak, żeby tusz stanął w pionie na środku — CZYSTE przesunięcie, kształt,\n"
            + "    rozmiar i grubość kreski bez zmian — ORAZ zaktualizuj kanoniczny plik w Assets/Icons/,\n"
            + "    żeby źródło i runtime się nie rozjechały;\n"
            + "  • albo dopisz wyjątek WRAZ Z POWODEM do listy `exempt` powyżej.");

        Assert.True(stale.Count == 0,
            "Wyjątek od wyśrodkowania przestał być potrzebny — usuń wpis, bo opisuje stan, którego już nie ma:\n  "
            + string.Join("\n  ", stale));
    }

    /// <summary>
    /// Pionowy środek POKRYTEGO TUSZEM pudełka — ścieżka obrysowana piórem 2 px z zaokrąglonymi końcami, czyli
    /// dokładnie tak, jak rysuje ją <c>SvgIcon</c>. ⚠ <c>TightBounds</c>, nie <c>Bounds</c>: to drugie zwraca
    /// pudełko punktów kontrolnych krzywej, więc dla łuku odpowiadałoby na inne pytanie niż zadane.
    /// </summary>
    private static double InkCentreY(string pathData, string key)
    {
        var path = SKPath.ParseSvgPathData(pathData);
        Assert.True(path is not null, $"Skia nie potrafi sparsować danych ścieżki `{key}`.");

        using (path!)
        using (var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        })
        using (var stroked = new SKPath())
        {
            stroke.GetFillPath(path, stroked);
            return stroked.TightBounds.MidY;
        }
    }

    private static Dictionary<string, int> BaselineFor(string property) => property switch
    {
        "FontSize" => FontSizeBaseline,
        "FontFamily" => FontFamilyBaseline,
        "CornerRadius" => CornerRadiusBaseline,
        "Spacing" => SpacingBaseline,
        "Padding" => PaddingBaseline,
        "Margin" => MarginBaseline,
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, "No baseline is declared for this property."),
    };

    public static TheoryData<string> GuardedProperties => new() { "FontSize", "FontFamily", "CornerRadius", "Spacing", "Padding", "Margin" };

    [Theory]
    [MemberData(nameof(GuardedProperties))]
    public void NoFileDeclaresMoreThanItsBaseline(string property)
    {
        var actual = Measure(property);
        var baseline = BaselineFor(property);

        var over = actual
            .Where(kv => kv.Value > baseline.GetValueOrDefault(kv.Key))
            .OrderByDescending(kv => kv.Value - baseline.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key}: {baseline.GetValueOrDefault(kv.Key)} → {kv.Value}")
            .ToList();

        Assert.True(over.Count == 0,
            $"New local `{property}` declarations appeared in a view:\n  " + string.Join("\n  ", over) +
            $"\n\nThe catalog is in Themes/Tokens.axaml and Themes/Typography.axaml. Two ways out, and the\n" +
            "point of this test is that you say which one it is:\n" +
            $"  • The value belongs to a role ⇒ read it from the token instead of writing the number here.\n" +
            $"  • The change is a deliberate design decision ⇒ raise the baseline above AND record the reason\n" +
            "    in docs/design/product-polish.md. That is a correct part of the process (§11.1), not a\n" +
            "    workaround — what this guard exists to catch is the number that moved unnoticed.");
    }

    [Theory]
    [MemberData(nameof(GuardedProperties))]
    public void TheBaselineHasNoStaleEntries(string property)
    {
        // A ceiling nobody lowers stops being a ceiling: a file migrated down from 26 to 4 would silently keep
        // permission for 22 more. Lowering the number as the work lands is what makes M2c's exit condition a
        // number rather than an opinion.
        var actual = Measure(property);
        var baseline = BaselineFor(property);

        var stale = baseline
            .Where(kv => actual.GetValueOrDefault(kv.Key) < kv.Value)
            .OrderBy(kv => kv.Key)
            .Select(kv => actual.ContainsKey(kv.Key)
                ? $"{kv.Key}: baseline {kv.Value}, actually {actual[kv.Key]} — lower it"
                : $"{kv.Key}: baseline {kv.Value}, now clean or gone — remove the entry")
            .ToList();

        Assert.True(stale.Count == 0,
            $"The `{property}` baseline is higher than reality — this is progress that was not written down:\n  " +
            string.Join("\n  ", stale) +
            $"\n\nCurrent total: {actual.Values.Sum()} across {actual.Count} file(s); baseline says " +
            $"{baseline.Values.Sum()} across {baseline.Count}. Update the numbers above so the next reader sees\n" +
            "how much is genuinely left.");
    }

    [Fact]
    public void TheTokenDictionaries_AreRegisteredInTheApplication()
    {
        // The catalog is only a catalog if the application actually merges it. A dictionary that exists in the
        // repository but is not registered resolves no key at runtime, and the failure surfaces as "the token
        // does not work" somewhere in M2b — far from its cause.
        var app = File.ReadAllText(Path.Combine(AppRoot(), "App.axaml"));

        foreach (var dictionary in new[] { "Themes/Tokens.axaml", "Themes/Typography.axaml" })
        {
            Assert.True(app.Contains($"avares://EmberTern/{dictionary}", StringComparison.Ordinal),
                $"{dictionary} is not merged in App.axaml — every token it declares is unreachable at runtime.");
            Assert.True(File.Exists(Path.Combine(AppRoot(), dictionary.Replace('/', Path.DirectorySeparatorChar))),
                $"App.axaml merges {dictionary}, but the file does not exist.");
        }
    }

    [Fact]
    public void NoResourceKey_IsDeclaredInMoreThanOneThemeFile()
    {
        // Every Themes/*.axaml dictionary is merged into ONE resource scope, so a key declared in two of them
        // resolves to whichever loaded last — silently, with no warning and no failing build. A spacing token
        // shadowed by something in Colors.axaml is the kind of defect that surfaces months later as "this one
        // screen is 3 px off", far from its cause.
        //
        // ⚠ Scope is the whole Themes/ folder, not just the two token files. The catalog is the newcomer here:
        // it added 76 keys to a folder that already had 247, and M2b will add more. Checking only the new files
        // against each other verifies the half that is least likely to be wrong.
        var duplicates = KeysByFile()
            .SelectMany(entry => entry.Value.Select(key => (entry.Key, Key: key)))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} — declared in {string.Join(", ", g.Select(x => x.Item1).OrderBy(f => f, StringComparer.Ordinal))}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "A resource key is declared in more than one theme dictionary; the later declaration silently wins:\n  " +
            string.Join("\n  ", duplicates) +
            "\n\nRename one of them. A key is a name in a single global namespace — two owners means the winner " +
            "depends on merge order in App.axaml, which nobody reads as an ownership decision.");
    }

    [Fact]
    public void NoThemeFile_DeclaresTheSameKeyTwiceInOneScope()
    {
        // ⚠⚠ THE SUBTLETY THAT MAKES THIS TEST CORRECT: a file with <ThemeDictionaries> declares each key TWICE
        // ON PURPOSE — once for Dark and once for Light. That is UI rule #3 ("every colour comes from both
        // dictionaries"); a key present in only one variant is the actual defect there. Colors.axaml is 283
        // declarations over 146 distinct keys for exactly that reason.
        //
        // So the duplicate rule only applies to VARIANT-FREE dictionaries, where all keys share one scope and a
        // repeat is unambiguously a mistake. Writing this test without the distinction would have reported 137
        // "collisions" in a file that is correct, and the natural next move — relaxing it until it went green —
        // would have removed the check that matters.
        var offenders = new List<string>();

        foreach (var file in ThemeFiles())
        {
            var text = File.ReadAllText(file);
            if (text.Contains("ThemeDictionaries", StringComparison.Ordinal)) continue;

            var repeated = Regex.Matches(text, @"x:Key=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .GroupBy(k => k, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => $"{Path.GetFileName(file)}: {g.Key} ×{g.Count()}");

            offenders.AddRange(repeated);
        }

        Assert.True(offenders.Count == 0,
            "A theme dictionary declares the same key twice in one scope — the second declaration wins and the " +
            "first is dead:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Token names that have been retired by a rename, with the name that replaced them. A straggler is not a
    /// compile error in either direction — XAML resolves a missing <c>{DynamicResource}</c> to nothing, and the
    /// C# call sites look it up <b>by string</b> (<c>Brush("…")</c>) with a <c>?? fallback</c>, so a missed
    /// rename silently paints the fallback colour instead.
    /// </summary>
    private static readonly Dictionary<string, string> RetiredTokens = new(StringComparer.Ordinal)
    {
        // RB‑4 (M2b): one token was doing two opposite jobs — "chrome a step further from the document" and
        // "this element floats above its container". They coincide in Dark and contradict each other in Light.
        ["ElevatedPanelBrush"] = "ChromeStrongBrush (chrome) or SurfaceRaisedBrush (raised) — see §7.1",
        ["ElevatedPanelColor"] = "ChromeStrongColor or SurfaceRaisedColor",

        // M2b step 11: the user ratified that COLOUR may express an action's priority and SIZE may not.
        // The role lost its only consumer (Button.primary's MinHeight), and a token with no consumer is
        // indistinguishable from a regression (#233) — so it leaves the catalog with it.
        ["Size.ControlPrimary"] = "nothing — a primary action is marked by the accent, not by height",

        // M4 / A-3: the density block gave the toolbar icon its own role, and that role took the name and the
        // value (16) of `Size.Icon.Lg`, which had ZERO consumers and a description ("header, empty state,
        // primary action") that described nothing in the product.
        ["Size.Icon.Lg"] = "Size.Icon.Toolbar — the icon as a standalone ACTION (toolbar, window button)",

        // ⭐ M4 / B-1: retired because it DUPLICATED `Text.Compact` by ROLE, not merely by value — that role
        // already says "chrome: panels, tabs, BARS". It never had a consumer, while three of the four toolbars
        // sat on `Text.Compact` (11) and the fourth on `Text.Application` (12).
        ["Text.Toolbar"] = "Text.Compact — toolbar text is chrome, and that is what Text.Compact names",
    };

    [Fact]
    public void NoRetiredTokenName_SurvivesAnywhereInTheApplication()
    {
        var appRoot = AppRoot();
        var stragglers = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appRoot, "*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) &&
                !file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = WithoutComments(File.ReadAllText(file), file);
            foreach (var (retired, replacement) in RetiredTokens)
            {
                if (text.Contains(retired, StringComparison.Ordinal))
                {
                    stragglers.Add($"{Path.GetRelativePath(appRoot, file).Replace('\\', '/')} still uses " +
                                   $"`{retired}` — use {replacement}");
                }
            }
        }

        Assert.True(stragglers.Count == 0,
            "A retired token name survived a rename:\n  " + string.Join("\n  ", stragglers) +
            "\n\nNeither XAML nor C# fails on this: a missing DynamicResource resolves to nothing and the " +
            "string-keyed lookups fall back to another brush, so the only symptom is a surface painted the " +
            "wrong colour on one screen.");
    }

    /// <summary>
    /// Strips comments before the retired-name scan. ⭐ The guard is about USAGE, not about mentioning history:
    /// the comment in <c>Colors.axaml</c> that explains <i>why</i> a token was split has to be able to name the
    /// token it replaced, and a guard that forbids documenting itself trains people to delete the explanation
    /// instead of the code.
    /// <para>⚠ Deliberately conservative on C#: only whole-line <c>//</c> comments and <c>/* … */</c> blocks. A
    /// naive "strip from the first //" would also eat the tail of any line containing an <c>avares://</c> URI,
    /// which is how a real straggler would go unnoticed — a guard is allowed to over-report, never to
    /// under-report.</para>
    /// </summary>
    private static string WithoutComments(string text, string path) =>
        path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase)
            ? Regex.Replace(text, "<!--.*?-->", " ", RegexOptions.Singleline)
            : Regex.Replace(
                string.Join('\n', text.Split('\n').Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))),
                @"/\*.*?\*/", " ", RegexOptions.Singleline);

    /// <summary>
    /// ⛔ <c>FluentBridge.axaml</c> is a MAPPING layer, never a second token catalog — the user's binding rule
    /// on ratifying it (2026-08-02): *"no local values and no new design decisions there; every number and role
    /// keeps its owner in the catalogs, and the Bridge only translates."*
    /// <para>Every value in that file must therefore be a <i>reference</i> (<c>{StaticResource …}</c> or
    /// <c>{DynamicResource …}</c>). A literal colour or number is what turns a translation layer into a second
    /// source of truth — and it would do so quietly, because a hard-coded brush works perfectly until the day
    /// someone changes the catalog and one control does not follow.</para>
    /// <para>⚠ This is also what keeps the rule structural rather than remembered: the file physically cannot
    /// accumulate design decisions if every entry has to point at one.</para>
    /// </summary>
    [Fact]
    public void FluentBridge_ContainsNoLocalValues()
    {
        var bridge = Path.Combine(AppRoot(), "Themes", "FluentBridge.axaml");
        Assert.True(File.Exists(bridge), $"FluentBridge.axaml is missing at {bridge}");

        var text = Regex.Replace(File.ReadAllText(bridge), "<!--.*?-->", " ", RegexOptions.Singleline);

        // Every element carrying an x:Key is a mapping entry; each must resolve its value from a resource.
        var offenders = KeyedElements(text)
            .Where(m => !m.Value.Contains("StaticResource", StringComparison.Ordinal) &&
                        !m.Value.Contains("DynamicResource", StringComparison.Ordinal))
            .Select(m => $"{m.Groups["key"].Value} ({m.Groups["tag"].Value}) — value written in place")
            .ToList();

        Assert.True(offenders.Count == 0,
            "FluentBridge declared a value of its own instead of translating one:\n  " +
            string.Join("\n  ", offenders) +
            "\n\nThe Bridge maps Fluent's resource keys onto Tokens/Typography/Colors and owns nothing. If the " +
            "value you need does not exist in a catalog, it belongs in the catalog — with a role and a reason " +
            "(§4.2.4) — not here.");
    }

    /// <summary>
    /// ⭐ Wskaźnik aktywnej zakładki nie może nieść LOKALNEGO <c>Background</c>.
    ///
    /// <para>⚠⚠ Ten strażnik pilnuje dokładnie tego, co się zepsuło w M3.1a (naprawione w §19.2), i jest
    /// napisany przeciw PRZYCZYNIE, a nie przeciw objawowi. Styl nadający akcent był przez cały czas
    /// poprawny; defektem było <c>Background="Transparent"</c> postawione lokalnie na tym samym elemencie —
    /// a <b>wartość lokalna bije setter stylu</b>, więc akcent nie malował się nigdy.</para>
    ///
    /// <para>⭐ Dlatego bliźniaczy test <c>TabStripPresentationTests</c> NIE wystarcza: sprawdza on, że styl
    /// się rozwiązuje, a styl był w porządku. Dwie połówki jednej gwarancji — styl musi istnieć (tam)
    /// i widok nie może go przykryć (tutaj). Żadna z nich osobno nie złapałaby tej regresji.</para>
    ///
    /// <para>⚠ Objaw był wyłącznie wizualny: zielony build, 7088 zielonych testów i czysty smoke. Zgłosił
    /// go użytkownik, patrząc na aplikację.</para>
    /// </summary>
    [Fact]
    public void TabIndicator_CarriesNoLocalBackground()
    {
        var view = Path.Combine(AppRoot(), "Views", "MainWindow.axaml");
        Assert.True(File.Exists(view), $"MainWindow.axaml is missing at {view}");

        var text = Regex.Replace(File.ReadAllText(view), "<!--.*?-->", " ", RegexOptions.Singleline);

        // Element otwierający z klasą `tab-indicator` — od `<` do najbliższego `>`.
        var offenders = Regex.Matches(text, @"<[A-Za-z:]+[^>]*Classes=""tab-indicator""[^>]*>")
            .Select(m => m.Value)
            .Where(el => Regex.IsMatch(el, @"\bBackground\s*="))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Wskaźnik aktywnej zakładki niesie lokalne `Background`:\n  " +
            string.Join("\n  ", offenders) +
            "\n\nOba jego stany — spoczynkowy i akcent — mieszkają w ControlStyles.axaml (`Border.tab-indicator` " +
            "oraz `Border.active-tab Border.tab-indicator`). Wartość lokalna bije setter stylu, więc atrybut " +
            "postawiony tutaj sprawia, że akcent nie maluje się NIGDY — bezgłośnie, przy zielonym buildzie. " +
            "Dokładnie ta regresja zdarzyła się w M3.1a (product-polish.md §19.2).");
    }

    /// <summary>
    /// ⭐ Język kolorów, rola <b>R‑4 „Destrukcja"</b> — znak kosza w widoku ZAWSZE niesie
    /// <c>DangerIconBrush</c> (<c>color-language.md</c> §3; krok K2, <c>product-polish.md</c> §19.16).
    ///
    /// <para>⚠⚠ To był najostrzejszy pojedynczy przypadek z pomiaru §20: ta sama operacja miała
    /// <b>trzy</b> kolory, a w drzewie połączeń i w panelu zapytań <b>przycisk nie zgadzał się z własnym
    /// menu kontekstowym</b> — dwie drogi do jednej operacji, dwa kolory obok siebie. Powodem nie był
    /// świadomy wyjątek, tylko porzucona legenda w <c>Colors.axaml</c> („Warning=delete"), która
    /// przeżyła zmianę, jaką opisywała.</para>
    ///
    /// <para>⭐ Dlatego dowodem nie jest liczba, tylko <b>warunek</b>: nowy kosz pomalowany „jakoś"
    /// przechodzi build, przechodzi każdy inny test i wygląda źle dopiero na cudzym ekranie. Skan
    /// czyta ŹRÓDŁO widoków, bo to tam rodzi się dryf.</para>
    ///
    /// <para>⚠ Gdy kiedyś pojawi się kosz, który celowo ma inny kolor — ten test ma <b>upaść</b>, a
    /// odpowiedzią jest wpis w §5 języka („wyjątek nazwany") wraz z wyjątkiem tutaj. ⛔ Nie wyciszać go
    /// przez rozluźnienie warunku: wyjątek bez zapisanego powodu jest dokładnie tym, co ten test łapie.</para>
    /// </summary>
    [Fact]
    public void DestructiveIcon_AlwaysCarriesTheDangerToken()
    {
        var appRoot = AppRoot();
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var folder in new[] { "Views", "Controls" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(appRoot, folder), "*.axaml", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);

                // Cały element SvgIcon (może być wielolinijkowy), niezależnie od prefiksu przestrzeni nazw.
                foreach (Match icon in Regex.Matches(text, @"<[\w]*:?SvgIcon\b[^>]*?/>", RegexOptions.Singleline))
                {
                    var markup = icon.Value;
                    if (!markup.Contains("Icon.Trash", StringComparison.Ordinal) &&
                        !markup.Contains("Icon.ListX", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    scanned++;
                    if (!markup.Contains("DangerIconBrush", StringComparison.Ordinal))
                    {
                        var brush = Regex.Match(markup, @"Foreground=""\{DynamicResource (?<key>[^}]+)\}""");
                        offenders.Add(
                            $"{Path.GetRelativePath(appRoot, file).Replace('\\', '/')} → "
                            + (brush.Success ? brush.Groups["key"].Value : "brak Foreground (neutralny)"));
                    }
                }
            }
        }

        // Sam skan nie może po cichu nic nie znaleźć — regex, który przestał pasować, „przechodzi" zawsze.
        Assert.True(scanned >= 8, $"Skan znalazł tylko {scanned} znaków destrukcji — wzorzec przestał pasować do widoków.");

        Assert.True(offenders.Count == 0,
            "Znak destrukcji niesie token inny niż `DangerIconBrush`:\n  " +
            string.Join("\n  ", offenders) +
            "\n\nRola R‑4 języka kolorów mówi, że operacja nieodwracalna ma JEDEN kolor w całym produkcie "
            + "(color-language.md §3). Jeśli ten przypadek ma być świadomym wyjątkiem — dopisz go najpierw "
            + "do §5 z powodem, a dopiero potem tutaj. Wyjątek bez zapisanego powodu jest defektem (§5).");
    }

    /// <summary>
    /// ⭐ Wysokość wiersza Metadata Explorera pochodzi z roli <c>Size.Row.Tree</c>, nie z literału
    /// (M3.4a, <c>product-polish.md</c> §19.26).
    ///
    /// <para>⚠⚠ Dlaczego to jest test CZYTAJĄCY ŹRÓDŁO, a nie headless na kontrolce. Styl
    /// <c>ListBox.sidebar-list ListBoxItem</c> mieszka w lokalnym bloku <c>&lt;ListBox.Styles&gt;</c>
    /// w <c>MainWindow.axaml</c>, więc gołe <c>new ListBox { Classes = { "sidebar-list" } }</c> go NIE
    /// zobaczy, a jedyną kontrolką, która go widzi, jest <c>MainWindow</c> — a headless test konstruujący
    /// <c>MainWindow</c> zawiesza suite (#94/#226/#286, pułapka 4 handovera M3).</para>
    ///
    /// <para>⛔ Przeniesienie tego stylu do <c>ControlStyles.axaml</c> „żeby dało się go przetestować" jest
    /// dokładnie tym ruchem, który w M3.3a odtworzył regresję §19.2: <b>zmiana MIEJSCA reguły jest zmianą
    /// jej PRIORYTETU</b>. Ten styl jest celowo zawężony do jednej listy, żeby nie dotknąć listy zapytań
    /// zapisanych.</para>
    ///
    /// <para>⭐ Co ten test realnie chroni — dwie rzeczy, obie ciche przy zielonym buildzie: powrót
    /// literału (#284) oraz literówkę w kluczu, bo <c>{DynamicResource}</c> na brakującym kluczu NIE rzuca
    /// wyjątku, tylko zostawia właściwość przy wartości odziedziczonej (pułapka 1) — a tutaj oznaczałoby
    /// to wiersz zapadnięty do wysokości treści, czyli 15 px zamiast 24.</para>
    ///
    /// <para>⚠ Czego ten test NIE mówi: jak drzewo WYGLĄDA. Kryterium odbioru jest ekran (R16) — to jest
    /// wyłącznie strażnik dryfu.</para>
    /// </summary>
    [Fact]
    public void SidebarRowHeight_ComesFromTheTreeRowRole()
    {
        var view = Path.Combine(AppRoot(), "Views", "MainWindow.axaml");
        Assert.True(File.Exists(view), $"MainWindow.axaml is missing at {view}");

        var text = Regex.Replace(File.ReadAllText(view), "<!--.*?-->", " ", RegexOptions.Singleline);

        // Blok stylu wiersza paska bocznego — od selektora do jego zamknięcia.
        var style = Regex.Match(
            text,
            @"<Style\s+Selector=""ListBox\.sidebar-list\s+ListBoxItem"">(?<body>.*?)</Style>",
            RegexOptions.Singleline);

        Assert.True(style.Success,
            "Nie znaleziono stylu `ListBox.sidebar-list ListBoxItem` w MainWindow.axaml. Jeśli wiersz paska "
            + "bocznego został przeniesiony gdzie indziej, ten strażnik musi pójść za nim — a nie zniknąć.");

        var minHeight = Regex.Match(
            style.Groups["body"].Value,
            @"<Setter\s+Property=""MinHeight""\s+Value=""(?<value>[^""]*)""");

        Assert.True(minHeight.Success,
            "Wiersz paska bocznego nie deklaruje już `MinHeight`. Wysokość wiersza ma dokładnie jednego "
            + "właściciela (padding pionowy jest zerowy właśnie po to) — jeśli właściciel się zmienił, "
            + "zmień ten test razem z nim i zapisz powód.");

        Assert.Equal("{DynamicResource Size.Row.Tree}", minHeight.Groups["value"].Value);
    }

    /// <summary>
    /// ⭐ Rola <c>Size.Row.Tree</c> ma wartość, którą PRODUKT faktycznie pokazuje (24), a nie tę, którą
    /// katalog kiedyś zadeklarował (20). Decyzja użytkownika <b>DB</b>, M3.4a.
    ///
    /// <para>⚠⚠ Ten test nie jest powtórzeniem poprzedniego. Tamten pilnuje, że widok CZYTA rolę; ten —
    /// że rola niesie liczbę, która przeszła przez oko użytkownika. Bez niego „migracja na rolę" byłaby
    /// zmianą wysokości wiersza z 24 na 20 przebraną za porządkowanie: zielony build, zielone testy,
    /// gęstość najgęstszego widoku zmieniona bez decyzji.</para>
    ///
    /// <para>⛔ Jeśli ta liczba ma się zmienić — to jest decyzja produktowa użytkownika (gęstość drzewa,
    /// pytanie K15 na przeglądzie §13.3), a nie skutek uboczny innej pracy.</para>
    /// </summary>
    /// <summary>
    /// ⭐ M4 / A‑2: rola nagłówka sekcji niesie **12**, czyli liczbę, którą produkt pokazywał w 17 z 36
    /// przypadków, zanim katalog ją przyjął.
    ///
    /// <para>⚠⚠ Ten test nie pilnuje „ładnej liczby", tylko RELACJI, która była złamana: nagłówek sekcji
    /// stoi nad `TextBlock.field-label` (rola <c>Text.Application</c>) i musi być co najmniej tak duży.
    /// Przy 11 nad 12 był MNIEJSZY od tekstu, który nazywa, a jego własny komentarz twierdził, że jest
    /// mocniejszy — i właśnie dlatego pięć widoków niezależnie odmówiło tej roli.</para>
    ///
    /// <para>⛔ Gdyby nagłówek miał kiedyś zejść poniżej treści, to jest decyzja produktowa użytkownika
    /// (jak A‑3, wariant „obniż `Text.Application` do 11"), a nie skutek uboczny innej pracy.</para>
    /// </summary>
    [Fact]
    public void SectionHeaderRole_IsNotSmallerThanTheLabelItHeads()
    {
        var text = File.ReadAllText(Path.Combine(AppRoot(), "Themes", "Typography.axaml"));

        var header = Regex.Match(text, @"x:Key=""Text\.SectionHeader\.Size""\s*>\s*(?<v>[\d.]+)\s*<");
        var label = Regex.Match(text, @"x:Key=""Text\.Application\.Size""\s*>\s*(?<v>[\d.]+)\s*<");
        Assert.True(header.Success && label.Success, "Brak roli `Text.SectionHeader` albo `Text.Application`.");

        var headerSize = double.Parse(header.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var labelSize = double.Parse(label.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(headerSize >= labelSize,
            $"Nagłówek sekcji mierzy {headerSize}, a podpis pola, nad którym stoi — {labelSize}. Nagłówek "
            + "mniejszy od treści, którą nazywa, jest dokładnie stanem, który M4 naprawiło (A‑2): pięć widoków "
            + "odmówiło wtedy tej roli i trzymało własne 12 SemiBold. Jeżeli ta relacja ma się odwrócić, to "
            + "jest decyzja użytkownika, a nie skutek uboczny.");

        // Waga jest drugą połową rozróżnienia „nazywa temat" vs „nazywa wartość" i bez niej sam rozmiar
        // nie wystarcza — przy równych rozmiarach zostaje ona jedyną różnicą.
        Assert.Contains("x:Key=\"Text.SectionHeader.Weight\">SemiBold", text);
    }

    /// <summary>
    /// ⭐ M4 / D (K10): zakładka ma WŁASNĄ rolę promienia, choć niesie tę samą liczbę co
    /// <c>Radius.Chip</c>. §3.3 katalogu pozwala na to wprost, a R12 zabrania podpięcia zakładki pod chip
    /// tylko po to, żeby zniknęła wartość lokalna.
    /// <para>⚠ Test pilnuje KONSUMENTÓW, nie wartości: rola z zerem konsumentów jest nieodróżnialna od
    /// regresji (#233), a to właśnie zabiło `Text.Toolbar` i `Size.Icon.Lg`.</para>
    /// </summary>
    [Fact]
    public void TabRadiusRole_ExistsAndIsReadByBothTabVariants()
    {
        var tokens = File.ReadAllText(Path.Combine(AppRoot(), "Themes", "Tokens.axaml"));
        Assert.Matches(@"x:Key=""Radius\.Tab""\s*>\s*[\d.]+\s*<", tokens);

        var styles = File.ReadAllText(Path.Combine(AppRoot(), "Themes", "ControlStyles.axaml"));
        foreach (var variant in new[] { "bottom-tab", "sub-tab" })
        {
            var block = Regex.Match(
                styles,
                @"Selector=""TabItem\." + variant + @" /template/ Border#PART_LayoutRoot""\s*>(?<body>[\s\S]*?)</Style>");
            Assert.True(block.Success, $"Nie znaleziono stylu kształtu zakładki `{variant}`.");
            Assert.Contains("{DynamicResource Radius.Tab}", block.Groups["body"].Value);
        }
    }

    /// <summary>
    /// ⭐ M4 / C (K9): bazowy styl <c>TabItem</c> czyta rolę, a nie literał 13. ⚠⚠ Wpis rejestru wskazywał
    /// tu ZŁY ELEMENT — mówił o dolnym panelu i pod‑zakładkach, a te były na roli już od M2c/M3; trzynastka
    /// siedziała na stylu bazowym, obsługującym zakładki dialogów.
    /// </summary>
    [Fact]
    public void BaseTabItem_ReadsItsFontSizeFromARole()
    {
        var styles = File.ReadAllText(Path.Combine(AppRoot(), "Themes", "ControlStyles.axaml"));
        var block = Regex.Match(styles, @"<Style Selector=""TabItem"">(?<body>[\s\S]*?)</Style>");
        Assert.True(block.Success, "Nie znaleziono bazowego stylu `TabItem`.");

        var setter = Regex.Match(block.Groups["body"].Value, @"<Setter Property=""FontSize"" Value=""(?<v>[^""]+)""");
        Assert.True(setter.Success, "Bazowy `TabItem` nie deklaruje już `FontSize`.");
        Assert.StartsWith("{DynamicResource", setter.Groups["v"].Value);
    }

    [Fact]
    public void TreeRowRole_CarriesTheHeightTheProductActuallyShows()
    {
        var tokens = Path.Combine(AppRoot(), "Themes", "Tokens.axaml");
        var text = File.ReadAllText(tokens);

        var declared = Regex.Match(text, @"x:Key=""Size\.Row\.Tree""\s*>(?<value>[^<]+)<");
        Assert.True(declared.Success, "Brak roli `Size.Row.Tree` w Tokens.axaml.");
        Assert.Equal("24", declared.Groups["value"].Value.Trim());
    }

    /// <summary>
    /// ⭐ M4 / B‑1: wiersz drzewa czyta ikonę i odstęp Z RÓL, nie z literałów. Pilnowane na trzech szablonach
    /// paska bocznego, bo to one niosą KAŻDY wiersz najgęstszego widoku aplikacji.
    ///
    /// <para>⚠ Czego ten test broni: powrotu literału (#284) — a to jest tu bardziej prawdopodobne niż zwykle,
    /// bo poprzednie wartości (15 i 5) stały w tych szablonach latami i mają w historii projektu obszerne
    /// uzasadnienie odroczenia. ⛔ Uzasadnienie wygasło razem z decyzją użytkownika; jeżeli waga optyczna ikony
    /// w wierszu ma się zmienić, zmienia się ROLA, i wtedy razem z zakładką i menu kontekstowym.</para>
    ///
    /// <para>⚠ Czego NIE mówi: jak drzewo wygląda. Kryterium odbioru jest ekran (R16).</para>
    /// </summary>
    [Theory]
    [InlineData("FolderNodeViewModel")]
    [InlineData("ConnectionNodeViewModel")]
    [InlineData("MetadataNodeViewModel")]
    public void SidebarRowIcon_AndItsGap_ComeFromTheirRoles(string template)
    {
        var text = File.ReadAllText(Path.Combine(AppRoot(), "Views", "MainWindow.axaml"));

        // ⚠ ZAKRES JEST CZĘŚCIĄ TEGO TESTU, nie jego szczegółem. Pierwsza wersja skanowała CAŁY plik
        // i zgłosiła cztery wiersze na roli `Size.Icon.Sm` (chip, ikona inline przy tekście 11 px) — czyli
        // element o INNEJ roli, całkowicie poprawny. Strażnik pytał wtedy „czy każdy `StackPanel` z ikoną
        // wygląda jak wiersz drzewa", a miał pytać o trzy konkretne szablony (#315: strażnik bywa zielony
        // albo czerwony z powodu, którego jego nazwa nie opisuje).
        var block = Regex.Match(
            text,
            @"<DataTemplate DataType=""vm:" + template + @"""\s*>(?<body>[\s\S]*?)</DataTemplate>");
        Assert.True(block.Success,
            $"Nie znaleziono szablonu wiersza `vm:{template}` w MainWindow.axaml. Jeżeli wiersz paska "
            + "bocznego przeniósł się gdzie indziej, ten strażnik musi pójść za nim — a nie zniknąć.");

        var row = Regex.Match(
            block.Groups["body"].Value,
            @"<StackPanel[^>]*?Spacing=""(?<gap>[^""]+)""[^>]*>\s*(?:<!--[\s\S]*?-->\s*)?"
            + @"<controls:SvgIcon\b(?:(?!/?>)[\s\S])*?Width=""(?<size>[^""]+)""");

        // Test, który przechodzi, bo NICZEGO nie dopasował, jest gorszy niż brak testu (R16).
        Assert.True(row.Success,
            $"W szablonie `vm:{template}` nie ma już pary odstęp + ikona, o którą pytała kolizja K15.");

        Assert.Equal("{DynamicResource Size.Icon}", row.Groups["size"].Value);
        Assert.Equal("{DynamicResource Space.Xs}", row.Groups["gap"].Value);
    }

    /// <summary>
    /// Ikony, które nadal deklarują rozmiar LICZBĄ zamiast roli — plik → liczba. Ta sama mechanika, co przy
    /// `FontSize`: sufit, który schodzi w dół razem z pracą, więc „ile jeszcze zostało" jest liczbą, a nie
    /// opinią.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ POWÓD, DLA KTÓREGO TEN STRAŻNIK POWSTAŁ DOPIERO W M4, jest sam w sobie znaleziskiem: liczniki M2c
    /// mierzyły `FontSize`, `FontFamily` i `CornerRadius` — rozmiaru ikony nie mierzył NIKT. Dlatego aplikacja
    /// doszła do <b>siedmiu</b> renderowanych rozmiarów ikon (10, 11, 12, 13, 14, 15, 16) przy zielonym
    /// buildzie, a rola `Size.Icon` miała DWÓCH konsumentów na 355 deklaracji.
    /// <para>⚠ Sweep pozostałych literałów należy do M4.3 — ten sufit istnieje po to, żeby ich nie przybyło,
    /// a nie żeby udawać, że ich nie ma.</para>
    /// </remarks>
    private static readonly Dictionary<string, int> IconSizeLiteralBaseline = new(StringComparer.Ordinal)
    {
        // M4 / A‑3 + B‑1: 152 → 95. Zeszło 16 literałów `16` (pasek narzędzi i okno → `Size.Icon.Toolbar`)
        // oraz 41 par 15+5 wiersza drzewa (→ `Size.Icon` + `Space.Xs`).
        //
        // ⭐⭐ M4.1: 95 → 20, i po tej iteracji NIE MA JUŻ ANI JEDNEGO LITERAŁU `14` ANI `16` — zostaje
        // wyłącznie ogon 10/11/12/13/15, czyli pytanie o ROLE, sparkowane osobno w §19.37.7.
        // Zeszło dwiema drogami, z których TYLKO DRUGA zmienia wygląd:
        //   • 75 ikon niosących 14 → `Size.Icon`. Wartość bez zmiany. Obie populacje trafiają w tę samą
        //     rolę: ikona PRZY ETYKIECIE (`Button.primary`/`.flat` z tekstem, chevron ujawnienia przy
        //     tytule, wiersz menu) oraz samotna ikona w PASKU SIATKI.
        //   • 18 ikon pasków siatki, które nie deklarowały nic i brały 16 z `ControlTheme`, dostało jawne
        //     `Size.Icon` (16 → 14): paski paginacji w edytorach funkcji / procedury / widoku, chevrony
        //     zwijania panelu w Session Managerze i Trace Monitorze, filtr + eksport w Trace Monitorze.
        //
        // ⭐ Decyzja użytkownika (2026‑08‑08) brzmiała „dokończyć regułę, którą produkt już ma", a nie
        // „ujednolicić liczby": kryterium A‑3 to drabina *stoi w SERII vs stoi SAMOTNIE*, a przycisk
        // paginacji stoi w serii czterech, trio filtr/agregacja/eksport w serii trzech. Regułę widać było
        // w produkcie — `Icon.RefreshCw` niesie 16 jako przycisk paska narzędzi i 14, gdy odświeża SIATKĘ.
        // ⚠ Wariant „wszędzie 16" odrzucony: rósłby wygląd powierzchni już odebranych. Ten wybrany
        // wyłącznie ZMNIEJSZA, więc nie rusza ani jednego piksela tam, gdzie M4 już przeszło QA.
        // ⭐⭐ M4.2: bliźniaki ZDJĘTE (3 + 3 → 0). Trzy ikony karty aktywności w każdym z nich niosły literał
        // 12 i przeszły na `Size.Icon.Sm`, którego własny opis brzmi „ikona inline w tekście 11 px" — a stoją
        // dokładnie obok tekstu `Text.Compact.Size` (11). Wartość 12 → 12, wygląd bez zmiany.
        //
        // ⛔⛔ M4.2 · B1 — `TableDetailTabView` = 5 to WPIS NOWY, ale NIE NOWY DŁUG: te pięć literałów istniało
        // od zawsze i było NIEWIDOCZNE dla licznika, bo plik rysuje PK / FK / Unique surowym `<Path>` po trzech
        // lokalnych `StreamGeometry`, zamiast przez `controls:SvgIcon`. Licznik pytał o nazwę kontrolki, więc
        // odpowiadał „0". ⚠ Wzrost sufitu 20 → 25 jest więc KOREKTĄ POMIARU, nie regresją — i nie wolno go
        // „naprawić" obniżeniem: te trzy glify leżą na siatce 14 jednostek, a nie na kanonicznych 24, więc
        // przeniesienie ich do `IconGeometries.axaml` ZMIENIA WYGLĄD i wymaga QA wizualnego użytkownika.
        // ⛔ Decyzja użytkownika (2026-08-08): B1 zostaje przygotowane jako osobny przypadek, wygląd NIE jest
        // rozstrzygany w M4.2. Do tego czasu wpis stoi tu po to, żeby dług był widoczny (R12), a nie zerowany.
        //
        // ⭐⭐ M4.3 / Q2: 19 → 14. Debugger ZDJĘTY W CAŁOŚCI (4 → 0), Session 2 → 1. Pięć ikon `Icon.X`
        // przeszło na `Size.Icon.Sm`, bo dla użytkownika są JEDNYM elementem — inline ✕, które czyści albo
        // usuwa to, w czym stoi — a renderowały się przy 12 (czyszczenie pola Immediate) i 11 (trzy razy
        // usuwanie wiersza w debuggerze, raz czyszczenie chipa w Session). ⚠ Rozjazd siedział W JEDNYM
        // PLIKU, więc nie opisywała go żadna reguła per ekran (#335).
        // ⭐ Rola trafia własnym opisem — „ikona inline w tekście 11 px (chip, wiersz siatki)" — i tym samym
        // argumentem M4.2 zmigrowało sześć ikon karty aktywności.
        // ⚠ Koszt zaakceptowany świadomie: cztery ikony ROSNĄ 11 → 12, co idzie pod prąd R18. Wariant
        // odwrotny („rola na 11") nie był wariantem, tylko cofnięciem odebranej już decyzji M4.2.
        //
        // ⚠⚠ SPROSTOWANIE POMIARU (M4.3): as-built M4.2 (§19.40.4) i „Current state" w CLAUDE.md mówiły
        // „sufit rośnie 20 → 25". Ta liczba NIGDY nie zgadzała się z kodem: do dwudziestki z M4.1 dodano
        // +5 za `TableDetailTabView`, ale nie odjęto −6 zdjętych w tej samej iteracji z bliźniaków.
        // Rzeczywisty sufit po M4.2 wynosił 19 (i tyle sumowała ta tablica, więc strażnik był zielony —
        // rozjeżdżała się wyłącznie PROZA). Po M4.3 jest 14.
        ["Views/TableDetailTabView.axaml"] = 5,
        ["Views/TraceMonitorTabView.axaml"] = 3,
        ["Views/DataImportTabView.axaml"] = 2,
        ["Views/MainWindow.axaml"] = 2,
        // ⛔ Zostaje 1: `Icon.Check` przy 15 px w komórce siatki — jedna z trzech ikon 15 px świadomie
        // wyłączonych z B‑1 w bloku gęstości (§19.37.3: „żadna nie jest wierszem drzewa").
        ["Views/SessionManagerTabView.axaml"] = 1,
        ["Views/AggregationBarView.axaml"] = 1,
    };

    [Fact]
    public void NoFileDeclaresMoreIconSizeLiteralsThanItsBaseline()
    {
        var actual = MeasureIconSizeLiterals();

        var over = actual
            .Where(kv => kv.Value > IconSizeLiteralBaseline.GetValueOrDefault(kv.Key))
            .OrderByDescending(kv => kv.Value - IconSizeLiteralBaseline.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key}: {IconSizeLiteralBaseline.GetValueOrDefault(kv.Key)} → {kv.Value}")
            .ToList();

        Assert.True(over.Count == 0,
            "Nowa ikona deklaruje rozmiar liczbą zamiast roli:\n  " + string.Join("\n  ", over) +
            "\n\nRole są w Themes/Tokens.axaml i po M4 są trzy, każda o innym zadaniu:\n" +
            "  • `Size.Icon.Toolbar` (16) — ikona jako samodzielna AKCJA: pasek narzędzi, przycisk okna.\n" +
            "  • `Size.Icon` (14) — ikona w WIERSZU: zakładka, drzewo, menu, etykieta na powierzchni roboczej.\n" +
            "  • `Size.Icon.Sm` (12) — ikona inline przy tekście 11 px.\n" +
            "⭐ Jeżeli ikona nie podaje rozmiaru w ogóle, bierze `Size.Icon.Toolbar` z `ControlTheme` — i to " +
            "jest poprawna droga dla ikony paska narzędzi, nie brak decyzji.");
    }

    [Fact]
    public void TheIconSizeLiteralBaselineHasNoStaleEntries()
    {
        var actual = MeasureIconSizeLiterals();

        var stale = IconSizeLiteralBaseline
            .Where(kv => actual.GetValueOrDefault(kv.Key) < kv.Value)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => actual.ContainsKey(kv.Key)
                ? $"{kv.Key}: sufit {kv.Value}, faktycznie {actual[kv.Key]} — obniż go"
                : $"{kv.Key}: sufit {kv.Value}, plik czysty albo zniknął — usuń wpis")
            .ToList();

        Assert.True(stale.Count == 0,
            "Sufit literałów rozmiaru ikony jest wyższy od rzeczywistości — to postęp, którego nikt nie " +
            "zapisał:\n  " + string.Join("\n  ", stale) +
            $"\n\nRazem: {actual.Values.Sum()} w {actual.Count} plikach; sufit mówi " +
            $"{IconSizeLiteralBaseline.Values.Sum()} w {IconSizeLiteralBaseline.Count}. Zaktualizuj liczby, " +
            "żeby następny czytelnik widział, ile naprawdę zostało do M4.3.");
    }

    /// <summary>
    /// Pliki deklarujące własną geometrię ikony poza <c>Themes/IconGeometries.axaml</c> — plik → powód.
    /// <para>
    /// ⭐⭐ <b>Wartością tej listy nie są nazwy, tylko to, że dopisanie się do niej zmusza autora do
    /// zadeklarowania, po której stronie granicy stoi</b> (wzorzec <c>DatePresentationTests</c>). Geometria
    /// poza systemem ikon nie jest sama w sobie błędem — jest decyzją, która musi mieć powód, bo omija
    /// <c>ControlTheme</c> (czyli A‑3 i domyślny rozmiar), audyt wyśrodkowania z rundy QA M4.1 oraz licznik
    /// literałów rozmiaru.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> IconGeometryOutsideTheSystem = new(StringComparer.Ordinal)
    {
        // ⛔ M4.2 · B1 — ZAREJESTROWANE, NIE ZAAKCEPTOWANE. Trzy glify (PK / FK / Unique) rysowane surowym
        // `<Path Fill=…>`, na siatce 14 jednostek zamiast kanonicznych 24. ⚠ To NIE jest duplikat: zmierzone
        // — `IconGeometries.axaml` nie ma ikony klucza głównego, obcego ani unikalności, więc te trzy glify są
        // realnie potrzebną treścią, a nie kopią. Przeniesienie ich do systemu = przerysowanie na siatkę 24
        // = ZMIANA WYGLĄDU pięciu ikon w siatkach pól i indeksów, czyli decyzja produktowa z własnym QA.
        // Decyzja użytkownika (2026-08-08): przygotować jako osobny przypadek, wyglądu nie rozstrzygać w M4.2.
        ["Views/TableDetailTabView.axaml"] =
            "M4.2 · B1 — PK/FK/Unique na siatce 14, czekają na decyzję wizualną użytkownika",
    };

    /// <summary>
    /// ⭐⭐ Ikona rysowana poza systemem ikon jest niewidoczna dla trzech mechanizmów naraz: domyślnego rozmiaru
    /// z <c>ControlTheme</c> (A‑3), audytu wyśrodkowania w siatce 24 i licznika literałów rozmiaru. Ten strażnik
    /// nie zabrania takiej geometrii — wymaga, żeby była ZADEKLAROWANA z powodem, zamiast istnieć w ciszy.
    /// </summary>
    [Fact]
    public void EveryIconGeometry_LivesInTheIconSystem_OrCarriesAReason()
    {
        var appRoot = AppRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(appRoot, file).Replace('\\', '/');
            if (relative == "Themes/IconGeometries.axaml") continue;

            var text = WithoutComments(File.ReadAllText(file), file);
            var count = Regex.Matches(text, @"<(?:StreamGeometry|PathGeometry)\b[^>]*x:Key=").Count;
            if (count == 0) continue;

            if (!IconGeometryOutsideTheSystem.ContainsKey(relative))
                offenders.Add($"{relative}: {count} geometrii bez zapisanego powodu");
        }

        Assert.True(offenders.Count == 0,
            "Geometria ikony zadeklarowana poza `Themes/IconGeometries.axaml`:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nTaka ikona omija TRZY mechanizmy naraz: domyślny rozmiar z `ControlTheme` (czyli A‑3), "
            + "audyt wyśrodkowania w siatce 24 jednostek i licznik literałów rozmiaru — czyli wygląda na "
            + "czystą dlatego, że nikt jej nie mierzy (#332/#285).\n"
            + "⭐ Właściwa droga to `Themes/IconGeometries.axaml` + `controls:SvgIcon`. Jeżeli geometria musi "
            + "zostać na miejscu, dopisz plik do `IconGeometryOutsideTheSystem` RAZEM Z POWODEM — wartością tej "
            + "listy nie są nazwy, tylko wymuszenie świadomej decyzji.");
    }

    /// <summary>
    /// Strażnik powyżej pilnuje też SIEBIE: wpis, którego przedmiot zniknął, przestaje być zapisem decyzji
    /// i staje się nieaktualnym wyjątkiem, który następny czytelnik weźmie za obowiązującą regułę (#333).
    /// </summary>
    [Fact]
    public void TheIconGeometryExemptions_HaveNoStaleEntries()
    {
        var appRoot = AppRoot();

        var stale = IconGeometryOutsideTheSystem.Keys
            .Where(relative =>
            {
                var full = Path.Combine(appRoot, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) return true;
                var text = WithoutComments(File.ReadAllText(full), full);
                return !Regex.IsMatch(text, @"<(?:StreamGeometry|PathGeometry)\b[^>]*x:Key=");
            })
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            "Wyjątek na geometrię poza systemem ikon nie ma już przedmiotu — usuń wpis:\n  "
            + string.Join("\n  ", stale)
            + "\n\nTo jest postęp, którego nikt nie zapisał: geometria wróciła do systemu ikon, "
            + "a lista nadal twierdzi, że stoi poza nim.");
    }

    /// <summary>
    /// Liczy deklaracje ikon z rozmiarem podanym LICZBĄ. ⚠ Liczy element ikony, nie atrybut <c>Width</c>:
    /// <c>Width</c> nosi w tych plikach także szerokości kolumn, ramek i separatorów, więc pomiar po samym
    /// atrybucie odpowiadałby na inne pytanie (#285 — pomiar po nośniku nie rozstrzyga o roli).
    /// </summary>
    /// <remarks>
    /// ⭐⭐ <b>M4.2: dopisany surowy <c>&lt;Path&gt;</c>, i to jest ZNALEZISKO, nie kosmetyka pomiaru.</b>
    /// Pierwsza wersja pytała o element o NAZWIE <c>SvgIcon</c>/<c>DebuggerIcon</c>/<c>CreateIcon</c> — czyli
    /// o wejście do systemu ikon, a nie o ikonę. <c>TableDetailTabView</c> rysuje PK / FK / Unique surowym
    /// <c>&lt;Path Fill=…&gt;</c> po lokalnej <c>StreamGeometry</c>, więc <b>plik raportował 0 przy pięciu
    /// literałach</b>, a strażnik wyśrodkowania z rundy QA M4.1 nigdy tych trzech geometrii nie widział.
    /// <para>
    /// ⭐ To gotcha #332 o jedną własność dalej: <b>wartość ustawiona tam, gdzie licznik nie zagląda, nie jest
    /// „czysta" — jest niezmierzona</b>; a licznik nie zaglądał, bo pytał o nazwę kontrolki zamiast o rolę
    /// (#285). ⚠ Zmierzone przy dopisywaniu: w całym <c>src/</c> jest <b>9</b> elementów <c>&lt;Path&gt;</c> —
    /// 5 to te ikony, 4 to wnętrza <c>ControlTemplate</c> w <c>IconGeometries.axaml</c> i <b>żaden z tych
    /// czterech nie deklaruje literału</b>, więc rozszerzenie nie potrzebuje ani jednego wyjątku. ⛔ Świadomie
    /// NIE wyłączam <c>IconGeometries.axaml</c> z reguły: wyjątek nieosiągalny czyta się jak realna siatka
    /// bezpieczeństwa (§15.7), a gdyby szablon systemu ikon kiedyś dostał literał, to też jest warte spojrzenia.
    /// </para>
    /// </remarks>
    /// <summary>
    /// ⭐⭐ M4.3 / Q3‑A — komunikat pustego stanu w Session Managerze i w Trace Monitorze musi czytać
    /// JEDNĄ rolę. To jest strażnik <b>relacyjny</b>, a nie przepisujący liczbę (wzorzec §19.41.4): nie
    /// interesuje go, ILE wynosi rola, tylko czy oba ekrany mówią to samo.
    /// <para>
    /// ⚠ Powód, dla którego w ogóle powstał, jest zmierzony, nie wyobrażony: te dwa elementy są
    /// konstrukcyjnie identyczne (ten sam pędzel, ten sam <c>Margin="0,40,0,0"</c>, to samo centrowanie)
    /// i przez cały czas niosły tę samą wartość 13 — czyli były JEDNYM elementem na dwóch ekranach.
    /// Dokładnie taka para rozjechała się w M4.1 (pasek paginacji, #335), i rozjechała się dlatego,
    /// że nikt nie pilnował ich RAZEM.
    /// </para>
    /// </summary>
    [Fact]
    public void BothEmptyStates_ShareOneTextRole()
    {
        var session = EmptyStateFontSize("Views/SessionManagerTabView.axaml", "ShowSessionsEmpty");
        var trace = EmptyStateFontSize("Views/TraceMonitorTabView.axaml", "ShowEmptyState");

        Assert.StartsWith("{DynamicResource ", session, StringComparison.Ordinal);
        Assert.Equal(session, trace);
    }

    /// <summary>
    /// ⭐⭐ M4.4 — treść komunikatu w <c>ChoiceDialog</c> i <c>ConfirmDialog</c> musi czytać JEDNĄ rolę.
    /// Ten sam kształt co <see cref="BothEmptyStates_ShareOneTextRole"/> i z tego samego powodu: oba
    /// elementy są konstrukcyjnie identyczne — ten sam wiersz, to samo wiązanie treści, to samo zawijanie,
    /// ten sam margines — czyli są JEDNYM elementem w dwóch oknach.
    /// <para>
    /// ⚠ <b>Czego NIE pilnuje istniejący licznik, a pilnuje ten test.</b> <c>FontSizeBaseline</c> zatrzymuje
    /// powrót LITERAŁU (oba pliki mają tam dziś zero), ale jest ślepy na rozjazd RÓL: gdyby jeden dialog
    /// zszedł na <c>Text.Compact</c>, a drugi został przy <c>Text.Application</c>, oba liczniki nadal
    /// pokazywałyby zero i rozjazd byłby niewidoczny. Dokładnie tak rozjechał się pasek paginacji (#335) —
    /// obie strony były „poprawne", nikt nie pilnował ich RAZEM.
    /// </para>
    /// <para>
    /// ⛔ <c>ForeignKeyDialog</c> celowo NIE należy do tej pary, mimo że migrował w tej samej iteracji na tę
    /// samą rolę: tam elementem jest nazwa obiektu bazy pisana monospace, a nie proza komunikatu. Wciągnięcie
    /// go tutaj grupowałoby po WYKONANEJ MIGRACJI zamiast po tym, czym element jest — czyli popełniałoby
    /// wewnątrz strażnika błąd, który #341 opisuje.
    /// </para>
    /// </summary>
    [Fact]
    public void BothMessageDialogs_ShareOneTextRole()
    {
        var choice = DialogMessageFontSize("Views/ChoiceDialog.axaml");
        var confirm = DialogMessageFontSize("Views/ConfirmDialog.axaml");

        Assert.StartsWith("{DynamicResource ", choice, StringComparison.Ordinal);
        Assert.Equal(choice, confirm);
    }

    private static string DialogMessageFontSize(string relative)
    {
        var text = WithoutComments(File.ReadAllText(Path.Combine(AppRoot(), relative.Replace('/', Path.DirectorySeparatorChar))), relative);

        var block = Regex.Match(
            text,
            @"<TextBlock(?<body>(?:(?!/>)[\s\S])*?)Text=""\{Binding Message\}""(?:(?!/>)[\s\S])*?FontSize=""(?<size>[^""]+)""");

        // Test, który przechodzi, bo NICZEGO nie dopasował, jest gorszy niż brak testu (R16).
        Assert.True(block.Success,
            $"Nie znaleziono treści komunikatu (TextBlock wiązany do `Message`) w {relative}. Jeżeli ten "
            + "element przeniósł się albo zmienił wiązanie, strażnik musi pójść za nim — a nie zniknąć.");

        return block.Groups["size"].Value;
    }

    private static string EmptyStateFontSize(string relative, string visibilityBinding)
    {
        var text = WithoutComments(File.ReadAllText(Path.Combine(AppRoot(), relative.Replace('/', Path.DirectorySeparatorChar))), relative);

        var block = Regex.Match(
            text,
            @"<TextBlock\s+IsVisible=""\{Binding " + visibilityBinding + @"\}""(?<body>(?:(?!/>)[\s\S])*?)FontSize=""(?<size>[^""]+)""");

        // Test, który przechodzi, bo NICZEGO nie dopasował, jest gorszy niż brak testu (R16).
        Assert.True(block.Success,
            $"Nie znaleziono komunikatu pustego stanu (`{visibilityBinding}`) w {relative}. Jeżeli ten element "
            + "przeniósł się albo zmienił nazwę wiązania, strażnik musi pójść za nim — a nie zniknąć.");

        return block.Groups["size"].Value;
    }

    /// <summary>
    /// ⭐⭐ M4.3 / Q4 — pole filtra Trace ma brać wysokość TĄ SAMĄ rolą, którą biorą pola tekstowe i listy
    /// stojące obok niego w tym samym pasku. Strażnik pilnuje <b>PRZESŁANKI, a nie POLITYKI</b> (#322):
    /// czyta rolę z <c>ControlStyles.axaml</c> (skąd bierze ją prawdziwy <c>TextBox</c>) i wymaga, żeby
    /// widok deklarował dokładnie ją — więc gdy kiedyś zmieni się rola kontrolek, ten test powie, że pole
    /// filtra ma pójść za nimi, zamiast pilnować liczby 24, która by się rozjechała w ciszy.
    /// <para>
    /// ⚠ Zmierzone w M4.3: pole stało przy <c>Height="26"</c>, czyli 2 px wyżej niż każdy prawdziwy sąsiad,
    /// i to ono wyznaczało wysokość paska. Liczby 26 nie było nigdzie indziej w <c>src/</c>.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTraceFilterField_TakesTheHeightOfTheControlsBesideIt()
    {
        var styles = File.ReadAllText(Path.Combine(AppRoot(), "Themes", "ControlStyles.axaml"));
        var textBoxRole = Regex.Match(
            styles,
            @"<Style Selector=""TextBox"">\s*<Setter Property=""MinHeight"" Value=""(?<role>[^""]+)""");

        Assert.True(textBoxRole.Success,
            "Nie znaleziono wysokości `TextBox` w ControlStyles.axaml — bez niej ten strażnik nie ma "
            + "z czym porównywać i pilnowałby wyłącznie liczby przepisanej z widoku (#333).");

        var trace = WithoutComments(
            File.ReadAllText(Path.Combine(AppRoot(), "Views", "TraceMonitorTabView.axaml")),
            "Views/TraceMonitorTabView.axaml");

        var field = Regex.Match(trace, @"PlaceholderText=""\{x:Static app:UiStrings\.TraceFilterWatermark\}""");
        Assert.True(field.Success, "Nie znaleziono pola filtra w TraceMonitorTabView.axaml.");

        // ⚠⚠ Zakotwiczone w OTWIERAJĄCYM ZNACZNIKU ramki, a nie „ostatnia wysokość nad polem" — pierwsza
        //   wersja szukała tak i złapała ogon `MinHeight="0"` z `TextBox`a w środku, po czym porównywała
        //   rolę z zerem. Stąd też `(?<![A-Za-z])`: `MinHeight` kończy się na „Height".
        var start = trace[..field.Index].LastIndexOf("<Border", StringComparison.Ordinal);
        Assert.True(start >= 0, "Nie znaleziono ramki otaczającej pole filtra.");

        var openingTag = Regex.Match(trace[start..], @"<Border\b[^>]*>");
        Assert.True(openingTag.Success, "Ramka pola filtra nie ma domkniętego znacznika otwierającego.");

        var height = Regex.Match(openingTag.Value, @"(?<![A-Za-z])Height=""(?<h>[^""]+)""");
        Assert.True(height.Success,
            "Ramka pola filtra nie deklaruje wysokości. Jeżeli ma ją brać od kontenera, ten strażnik musi "
            + "zostać przepisany na tę przesłankę — a nie usunięty.");

        Assert.Equal(textBoxRole.Groups["role"].Value, height.Groups["h"].Value);
    }

    /// <summary>
    /// ⭐ M4.3 / Q2 — inline ✕ (czyszczące albo usuwające to, w czym stoi) ma jeden rozmiar w całej
    /// aplikacji. ⚠ Strażnik jest <b>relacyjny</b>: wymaga, żeby wszystkie takie ikony deklarowały tę samą
    /// rolę, a nie żeby deklarowały konkretną liczbę — bo pytanie brzmiało „czy to jest jeden element",
    /// a nie „ile ma pikseli".
    /// <para>
    /// ⚠⚠ Rozjazd, który to wywołał, siedział W JEDNYM PLIKU (debugger: 12 i trzy razy 11), więc nie
    /// opisywała go żadna reguła sformułowana per ekran — to jest #335 czytane o poziom niżej.
    /// ⛔ <c>AggregationBarView</c> jest świadomie poza tą regułą: jego ✕ niesie 10 px i należy do ogona
    /// 10/11/13/15, sparkowanego jako osobne pytanie o ROLE (§19.37.7).
    /// </para>
    /// <para>
    /// ⚠⚠ <b>PIERWSZA WERSJA TEGO STRAŻNIKA BYŁA BŁĘDNA I ZŁAPAŁ TO DOPIERO PRZEBIEG — grupowała po NAZWIE
    /// GEOMETRII, a nie po tym, CZYM RZECZ JEST DLA UŻYTKOWNIKA</b> (§19.39.2a, ten sam błąd o poziom dalej).
    /// <c>MainWindow.axaml</c> ma trzeci <c>Icon.X</c> — przycisk „Zamknij zakładkę" w pasku narzędzi —
    /// czyli samodzielną AKCJĘ, która <b>poprawnie nie deklaruje nic</b> i bierze <c>Size.Icon.Toolbar</c>
    /// (16) z <c>ControlTheme</c> (#332, A‑3). Ten sam glif pełni więc dwie role, dokładnie jak
    /// <c>Icon.RefreshCw</c> (16 w pasku, 14 gdy odświeża siatkę).
    /// ⭐ Dlatego reguła brzmi: <b>ikona, która DEKLARUJE rozmiar, deklaruje tę samą rolę</b> — a brak
    /// deklaracji jest osobną, poprawną drogą, nie brakiem decyzji.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryInlineClearIcon_SharesOneSizeRole()
    {
        string[] hosts = ["Views/DebuggerTabView.axaml", "Views/SessionManagerTabView.axaml", "Views/MainWindow.axaml"];
        var sized = new List<string>();
        var unsized = 0;

        foreach (var relative in hosts)
        {
            var text = WithoutComments(
                File.ReadAllText(Path.Combine(AppRoot(), relative.Replace('/', Path.DirectorySeparatorChar))),
                relative);

            foreach (Match icon in Regex.Matches(text, @"<controls:SvgIcon\b(?:(?!/?>)[\s\S])*?/>"))
            {
                if (!icon.Value.Contains("Icon.X", StringComparison.Ordinal)) continue;

                var size = Regex.Match(icon.Value, @"(?<![A-Za-z])Width=""(?<w>[^""]+)""");
                if (size.Success)
                {
                    sized.Add(size.Groups["w"].Value);
                }
                else
                {
                    unsized++;
                }
            }
        }

        Assert.True(sized.Count >= 6,
            $"Znaleziono tylko {sized.Count} ikon `Icon.X` deklarujących rozmiar — po M4.3 jest ich sześć "
            + "(4 debugger + 1 Session + 1 MainWindow). Test, który przechodzi, bo niczego nie dopasował, "
            + "jest gorszy niż brak testu (R16).");

        // ⚠ Ta asercja jest tu po to, żeby przypadek „samodzielna akcja bez deklaracji" nie zniknął
        //   w ciszy — gdyby ktoś dopisał mu rozmiar, reguła wyżej przestałaby opisywać rzeczywistość.
        Assert.True(unsized >= 1,
            "Zniknal `Icon.X`, ktory swiadomie NIE deklaruje rozmiaru (przycisk zamkniecia zakladki w pasku "
            + "narzedzi bierze `Size.Icon.Toolbar` z `ControlTheme`). Jezeli to celowa zmiana, popraw ten "
            + "straznik i jego uzasadnienie — a nie tylko liczbe.");

        Assert.Single(sized.Distinct(StringComparer.Ordinal));
        Assert.Equal("{DynamicResource Size.Icon.Sm}", sized[0]);
    }

    private static Dictionary<string, int> MeasureIconSizeLiterals()
    {
        var appRoot = AppRoot();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(appRoot, "*.axaml", SearchOption.AllDirectories))
        {
            var text = WithoutComments(File.ReadAllText(file), file);
            var count = Regex.Matches(
                text,
                @"<(?:controls:(?:SvgIcon|DebuggerIcon|CreateIcon)|Path)\b(?:(?!/?>)[\s\S])*?Width=""[0-9]").Count;
            if (count == 0) continue;

            result[Path.GetRelativePath(appRoot, file).Replace('\\', '/')] = count;
        }

        return result;
    }

    private static IEnumerable<string> ThemeFiles() =>
        Directory.EnumerateFiles(Path.Combine(AppRoot(), "Themes"), "*.axaml").OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>
    /// File name → the DISTINCT keys it declares. Distinct per file on purpose: within a variant dictionary the
    /// same key legitimately appears once per theme, and that is not what the cross-file check is looking for.
    /// </summary>
    private static Dictionary<string, HashSet<string>> KeysByFile() =>
        ThemeFiles().ToDictionary(
            // Method group would infer string? (GetFileName is NotNullIfNotNull-annotated), which a dictionary
            // key may not be — the input is always a real path here.
            f => Path.GetFileName(f)!,
            f => KeyedElements(File.ReadAllText(f)).Select(m => m.Groups["key"].Value).ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>
    /// Every element that DECLARES a resource, with its tag and key.
    /// <para>⚠ <c>ResourceDictionary</c> is excluded, and that is not a detail: <c>&lt;ResourceDictionary
    /// x:Key="Dark"&gt;</c> names a THEME SCOPE, not a resource. Both guards below first reported "Dark is
    /// declared in two files" and "Dark has no value" — true of the regex, meaningless about the code. A
    /// key-shaped string is not automatically a key.</para>
    /// </summary>
    private static IEnumerable<Match> KeyedElements(string text) =>
        Regex.Matches(text, @"<(?<tag>[\w:]+)(?=[\s>])[^>]*?x:Key=""(?<key>[^""]+)""[^>]*>")
             .Where(m => m.Groups["tag"].Value != "ResourceDictionary");

    /// <summary>
    /// Counts declarations per file, keyed by a repository-relative path with forward slashes. Keyed by path
    /// rather than by file name because a name can repeat between <c>Views/</c> and <c>Controls/</c>, and an
    /// exemption granted to the wrong file is worse than no exemption.
    /// </summary>
    private static Dictionary<string, int> Measure(string property)
    {
        var declaration = DeclarationOf(property);
        var appRoot = AppRoot();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var folder in new[] { "Views", "Controls" })
        {
            var root = Path.Combine(appRoot, folder);
            Assert.True(Directory.Exists(root), $"Could not locate {folder} at {root}");

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) &&
                    !file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hits = declaration.Matches(File.ReadAllText(file)).Count;
                if (hits > 0)
                {
                    counts[Path.GetRelativePath(appRoot, file).Replace('\\', '/')] = hits;
                }
            }
        }

        return counts;
    }

    private static string AppRoot() => Path.Combine(RepositoryRoot(), "src", "EmberTern.App");

    // Walks up from the test binary to the directory holding EmberTern.slnx. The test reads SOURCE, so it needs
    // the repository rather than the output folder.
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

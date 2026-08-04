using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace EmberTern.App.Diagnostics;

/// <summary>
/// ⭐⭐ INSTRUMENT DO ZŁAPANIA MECHANIZMU starego defektu Metadata Explorera: „rozwijam kilka dużych
/// kategorii → lista sama zaczyna przewijać się w dół → nie da się tego zatrzymać → kliknięcie zawiesza
/// i zamyka proces".
///
/// <para><b>Po co osobny instrument, skoro pomiary już były.</b> Dwa poprzednie kroki wykluczyły dwie
/// hipotezy — koszt projekcji w modelu (M3.4a: 2,3 ms) i wirtualizację (krok 15b: brak zawieszeń, pozycja
/// przewijania stabilna). ⛔ Obie były pomiarami <b>syntetycznymi</b>. Ten defekt odtwarza się wyłącznie
/// u użytkownika, więc jedyną drogą jest log z <b>prawdziwego przebiegu</b>.</para>
///
/// <para>⚠⚠ <b>Kluczowa obserwacja użytkownika, która ustawia całą konstrukcję:</b> uruchomiony z EXE
/// proces <b>ginie</b>, a uruchomiony spod Visual Studio <b>przewija się do pewnego miejsca, zatrzymuje
/// i działa dalej</b>. Różnica „jest debugger / nie ma debuggera" wskazuje na <b>wyjątek</b>, nie na koszt:
/// pod debuggerem wyjątek w callbacku Dispatchera bywa przechwycony, a bez niego kończy proces. Dlatego
/// pkt 5 (wyjątki, łącznie z <c>FirstChanceException</c>) jest tu tak samo ważny jak pkt 1–4, a
/// <c>AutoFlush</c> jest włączony — <b>ostatnie linie przed śmiercią procesu są całym sensem tego pliku</b>.</para>
///
/// <para><b>Włączanie:</b> zmienna środowiskowa <c>EMBERTERN_TREE_DIAG</c> (dowolna wartość). Bez niej
/// klasa nie robi <b>nic</b> — żadnego pliku, żadnych subskrypcji, zero kosztu.</para>
///
/// <para><b>Log:</b> <c>%TEMP%\EmberTern-tree-diag-&lt;pid&gt;-&lt;stamp&gt;.log</c>. ⭐ Własny plik, nie
/// wspólny <c>EmberTern-debug.log</c>: burza przewijania potrafi wyprodukować dziesiątki tysięcy linii
/// i wymieszana z logiem połączeń czyni oba bezużytecznymi.</para>
///
/// <para>⚠ <b>Efekt obserwatora jest realny i świadomy.</b> <c>AutoFlush</c> to jeden syscall na linię, a
/// zrzuty stosu kosztują. Instrument zmienia więc timing tego, co mierzy. ⭐ Przyjęte świadomie: log, który
/// urywa się przed momentem śmierci, nie odpowiada na żadne z pięciu pytań. Zrzuty stosu są <b>budżetowane</b>
/// (niżej), żeby log nie utonął we własnym szumie.</para>
///
/// <para>⛔ To NIE jest naprawa i nie zmienia zachowania aplikacji. Wyłącznie obserwacja.</para>
/// </summary>
internal static class TreeDiagnostics
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("EMBERTERN_TREE_DIAG") is not null;

    /// <summary>Twardy limit linii — instrument nie ma prawa zapełnić dysku użytkownika.</summary>
    private const int MaxLines = 400_000;

    /// <summary>Pierwsze N zmian offsetu dostaje pełny stos zawsze — początek burzy jest najciekawszy.</summary>
    private const int EagerStackBudget = 25;

    /// <summary>Potem stos co najwyżej raz na tyle milisekund (poza eskalacją).</summary>
    private const int StackThrottleMs = 250;

    /// <summary>Powyżej tylu zdarzeń w oknie 100 ms uznajemy, że trwa burza, i eskalujemy.</summary>
    private const int StormEventsPer100Ms = 200;

    private static StreamWriter? _writer;
    private static readonly object Gate = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static long _seq;
    private static int _lines;
    private static bool _truncated;
    private static bool _started;

    private static int _stacksTaken;
    private static long _lastStackMs;

    private static long _windowStartMs;
    private static int _windowEvents;
    private static bool _inStorm;

    // Zagnieżdżenie własnych zakresów — reentrancy jest jedną z hipotez, więc mierzymy ją wprost.
    private static readonly Dictionary<string, int> Depth = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, int> PostCounts = new(StringComparer.Ordinal);

    public static bool IsEnabled => Enabled;

    public static string? LogPath { get; private set; }

    /// <summary>
    /// Otwiera plik i instaluje haki procesowe. Idempotentne. ⚠ Wołane raz, z okna głównego.
    /// </summary>
    public static void Start(string context)
    {
        if (!Enabled) return;
        lock (Gate)
        {
            if (_started) return;
            _started = true;

            try
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                LogPath = Path.Combine(
                    Path.GetTempPath(),
                    $"EmberTern-tree-diag-{Environment.ProcessId}-{stamp}.log");

                // ⭐ AutoFlush: patrz komentarz klasy. Ostatnie linie przed śmiercią procesu są celem.
                _writer = new StreamWriter(
                    new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true,
                };
            }
            catch (IOException)
            {
                _writer = null;
                return;
            }
            catch (UnauthorizedAccessException)
            {
                _writer = null;
                return;
            }
        }

        WriteHeader(context);
        InstallProcessHooks();
    }

    private static void WriteHeader(string context)
    {
        Raw("════════════════════════════════════════════════════════════════════════════════");
        Raw("EmberTern — diagnostyka drzewa metadanych (EMBERTERN_TREE_DIAG)");
        Raw("════════════════════════════════════════════════════════════════════════════════");
        Raw($"start        : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        Raw($"kontekst     : {context}");
        Raw($"proces       : PID {Environment.ProcessId}, .NET {Environment.Version}");
        Raw($"debugger     : {(Debugger.IsAttached ? "PODPIĘTY (uruchomienie spod IDE)" : "brak (uruchomienie z EXE)")}");
        Raw($"64-bit       : {Environment.Is64BitProcess}, procesorów: {Environment.ProcessorCount}");
        Raw("");
        Raw("FORMAT LINII:  #seq  t=<ms od startu>  [wątek]  KATEGORIA  treść");
        Raw("");
        Raw("KATEGORIE — i na które z pięciu pytań odpowiadają:");
        Raw("  SCROLL   (1) offset/ekstent/viewport + delty; przy zmianie offsetu bywa STACK");
        Raw("  EVENT    (2) ScrollChanged / SelectionChanged / RequestBringIntoView / EffectiveViewport");
        Raw("  COLL     (3) zmiany kolekcji wierszy (Add/Remove/Reset) — przebudowy listy");
        Raw("  REBUILD  (3) jawne przebudowy (LoadGroup, ApplyFilter, ReloadConnections)");
        Raw("  SCOPE    (4) wejście/wyjście z naszego zakresu + GŁĘBOKOŚĆ ZAGNIEŻDŻENIA (reentrancy)");
        Raw("  POST     (4) Dispatcher.Post — nazwa zadania i licznik powtórzeń");
        Raw("  DEPTH    (4) głębokość stosu wywołań — rośnie przy rekurencji");
        Raw("  EXC      (5) wyjątki: FIRST-CHANCE (nawet obsłużone), UNHANDLED, TASK");
        Raw("  STORM    (-) wykryto lawinę zdarzeń; wtedy log eskaluje i bierze stos");
        Raw("");
        Raw("CZEGO SZUKAĆ:");
        Raw("  · SCROLL z rosnącym offsetem BEZ poprzedzającego EVENT od użytkownika = przewijanie samoczynne");
        Raw("  · powtarzający się cyklicznie ten sam STACK = pętla; jego szczyt to jej sprawca");
        Raw("  · SCOPE z depth>1 = reentrancy (wejście w zakres, który już trwa)");
        Raw("  · DEPTH rosnące monotonicznie = rekurencja; koniec = StackOverflow, którego NIE DA SIĘ złapać");
        Raw("  · EXC FIRST-CHANCE tuż przed końcem logu = najpewniejszy kandydat na przyczynę śmierci EXE");
        Raw("════════════════════════════════════════════════════════════════════════════════");
        Raw("");
    }

    private static void InstallProcessHooks()
    {
        // (5) Wyjątki. ⭐ FIRST-CHANCE jest tu najważniejszy i to on tłumaczy różnicę EXE vs Visual Studio:
        //     łapie rzut ZANIM ktokolwiek go obsłuży, więc pokaże także ten, który pod debuggerem zostaje
        //     przechwycony, a bez debuggera kończy proces.
        // ⚠ Jest z natury hałaśliwy (łapie też wyjątki oczekiwane). To cena za to, że nie przegapi tego
        //   jednego, o który chodzi — a log i tak czyta się od końca.
        AppDomain.CurrentDomain.FirstChanceException += (_, e) =>
        {
            // Odfiltrowane: rzuty z samego zapisu logu — inaczej instrument potrafi napędzać sam siebie.
            if (e.Exception is IOException) return;
            Log("EXC", $"FIRST-CHANCE {e.Exception.GetType().Name}: {Flatten(e.Exception.Message)}");
            WriteStack(e.Exception.StackTrace, "  ");
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Log("EXC", $"⛔ UNHANDLED (terminating={e.IsTerminating}) "
                + $"{ex?.GetType().Name ?? "?"}: {Flatten(ex?.Message ?? e.ExceptionObject?.ToString())}");
            WriteStack(ex?.StackTrace, "  ");
            Raw("── KONIEC: proces kończy się przez nieobsłużony wyjątek ──");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("EXC", $"TASK (nieobserwowany) {Flatten(e.Exception.Message)}");
            WriteStack(e.Exception.StackTrace, "  ");
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            Raw($"── ProcessExit, linii: {_lines}{(_truncated ? " (LOG UCIĘTY)" : string.Empty)} ──");
            try { _writer?.Flush(); } catch (IOException) { }
        };

        SelfTestExceptionChannel();
    }

    /// <summary>
    /// ⭐⭐ SAMOTEST KANAŁU WYJĄTKÓW — rzuca i natychmiast łapie nieszkodliwy wyjątek, żeby w logu
    /// pojawił się dowód, że hak <c>FirstChanceException</c> ŻYJE.
    ///
    /// <para>⚠⚠ Bez tego brak wpisów <c>EXC</c> na końcu logu jest <b>nierozstrzygalny</b>: znaczy albo
    /// „żaden wyjątek nie poleciał", albo „hak nie działa" — a to są przeciwne wnioski prowadzące do
    /// przeciwnych poszukiwań. <b>Pomiar negatywny jest tym niebezpiecznym</b> (gotcha #285), więc kanał
    /// musi się przedstawić na starcie.</para>
    /// </summary>
    private static void SelfTestExceptionChannel()
    {
        Raw("── samotest kanału wyjątków: poniżej MUSI pojawić się jedna linia EXC FIRST-CHANCE ──");
        try
        {
            throw new InvalidOperationException("samotest diagnostyki — ten wyjątek jest celowy i obsłużony");
        }
        catch (InvalidOperationException)
        {
            // Celowo połknięty. Jeżeli powyżej NIE ma linii EXC, hak wyjątków nie działa i pytanie 5
            // pozostaje bez odpowiedzi — o czym czytelnik logu musi wiedzieć od razu.
        }
        Raw("── koniec samotestu ──");
        Raw("");
    }

    // ── (1) Przewijanie ──────────────────────────────────────────────────────────────────────────

    /// <summary>Jedna zmiana geometrii przewijania. <paramref name="withStack"/> wymusza zrzut stosu.</summary>
    public static void Scroll(
        double offsetY, double extentH, double viewportH,
        double dOffset, double dExtent, int realized, string source)
    {
        if (!Enabled) return;

        var moved = Math.Abs(dOffset) > 0.01;
        Log("SCROLL", string.Format(CultureInfo.InvariantCulture,
            "{0,-18} offsetY={1,10:0.0} extentH={2,11:0.0} viewportH={3,7:0.0} "
            + "dOffset={4,+8:0.0} dExtent={5,+9:0.0} realized={6}{7}",
            source, offsetY, extentH, viewportH, dOffset, dExtent, realized,
            Math.Abs(dExtent) > 0.5 ? "  <-- EKSTENT PRZELICZONY" : string.Empty));

        // ⭐ (1) „kto go zmienia i z jakiego miejsca" — odpowiada wyłącznie stos wywołań.
        //    Budżetowany, żeby log nie utonął: pierwsze N zawsze, potem z throttlem, zawsze w burzy.
        if (moved && ShouldTakeStack())
        {
            Log("SCROLL", "  ↑ stos zmiany offsetu:");
            WriteStack(new StackTrace(1, fNeedFileInfo: true).ToString(), "     ");
        }
    }

    // ── (2) Zdarzenia mogące tworzyć pętlę ───────────────────────────────────────────────────────

    public static void Event(string name, string detail)
    {
        if (!Enabled) return;
        NoteEvent();
        Log("EVENT", $"{name,-24} {detail}");
        if (_inStorm && ShouldTakeStack())
        {
            Log("EVENT", "  ↑ stos (burza):");
            WriteStack(new StackTrace(1, fNeedFileInfo: true).ToString(), "     ");
        }
    }

    // ── (3) Przebudowy listy ─────────────────────────────────────────────────────────────────────

    public static void Collection(NotifyCollectionChangedEventArgs e, int total)
    {
        if (!Enabled) return;
        NoteEvent();
        Log("COLL", string.Format(CultureInfo.InvariantCulture,
            "{0,-8} newIndex={1,6} newCount={2,5} oldIndex={3,6} oldCount={4,5} razem={5}",
            e.Action, e.NewStartingIndex, e.NewItems?.Count ?? 0,
            e.OldStartingIndex, e.OldItems?.Count ?? 0, total));
    }

    /// <summary>Jawna przebudowa (LoadGroup, ApplyFilter, ReloadConnections, Rebuild).</summary>
    public static void Rebuild(string what)
    {
        if (!Enabled) return;
        Log("REBUILD", what);
        if (ShouldTakeStack())
        {
            WriteStack(new StackTrace(1, fNeedFileInfo: true).ToString(), "     ");
        }
    }

    // ── (4) Reentrancy, Dispatcher, głębokość stosu ──────────────────────────────────────────────

    /// <summary>
    /// Otacza nasz własny zakres i mierzy ZAGNIEŻDŻENIE. ⭐ <c>depth &gt; 1</c> znaczy, że weszliśmy
    /// w zakres, który już trwa — czyli reentrancy, jedna z trzech hipotez.
    /// </summary>
    public static IDisposable? Scope(string name)
    {
        if (!Enabled) return null;
        int depth;
        lock (Gate)
        {
            Depth.TryGetValue(name, out depth);
            depth++;
            Depth[name] = depth;
        }

        Log("SCOPE", $"→ {name} depth={depth}{(depth > 1 ? "  <-- REENTRANCY" : string.Empty)}");
        if (depth > 1)
        {
            WriteStack(new StackTrace(1, fNeedFileInfo: true).ToString(), "     ");
        }

        return new ScopeExit(name);
    }

    private sealed class ScopeExit : IDisposable
    {
        private readonly string _name;
        private bool _done;
        public ScopeExit(string name) => _name = name;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            int depth;
            lock (Gate)
            {
                Depth.TryGetValue(_name, out depth);
                depth = Math.Max(0, depth - 1);
                Depth[_name] = depth;
            }
            Log("SCOPE", $"← {_name} depth={depth}");
        }
    }

    /// <summary>Zadanie oddane Dispatcherowi. Licznik powtórzeń nazwy wychwytuje cykl.</summary>
    public static void Posted(string name)
    {
        if (!Enabled) return;
        int count;
        lock (Gate)
        {
            PostCounts.TryGetValue(name, out count);
            count++;
            PostCounts[name] = count;
        }
        Log("POST", $"post  {name,-34} razem={count}");
    }

    public static void Executing(string name)
    {
        if (!Enabled) return;
        NoteEvent();
        Log("POST", $"exec  {name}");
    }

    /// <summary>
    /// Głębokość stosu wywołań. ⭐ Rekurencja przez układ/BringIntoView rośnie tu monotonicznie,
    /// a jej końcem jest <c>StackOverflowException</c>, którego <b>nie da się przechwycić</b> — proces
    /// ginie natychmiast. To jest jedyny sposób, żeby ten scenariusz zobaczyć w logu.
    /// </summary>
    public static void StackDepth(string where)
    {
        if (!Enabled) return;
        var frames = new StackTrace(1, fNeedFileInfo: false).FrameCount;
        Log("DEPTH", $"{where,-28} ramek={frames}{(frames > 400 ? "  <-- GŁĘBOKO" : string.Empty)}");
    }

    // ── Wykrywanie burzy ─────────────────────────────────────────────────────────────────────────

    private static void NoteEvent()
    {
        var now = Clock.ElapsedMilliseconds;
        lock (Gate)
        {
            if (now - _windowStartMs >= 100)
            {
                if (_windowEvents >= StormEventsPer100Ms && !_inStorm)
                {
                    _inStorm = true;
                    Raw($"#{Interlocked.Increment(ref _seq)} t={now} ── STORM: {_windowEvents} zdarzeń w 100 ms; log eskaluje ──");
                }
                else if (_windowEvents < StormEventsPer100Ms / 4 && _inStorm)
                {
                    _inStorm = false;
                    Raw($"#{Interlocked.Increment(ref _seq)} t={now} ── koniec burzy ──");
                }
                _windowStartMs = now;
                _windowEvents = 0;
            }
            _windowEvents++;
        }
    }

    private static bool ShouldTakeStack()
    {
        var now = Clock.ElapsedMilliseconds;
        lock (Gate)
        {
            if (_stacksTaken < EagerStackBudget)
            {
                _stacksTaken++;
                _lastStackMs = now;
                return true;
            }
            if (now - _lastStackMs >= StackThrottleMs)
            {
                _lastStackMs = now;
                return true;
            }
            return false;
        }
    }

    // ── Zapis ────────────────────────────────────────────────────────────────────────────────────

    public static void Log(string category, string message)
    {
        // ⚠ Także gdy plik jeszcze nie istnieje — inaczej wpisy sprzed `Start()` (np. przebudowa
        // z `ReloadConnections`, która biegnie przed otwarciem okna) zjadałyby numery i log zaczynałby
        // się od dziury w sekwencji. Czytelnik logu ma prawo zakładać, że numeracja jest ciągła.
        if (!Enabled || _writer is null) return;
        var seq = Interlocked.Increment(ref _seq);
        Raw(string.Format(CultureInfo.InvariantCulture,
            "#{0,-7} t={1,-9} [{2,3}] {3,-8} {4}",
            seq, Clock.ElapsedMilliseconds, Environment.CurrentManagedThreadId, category, message));
    }

    private static void WriteStack(string? stack, string indent)
    {
        if (!Enabled || string.IsNullOrEmpty(stack)) return;
        foreach (var line in stack.Split('\n'))
        {
            var t = line.TrimEnd('\r');
            if (t.Length == 0) continue;
            Raw(indent + t.Trim());
        }
    }

    private static void Raw(string line)
    {
        var w = _writer;
        if (w is null) return;
        lock (Gate)
        {
            if (_truncated) return;
            if (_lines >= MaxLines)
            {
                _truncated = true;
                try { w.WriteLine($"── LOG UCIĘTY po {MaxLines} liniach ──"); } catch (IOException) { }
                return;
            }
            _lines++;
            try { w.WriteLine(line); } catch (IOException) { } catch (ObjectDisposedException) { }
        }
    }

    private static string Flatten(string? s)
        => s is null ? "<null>" : s.Replace("\r", " ", StringComparison.Ordinal)
                                   .Replace("\n", " ", StringComparison.Ordinal);
}

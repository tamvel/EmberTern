# `HeadlessUnitTestSession` (PerTest) races with any parallel thread that touches Avalonia

> **Status:** open, upstream. Not fixable in EmberTern — see § "What we tried".
> **Written:** 2026-08-14. **Reproduction:** deterministic (below).
> This file is both our record of the defect and the text to file with Avalonia.

---

## 1. The report (ready to file)

**Package:** `Avalonia.Headless` 12.1.1 · **Runner:** xunit 2.9.2 / `xunit.runner.visualstudio` 2.8.2 ·
**Target:** `net9.0` · **OS:** Windows 11

### Summary

`HeadlessUnitTestSession` in the default `AvaloniaTestIsolationLevel.PerTest` mode rebuilds the Avalonia
application on **every** `Dispatch`. `EnsureIsolatedApplication` begins by calling
`Dispatcher.ResetBeforeUnitTests()`, which clears **process-wide** dispatcher state. While that window is
open, `Dispatcher.UIThread` is unset and the first thread to touch it claims it — and merely constructing
an Avalonia object touches it.

Because xunit parallelises **test collections by default**, any test outside the headless collection that
constructs an Avalonia object can land in that window. The session's own `Compositor` then fails
`Dispatcher.VerifyAccess()` on the session thread and the dispatch dies.

### Observed exception

```text
System.InvalidOperationException : The calling thread cannot access this object because a different thread owns it.
   at Avalonia.Threading.Dispatcher.VerifyAccess()
   at Avalonia.Rendering.DefaultRenderLoop.Add(IRenderLoopTask i)
   at Avalonia.Rendering.Composition.Server.ServerCompositor..ctor(...)
   at Avalonia.Rendering.Composition.Compositor..ctor(...)
   at Avalonia.Headless.AvaloniaHeadlessPlatform.Initialize(AvaloniaHeadlessPlatformOptions opts)
   at Avalonia.Headless.AvaloniaHeadlessPlatformExtensions.<>c__DisplayClass0_0.<UseHeadless>b__0()
   at Avalonia.AppBuilder.SetupUnsafe()
   at Avalonia.Headless.HeadlessUnitTestSession.EnsureIsolatedApplication()
   at Avalonia.Headless.HeadlessUnitTestSession.<>c__DisplayClass12_0`1.<DispatchCore>b__0()
```

### Minimal reproduction

A console app referencing `Avalonia.Headless`. `control` and `race` differ **only** by whether four
background threads construct `Grid`s while the dispatch loop runs.

```csharp
static class Entry
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

var race = args.Contains("race");
var session = HeadlessUnitTestSession.StartNew(typeof(Entry));

var stop = false;
var noise = race
    ? Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
      {
          while (!Volatile.Read(ref stop))
          {
              var g = new Grid();
              g.RowDefinitions.Add(new RowDefinition(GridLength.Star));
          }
      })).ToArray()
    : Array.Empty<Task>();

int ok = 0, failed = 0;
for (var i = 0; i < 150; i++)
{
    try { await session.Dispatch(() => { var g = new Grid(); }, CancellationToken.None); ok++; }
    catch { failed++; }
}

Volatile.Write(ref stop, true);
Task.WaitAll(noise);
Console.WriteLine($"OK {ok}, FAILED {failed}");
```

### Result

| Mode | Dispatches | OK | Failed | Repeats |
|---|---:|---:|---:|---:|
| `control` — nothing else touches Avalonia | 150 | **150** | **0** | 2/2 |
| `race` — 4 threads constructing `Grid`s | 150 | **1** | **149** | 2/2 |

### Why this matters beyond a synthetic loop

`Avalonia.Headless.XUnit` targets xunit, and xunit parallelises collections by default. Any repository
that has headless tests **and** ordinary tests touching Avalonia types will hit this. In our suite
(~8 800 tests) it costs one test in roughly **one full run in eight**, and the failing test's *name*
changes every time — it is whichever headless test dispatched first — so it reads as several unrelated
flaky tests rather than one defect.

### Suggested direction (non-prescriptive)

Guard the reset/initialise window in `EnsureIsolatedApplication` so that a foreign thread cannot claim
`Dispatcher.UIThread` while the session is rebuilding its application; or document that `PerTest` requires
that no other thread in the process touches Avalonia, which the default xunit configuration cannot
guarantee.

### Additional observation — weaker, no minimal repro

In our environment `AvaloniaTestIsolationLevel.PerAssembly` fails **deterministically**: every headless
test (~180) dies with the same `VerifyAccess` exception, in every run, **including with collection
parallelism disabled**, and identically via `StartNew(type, level)` and via
`[assembly: AvaloniaTestApplication]` + `[assembly: AvaloniaTestIsolation]` + `GetOrStartForAssembly`.
In the rare run where initialisation did succeed, one unrelated assertion failed, suggesting shared
application state leaks between tests. **We have not reduced this to a minimal reproduction**, so we
report it only as an observation, not as a second bug report.

---

## 2. What we tried, and why each was rejected

All measured on the full ~8 800-test suite unless stated.

| Approach | Result | Verdict |
|---|---|---|
| Warm-up `Dispatch` in the collection fixture ctor | Same failure rate; a throwing collection fixture fails **every** test in the collection — a bad run lost **375** tests instead of 1 | Rejected — no gain, 375× blast radius |
| Put every test that names an Avalonia type into `HeadlessCollection` | 1 red run in 8. Excluding **all 18** such classes outright still left **4 red runs in 12** | Rejected — cannot converge: `EmberTern.App` is *made of* Avalonia types, so the rule grows into a list of exceptions |
| `AvaloniaTestIsolationLevel.PerAssembly` (manual `StartNew`) | ~180 failures, 10/10 runs | Rejected |
| Supported path + `PerTest` (`[AvaloniaTestApplication]` + `GetOrStartForAssembly`) | 1 red run in 12 — not distinguishable from baseline at this sample size | Rejected — no measurable improvement |
| Supported path + `PerAssembly` | ~180 failures, 12/12 runs; plus one real assertion failure in the single run that initialised | Rejected |
| Disabling collection parallelism | 3/3 green, ~52 s instead of ~35 s | **Works**, but it is the thing we refuse — it hides the defect and slows every healthy test |

⛔ No warm-up, `Delay`, retry or artificial synchronisation was adopted, and none should be: they would
convert a visible, recognisable false failure into an invisible one.

## 3. Living with it

The signature and the "re-run once" rule are in `CLAUDE.md` § "Running the suite". The short version:
**one** failed test, a **different name each run**, and the stack above ⇒ this defect, re-run. The same
test failing twice in a row ⇒ a real defect.

# 32 — The audit follow-up etap (2026-08-14 … 2026-08-15)

> **Why this file exists.** Everything below stood in `docs/current-state.md` while the etap was open, and
> is moved here **verbatim** now that it is closed, merged to `master` and pushed to both remotes
> (`2c3da45`). `current-state.md` keeps one line per closed stage; the narrative lives here. ⛔ Nothing was
> edited on the way across — the only additions are this header and the section numbers, which were `§2a`
> and three subsections of `§3` in the file it came from.
>
> The etap ran on `fix/audit-followup-2026-08`, cut from `master`. Phases: test isolation · E (settings
> read-modify-write) · the Avalonia headless race · 4 (debugger irreversible-effects warning) · 5 (charset
> guard) · 6 (NuGet to latest stable) · 7 (`ARCHITECTURE.md` as-built).

---

## 1. The etap — as accepted

⚠ Delivered on `fix/audit-followup-2026-08`, **not yet merged and not pushed.**

> ⭐ *[Editorial note, added on the move, 2026-08-15 — the sentence above was true when written and is
> kept because this file is a verbatim record. It has since been overtaken: the etap **was** merged to
> `master` as `2c3da45` and pushed to both remotes. Nothing else below has been touched.]*

**Test isolation (`440c0ce`).** The full suite failed 45 tests deterministically because
`Loc.LanguageChanged` is `static`: every view model any earlier test built stayed subscribed, and the
next test to swap the catalog broadcast into all of them. Fixed at the source —
`Loc.IsolateSubscribersForVerification()` plus `IsolatesGlobalLanguageState` on `HeadlessCollection`, so
each headless test gets a clean subscriber list **automatically**. ⛔ The old three-partition manual
split is gone and must not return; it hid this for months. `DiagnosticsPanelViewModel` stopped
subscribing to the static event (it leaked one live VM per editor tab) and became an ordinary child of
the app's single long-lived subscriber. Two source guards keep both rules armed.

**E — settings read-modify-write (`972426e`).** Measured data loss: a facade doing
`Load() ?? new ApplicationSettings()` → mutate → `Save()` turned a *transient* read failure into
DEFAULTS and wrote them. Against a concurrent publisher: **182 failed reads, 89 of which wrote
defaults, ending with 0 of 5 connection profiles surviving** — profiles and passwords, silently.
`ApplicationSettingsStore.Update()` now takes the cross-process lock, reads **under it**, mutates and
writes via `SaveCore`. `Missing` is the ONLY status that may produce a default aggregate and this is
the only place that may; `Unreadable` / `Corrupt` / `FutureVersion` end the operation untouched.
15 call sites migrated. ⛔ Not a retry — the lock's scope removes the window.

**Phase 4 — debugger irreversible-effects warning (`1130e3d`, `1852611`), ✅ user-verified in the app:**

- detection of `IN AUTONOMOUS TRANSACTION` / `GEN_ID` / `NEXT VALUE FOR` reuses the existing
  `DebugPreflight`; `Scan` gained `out bool irreversible`, so **one scan** answers both the launch
  panel's sentences and the running view's bar and they cannot disagree;
- a **one-time modal** before launching risky code, with **"Nie pokazuj tego ostrzeżenia ponownie"**;
  Cancel really stops the launch;
- a **dismissible bar** at the foot of the debug view (shared `MessageBanner`, `Classes="docked"`) —
  the launch panel disappears when a session starts, so that is where the warning was missing;
- `BuildPreflight` runs on every Launch **and** Restart, so re-arming is automatic; dismissing is per
  run;
- ⭐ the preference silences the **modal only, never the bar** — pinned by a test;
- ⛔ **no safe mode and no blocking of valid SQL**: suppressing a generator or an autonomous
  transaction would mean refusing to execute correct SQL, against the debugger's fidelity law (§F).

**UX fix (`1852611`).** The suppress checkbox was clipped (*"Nie pokazuj tego ostrzeż…"*). Measured:
the label needs **358 px in English and 435 px in Polish** against ~380 px of content width — so a row
of its own is necessary and **still not sufficient**, hence the label also wraps. ⛔ The shared dialog
width (420, also `TextPromptDialog`) and the font size were **not** touched, and the wording stays as
accepted — the layout absorbs longer localizations instead. `ConfirmDialogLayoutTests` measures the
property ("nothing is cut"), verified red in both broken shapes before being accepted green.

---


---

## 2. Phase 7 — `ARCHITECTURE.md` "as built"


Rewritten from scratch against the code. ⚠ The previous file dated from **2026-06-02** (the V1 era) and had
gone silently stale in the most misleading way: it described `EmberTern.Core` as **17 files** against a real
**304**, Firebird 12 against 42, App 30 against 274 — i.e. it read as a plausible document while describing a
different product.

**Scope:** solution shape and the one-way dependency graph · the three connection lanes as the central domain
boundary · F5 end-to-end · inter-layer communication · shell/theming/commands · the SQL/PSQL front-end and its
three safety properties · metadata + DDL change safety · debugger and the Fidelity Law · charset guard ·
`ApplicationSettingsStore` guarantees · localization incl. `ErrorText` · modules · test infrastructure incl. the
upstream headless race and the guard tests · architectural invariants · deliberate limits.

**Validation:** every cited type name checked to exist in `src/`/`tests/` (the only three that do not resolve
are `IDbProvider` and `IMessenger`, cited precisely as things the project does **not** have, plus
`tools/probes/CharsetProbe` which is outside `src/`); every referenced document path checked to exist; the file
counts, the six `IPerformanceRule` implementations and the ten per-object editors re-counted from the tree.

**Discrepancies found while documenting — recorded, not fixed** (documentation phase, no code touched):

- ⚠ **`MessageBanner` is used by 21 views**, where prior prose said "23".
- ⚠ **Naming trap around "breadcrumbs".** `CLAUDE.md` lists Breadcrumbs as deliberately unbuilt — true of
  *editor* breadcrumbs — but `Controls/BreadcrumbBar` **does exist** as the debugger's call-stack breadcrumb.
  A reader grepping the word finds a real control and concludes the docs are wrong. Written down explicitly in
  `ARCHITECTURE.md` §16. Editor folding, by contrast, has genuinely zero occurrences.
- ⚠ **`SourceObjectDetailTabViewModel` is an abstract base**, not an eleventh editor — the "ten per-object
  editors" count is correct, but a file listing suggests eleven.

---

## 3. Phase 6 — NuGet update to latest stable


Target versions taken from nuget.org (`--outdated` + the flat-container API per package), applied in ONE pass;
nothing pre-release. Full reasoning in commit `8ba4215`.

⭐ **The packages expected to be hard were already current** — `Avalonia` (+Desktop/Themes.Fluent/Fonts.Inter/
Headless) 12.1.1, `Avalonia.AvaloniaEdit` 12.0.0, `Avalonia.Controls.DataGrid` 12.1.2,
`FirebirdSql.Data.FirebirdClient` 10.3.4, `CommunityToolkit.Mvvm` 8.4.2, `AvaloniaUI.DiagnosticsSupport` 2.2.3
are each **the newest stable that exists**. So the two "deliberate mismatches" are **not pins**: no 12.1.x
AvaloniaEdit and no 12.1.1 DataGrid were ever published.

**Raised (9):** `System.Security.Cryptography.ProtectedData` 9.0.0→10.0.11 (⭐ rule #11 path — closed with 215
targeted settings/crypto tests; the old "our TFM is net9.0" objection was measured false, 10.0.11 ships a real
`lib/net9.0`), `System.IO.Packaging` 9.0.18→10.0.11, `System.Security.Cryptography.Xml` 8.0.4→10.0.11,
`DocumentFormat.OpenXml` 3.1.0→3.5.1, `ExcelDataReader` 3.7.0→3.9.0, `Microsoft.NET.Test.Sdk` 17.11.1→18.9.0,
`xunit` 2.9.2→2.9.3, `xunit.runner.visualstudio` 2.8.2→4.0.0, `SixLabors.ImageSharp` 2.1.11→2.1.13.

**The one breaking change reaching our code:** OpenXml 3.5.1 annotates `WorkbookPart.Workbook` and
`WorksheetPart.Worksheet` as nullable, so `XlsxExporterTests`' read-back helpers failed `CS8602`. Adapted, not
suppressed — ⛔ no `#pragma`, no `NoWarn`. Product code needed no change.

⛔ **Two held back, both TEST-ONLY and both LICENSING — for the licensing etap, not technical limits:**
**NPOI** stays 2.7.2 (2.7.2 is `Apache-2.0`; **2.8.0 is `OSMFEULA.txt`** and adds a build gate demanding
`<AcceptNPOIOSMFLicense>true</AcceptNPOIOSMFLicense>` — accepting terms on the owner's behalf).
**SixLabors.ImageSharp** raised only within the 2.x line NPOI supports; 3.0+ moved to the Six Labors Split
Licence. ⭐ If NPOI ever reaches 2.8.0+, the ImageSharp override disappears entirely — 2.8.0 renders through
SkiaSharp.

**Verified:** Debug + Release 0/0; full suite **8 853/8 853** (total unchanged, so nothing left discovery);
`--vulnerable --include-transitive` zero across all five projects; `--outdated` now lists only the two held.

---

## 4. Phase 5 — charset guard


Full narrative is in commit `aa12d9a` and gotchas **#372/#373**; the rule it produced is **architecture rule 12**
in `CLAUDE.md`. In one paragraph: a character the CONNECTION charset cannot hold was destroyed **client-side, in
the driver's encoder, before the server saw it** — no exception, no server error. ⚠ The audit's "becomes `?`" was
incomplete: **330** characters become a *plausible different* one (`£`→`L`, `¼`→`1`, `À`→`A`), so a procedure body
sent as `R = 'Cena £100 ¼ À'` was stored as `R = 'Cena L100 1 A'` — valid PSQL, wrong number. Reads were already
safe (the server refuses transliteration loudly), so this was write-side only.

Built as **ONE seam**, `FirebirdCommandGuard`, which every command creation and parameter bind goes through (96
sites + the import batch), refusing **before** the driver encodes; DDL validates the whole batch **before a
transaction opens**, so refused source never reaches the server at all. Core owns the oracle
(`CharsetRepresentation`) and the wire question (`CharsetCatalog.ResolveWireEncoding`); ⛔ `CharsetCatalog.Resolve`
was deliberately left untouched — it answers a *different* question, and merging them was the live `NONE` defect
this closed in the shipped import guard. Messages go through localization in both languages, which needed
`App/Localization/ErrorText.cs` (a refusal is *wrapped*, so the display site was reading the English `Message`).

**Measured:** 8 844/8 844 after the guard, **8 853** after the localization fix; Debug and Release 0/0; live probe
`tools/probes/CharsetProbe` **15/15** across parameter · F5 · DDL (stored source proven byte-identical after a
refusal) · import · debugger. Three `CharsetGuardSeamTests` fail the build if a raw `CreateCommand` /
`CommandText =` / `AddWithValue` reappears — verified red, then green.

⛔ **Still open, deliberately out of Phase 5:** the UX of the read-side "cannot transliterate" message, and whether
`NONE` should stay in `CharsetCatalog.Supported` (lossy and machine-dependent — gotcha #373).


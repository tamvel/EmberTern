# Keyboard Manager & Context Menu UX — sprint design document

**Status: ETAPS 1–4 COMPLETE.** Audit accepted · registry (§11) · shortcuts (§12) · tooltips (§14).
Collision report: §13. Branch: `feat/keyboard-manager`. Build 0/0; suite **5943 green** in the two
documented partitions (5900 + 43); smoke clean.

**A keyboard gesture is now written down in exactly ONE place** (`CommandCatalog`) and reaches every
shortcut, tooltip and shortcut-chip from there. **Etap 5 (context menus) is the only one left, and it adds
no gestures.**

**The shortcut map was ratified by the user on accepting etap 1** — `F3` New · `F4` Refresh ·
`F5` Execute (Continue in the debugger stays the one accepted contradiction) · `F6` Commit ·
`Shift+F6` Rollback · `F7` Compile · `F8` Delete in trees and lists, Next Diagnostic in the editor ·
`Ctrl+K` Format SQL · **no `Alt+letter` exceptions at all**, and F-keys are reserved for the most
frequent operations. The architecture (`CommandDescriptor` + `CommandResolver`, no `ICommand` in the
registry) was ratified with it, and the double `SearchPanel` (§2.2) was pulled into the sprint.

This document is the sprint's single home: the audit (§1–§6), the verified facts that constrain the
design (§2), the architecture proposal (§7), the shortcut map proposal (§8) and the context-menu plan
(§9). A future session starts any etap from here.

---

## 0. What this sprint is, in one line

Not "add a few shortcuts". Build the **command system** EmberTern's UI reads from — shortcuts,
tooltips, context menus, and later a Command Palette and a shortcut editor — so a command's gesture is
declared **once** and every surface renders the same truth.

---

## 1. Method

Everything below was read out of the code, not recalled. Two claims were settled by **measurement**
because the design depends on them and inherited lore turned out to be wrong (§2.1, §2.2).

Surfaces swept: `Window.KeyBindings`, all XAML `KeyBinding` / `InputGesture`, every `OnKeyDown` /
`KeyDown` handler (30 files), all `[RelayCommand]` (365) and `Click=` handlers (73), every
`ContextMenu` (32) and `MenuItem` (142), all `ToolTip.Tip` (292), the toolbar, the debugger, the SQL
editor controllers, the Metadata tree, and every dialog.

### 1.1 The audit's A-10 is not a design — verified

CLAUDE.md warned that A-10's `CommandRegistry` "sketch" was unverified. It is thinner than that: A-10
is **one table row** in `docs/audits/embertern-full-audit-2026-07-26.md:234` —
*"Skróty są rozproszone; konfigurator będzie kosztowny | `CommandRegistry` z zakresami, priorytetami i
walidacją kolizji."* There is no scope list, no resolver, no `CommandId` shape, no collision validator
described anywhere in that document. The richer sketch summarised in CLAUDE.md is an **expansion
written during the previous sprint's triage, not a finding**. So there is nothing to copy, and §7
below is designed from EmberTern's actual code. Its diagnosis ("scattered", "a configurator will be
expensive") is correct and confirmed by §3–§5.

---

## 2. Measured facts that constrain the design

### 2.1 ⭐ AvaloniaEdit claims **no** function key — measured, and it reverses the expected answer

A throwaway headless probe dumped every `KeyBinding` / `CommandBinding` reachable from a live
`TextEditor` after `EditorSearch.Install` (shared session, gotcha #94/#226; probe removed after the
run — it returns in Etap 2 as a *pinned* test, §7.6).

**What the editor actually claims:**

| Class | Gestures |
|---|---|
| Caret / selection | `←→↑↓`, `Shift+`, `Ctrl+←→`, `Ctrl+Shift+←→`, **`Shift+Alt+`** arrows + `Shift+Alt+Home/End` (box select), `Home`, `End`, `Ctrl+Home`, `Ctrl+End`, `PageUp`, `PageDown` (+`Shift+`) |
| Editing | `Delete`, `Ctrl+Delete`, `Back`, `Shift+Back`, `Ctrl+Back`, `Return`, `Shift+Return` |
| Commands without a gesture | Undo, Redo, Cut, Copy, Paste, SelectAll, ToggleOverstrike, DeleteLine, case conversions, tab/space conversions, IndentSelection |
| Search | `SearchInputHandler` registers **`CommandBindings` only — Find / Replace / FindNext / FindPrevious / ReplaceNext / ReplaceAll / CloseSearchPanel all carry NO `KeyGesture`** |

**Consequences, all load-bearing:**

1. **`F1`–`F12` are entirely free inside the editor.** The AvalonEdit lore that `F3`/`Shift+F3` are
   find-next/previous does **not** hold in AvaloniaEdit 12.0.0 — the commands exist, the gestures do
   not. The user's F-key-first preference is therefore unobstructed by the editor.
2. **`Delete`, `Back`, `Return` are editor-owned**, so a global `F8 = Delete` (§8) is safe but a
   global `Delete = Delete` would not be — scope resolution is mandatory, not decorative.
3. **`Shift+Alt+arrow` is box-selection**, reinforcing the user's "no Alt combos" rule from a second
   direction: Alt is already spoken for inside the editor.

### 2.2 ⭐ Every `TextEditor` carries **two** SearchPanels — measured

| Moment | `SearchInputHandler` instances | `editor.SearchPanel` |
|---|---|---|
| bare `new TextEditor()` | **1** | already non-null |
| after `EditorSearch.Install(editor)` | **2** | still the built-in one |

`AvaloniaEdit.TextEditor` ships its own `SearchPanel`; `EditorSearch.Install` calls
`SearchPanel.Install(editor)` and gets back a **different** instance
(`ReferenceEquals(editor.SearchPanel, returned) == false`). So the panel the context menu's
Find/Replace and `Ctrl+H` drive is **not** the panel `editor.SearchPanel` refers to.

`EditorSearch`'s own docstring — *"Find via Ctrl+F is auto-wired by `SearchPanel.Install`"* — and
`MainWindow.axaml.cs:744-764`'s Ctrl+F router, which deliberately leaves the gesture unhandled inside
an editor so "the editor's SearchPanel opens Find", both rest on there being **one** panel.

**Scope note:** the structural duplication is measured; **which** panel `Ctrl+F` opens was *not*
provable headlessly (the injected `Ctrl+F`/`Ctrl+H` produced no observable state change in either
panel, which is a headless-input limitation, not evidence about the product). Treat this as a
**confirmed duplicate with an unproven user-visible consequence** — one live click settles it. It is
in scope for this sprint only because Find/Replace are commands the registry must describe; the fix,
if the live check confirms a split, is to stop installing a second panel and use `editor.SearchPanel`.

### 2.3 There is **no** `MenuItem` or `ContextMenu` style in the app — measured

`Themes/ControlStyles.axaml` contains **zero** selectors for `MenuItem`, `ContextMenu`, or menu
`Separator`. All 142 menu items sit on FluentTheme defaults — which is precisely the screenshot the
user sent: tall rows, large type, no icon column, no gesture column. This mirrors the already-known
gap for `TextBox`/`ComboBox`/`Button` (the backlogged density sprint), but **context menus are this
sprint's, by the user's own line**: menu typography and icon layout here, global control heights
there.

### 2.4 `MenuItem.InputGesture` is already used — three times

`TableDetailTabView.axaml:127/131/135` set `InputGesture="Insert" / "F2" / "Delete"` on the fields
grid's Add/Edit/Drop items. **These are the only 3 of 142 menu items that show a shortcut.** So
Avalonia's built-in gesture-display slot exists and is already the local precedent — whether
FluentTheme's template renders it *well enough* is an Etap-5 verification (§9.2), not an assumption.

### 2.5 The command surface is far too large to enumerate

**365 `[RelayCommand]` + 73 `Click=` handlers = ~438 command-shaped things.** `MetadataNodeViewModel`
alone declares 15, and it is instantiated **per tree node**. This kills the naive
"registry holds `ICommand`s" model before it starts (§7.2).

---

## 3. Inventory — every gesture that exists today

### 3.1 Window scope (`MainWindow`)

| Gesture | Action | Declared in |
|---|---|---|
| `F5` | **Go** — debugger tab ⇒ the debugger decides; **anything else ⇒ Execute Query** | `MainWindow.axaml:33` |
| `Ctrl+Enter` | Execute Query | `MainWindow.axaml:34` |
| `Shift+F5` | Execute Query (Full) | `MainWindow.axaml:35` |
| `Alt+F` | Format SQL (the SQL editor's) | `MainWindow.axaml:36` |
| `Ctrl+Shift+F` | Global Search | `MainWindow.axaml.cs:751` |
| `Ctrl+F` | focus the sidebar filter — **unless focus is inside a `TextEditor`** | `MainWindow.axaml.cs:760` |
| `Escape` | (sidebar filter box) clear filter, focus tree | `MainWindow.axaml.cs:736` |

### 3.2 SQL/PSQL editor scope (controllers attached by the one `SqlEditorBehavior.Attach`)

| Gesture | Action | Owner |
|---|---|---|
| `Ctrl+Space` | completion | `SqlCompletionController:314` |
| `Ctrl+Shift+Space` | parameter helper | `SqlCompletionController:316` |
| `Escape` | dismiss popup | `SqlCompletionController:339` |
| `Tab` | Language Completion expansion (tunnelled, #224) | `LanguageExpansionController:111` |
| `Escape` | dismiss the expansion hint | `LanguageExpansionController:115` |
| `Enter`, `Backspace` | typing ergonomics (block/delimiter pairing) | `TypingErgonomicsController:47` |
| `F2` | safe local rename | `NavigationController:524` |
| `Alt+F12` | Peek Definition | `NavigationController:528` |
| `Ctrl+.` | Quick Fix / code actions | `NavigationController:532` |
| `Ctrl+Click`, `Ctrl+hover` | navigate / affordance | `NavigationController:310/434` |
| `F8` / `Shift+F8` | next / previous diagnostic | `DiagnosticsPanelHost:126` |
| `Ctrl+H` | Replace | `EditorSearch:41` |
| `Ctrl+F` | Find | AvaloniaEdit built-in |
| *(plus everything in §2.1)* | | AvaloniaEdit |

### 3.3 Debugger tab scope (`DebuggerTabView.axaml.cs:458-478`)

`F5` Continue · `Shift+F5` Stop · `Ctrl+Shift+F5` Restart · `F10` Step Over · `Ctrl+F10` Run To Cursor ·
`F11` Step Into · `Shift+F11` Step Out · `F9` Toggle breakpoint · `Shift+F9` Evaluate selection ·
`Ctrl+S` Save source · `Ctrl+Alt+↑`/`Ctrl+Alt+↓` move frame selection.
Plus `Enter` in the Immediate / Watches / breakpoint-condition boxes (`DebuggerTabView.axaml:538-673`).

### 3.4 Other tab scopes

| Scope | Gestures |
|---|---|
| Table Detail — fields grid | `Insert` Add · `F2` Edit · `Delete` Drop (`DataGrid.KeyBindings`) |
| Table Detail — dependencies grid | `Enter` navigate |
| Data Import | `F5` Import · `Ctrl+F5` Validate · `Escape` Cancel run · `Ctrl+O` Browse · `Ctrl+V` / `Ctrl+R` Refresh |
| Script Executor | `F5` Run — **only while the script editor has focus** |
| Global Search tab | `F3` / `Shift+F3` next / previous match in the preview |
| Diagnostics panel | `Enter` activate row · `F8` / `Shift+F8` next / previous |
| Procedure / Function / Trigger / View / Package editors | `Alt+F` Format SQL (that editor's) |
| Dialogs | `Escape` close (×4) · `Enter` commit (rename boxes, `TableColumnPicker`, `SearchableComboBox`) |

### 3.5 Menus and tooltips

* **32 context menus** (31 in XAML + 1 built in C# — `EditorSearch.BuildContextMenu`), **142 items**.
* **3 items** show a shortcut (§2.4). The remaining **139 show none** — including the editor menu's
  Undo/Redo/Cut/Copy/Paste/Select All/Find/Replace, whose gestures every user already knows.
* **No context menu anywhere has an icon.**
* **292 tooltips**; **~24** embed a gesture as literal text inside a `UiStrings` constant
  (`"Continue · F5"`, `"Format SQL · Alt+F"`, `"Run … · F5"`, …). This is the "one source" the user
  asked for and it does not exist: the gesture is typed by hand next to the label.

---

## 4. Duplication

| # | What | Where | Severity |
|---|---|---|---|
| **D1** | **`Alt+F` declared 6×** — one window binding + five per-view `OnKeyDown` handlers, all meaning "format the editor I'm looking at" | `MainWindow.axaml:36` + Procedure/Function/Trigger/View/Package `.axaml.cs` | high — 6 implementations of one command |
| **D2** | **`F5` declared 5×** with 4 different meanings | window, debugger, Script Executor, Data Import | high — see C1 |
| **D3** | **`F8`/`Shift+F8` declared 2×** for the same action | `DiagnosticsPanelHost:126`, `DiagnosticsPanelView.axaml.cs:65` | low — deliberate (editor vs panel focus) |
| **D4** | **Two `SearchPanel`s per editor** | §2.2 | medium — measured; consequence unproven |
| **D5** | **`Ctrl+F` double-owned**, arbitrated by a focus probe rather than a declared scope | `MainWindow.axaml.cs:760` + `EditorSearch.IsInsideEditor` | medium — correct today, fragile |
| **D6** | Collection add/remove/move labels reused as menu headers (`CollectionAddTooltip` as a `MenuItem.Header`) | `NewTableTabView.axaml:73`, `TriggerDetailTabView.axaml:111` | low — a tooltip string doing a label's job |

---

## 5. Collisions

### ⭐ C1 — `F5` leaks Execute Query into tabs that have nothing to execute (confirmed in code)

`GoCommand` is `[RelayCommand]` with **no `CanExecute`** (`MainWindowViewModel.cs:5955`). It routes to
the debugger only when the *selected* tab is a debugger tab, and **otherwise calls
`ExecuteQueryAsync()` unconditionally**. `ExecuteQueryAsync`'s gate is `CanExecute => !IsExecuting`
(`:1069`), and `ResolveActiveSql()` (`:3136`) falls back to **`QueryText`** — the SQL editor's text —
with no reference to which tab is active.

Meanwhile the per-tab `F5` handlers mark the event handled **only when their own command can run**
(Data Import `:266-273`, Script Executor `:135`). So:

* On **Table Detail, Security Manager, Trace Monitor, Session Manager, Global Search, New Table and
  all 10 object editors** — no `F5` handler exists at all — `F5` **always** executes the SQL editor's
  text.
* On **Script Executor** with focus outside its editor, and on **Data Import** whenever Import is
  unavailable, `F5` **falls through** to the same place.

The SQL editor's statement runs on the Data lane **inside the user's one working transaction**, so if
`QueryText` holds a `DELETE`/`UPDATE`, an `F5` meant for the tab in front of the user runs it. The
code comment at `:5947-5954` shows this is *intentional as written* ("anything else → Execute Query,
exactly as F5 always behaved") — so this is a **design defect, not an oversight**, and it is the
clearest possible argument for scope resolution. **Recommend confirming with one live keypress** before
the fix lands, then fixing it in Etap 2 as the first thing the registry earns.

### C2 — the user's proposed `F8 = Delete` collides with shipped diagnostics navigation

`F8`/`Shift+F8` are next/previous diagnostic (Stage 7 / S5), wired once in `DiagnosticsPanelHost.Track`.

**Recommendation: keep both, split by scope.** "Delete the selected object/row" is meaningless inside
a code editor, and "next diagnostic" is meaningless in the Metadata tree. So `F8` = Delete in
tree/grid scopes, `F8` = next diagnostic in editor scope. This is exactly what a scope resolver is
for, and neither shipped behaviour changes. *Needs the user's decision — the alternative is moving
diagnostics navigation to `F4`/`Shift+F4`.*

### C3 — the user's proposed `F3 = New/Add` versus `F3 = Find Next`

`F3` = find-next is a strong Windows standard, and the user's own rule says leave standards alone.
Three measured facts decide how much that costs here:

1. AvaloniaEdit binds **no** `F3` (§2.1) — `F3` is *not* find-next in EmberTern's editors today.
2. The Find bar has its own next/previous buttons and `Enter`.
3. The only `F3` in the app is **next match in the Global Search tab's preview**
   (`GlobalSearchTabView.axaml.cs:83`) — a leaf scope that needs no "New".

**Recommendation: `F3` = New/Add as the user prefers**, with the Global Search preview keeping `F3`
in its own scope (same shape as C2). *Flagging it rather than deciding it, because it trades a
Windows standard for an EmberTern convention and that is the user's call.*

### C4 — `F5` = Execute conflicts with the Windows standard `F5` = Refresh

Already resolved in EmberTern's favour and correctly: `F5` = Execute is the **SQL-tool** standard
(SSMS, IBExpert), and the user has ratified it. Consequence: **Refresh needs a different key** — it
cannot be `F5`. (The `Refresh  F5` line in the sprint brief's tooltip example was formatting
illustration, not a binding decision — confirming that reading.)

### C5 — `F2` has two meanings

Rename symbol (editor) vs Edit field (Table Detail grid). Disjoint focus, so harmless today — but
nothing *declares* the split, so the next `F2` consumer will not know it exists.

### C6 — `Alt+letter` and the Polish keyboard

`Alt+F` is the app's **only** `Alt+letter` binding, and it must go under the user's rule. Precision
worth recording: on Windows `AltGr` reports as `Ctrl+Alt`, and the Polish-programmers diacritics are
`AltGr` + `a c e l n o s x z` — so `Alt+F` is not itself stealing a letter. The rule is still the
right policy (it removes a whole class of future risk and `Ctrl+Alt+…` is unusable for the same
reason), and this sprint honours it. `Alt+F12` (Peek Definition) is `Alt`+function-key, not
`Alt`+letter — **out of the rule's scope**, and it matches Visual Studio; recommend keeping it.

---

## 6. Frequently used, no shortcut at all

Ordered by how often an ERP/Firebird developer touches them. Every one is toolbar- and/or
context-menu-only today.

| # | Command | Reachable today only via |
|---|---|---|
| **1** | **Compile** (10 object editors + New Table) | toolbar button / PPM |
| **2** | **Commit** / **Rollback** | toolbar |
| **3** | **Revert / Discard changes** | toolbar |
| **4** | **Comment / Uncomment selection** | toolbar (4 editors) + editor PPM |
| **5** | **Refresh metadata** | connection PPM |
| **6** | **Close tab** (`CloseActiveTabCommand`) | toolbar / tab ✕ |
| **7** | **New Query** | toolbar |
| **8** | **New \<object\>** (table/view/procedure/trigger/function/generator/domain/package/exception) | toolbar / tree PPM |
| **9** | **Delete object** | tree PPM |
| **10** | **Execute Procedure** / **Debug** | toolbar / tree PPM |
| **11** | **Export results** / **Copy as INSERT/UPDATE** | grid PPM |
| **12** | Tool tabs: Script Executor, Data Import, Security, Trace, Sessions | toolbar |
| **13** | Next / previous document tab | nothing |
| **14** | Focus the grid filter | nothing |
| **15** | Recompile dependents / Recompute statistics | PPM |

`Ctrl+S` exists in exactly one place in the whole application — the debugger's Save.

---

## 7. Architecture proposal (Etap 2)

### 7.1 The shape the code forces

Three measured facts drive every decision below: ~438 command-shaped members (§2.5), commands living
on **per-instance** VMs (`MetadataNodeViewModel` per tree node), and ~24 gestures already hand-typed
into `UiStrings` (§3.5).

### 7.2 ⭐ The registry holds **descriptions**, never `ICommand` instances

The obvious sketch — a registry mapping gesture → `ICommand` — cannot work here: the command that
"Delete object" must invoke belongs to whichever `MetadataNodeViewModel` is selected *right now*, and
that object did not exist when the registry was built. So:

* **`CommandDescriptor`** (Core-free, App-layer, immutable): `CommandId`, label, icon key, default
  gesture(s), `CommandScope`, and an optional short description for the future palette.
* **`CommandCatalog`**: one **static, declarative table** built **once**, at type-init — the exact
  shape `LanguageConstructCatalog` already uses in this codebase (reuse the established pattern, not
  a new one). No reflection, no scanning, no per-menu-open work — this satisfies the user's
  performance requirement structurally.
* **`CommandResolver`**: given the live UI context (focused element → active tab → window), returns
  the `ICommand` **instance** for a `CommandId`, or null when unavailable in this context. This is the
  only piece that touches live VMs, and it is where C1's leak dies.

**Only curated, user-facing commands get a `CommandId`.** The other ~400 stay ordinary
`[RelayCommand]`s. Opt-in, not exhaustive — anything else is a 438-row table nobody maintains.

### 7.3 Scopes and resolution

Proposed scopes, taken from the tab kinds and focus surfaces that actually exist:
`Global` · `Editor` (any `TextEditor`) · `Tree` (Metadata explorer) · `Grid` (the 5 data grids) ·
`Tab` (the active workspace tab kind) · `Dialog`.

Resolution order **most specific first**: `Dialog → Editor/Tree/Grid (by focus) → Tab → Global`. A
command unavailable in the current context is **not invoked and the gesture is not swallowed** —
which is exactly the rule C1 violates today.

### 7.4 Tooltips from one source (Etap 4)

The gesture must never be typed into a string again. Proposal: a markup extension reading the
catalog —

```xml
ToolTip.Tip="{app:CommandTip Compile}"   <!-- renders: "Compile · F7" -->
```

keeping the established `Action · Key` convention from UX Polish Seam 1, and letting the ~24
gesture-carrying `UiStrings` constants lose their hand-typed suffix. Labels stay in `UiStrings`
(architecture rule #6); only the **gesture** comes from the catalog.

### 7.5 What this sprint does **not** build

No Command Palette, no shortcut configurator, no user-remappable gestures, no persisted keymap. The
catalog is designed so each is additive later — that is the whole point of a descriptor table — but
none is in scope.

### 7.6 The gesture inventory becomes a pinned test

§2.1's throwaway probe returns in Etap 2 as a real test asserting that AvaloniaEdit claims **no**
`F1`–`F12` and that the registry's reserved-gesture set does not intersect the editor's. Without it, an
AvaloniaEdit upgrade could claim a function key and silently break a global shortcut — with a green
build.

---

## 8. Shortcut map proposal (Etaps 2–3) — **needs the user's decision**

Honouring: F-keys over modifier stacks · no `Alt+letter` · `F3` New, `F8` Delete, `F5` Execute ·
`F5` stays Continue in the debugger (the one ratified contradiction) · leave real standards alone.

### 8.1 Function keys

| Key | Proposed | Scope | Note |
|---|---|---|---|
| `F2` | Rename | Editor / Tree / Grid | already Rename+Edit — formalise, don't change |
| `F3` | **New / Add** | Global | C3 — the user's preference; Global Search preview keeps `F3` locally |
| `Shift+F3` | — | | left free (was find-previous by convention) |
| `F4` | **Refresh** | Tab / Tree | C4 forces Refresh off `F5`; `F4` is entirely free |
| `F5` | Execute / Go | Global | unchanged — but **scoped** (C1) |
| `Shift+F5` | Execute (Full) | Editor | unchanged |
| `F6` | **Commit** *(?)* | Global | see the open question below |
| `Shift+F6` | **Rollback** *(?)* | Global | " |
| `F7` | **Compile** | Tab | free today; the app's #1 missing shortcut |
| `F8` | **Delete** | Tree / Grid | C2 — editor scope keeps next-diagnostic |
| `F8` / `Shift+F8` | next / previous diagnostic | Editor | unchanged |
| `F9` | Toggle breakpoint | Debugger | unchanged |
| `F10`, `F11` | stepping | Debugger | unchanged (VS standard) |
| `F12` | Go to Definition | Editor | free today; `Alt+F12` Peek already exists — completes the pair |

### 8.2 Standards kept as-is (the user's "don't force it" rule)

`Ctrl+S` Save/Compile (as an **alias** for `F7`, and already the debugger's) · `Ctrl+F` Find ·
`Ctrl+H` Replace · `Ctrl+Z`/`Y`/`X`/`C`/`V`/`A` · `Ctrl+Enter` Execute · `Ctrl+Space` completion ·
`Ctrl+.` Quick Fix · `Ctrl+Shift+F` Global Search · `Escape` dismiss · `Insert`/`Delete` inside the
fields grid.

### 8.3 New non-F-key gestures for §6's gaps

`Ctrl+W` Close tab · `Ctrl+Tab` / `Ctrl+Shift+Tab` next / previous document tab ·
`Ctrl+/` Toggle comment (the cross-IDE standard; replaces the missing Comment/Uncomment pair) ·
`Ctrl+N` New Query.

### 8.4 Removed

`Alt+F` Format SQL → **`Ctrl+Shift+F`?** is taken by Global Search. Proposal: **`Ctrl+Alt+F` is
forbidden** (AltGr, C6), so Format SQL moves to **`Ctrl+K`** or keeps a bare F-key. *Open — see below.*

### 8.5 Open questions for the user

1. **`F6` = Commit / `Shift+F6` = Rollback** — a bare F-key commits the working transaction. Fast and
   consistent with the F-key preference; also one keystroke from an irreversible act. Alternative:
   leave Commit/Rollback gesture-free (toolbar only), or require `Ctrl+F6`. **My recommendation:
   `F6`/`Shift+F6`, because Commit is not destructive** (it is the *intended* end of the user's work,
   and Rollback is the reversible one) — but this is a product-safety call and yours to make.
2. **Format SQL's new gesture** (§8.4) — `Ctrl+K`, or a spare F-key, or keep `Alt+F` as the single
   sanctioned exception?
3. **C2** — scope-split `F8`, or move diagnostics navigation to `F4`/`Shift+F4`? (If diagnostics move
   to `F4`, Refresh needs yet another key.)
4. **C3** — confirm `F3` = New despite `F3` = find-next being a Windows standard.

---

## 9. Context menus (Etap 5)

### 9.1 Target

The user's reference is Visual Studio's polish, not its identity: smaller type, **icons left**,
**gestures right**, real separators, consistent spacing and alignment — across **all 32 menus**.

### 9.2 Style first, control only if the style provably can't

Project rule is reuse-before-create, and a custom control means re-templating **142 items**. The order
is therefore: **(a)** add `MenuItem` / `ContextMenu` / `Separator` styles to
`Themes/ControlStyles.axaml` (the file that today has none, §2.3) and see whether FluentTheme's
template gives an icon slot, a gesture slot (`InputGesture`, §2.4) and controllable density; **(b)**
only if it provably cannot, build one `EmberTernContextMenu` — and then, per the user's explicit
instruction, **the whole application uses it and nothing else.**

This is a **verification step, not a preference**: it will be measured in Etap 5 against a real menu
in both themes before a line of a custom control is written.

### 9.3 Icons

Reuse first — **88 geometries** already exist in `Themes/IconGeometries.axaml`, and most menu actions
already have one (`Icon.Plus`, `Trash`, `Pencil`, `Copy`, `Play`, `Hammer`, `RefreshCw`, `Save`,
`Undo`, `Download`, `Filter`, `Comment`, `Uncomment`, the per-kind metadata icons, the whole debugger
set). Anything genuinely missing is drawn in the existing Lucide-derived stroke style, canonical
`.svg` under `Assets/Icons/` + mirrored into `IconGeometries.axaml` (the D15.2 rule). **No second icon
style.**

### 9.4 Gestures in menus come from the catalog

Not hand-typed. `MenuItem` gains its gesture from the `CommandId`, the same source as the tooltip —
so `139` items that show nothing today start showing the truth, and cannot drift from it.

---

## 10. Etap order

| Etap | Content | State |
|---|---|---|
| **1** | Audit (§1–§6) | ✅ **DONE + accepted** |
| **2** | `CommandDescriptor` / `CommandCatalog` / `CommandRouter` + scope resolution + the §7.6 pinned test + **fix C1** + the `SearchPanel` duplication | ✅ **DONE** — §11 |
| **3** | The ratified new gestures through the catalog; retire `Alt+F`'s copies (D1); add `Tree` + `Grid` scopes | ✅ **DONE** — §12 |
| **4** | Tooltips + chips read the catalog through `CommandTip`; every hand-typed gesture stripped; guarded by a test | ✅ **DONE** — §14 |
| **5** | Context menus: styles (§9.2 (a)), then a control only if measured necessary; icons; gestures; all 32 menus | |

One etap per session, each ending build 0/0 + tests green + smoke clean + committable, per the
project's session protocol.

---

## 11. Etap 2 — as built

`src/EmberTern.App/Commands/` — `CommandScope`, `CommandId`, `CommandDescriptor` (+ `CommandDispatch`),
`CommandCatalog`, `CommandRouter`.

### 11.1 The shape, and the two decisions that produced it

**The registry describes; it never holds an `ICommand`.** Forced by the code, not chosen for elegance:
"Go" must invoke whichever tab is selected *now*, and the Object Explorer's commands belong to whichever
`MetadataNodeViewModel` is selected — 15 commands on an object built per tree node. None of those exist
when a static table is built. So `CommandCatalog` is a literal array built once at type-init (the
`LanguageConstructCatalog` pattern this codebase already uses — no reflection, no scanning, nothing
recomputed when a menu opens), and the instance is resolved at invoke time.

**Resolution lives where the knowledge already lives.** `WorkspaceTabViewModel.ResolveCommand(CommandId)`
joined `UnsavedWork` / `SavableEditor` / `RefreshAsync` as the **fourth member of the existing per-kind
family** rather than becoming a new mechanism beside them. `MainWindowViewModel.ResolveCommand` answers
for `Global`. The router does only what needs Avalonia: the focus probe.

**⭐ That split keeps `KeyGesture` out of the view models entirely.** A view model answers questions about
a `CommandId`; gestures belong to the catalog and the view layer. This is the line to hold in etaps 3–5.

### 11.2 Scope resolution

`CommandScope`'s numeric values **are** the specificity order (`Editor` 2 > `Tab` 1 > `Global` 0) — the
router walks candidates from the highest down. For each it asks whether the scope is *live* (caret in an
editor / this tab kind declared), then:

* **live + `Routed`** → resolve and invoke; handled.
* **live + `Reserved`** → stop, *unhandled*. The owner has the claim, and no broader scope may answer for
  it. This is how the editor's typing mechanics (#224/#228) and the debugger's stepping surface stay local
  while still being *declared*, so nothing global can quietly steal one of their keys.
* **live but unavailable** (no command on this tab kind, `CanExecute` false) → fall through to the next,
  less specific candidate.
* nothing resolves → the key is left alone.

The router listens on **Bubble**, so a control that owns a keystroke still sees it first — exactly how the
`Window.KeyBindings` block behaved. The deleted handler was on **Tunnel**, which is precisely why it had to
probe the focus to hand `Ctrl+F` back to the editor.

### 11.3 What was deleted

| Gone | Replaced by |
|---|---|
| `MainWindow.axaml`'s whole `Window.KeyBindings` block (4 gestures) | catalog + router |
| `MainWindow.OnWindowKeyDown` (the Tunnel handler + its `IsInsideEditor` probe) | `Editor` outranking `Global` |
| `MainWindowViewModel.GoCommand` / `GoAsync` — whose last line was the C1 defect | `CommandId.Go` + per-kind resolution |
| `DebuggerTabViewModel.RequestGoAsync` | its own `GoCommand`, with a real `CanGo` gate |
| `DebuggerTabView`'s `case Key.F5` (the second owner of F5) | `CommandId.Go` |
| `ScriptExecutorTabView.OnScriptEditorKeyDown` (F5 only while the script editor had focus) | `CommandId.Go` at tab scope |
| Data Import's local `F5` / `Ctrl+F5` / `Ctrl+O` / `Ctrl+R` cases | `CommandId.Go` / `ImportValidate` / `ImportBrowse` / `ImportRefresh` |
| `EditorSearch`'s `SearchPanel.Install` call + its `Ctrl+H` `AddHandler` | the editor's own panel + `CommandId.EditorFind`/`EditorReplace` |
| `EditorSearch.IsInsideEditor` | `EditorSearch.EditorFor` (returns the editor the router needs) |

### 11.4 ⭐ C1 fixed, and proven twice

The defect was that `Go` *interpreted* F5 and ended "anything else → Execute Query". Now F5's reach is a
**declaration**: `CommandId.Go` names four tab kinds, so the remaining 16 — every object editor, Security
Manager, Trace, Sessions, Global Search, Table Detail — are structurally unable to see it.

Pinned at both levels, deliberately:

* `CommandCatalogTests.Go_IsDeclaredOnlyForTabsThatHaveAMainAction` — the declaration, exhaustively.
* `CommandCatalogTests.ResolveCommand_MapsGoOnAQueryTab_AndNowhereOnADdlTab` — the per-kind switch agrees
  with the declaration, so the two halves cannot drift.
* `ConnectionExpandBindingProbe.CommandRouter_ResolvesByScope_AndDeclinesWhereNothingIsLive` — the **real
  router** with a real focus probe: F5 handled on a Query tab, **declined** on a Ddl tab, and `Ctrl+F`
  going to the sidebar outside an editor but to the Find bar inside one, with the sidebar untouched.

Two side effects worth stating, both improvements: **Script Executor's F5 now works anywhere in the tab**
(it used to require focus in the script editor, and otherwise executed the SQL editor's query instead), and
`F5` on a debugger tab in a non-actionable phase now leaves the key alone instead of silently doing nothing.

### 11.5 The `SearchPanel` duplication

`EditorSearch.Install` no longer calls `SearchPanel.Install`; everything goes through
`TextEditor.SearchPanel`, the panel the editor creates itself. `Ctrl+F` and the context menu's
Find/Replace therefore drive one instance. `OpenReplace` now also **refuses a read-only editor** — a DDL
preview must not be offered a mutation. Pinned by asserting the **handler count** (1 before Install, 1
after): asserting the panel is non-null would have passed before the fix too.

### 11.6 §7.6 — the measured fact is now a permanent guard

`ConnectionExpandBindingProbe.Editor_ClaimsNoFunctionKey_AndClaimsTheEditingKeys` walks every
`KeyGesture` reachable from a live editor (39 of them) and asserts **none is `F1`–`F12`**, while asserting
that `Delete` / `Back` / `Return` *are* claimed. The risk it guards is silent: an AvaloniaEdit upgrade that
began binding a function key would break a global shortcut with the build still green.

### 11.7 Deliberately still local, with the reason

* **`Escape`** — a universal dismiss owned by every popup, dialog and filter box. Declaring it would invent
  collisions with all of them.
* **Data Import's `Ctrl+V`** — it means "re-read the clipboard source", i.e. paste semantics, and must yield
  to a focused text box via a source check. `Ctrl+R` (the same Refresh without the paste overlap) *is*
  routed, so the command is in the catalog; only that one gesture stayed behind.
* **The editor's typing mechanics** and **the debugger's stepping keys** — declared `Reserved`, dispatched
  locally. Several debugger keys (Run To Cursor, Toggle Breakpoint) are *view* actions needing the source
  editor's caret, with no view-model command to route to.

### 11.8 Not yet in the descriptor, on purpose

`Label` and `IconKey` are absent until etaps 4 and 5 consume them. A descriptor field that nothing reads
looks like working infrastructure and is not (gotcha #233); adding them later is additive.

---

## 12. Etap 3 — as built

Build 0/0; suite **5929 green** in the two partitions (5886 + 43); smoke clean.

### 12.1 The map, as shipped

| Gesture | Command | Scope | Resolves to |
|---|---|---|---|
| `F3` | `NewObject` | Tree | the selected category's own `NewCommand` |
| `F3` | `CollectionAdd` | Grid | the unified collection router's `+` |
| `F4` | `RefreshMetadata` | Tree | the selected connection's `RefreshMetadataCommand` |
| `F5` | `Go` | Tab | *(etap 2)* the tab's main action |
| `F6` | `Commit` | Global | `CommitAllCommand` — the toolbar button's own command |
| `Shift+F6` | `Rollback` | Global | `RollbackAllCommand` |
| `F7` | `Compile` | Tab (11 kinds) | each editor's own `CompileCommand` |
| `F8` | `DeleteObject` | Tree | the leaf's own `DeleteCommand` (**confirmed**, see 12.3) |
| `F8` | `CollectionRemove` | Grid | the unified collection router's `−` |
| `F8` | `EditorNextDiagnostic` | Editor | *(unchanged)* — the ratified scope split |
| `Ctrl+K` | `FormatSql` | Tab (6 kinds) | each tab's own `FormatSqlCommand` |
| `Ctrl+W` | `CloseTab` | Global | `CloseActiveTabCommand` (the confirming close) |

Nothing else was added. `Ctrl+N` (New Query) from the audit's §8.3 was **not** implemented — it was never
ratified, and the rule was "only the ratified gestures".

### 12.2 ⭐ Two scopes were enough, and one was avoidable

`Tree` and `Grid` joined `Editor` as focus scopes. `CommandScope`'s numeric values remain the resolution
order; the three focus scopes sit above `Tab` and are ordered innermost-first only as a safety net for
nesting, since the caret is in at most one of them.

**Grid scope needed no per-grid knowledge**, which is the part worth remembering: `F3`/`F8` route through
the application's **existing** unified collection router (`AddCollectionItemCommand` /
`RemoveCollectionItemCommand`), whose `ActiveCollection()` already answers "which collection is the user
editing" and returns null when there is none. So its `CanExecute` does all the gating — no tab-kind list, no
grid registry, and the Table Data grid or the SQL result grid simply decline.

**Tree scope needed one small addition**: `MetadataExplorerViewModel.SelectedNode`, fed by the sidebar's
existing selection handler exactly as `SelectedConnection` and `SetSelectedTriggers` already are. It is
deliberately **not** observable — nothing binds to it, `ResolveCommand` reads it when a key is pressed, and
notifying on every arrow-key move through a long tree would be pure noise.

**⚠ A design trap avoided.** The first shape had `CollectionAdd`/`CollectionRemove` at *Global* scope, since
their commands live on `MainWindowViewModel` and self-gate. It is subtly wrong: with a table leaf selected in
the tree (no `New`) and a Procedure editor open, `F3` would fall through from Tree to Global and add a
**parameter row to the background tab**. Grid scope removes the fall-through entirely, because the scope
simply is not live while the caret is in the tree.

### 12.3 Nothing destructive became a one-keystroke action

* **`F8` on a tree leaf** routes to `MetadataNodeViewModel.DeleteCommand`, which raises the **existing
  confirmation dialog** — verified in the code, not assumed. F8 opens a question; it never drops an object.
* **`F6`/`Shift+F6`** bind the very commands the toolbar buttons bind, gated by the same
  `CanCommitAll`/`CanRollbackAll`, so a key can never settle a transaction the button refuses.
* **`Ctrl+W`** routes through the confirming close, so a tab with unsaved work still offers
  Save / Discard / Cancel.
* **`F7`** compiles without a prompt — which is intended, and the DDL change-safety gate from the previous
  sprint stands between it and an overwrite.
* **`F8` in a grid** removes a row from an *uncompiled buffer*; Revert undoes it, and it is the same command
  the toolbar's `−` runs.

### 12.4 `Alt+F` is gone — four of the six copies with it

| Was | Now |
|---|---|
| window `KeyBindings` `Alt+F` | *(deleted in etap 2)* |
| Trigger / View / Package local `Alt+F` handlers | **deleted** — `CommandId.FormatSql` covers them |
| Procedure / Function local `Alt+F` handlers | **narrowed to `Ctrl+K` on two sub-editors only** — see below |

**⚠ The one place etap 3 could not fully centralise, and the reason is structural.** In Easy mode the
Procedure and Function editors format the **cursor** and **subprogram** grid-row editors *in place*, and that
action is identified by a specific `TextEditor` **instance**. The router resolves *commands*, not controls, so
it has nothing to route to. Those two handlers therefore survive, rebound to `Ctrl+K` and **narrowed to
handle the key only for those two editors** — everything else in the tab falls through to the catalog. The
alternative was to delete a working behaviour, which is not a refactor's call to make.

### 12.5 Deliberately undeclared, added to etap 2's list

* **The Global Search preview's `F3`/`Shift+F3`** (next/previous match). Verified: the preview is a
  `TextEditor` holding the key on the **tunnel** phase, so it never reaches the router, and `F3` has no
  Editor-scope claim — the audit's C3 resolution holds exactly as designed. Declaring it as Editor-scope
  `Reserved` would have been *worse*: it would claim `F3` in every editor to describe one preview.

### 12.6 What the tests pin

`CommandCatalogTests` grew to 27 tests. The load-bearing ones:

* **`RatifiedGesture_IsTheDeclaredOne`** — a table of all 11 ratified bindings, so a silent re-binding fails
  here rather than in someone's muscle memory.
* **`NoCommandUsesAltPlusALetter`** — the rule, enforced rather than remembered.
* **`F8_MeansDeleteInTreesAndLists_ButNextDiagnosticInCode`** — the three-way scope split, in order.
* **`TreeCommands_ResolveAgainstTheSelectedNode`** — group offers New and not Delete, leaf the reverse,
  nothing selected offers neither, and Delete is the node's **own** (confirming) command.
* **`Compile_AndFormatSql_ResolveOnTheEditorTabs`** — declared reach (11 / 6 kinds) and actual resolution
  agree, so the descriptor and the per-kind switch cannot drift.
* `CommandRouter_ResolvesByScope_AndDeclinesWhereNothingIsLive` gained cases [6]–[11], all asserting the
  router **declines**: outside a tree/grid, in the tree with nothing selected, in a grid on a tab with no
  collection, F7 on a non-compilable tab, Ctrl+W on the non-closable console tab, F6 with no transaction.
  Deliberately about declining — a test must not need to run a real New / Delete / Compile / Close to prove
  routing, and the mappings are asserted without a UI in the pure tests.

---

## 13. ⚠ Collisions with Windows / IDE standards — REPORTED, not silently resolved

The user's standing instruction for etap 3 was to surface any clash with an established Windows or IDE
convention rather than decide it quietly. None of these blocked the work; all are shipped as ratified, and
each is here so the decision is an informed one.

| # | Gesture | The standard it touches | Assessment |
|---|---|---|---|
| **K1** | **`F3` = New** | **`F3` = Find Next** is close to universal — Explorer, browsers, and most editors. The strongest clash in the set. | Already raised as audit C3 and ratified. Measured mitigation: AvaloniaEdit binds no `F3` (§2.1), so find-next was never `F3` in EmberTern's editors; the Find bar uses its own buttons and `Enter`. The only surviving `F3` is the Global Search preview's next-match, which keeps it locally (§12.5). **Cost is real but small: a user reflexively pressing `F3` to repeat a search now creates an object instead — mitigated by `F3` being live only in the tree and grids, never in a text editor.** |
| **K2** | **`F6` = Commit, `Shift+F6` = Rollback** | In Visual Studio `F6` = **next pane** and `Shift+F6` = **Build current project**. In browsers `F6` = focus the address bar. | EmberTern has no split panes, and "build" is now `F7` (Compile), so nothing in the app competes. The residual risk is a VS user's reflex: `Shift+F6` meaning "build" would instead **roll back the working transaction**. That is the one pairing in this table worth a second thought, because the two actions are not equally recoverable. |
| **K3** | **`F7` = Compile** | Visual Studio: `F7` = **View Code** (build is `Ctrl+Shift+B`). **Delphi / Borland: `F7` = Trace Into, `F8` = Step Over**, and compile is `Ctrl+F9`. | No in-app conflict: EmberTern's debugger uses the VS stepping convention (`F10`/`F11`), so `F7`/`F8` are free. But the audience is ERP developers, many of them Delphi-trained, for whom `F7`/`F8` are *stepping* keys. Worth knowing that both of this sprint's new F-keys land on Delphi's debugger pair. |
| **K4** | **`F8` = Delete** | Visual Studio: `F8` = **Next Error / next result** — which is exactly what EmberTern's editor already uses it for. | **This one is a match, not a clash**, and it is why the scope split was the right call: the VS meaning survives precisely where a VS user expects it (in code), and `F8` means Delete only where VS has no meaning for it. |
| **K5** | **`Ctrl+K` = Format SQL** | `Ctrl+K` is a **chord prefix** in both Visual Studio and VS Code (`Ctrl+K, Ctrl+D` = format document; `Ctrl+K, Ctrl+C` = comment). VS Code's format-document is `Shift+Alt+F` / `Ctrl+Shift+I`. | EmberTern has no chords, so a bare `Ctrl+K` is unambiguous *here*. The clash is with muscle memory: a VS user pressing `Ctrl+K` intending to follow it with `Ctrl+D` gets an immediate format. That happens to be the intended action anyway, so the failure mode is benign — it simply fires one keystroke earlier than expected. |
| **K6** | **`F4` = Refresh** | Windows: `F4` opens the address-bar dropdown. Visual Studio: `F4` = **Properties window**. Some DB tools: `F4` = describe object. Refresh is normally `F5` — taken here by Execute. | No single dominant standard is violated, and `F5` was unavailable by ratified design. The weakest clash in the table. |
| **K7** | **`Ctrl+W` = Close tab** | The browser/editor standard. Visual Studio uses `Ctrl+F4` and leaves `Ctrl+W` effectively free. | A match. Noted only for completeness. |

### 13.1 One scope narrowing worth confirming

**`F4` (Refresh) is Tree-scoped only.** It refreshes the object tree, and only while the caret is in the
Object Explorer. Deliberate: a full refresh re-projects the whole tree and scrolls it to top (a documented,
accepted trade-off), so firing it while the user is inside a debugger session or a data grid would be
surprising. **The data grids' own Refresh was not given a gesture** — it is a different action with a
pending-edit question of its own, and no gesture for it was ratified. Say the word and it becomes
`Tab`- or `Grid`-scoped in a follow-up.

### 13.2 An unrelated discrepancy noticed in passing — NOT touched

`MainWindowViewModel.CommitAllAsync` carries a comment stating *"The TOOLBAR's Commit stays deliberately
narrower — it is the console's button"*, yet `MainWindow.axaml`'s Commit and Rollback buttons bind
`CommitAllCommand` / `RollbackAllCommand`, and a separate narrower `CommitCommand` / `RollbackCommand` pair
exists unused by that toolbar. Either the comment is stale or the binding is not what was intended. **`F6`
binds what the button binds** — that is the correct rule for a shortcut and it keeps the two in step
whichever way the discrepancy is resolved. Flagged rather than changed: transaction settlement is
rule-#11 territory and not this sprint's to reinterpret.

---

## 14. Etap 4 — as built

Build 0/0; suite **5943 green** in the two partitions (5900 + 43); smoke clean.
**A keyboard gesture is now written down in exactly one place.**

### 14.1 ⭐ The etap justified itself before it started

Etap 3 re-bound Format SQL from `Alt+F` to `Ctrl+K`. `UiStrings.ToolbarFormatSqlTooltip` went on reading
**`"Format SQL · Alt+F"`** — a tooltip confidently teaching a shortcut that no longer existed — with a green
build and a green suite. That is the whole argument for this etap in one artefact: **a hand-typed gesture does
not merely duplicate the catalog, it goes stale silently**, which is worse than showing nothing.

### 14.2 The composer, and why the text did NOT move into the catalog

`Commands/CommandTip.cs` is the ONE place a gesture becomes text a user reads:

* `For(id, text)` → `"Compile the procedure · F7"` (and just `text` when the command has no gesture, so no
  caller has to ask).
* `Gesture(id)` → `"F7"` alone, for a `TextBlock.shortcut-chip`.
* `Sentence(id, format)` → the gesture substituted mid-sentence, for prose that names a shortcut.
* `Format(gesture)` → `Ctrl` → `Shift` → `Alt` then the key, the way Windows writes it.

**The label text stayed in `UiStrings`** and is passed in. Two reasons, both binding: architecture rule #6
puts every user-visible string there, and **one `CommandId` serves eleven differently-worded Compile
buttons** ("Compile view (CREATE OR ALTER VIEW)", "Compile package (header then body)", …) — so a single
`Tooltip` field on the descriptor could not have served them. The catalog owns the *gesture*; `UiStrings`
owns the *words*.

**⚠ `Format` is deliberately not `KeyGesture.ToString()`**, which spells the raw enum name: `Ctrl+.` would
have reached the user as **`"Ctrl+OemPeriod"`**. The named-key table covers what EmberTern actually shows and
falls back to the enum name, which is already right for letters, digits and function keys.

### 14.3 How the migration was made, and why `const` → `static readonly`

~25 members now compose their gesture. Nothing in XAML changed: `x:Static` resolves a `static readonly`
field exactly as it did a `const`, so the migration is 25 lines in one file instead of edits across 15 views —
a much smaller blast radius for a change with no intended visual effect. Verified first that none of them is
used in a `const` expression (attribute argument, `case` label, another `const` initialiser), which is the
one thing that would have made this unsafe.

Migrated: the 11 Compile tooltips (**new** — `F7` had no tooltip presence at all), Commit `F6`, Rollback
`Shift+F6`, Close tab `Ctrl+W`, Format SQL `Ctrl+K`, Execute (both `F5` preview and `Shift+F5` all rows),
Global Search, Quick Fix (tooltip + hover hint), Import Run / Validate / Refresh, Script Run, the nine
debugger toolbar tooltips, Save, Evaluate, both shortcut **chips**, and three **prose** messages that name a
key mid-sentence (the two debugger session-ended lines, the Session Manager analyze tip, the Harness Log
empty state) — those went through `Sentence`, because a trailing `· key` does not read inside a sentence.

### 14.4 A gesture shown on a button that cannot use it would be a lie

Only **`Global`- and `Tab`-scoped** commands got their gesture into a tooltip. The **focus-scoped** ones
(`F3`/`F4`/`F8` in the tree, `F3`/`F8` in a grid) did **not**: their buttons live in the toolbar, outside the
scope where the key works, and a tooltip promising `F3` on a toolbar button that ignores `F3` teaches the
user something false. Those gestures belong in the **context menu** of the surface that owns them — etap 5,
where the user is already in that scope. The collection `+` / `−` toolbar buttons are the concrete case: same
commands as `F3`/`F8`, deliberately no gesture shown.

### 14.5 The guard — enforced, not remembered

`UiStringsShortcutSourceTests` (14 tests) makes the rule structural.

**⚠ It keys on `const` vs `static readonly`, not on the runtime text — and it has to.** A composed string
legitimately *contains* `" · F7"` at run time, so its value proves nothing about where the gesture came from.
What separates them is that **a `const` is a literal by definition**. So the rule is: no `const` in
`UiStrings` may contain gesture-shaped text.

**Verified by planting a violation** — a temporary `const` reading `"Do something · Ctrl+J"` made the test
fail and name the offender, then was reverted. A guard nobody has seen fail is a guard nobody knows works.

Three exemptions, each with its reason in the test (and a second test that fails if an exemption goes stale —
names a member that no longer exists, or one that no longer contains a gesture):

| Constant | Why the gesture is not a catalog command |
|---|---|
| `ImportCancelTooltip` (`Esc`) | `Escape` is a universal dismiss owned by every popup, dialog and filter box; declaring it would invent collisions with all of them. |
| `ImportSourceUseClipboardTooltip` (`Ctrl+V`) | Means "re-read the clipboard SOURCE" — paste semantics that must yield to a focused text box, so it stayed a local handler. |
| `FieldEditEditTooltip` (`F2`) | The fields grid's local `DataGrid.KeyBinding`. `F2` was not in the ratified set, so no `CommandId` was invented for it. |

Also verified: **no gesture is hardcoded anywhere outside `UiStrings`** — no literal `ToolTip.Tip` in XAML
carries one, and no code-behind composes one. The remaining hardcoded gestures in the app are the three
`MenuItem.InputGesture="Insert|F2|Delete"` attributes on the fields grid, which are etap 5's business.

### 14.6 Still not on the descriptor

`Label` and `IconKey` remain absent. Etap 4 needed neither: the tooltip text comes from `UiStrings`, and a
menu label is a different (shorter) string that etap 5 will introduce where it is consumed.

# Keyboard Manager & Context Menu UX — sprint design document

**Status: ETAP 1 (AUDIT) COMPLETE — awaiting the user's acceptance before any implementation.**
Branch: `feat/keyboard-manager`. Build 0/0 at audit close; no production code touched.

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

| Etap | Content | Ends with |
|---|---|---|
| **1** | **Audit — DONE** (this document §1–§6) | user acceptance |
| **2** | `CommandDescriptor` / `CommandCatalog` / `CommandResolver` + scope resolution + the §7.6 pinned test + **fix C1** | build 0/0, suite green, committable |
| **3** | Fill in §6's missing shortcuts through the catalog; retire `Alt+F`'s 6 copies (D1) | " |
| **4** | `{app:CommandTip}` — every gesture-bearing tooltip reads the catalog; strip the ~24 hand-typed suffixes | " |
| **5** | Context menus: styles (§9.2 (a)), then a control only if measured necessary; icons; gestures; all 32 menus | " |

One etap per session, each ending build 0/0 + tests green + smoke clean + committable, per the
project's session protocol.

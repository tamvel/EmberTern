# Icon catalog

Central icon library for EmberTern. **SVG = source of truth (this folder); runtime
renders the geometries** in [`../../Themes/IconGeometries.axaml`](../../Themes/IconGeometries.axaml)
via the [`SvgIcon`](../../Controls/SvgIcon.cs) control.

- **Source set:** [Lucide](https://lucide.dev) (ISC license). 24×24 viewBox, 2px
  stroke, round caps/joins, `fill="none"`, `stroke="currentColor"`.
- **Coloring:** never bake a color into the SVG/geometry. Color flows through a
  theme token on `SvgIcon.Foreground` (see semantic tokens below).
- **Build:** the `.svg` files here are **excluded** from the build output
  (`AvaloniaResource Remove` in the csproj). They are repo source only — zero
  app-size impact. Only the geometry strings in `IconGeometries.axaml` ship.

## Reuse before create

Before adding an icon: (1) check the geometry keys in `IconGeometries.axaml`,
(2) check this folder, (3) reuse if a Lucide icon already fits. Add a new one only
when no existing icon fits / is legible. **Never two icons for the same action.**

To add: drop the Lucide `.svg` into the right category folder here, then add its
path data as `<StreamGeometry x:Key="Icon.Name">` in `IconGeometries.axaml`.

## Semantic color tokens (defined in `Themes/Colors.axaml`, both themes)

| Token | Use |
|---|---|
| `AccentIconBrush` | execute-class actions on a neutral surface |
| `SuccessIconBrush` | commit |
| `DangerIconBrush` | rollback / abort (cancel) |
| `WarningIconBrush` | delete / clear |
| `InfoIconBrush` | refresh |
| `NeutralIconBrush` | everything else (default) |

Disabled state is conveyed by button `Opacity`, not a separate color.

## Catalog (Etap 1)

| Geometry key | Source file | Purpose | Used in (Etap 1) |
|---|---|---|---|
| `Icon.Play` | Actions/play.svg | Execute | SQL editor Execute button (OnAccent) |
| `Icon.Stop` | Actions/stop-square.svg | Cancel / abort running query | SQL editor Cancel (Danger) |
| `Icon.Check` | Actions/check.svg | Commit | Editor toolbar Commit (Success) |
| `Icon.Undo` | Actions/undo.svg | Rollback / revert | Editor toolbar Rollback (Danger) |
| `Icon.Plus` | Actions/plus.svg | Add / new query | Editor toolbar + Saved Queries header (Neutral) |
| `Icon.Pencil` | Actions/pencil.svg | Rename | Saved Queries row hover (Neutral) |
| `Icon.Trash` | Actions/trash-2.svg | Delete selected | Saved Queries header (Warning) |
| `Icon.ListX` | Actions/list-x.svg | Clear all queries | Saved Queries header (Warning) |
| `Icon.Save` | Actions/save.svg | Save (reserved — no button yet) | — |
| `Icon.X` | Actions/x.svg | Close / dismiss (non-window) | Tab-strip close + Close-tab toolbar (Neutral) |
| `Icon.Copy` | Actions/copy.svg | Copy / duplicate | Connection toolbar — Copy Connection (Neutral) |
| `Icon.Crosshair` | Actions/crosshair.svg (geometry-only) | Focus on selection | Activity Monitor — "Show only selected" toggle (distinct from the funnel `Icon.Filter`) |
| `Icon.Eraser` | Actions/eraser.svg | Clear editor text | SQL editor toolbar (Neutral) |
| `Icon.Braces` | Actions/braces.svg | Format SQL (`{ }` code) | SQL editor toolbar (Neutral) |
| `Icon.Hammer` | Actions/hammer.svg | Compile / build DDL | Pola + New Table toolbar — accent **primary** CTA button (OnAccent) |
| `Icon.TablePlus` | Actions/table-plus.svg (composed) | New Table | Connection toolbar (IconColor_Table). No Lucide `table-plus` exists → composed from Lucide table grid + plus. |
| `Icon.Import` | Actions/import.svg (composed) | Data Import | Main toolbar, beside the Script Executor (Accent). Composed: an arrow descending **into a table grid**. Deliberately NOT `Icon.Download` — that tray means "fetch a file to disk", while this module puts rows into a TABLE, so the glyph rhymes with `Icon.Table`/`Icon.TablePlus`. |
| `Icon.PencilRuler` | Actions/pencil-ruler.svg | Design / edit table structure | Pola field-edit toggle (Neutral; blue selection bg when checked) |
| `Icon.FolderPlus` | Actions/folder-plus.svg | New folder | Connection toolbar (Neutral) |
| `Icon.PlugZap` | Connection/plug-zap.svg | Connect | Connection toolbar (Accent) |
| `Icon.Unplug` | Connection/unplug.svg | Disconnect | Connection toolbar (Neutral) |
| `Icon.RotateCw` | Transactions/rotate-cw.svg | Reconnect | Connection toolbar (Neutral) |
| `Icon.RefreshCw` | Transactions/refresh-cw.svg | Refresh metadata | Connection toolbar (Info) |
| `Icon.Moon` / `Icon.Sun` | Navigation/moon.svg, sun.svg | Toggle Light/Dark | Titlebar theme toggle — action-aware (Sun in Dark, Moon in Light) via ThemeToggleIconConverter |
| `Icon.PanelLeft` | Navigation/panel-left.svg | Collapse/expand sidebar | Titlebar sidebar toggle (Neutral) |
| `Icon.PanelRight` | Navigation/panel-right.svg | Toggle Saved Queries panel | SQL editor toolbar (Neutral) |
| `Icon.ChevronFirst` | Navigation/chevron-first.svg | First page | SQL results pagination (Neutral) |
| `Icon.ChevronLeft` | Navigation/chevron-left.svg | Previous page | SQL results pagination (Neutral) |
| `Icon.ChevronRight` | Navigation/chevron-right.svg | Next page | SQL results pagination (Neutral) |
| `Icon.ChevronLast` | Navigation/chevron-last.svg | Last page | SQL results pagination (Neutral) |
| `Icon.WindowMinimize` | Window/minimize.svg | Minimize window | Titlebar caption (Neutral) |
| `Icon.WindowMaximize` | Window/maximize.svg | Maximize window | Titlebar caption (Neutral) |
| `Icon.WindowRestore` | Window/restore.svg | Restore window | Titlebar caption (Neutral, swapped in code-behind) |
| `Icon.WindowClose` | Window/close.svg | Close window | Titlebar caption (Neutral → white on hover) |

## Catalog (Etap 2) — DB object-type icons + tree chrome

13 metadata object kinds, keyed `Icon.<MetadataObjectKind>` (so
`MetadataNodeViewModel.GeometryKeyFor(kind) = $"Icon.{kind}"`). Shape resolves via
`IconGeometryConverter`; color via the per-kind `IconColor_*` token through
`IconBrushConverter`. Rendered in EVERY object-icon site: metadata tree (group +
leaf), workspace tab strip, dependency tree (Used by / Depends on), and the
field-dependencies panel — one source of truth, no second icon set.

| Geometry key | Lucide source | Object kind | Notes |
|---|---|---|---|
| `Icon.Table` | table.svg | Table | rect + grid |
| `Icon.View` | eye.svg | View | a "view" you look at |
| `Icon.Procedure` | square-terminal.svg | Procedure | executable `>_` |
| `Icon.Trigger` | zap.svg | Trigger | fires on event |
| `Icon.Function` | square-function.svg | Function | ƒ (returns value) |
| `Icon.Generator` | (authored) hash | Generator/Sequence | `#` number sequence |
| `Icon.Domain` | (authored) diamond | Domain | custom type shape |
| `Icon.Package` | package.svg | Package | box of routines |
| `Icon.Exception` | triangle-alert.svg | Exception | ⚠ |
| `Icon.Role` | shield.svg | Role | privilege set |
| `Icon.User` | user.svg | User | single person |
| `Icon.Index` | key-round.svg | Index | DB-tool key convention |
| `Icon.SystemTable` | database.svg | System Table | system catalog (grey) |

Tree chrome (not a `MetadataObjectKind`):

| Geometry key | Lucide source | Purpose | Used in |
|---|---|---|---|
| `Icon.Query` | file-code.svg | SQL Editor tab | tab strip (IconColor_Query) |
| `Icon.Connection` | server.svg | Connection root node | sidebar tree — color = `StatusBrushKey` (green connected / subtle disconnected) |
| `Icon.Folder` | folder.svg | Connection folder | sidebar tree (Neutral) |

New action primitives added this etap (Table Detail toolbar completeness):

| Geometry key | Lucide source | Purpose | Used in |
|---|---|---|---|
| `Icon.Minus` | minus.svg | Drop field / Delete row | Pola + New Table + Dane toolbars (Neutral) |
| `Icon.ArrowUp` | arrow-up.svg | Move field up | Pola + New Table toolbars (Neutral) |
| `Icon.ArrowDown` | arrow-down.svg | Move field down | Pola + New Table toolbars (Neutral) |

Reused (no new geometry) for the rest of the Table Detail toolbar: `Icon.Plus`
(add field/row), `Icon.RefreshCw` (refresh data, Info), `Icon.ChevronFirst/Left/Right/Last`
(Dane pagination), `Icon.Save` (save description).

### Catalog (D15.2 Seam A) — debugger toolbar

The debugger toolbar's action set. Authored in the same idiom (24×24, 2px stroke, round
caps/joins); source `.svg` in `Debugger/`. **Colour = category/weight, not decoration** — most
are neutral; only the load-bearing actions take a token. Continue reuses `Icon.Play`
(Accent, the single primary action); Stop reuses `Icon.Stop` (Danger).

| Geometry key | Debug source | Action | Colour token |
|---|---|---|---|
| `Icon.StepInto` | Debugger/step-into.svg | Step into | Neutral |
| `Icon.StepOver` | Debugger/step-over.svg | Step over | Neutral |
| `Icon.StepOut` | Debugger/step-out.svg | Step out | Neutral |
| `Icon.RunToCursor` | Debugger/run-to-cursor.svg | Run to cursor | Neutral |
| `Icon.RunToSuspend` | Debugger/run-to-suspend.svg | Run to next SUSPEND | Neutral |
| `Icon.NextIteration` | Debugger/next-iteration.svg | Next loop iteration (two-arrow cycle) | `DebugLoopIconBrush` (teal) |
| `Icon.LoopExit` | Debugger/loop-exit.svg | Continue until loop exit | `DebugLoopIconBrush` (teal) |
| `Icon.Restart` | Debugger/restart.svg | Restart (skip-to-start) | Neutral |
| `Icon.BreakException` | Debugger/break-on-exception.svg | Break on exception (toggle) | Warning |

### Catalog (D15.2 Seam B) — debugger identity

The debugger's tab + entry-point identity mark, replacing the old `Icon.Bug`. It is a
**two-colour composite**, not a single stroked geometry: a **blue Play triangle** (the
execution pointer, dominant) + a **small red breakpoint dot** nested into its lower-right,
overlapping the tip so the two read as one "Start Debugging" glyph. Two colours + a filled
dot can't be a single `SvgIcon`, so it is a dedicated control — `Controls/DebuggerIcon.cs`
with its ControlTheme in `Themes/IconGeometries.axaml`. Both colours are **reused theme
tokens**: `AccentIconBrush` (Play) + `DebugBreakpointBrush` (the same red the gutter
breakpoint dot uses). Same idiom (24×24, 2px stroke, round caps/joins); canonical source
`Debugger/debugger.svg`.

| Control | Source | Purpose | Used in |
|---|---|---|---|
| `DebuggerIcon` | Debugger/debugger.svg | Debugger identity — blue Play + red breakpoint = "Start Debugging" | Debugger tab; Procedure + Trigger editor toolbar "Debug…" buttons |

The fault message bar is Seam C.

## Application Menu (hamburger-navigation, etap 2)

⛔ **`Icon.Menu` is closed — user-accepted 2026-07-28 after three QA rounds. Do not adjust its geometry**
(not the fractional coordinates, not the ink box, not a revert to upstream Lucide). The two rules below
are the reusable part; the icon itself is settled.

| Geometry key | Source file | Purpose | Used in |
|---|---|---|---|
| `Icon.Menu` | Navigation/menu.svg | Open the Application Menu | Titlebar — the first button of the action zone |
| `Icon.Settings` | Actions/settings-sliders.svg (composed) | Settings | Application Menu (disabled placeholder) |
| `Icon.Exit` | Actions/log-out.svg (composed) | Leave the application | Application Menu |

`Icon.Settings` and `Icon.Exit` are **composed** in the Lucide style rather than taken verbatim
(precedent: `table-plus.svg`, `import.svg`), and each `.svg` says so in a comment. A gear was
rejected for Settings — its outline cannot be authored cleanly at a 2px stroke rendered into the
14px menu icon column; a power symbol was rejected for Exit for the same reason (a near-full arc
is the shape that degrades worst at that size).

### ⭐ Optical size is a property of the GEOMETRY, not of the control

Worth knowing before adding any icon, because it cost a QA round. The `SvgIcon` ControlTheme is a
`Viewbox Stretch="Uniform"` wrapping a **fixed `Canvas Width="24" Height="24"`** — so the Viewbox
scales the *Canvas*, never the path's ink. Every icon therefore renders at exactly the same 24→16
scale, and **an icon looks small purely because its geometry fills less of the 24×24 box.** No
stretching compensates.

The useful measure is the **ink box** = the path's extremes ±1 (half of the 2px stroke, round caps).
Measured across the titlebar:

| Icon | Ink box |
|---|---|
| `Icon.Copy`, `Icon.FolderPlus` | 22×22, 22×19 |
| `Icon.PanelLeft`, `Icon.Trash` | 20×20, 20×22 |
| `Icon.Menu` (as shipped) | **18×17** — smaller on purpose, see below |
| `Icon.Menu` (verbatim Lucide) | 18×14 ← the shortest glyph on the bar, and it showed |
| `Icon.Plus` | 16×16 (a compact symbol; reads fine small) |

A toolbar icon lives in roughly a **20×20** ink box, so that is the right *starting* point — Lucide's own
set is not internally consistent about it, and a verbatim file is not a guarantee.

### ⚠⚠ But the target is OPTICAL, not geometric — do not equalise ink boxes

This is the correction that cost a QA round, and it reverses the naive reading of the table above.
`Icon.Menu` was once given PanelLeft's exact 20×20 box and **looked bigger than every icon around it**.
Equal boxes are not equal weight: three full-width rules are far denser than a thin rectangle *outline*,
so at the same extent the hamburger has to dominate. **A dense glyph needs a smaller box to look the same
size** — hence 18×17 against its neighbours' 20×20.

So: use the ink box to *diagnose* ("this glyph is 30% shorter than its neighbours, that is why it looks
small"), never as the *goal*. The goal is what the icon looks like beside the icons it actually sits
next to, at the size it actually renders. When in doubt, put the candidates side by side with the real
neighbour geometries at 16px and look — that is what settled this one, after arithmetic had twice
produced a confidently wrong answer.

`ConnectionExpandBindingProbe` therefore pins a **range**, not an equality: big enough not to look lost,
strictly smaller than a rectangle outline, centred, and phase-consistent (below).

### ⭐ Repeated parallel strokes: keep the spacing a multiple of 3

The second half of the same fact, and it cost its own QA round. The 24→16 render is a **×2/3** scale,
so a rule declared at `y` has its top edge at **2(y−1)/3** with a 1.333px thickness — and the
*fractional part* of that edge decides the anti-aliasing. Two strokes on different fractions are
**drawn differently no matter how symmetric the coordinates look**:

| Declared y | Rendered band | Pixel coverage | Reads as |
|---|---|---|---|
| 4 | [2.000, 3.333] | row 2 → 100%, row 3 → 33% | crisp, faint edge below |
| 12 | [7.333, 8.667] | rows 7 **and** 8 → 67% each | two grey rows: softer, **thicker** |
| 20 | [12.667, 14.000] | row 12 → 33%, row 13 → 100% | crisp, faint edge above; the round cap lands on another phase, so the end looks clipped |

Equal rendering requires equal phases, i.e. `2·Δy/3 ∈ ℤ` ⇒ **Δy must be a multiple of 1.5** (because
1.5 × 2/3 = 1). The hamburger ships with Δy = **7.5** — all three rules on phase .333.

**So: any icon with repeated parallel strokes** (a hamburger, a list, a stack, a set of rules) **spaces
them by a multiple of 1.5 in the 24-unit grid.** Otherwise one stroke will look thicker than its
siblings and no amount of nudging fixes it — the cause is the scale, not the coordinates. The multiples
of 1.5 are what leave room to choose the *extent* freely (14 / 17 / 20 for a three-rule glyph), which is
what the optical rule above needs.

⚠ This holds at the rendered **16px**, the `SvgIcon` default. A host that overrides `Width`/`Height`
(the debugger tab renders its mark at 14) re-scales the grid and changes the phase; that is inherent
to scaling and cannot be fixed by choosing coordinates.

### Migration status — COMPLETE

No Unicode/emoji object or action glyphs remain in any view — the debugger toolbar was the
last holdout (Unicode `▶ ⤵ ↷ ⤴ ■ ↻`), moved onto `SvgIcon` in D15.2 Seam A. The legacy
`MetadataNodeViewModel.IconFor(...)` glyph map + the `*Icon` string constants in
`UiStrings` are retained only as dead fallback / for tests — live UI renders SVG
everywhere (metadata tree, tabs, Table Detail, DDL, dependency + field-dependency
views, connection/folder nodes, every toolbar).

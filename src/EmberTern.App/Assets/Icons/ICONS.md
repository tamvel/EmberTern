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
| `Icon.Eraser` | Actions/eraser.svg | Clear editor text | SQL editor toolbar (Neutral) |
| `Icon.Braces` | Actions/braces.svg | Format SQL (`{ }` code) | SQL editor toolbar (Neutral) |
| `Icon.Hammer` | Actions/hammer.svg | Compile / build DDL | Pola + New Table toolbar — accent **primary** CTA button (OnAccent) |
| `Icon.TablePlus` | Actions/table-plus.svg (composed) | New Table | Connection toolbar (IconColor_Table). No Lucide `table-plus` exists → composed from Lucide table grid + plus. |
| `Icon.PencilRuler` | Actions/pencil-ruler.svg | Design / edit table structure | Pola field-edit toggle (Neutral; blue selection bg when checked) |
| `Icon.FolderPlus` | Actions/folder-plus.svg | New folder | Connection toolbar (Neutral) |
| `Icon.PlugZap` | Connection/plug-zap.svg | Connect | Connection toolbar (Accent) |
| `Icon.Unplug` | Connection/unplug.svg | Disconnect | Connection toolbar (Neutral) |
| `Icon.RotateCw` | Transactions/rotate-cw.svg | Reconnect | Connection toolbar (Neutral) |
| `Icon.RefreshCw` | Transactions/refresh-cw.svg | Refresh metadata | Connection toolbar (Info) |
| `Icon.ThemeToggle` | Navigation/moon.svg | Toggle Light/Dark | Titlebar theme toggle (Neutral) |
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

### Migration status — COMPLETE

No Unicode/emoji object or action glyphs remain in any view. The legacy
`MetadataNodeViewModel.IconFor(...)` glyph map + the `*Icon` string constants in
`UiStrings` are retained only as dead fallback / for tests — live UI renders SVG
everywhere (metadata tree, tabs, Table Detail, DDL, dependency + field-dependency
views, connection/folder nodes, every toolbar).

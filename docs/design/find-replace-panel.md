# Find / Replace panel — ratified direction + the measurements it rests on

**Status:** 📋 **Ratified 2026-08-12, not started.** The user chose *"our own Find/Replace panel over
AvaloniaEdit's search engine"* from four measured options.
**Origin:** a user report — *"the search inside a procedure's metadata is 100 % English despite Polish
being on"*. Narrative: [`history/31`](../history/31-the-constant-rule-and-two-ui-defects.md) §3.

> ⛔ This document exists because its **measurements are perishable**, not to pre-design the stage. It
> records what was eliminated and why, so the implementing session starts from evidence instead of
> re-deriving it. It is **not** a task list and it does not pre-commit the layout.

---

## 1. The decision, and what it does NOT overturn

Build **our own Find/Replace UI** in `EmberTern.App`, driving **AvaloniaEdit's search engine**.

⭐ This does **not** reverse `history/12`'s rule *"no custom find/replace engine — `SearchPanel`
already does next/previous"*. That rule is about the **engine**, and the engine stays theirs
(`ISearchStrategy` / `SearchStrategyFactory`). Only the **presentation** becomes ours — which is the
half the rule never claimed.

## 2. Why the cheaper options were rejected — measured, 2026-08-12

AvaloniaEdit 12.0.0 exposes **no localization seam** for `SearchPanel`. Each candidate was eliminated
by measurement, not by inspection of the docs:

| Candidate | Measured result |
|---|---|
| A `Localization` class with virtual strings (older ICSharpCode/AvaloniaEdit shipped one) | **Absent.** No type named `*Localization*` anywhere in the assembly. |
| The public `SearchPanel` API | 6 properties (`SearchPattern`, `ReplacePattern`, `MatchCase`, `WholeWords`, `UseRegex`, `IsReplaceMode`), 10 methods, 1 event (`SearchOptionsChanged`). **No string and no message hook.** |
| A `pl` satellite for `AvaloniaEdit.SR.resources` | **Impossible.** The mechanism exists (a `zh-Hans` satellite ships), but the assembly is **strong-named** (`PublicKeyToken=c8d484a7012f9a8b`) and a satellite must carry the main assembly's key. |
| A style setter over the template | **Partly viable, and that is the trap.** On the realized tree the two placeholders and all five tooltips read priority **`Template`** — the lowest — so a style beats them. But only the placeholders are **named** parts; the three option toggles and the nav buttons are **unnamed**, reachable positionally only. A positional selector would silently **mislabel** a toggle after an AvaloniaEdit upgrade, which is worse than English. |
| The reported string itself | `PART_MessageContent`, written from the panel's **own code** — a local value. ⛔ No style can beat a local value (the same priority fact as `product-polish.md` §16's resource-alias route). |

## 3. The string inventory — read from `AvaloniaEdit.SR.resources`, verbatim

15 strings, which is the localization scope. ⚠ Five carry a **gesture inside the words** — those must
come from `CommandTip`, never be retyped (`CLAUDE.md` rule: no gesture is typed by hand anywhere, and
gotcha #284 is exactly a shortcut that went stale inside a string).

| Key | English | Note for our version |
|---|---|---|
| `SearchLabel` | `Find...` | placeholder |
| `ReplaceLabel` | `Replace...` | placeholder |
| `SearchMatchCaseText` | `Match case` | toggle tooltip |
| `SearchMatchWholeWordsText` | `Match whole words` | toggle tooltip |
| `SearchUseRegexText` | `Use regular expressions` | toggle tooltip |
| `SearchFindNextText` | `Find next (F3)` | ⚠ gesture → `CommandTip` |
| `SearchFindPreviousText` | `Find previous (Shift+F3)` | ⚠ gesture → `CommandTip` |
| `SearchReplaceNext` | `Replace next (Alt+R)` | ⚠ gesture → `CommandTip` |
| `SearchReplaceAll` | `Replace all (Alt+A)` | ⚠ gesture → `CommandTip` |
| `SearchToggleReplace` | `Toggle between find and replace modes` | ⚠ gesture → `CommandTip` |
| `SearchNoMatchesFoundText` | `No matches found` | **the reported string** |
| `Search1Match` | `1 match` | ⚠ see §5 |
| `SearchXMatches` | `{0} matches` | ⚠ see §5 |
| `SearchXOfY` | `{0} of {1}` | |
| `SearchErrorText` | `Error: ` | a bad regex; ⛔ do not glue — one sentence (D‑3) |

## 4. The seams to touch — all of them already exist

⭐ The consolidation work is **already done**, which is most of why this is tractable:

- [`App/Completion/EditorSearch.cs`](../../src/EmberTern.App/Completion/EditorSearch.cs) is the **one**
  place find/replace is opened, and it already resolves through `TextEditor.SearchPanel` only
  (`keyboard-manager.md` §11.5 removed the second panel). It is the seam to re-point.
- `CommandId.EditorFind` / `EditorReplace` in [`App/Commands`](../../src/EmberTern.App/Commands) already
  own the gestures, the tooltips and the context-menu entries. ⛔ Do not introduce a gesture here.
- `SqlEditorBehavior.Attach` is the one attachment seam for the editor-intrinsic block (D3, gotcha
  #219 — resolved), so the panel is installed in exactly one place for every host.

## 5. Known unknowns the implementing session must measure FIRST

⛔ Do not treat these as decided. Each one can change the shape of the work.

1. **`SearchResultBackgroundRenderer` is `internal`.** The highlight-all-matches layer is not reachable,
   so ours is a new `IBackgroundRenderer` (the project already ships several — diagnostics squiggles,
   current-line). **Measure whether `SearchPanel`'s own highlighting can be reused at all before
   assuming it must be rebuilt.**
2. **Plural forms.** `1 match` / `{0} matches` is the same three-forms-in-Polish problem C6 solved for
   the Execution Summary (`history/28`, gotchas #362–#365). ⭐ The mechanism exists — reuse
   `Loc.FormatParts` / the plural catalog; ⛔ do not invent a second one, and ⛔ do not reach for an
   `(s)` hedge (`current-state.md` records 30 of those as debt already).
3. **Does the engine even need the panel?** `SearchStrategyFactory` is public and `ISearchStrategy` has
   `FindAll` / `FindNext` — so a fully independent implementation may be possible without instantiating
   `SearchPanel` at all. **Measure this before writing the wiring**, because if it holds, `Uninstall()`
   and the panel's lifecycle disappear from the design entirely.
4. **Selection-scope Replace** stays out of scope, as it has since `history/12` §Etap 1. ⛔ Do not let
   "we own the UI now" turn into scope growth: this stage's goal is a localized panel at parity, not a
   better search.
5. **The read-only surfaces.** `AttachReadOnlyHighlighting` / `AttachDdlPreview` hosts get **Find**, and
   must not get Replace — the existing `editor.IsReadOnly` guard in `EditorSearch.TryOpenReplace` is the
   rule to preserve.

## 6. Definition of done

- Every one of §3's 15 strings resolves through `UiStrings` / `{app:Loc}`, live on a language change
  (D‑1: no restart), with Polish complete.
- Parity with today: find next/previous, replace next/all, the three option toggles, match count,
  no-match state, bad-regex error, seed-from-selection, `Ctrl+F` / `Ctrl+H`, Escape to close.
- Every gesture in a tooltip comes from the registry; `CommandTip` is the only source.
- ⛔ No literal colour, no literal metric a token names (`Colors.axaml` / `Tokens.axaml` /
  `Typography.axaml`); judged in Light **and** Dark, and in all control states.
- The AvaloniaEdit `SearchPanel` is no longer reachable from any EmberTern surface — ⚠ **pinned by a
  test**, because a second, un-localized find panel behind one host is exactly the shape gotcha #219
  describes.

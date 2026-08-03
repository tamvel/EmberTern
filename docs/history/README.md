# EmberTern — Project History Archive

This folder holds the **full narrative history** of EmberTern's development: every milestone,
session, bugfix investigation, and design decision, in the words they were originally written
in. It was split out of `CLAUDE.md` during the **Documentation Cleanup Sprint (2026-07-11)**,
whose goal was to shrink the cost of starting a new Claude Code session without losing any
project knowledge — see `docs/DOCUMENTATION-MAP.md` for the full rationale and the resulting
structure.

**Nothing here is loaded automatically at the start of a session.** Read a file only when you
need the backstory on a specific feature or bug — e.g. "why does the metadata reader capture
the command lock exactly once?" or "what did we try before the FlatTree sidebar migration?".
For **current** rules, architecture, and state, read `CLAUDE.md` and
`docs/design/editor-architecture.md` instead — those are kept current and are what every new
session actually needs.

Every file below is a **verbatim extract** — copied byte-for-byte out of the original
`CLAUDE.md`/design-doc text, with only a small provenance header added. No content was
rewritten, summarized, or dropped in the split (see §0 "never lose information"). Gotchas that
appear inline in this narrative are also indexed, thematically and de-duplicated, in
`docs/gotchas.md` — the narrative here is the fuller "how/why we got there" story; the gotchas
file is the fast lookup.

## Files, in chronological order

| File | Covers | Roughly |
|---|---|---|
| [00-v1-definition-of-done-and-backlog.md](00-v1-definition-of-done-and-backlog.md) | V1 DoD checklist + the original post-V1 backlog (mostly stale — later superseded by name) | shipped 2026-05 |
| [01-v1-foundation-and-workspace.md](01-v1-foundation-and-workspace.md) | M1–M6 (scaffold → connections → SQL editor → transactions → metadata sidebar → DDL preview), Explorer Redesign, Custom Titlebar, V1.1 Workspace Persistence, Per-Connection Workspace | 2026-05 |
| [02-sql-editor-and-completion.md](02-sql-editor-and-completion.md) | SQL Editor UX, Saved Queries panel, execute-selected + autoformat, first SQL Autocomplete, formatter lowercase + light-theme highlighting | 2026-05–06 |
| [03-table-detail-core.md](03-table-detail-core.md) | Table Detail tab (Pola/Indeksy/Dane/Zależności core), inline data editing, pagination, smart cell editors | 2026-06 |
| [04-table-structure-editing.md](04-table-structure-editing.md) | `DdlGenerator`, Add/Edit Field dialog, FK Wizard, Field Dependencies panel, Constraint Management, destructive-op confirmation audit | 2026-06-12–13 |
| [05-transactions-settings-connections.md](05-transactions-settings-connections.md) | Transaction TPB hardening (C1/C2), DPAPI-encrypted settings, unified `settings.dat`, transaction-profile lane split, grid layout profiles | 2026-06-13–14 |
| [06-ui-premium-and-stabilization.md](06-ui-premium-and-stabilization.md) | SVG icon system rollout, two stabilization sprints (refresh-storm root cause #1), read-only System Tables | 2026-06-15–18 |
| [07-view-procedure-editors-and-formatter.md](07-view-procedure-editors-and-formatter.md) | View Detail V1, Procedure Detail V1–V1.4, the first PSQL-aware shared formatter, structured Easy Mode | 2026-06-16–20 |
| [08-data-loss-sidebar-and-searchable-combo.md](08-data-loss-sidebar-and-searchable-combo.md) | WorkGuard data-loss protection, UI-state persistence, the sidebar type-ahead → type-to-filter saga, SearchableComboBox (Domain/Column picker) | 2026-06-19–28 |
| [09-object-editors-and-metadata-tree.md](09-object-editors-and-metadata-tree.md) | Function/Generator/Domain/Package/Exception/Index Detail editors, Security Manager, Metadata Tree context menus, the TreeView scroll-jump investigation and the FlatTree sidebar migration that fixed it | 2026-06-29–07-01 |
| [10-grid-execution-performance-metrics.md](10-grid-execution-performance-metrics.md) | SQL Templates drag & drop, Data Grid Filtering & Aggregations, Performance Analysis (Phases 1–6), Execution Metrics module | 2026-07-02–03 |
| [11-activity-monitor-and-session-manager.md](11-activity-monitor-and-session-manager.md) | Database Activity Monitor (Trace) engine + UI + V1.1/V1.2 polish, Session Manager V1 | 2026-07-03–05 |
| [12-search-scripting-export-and-misc.md](12-search-scripting-export-and-misc.md) | Global Search + editor Find/Replace, Firebird routine-reconstruction fixes, Metadata export, Script Executor, Recompile Dependents, Smart SQL Parameters, Export Framework (CSV/XLSX/Clipboard) | 2026-07-05–08 |
| [13-transaction-audit-and-table-designer.md](13-transaction-audit-and-table-designer.md) | The full Transaction Architecture Audit (R1 fixed, R2 open, R3 resolved), the Table Designer buffered-model restore, single-attachment DDL + Developer Mode | 2026-06-17–18 |
| [14-editor-language-frontend-history.md](14-editor-language-frontend-history.md) | The editor rebuild's own etap-by-etap "as-built" record (Etap 0–6 completion notes, UX Polish Phase, post-polish bug-fix sprint) — the history half of `docs/design/editor-architecture.md` | 2026-07-09–11 |
| [15-ux-stabilization-sprint-and-console-refactor.md](15-ux-stabilization-sprint-and-console-refactor.md) | The 11-item UX & Stabilization Sprint, and the transaction/attachment rewrite it turned into: the dedicated **DDL attachment** (WAIT, not co-location — correcting gotcha #122), and the **SQL Editor as a classic Firebird console** (one attachment, one transaction, routing deleted). Also records why `FirebirdScriptExecutor` is known-broken and deferred. **Read before touching transactions, attachments, or the Script Executor.** | 2026-07-14 |
| [16-stage8-smart-editing.md](16-stage8-smart-editing.md) | **Stage 8 — Smart Editing & Structural Assistance.** M1 **Structural Matching** (Related Elements Highlighting, done + finalized). M2 **Smart Snippets** was **built then reverted** — the VS/Rider snippet-session UX was wrong for experienced devs; the code-writing experience was redesigned into **Language Completion + Typing Ergonomics** (see `docs/design/editor-language-expansion.md`). | 2026-07-16 |
| [17-completion-matching-philosophy.md](17-completion-matching-philosophy.md) | **Completion milestone — prefix-first IntelliSense** (separate from Stage 8). Interactive completion becomes a prediction engine, not a substring search: `CompletionEngine` the single authority, pure `CompletionMatcher` owns filtering/ranking, UI a passive view, AvaloniaEdit substring filter disabled. **`CompletionMatcher` DONE**; engine-fold + App passive-view rewrite is the recorded, entangled remaining step (needs interactive visual QA). | 2026-07-16 |
| [18-language-completion.md](18-language-completion.md) | **Language Completion & Typing Ergonomics** — the code-writing experience that replaced Stage 8 M2. Finishes daily Firebird constructs the developer already started typing (Tab + a passive hint, grammar-armed, synchronous), with `begin…end` as a delimiter pair (Typing Ergonomics, not a construct). As-built: revert M2 → Core catalog+resolver → grammar-aware arming → App Tab-expand + hint (done, awaits visual QA). Typing Ergonomics is next. | 2026-07-16 |
| [19-firebird-debugger.md](19-firebird-debugger.md) | **Stage X — Firebird Debugger** arc, as-built diary. **P1 (AST: exception handlers)** — `WHEN … DO` becomes a `WhenHandler` node (one per clause, ordered `WhenCondition` list + body) on `BlockStatement.Handlers`; parser producer + binder consumer, additive, formatter untouched, malformed→`Other` valve. Records the ratified decision-3 refinement (a `WHEN` may list several comma-separated conditions, so the model carries the whole list). | 2026-07-17 |
| [20-stage-q-quick-fixes.md](20-stage-q-quick-fixes.md) | **Stage Q — Quick Fixes & Code Actions (COMPLETE, Q0–Q5).** The first stage that modifies the user's code on its own initiative. Records the three decisions that came from reading the shipped code rather than planning (fixes are computed on demand, not stored on `Diagnostic`; the §0-safe apply idiom was generalized out of rename into ONE `TextEditApplier`; hover stays read-only), the evaluated naming split (`CodeAction` currency vs `QuickFixEngine` producer), the v1 action set and what was refused and why, and the **six-round light-bulb investigation** whose cause was a theme-scoped brush looked up without a theme variant (gotchas #250–#252). | 2026-07-25 |

| [21-data-import.md](21-data-import.md) | **Data Import (etapy I0–I12), the module end to end.** One working surface with collapsible sections — deliberately NOT a wizard; one pipeline for every source (proven three times: XLSX, XLS, clipboard, none of which touched it); `ImportConfiguration` as the single representation of every user decision; the module’s own transaction on its own attachment (I7.5); `CREATE TABLE` on the Ddl lane (#213); named profiles that required no model change (I11 was the design’s own audit). **I12 close-out**: a million rows in 14.0 s at 71 437 rows/s with a FLAT heap, the UI audit in both palettes, and a closing table of what stays OPEN with a reason for each. | 2026-07-26–27 |

| [22-architecture-hardening-sprint.md](22-architecture-hardening-sprint.md) | **Architecture Hardening / Product Safety — an external audit verified, then acted on.** Not a functional sprint: seal real risks only. Records which audit findings were real and which were not (A-02's P0 rating rejected as a ratified design decision; A-04 real as a documentation defect but not as a corruption path; A-08 declined), and the two the audit got *wrong in the safe direction* — A-05's mitigating argument was stale (the Office module now READS untrusted `.xlsx`), and A-01 had a second failure mode needing no concurrency at all (the New-object template is `CREATE OR ALTER`, so a name collision silently overwrites). Delivers the **DDL change-safety gate** (fingerprint + refuse; live-proven 19/19, including the correction that a DDL reconstruction synthesizes a stub and therefore cannot answer "does this exist"), **settings load health** (`Save` refuses over a file it cannot read — a grid-column resize used to destroy connection profiles and passwords), the corrected document-mutation contract + its tripwire test, the patched dependencies, and a status chip that could no longer lie. | 2026-07-27 |
| [23-acceptance-fix-round.md](23-acceptance-fix-round.md) | **Fourteen defects from ordinary use, grouped into SIX causes and closed in one pass** before M3.2d. Read it for how the grouping paid: three reports were not where they pointed (the result grid's column order was a *replayed profile*, not the column builder; the disabled hammer was `Opacity` letting the toolbar through, not a colour — `AccentColor` is identical in both themes; the "tooltip" that only a restart removed is an `OverlayLayer` card). Delivers **cross-process safety for `settings.dat`** (two instances could publish an EMPTY file, which then loaded as `Missing` and made defaults permanent — and the lesson that *a race is the wrong instrument for pinning a race*), the `EXTRACT(YEAR …)` binder position, positional typing for `EXECUTE PROCEDURE` placeholders (**not** a regression — the count gate was original), the `INTO` list on the shared formatter builder, resize-vs-sort on the result header, three M2b/M2c residues, and **`Alt+F`'s return** — where two guards fired in opposite directions without anyone touching a string. Also records a fix that was **deleted for being inert**. | 2026-08-03 |

## Where the gotchas went

Every numbered gotcha (`#1`–`#202`) that was embedded in this narrative is also collected,
de-duplicated, and organized **thematically** (not chronologically) in
[`docs/gotchas.md`](../gotchas.md) at the repo root's `docs/` folder. That file is the one to
search when you hit a familiar-looking bug — this history archive is for the deeper "why".

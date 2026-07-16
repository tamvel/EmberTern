# History — V1 Definition of Done + V1.1 Backlog Candidates (mostly stale/superseded)

> Archived from CLAUDE.md during the Documentation Cleanup Sprint (2026-07-11).
> Verbatim extract, lines 4471-4500 of the original file. Written shortly after V1
> shipped (M1-M6); many of the V1.1 backlog items below were later delivered by name
> in subsequent milestones (see the other docs/history files) — kept here verbatim
> as a record, not as an active backlog.

---

## V1 — definition of done (all met)

1. Add a Firebird connection ✓ (M2)
2. Connect to a database ✓ (M2)
3. Write SQL in editor ✓ (M3)
4. Execute and see results ✓ (M3)
5. Manual transaction with visible status ✓ (M4)
6. Commit / Rollback ✓ (M4)
7. Browse metadata tree ✓ (M5)
8. Double-click object → see DDL ✓ (M6)

## V1.1 candidates (post-V1 polish, not committed)

Surfaced by what was actually built; ordered roughly by user-visible value:
1. **Refresh button on TableDetail / DDL tabs** — content is fetched once at open (lazy-loaded the first time the tab activates). After an external schema change there's no way to re-fetch without closing and reopening. Resetting `_loadTask = null` and re-firing `EnsureLoadedAsync` is mechanically straightforward.
2. **TableDetail persistence schema upgrade** — TableDetail tabs serialize as `CoreTabKind.Ddl` today (Fields/Indexes/Constraints discarded; only `DdlText` survives). A native `TableDetail` kind in the persistence DTO would keep the per-tab cache hot across restarts, at the cost of versioning the schema.
3. **Procedure / function param signature in tab header** — currently `Procedure: SP_BALANCE` is just the name. IBExpert shows `(IN, OUT)` shape; would help disambiguate overloads.
4. **DDL for FB 2.5 functions** — currently we just emit a one-line comment. Reconstructing the `DECLARE EXTERNAL FUNCTION` from `RDB$FUNCTION_ARGUMENTS` is mechanical and would close that gap.
5. **DDL syntax: domains, character sets, COMPUTED BY columns** — table reconstruction handles `COMPUTED BY` and references domains, but doesn't emit `CREATE DOMAIN` for the user-defined ones a table depends on. A "show dependencies" toggle would be a natural extension.
6. **Tab right-click menu** — Close, Close Others, Copy DDL to Clipboard.
7. **M7 hardening** — test against FB 2.5 / 3.0 / 4.0 (only FB5 has been used so far). Verify WIN1250 round-trip in DDL text. Verify large tables (50+ columns) render correctly in the column loop. Verify the new constraints query (with the `RDB$TRIGGERS chk_src` join) against FB 2.5 — should work but unverified.
8. **Trigger types 8192+** — DB-level / DDL triggers currently render as `/* trigger type 8192 */`. Decoding is non-trivial but feasible.
9. **Smart tab limit** — no cap on open DDL/TableDetail tabs right now; ten+ tabs and the strip wraps. A most-recently-used eviction policy at ~10 tabs would be cleaner.
10. **Editor: keyboard close (Ctrl+W)** — DDL/TableDetail tabs only close via the × button.
11. **Constraints/Indexes sub-tabs counts in the tab strip** — Pola shows N fields immediately but Ograniczenia/Indeksy/Dane need a click to learn their size. A `(N)` badge per sub-tab header would surface that.
12. **Drag a connection to the empty root area to un-folder it.** Today `ExecuteDrop` only moves a folder member back to root when it's dropped *onto a root sibling* (Before/After). Dropping onto blank space below the tree resolves to a null target and cancels. A "no row under pointer → treat as root append" branch in `ResolveDropTarget` would let users drag a connection straight out of a folder without needing a root sibling to aim at.
13. **Multi-select drag.** Drag grabs exactly one row. Selecting several connections and dragging them into a folder as a batch would speed up bulk reorganization of a large connection list.
14. **Insertion-line drop indicator.** Drop feedback is a full-row background tint (`IsDropTarget` → `DropTargetBrush`). A thin line between rows for Before/After (vs. the row tint reserved for Into) would make the exact landing position clearer, IBExpert-style. Deferred deliberately — the spec said "keep it simple, no animated insertion line."
15. **Headless UI test harness, expanded.** `ConnectionExpandBindingProbe` proved its worth (caught the `x:DataType` style clobber that VM-level tests can't see). Worth growing into a small suite that pins the other tree bindings (drop-target highlight, selection brush, folder rename TextBox focus) — the kind of regressions that only surface against the real compiled XAML.


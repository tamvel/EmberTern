# Closed stage handovers & "next session" prompts

These are the **startup documents** written at the start (or between the etaps) of stages that are now
**closed and merged**: Product Polish M2a → M5, and Localization App + Core/Firebird.

They lived in `docs/design/` while their stage was running. They were moved here on **2026-08-11**
by the second Documentation Cleanup Sprint, because sitting in `docs/design/` implied they were
*binding specification* — and a "next session" prompt for a finished stage is the most misleading kind
of document a repository can hold: it reads as instructions.

⚠ **A path note, so nothing looks broken.** These files cross-reference each other by their *old*
location, e.g. `docs/design/product-polish-m3-handover.md`. They all moved **together**, so any such
reference means **this folder**. The internal text was deliberately **not rewritten** — a historical
document is archived, not edited (see `docs/history/README.md`, §0 "never lose information").

## ⛔ Do not plan from these

Read them for *why* a decision went the way it did — never for *what to do next*. Several of their
premises were **refuted by measurement** during the stages they were written for, which is itself the
most valuable thing in them:

- `product-polish-m4-migration-next-session.md` — **four** of its premises were refuted, including its
  claim that `GridSplitter` was the one parked item M4.4 would meet (there is not one in any of the 25
  windows). ⭐ Its main prediction was right, though: M4.4 turned out to be the acceptance of orphaned
  deferrals, in exactly the three files it named.
- `product-polish-m5-next-session.md` — records the three candidate next stages with measured scope.
  ⭐ Read it if the **spacing stage** or the **app-wide UX sprint** is ever picked up: the measurement
  (969 local spacing values, `Padding` reading a role zero times) is already done and does not need
  repeating.
- `product-polish-m3-handover.md` — the largest and most reusable: rules **R1–R18** (§5) and **21
  traps** (§9). ⭐ These are the Product Polish rules future UI work still inherits; `product-polish.md`
  and `color-language.md` are their live home, but the reasoning is here.
- `localization-app-stage-handover.md` · `localization-core-stage-handover.md` — the D‑3 migration recipe
  and the binding rules. ⭐ Useful if the ≈430 remaining hardcoded strings are ever picked up; the live
  contract is `docs/design/localization.md`.

## Files

| File | Stage | Status |
|---|---|---|
| `product-polish-m2a-handover.md` | Product Polish M2a (token catalog) | 🔒 closed |
| `product-polish-m2b-handover.md` | Product Polish M2b (base controls) | 🔒 closed |
| `product-polish-m2c-handover.md` | Product Polish M2c (de-localization sweep) | 🔒 closed |
| `product-polish-m3-handover.md` | Product Polish M3 — **rules R1–R18 + 21 traps** | 🔒 closed, still the reasoning source |
| `product-polish-m3-next-session.md` | entering M3 | 🔒 closed |
| `product-polish-m4-next-session.md` | entering M4 | 🔒 closed |
| `product-polish-m4-migration-next-session.md` | M4 screen migration | 🔒 closed, 4 premises refuted |
| `product-polish-m5-next-session.md` | entering M5 → the post-M5 candidates | 🔒 closed, measurements reusable |
| `localization-app-stage-handover.md` | Localization / App → Core | 🔒 closed |
| `localization-core-stage-handover.md` | Localization / Core+Firebird | 🔒 closed |

**Live equivalents:** rules and architecture → `CLAUDE.md`; status and open work →
`docs/current-state.md`; per-area specs → `docs/design/`.

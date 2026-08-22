# 34 — Licensing L9: the License Manager's identity (2026-08-22)

> **Why this file exists.** L9 gave the License Manager a wordmark and an About window. Both are small;
> what is worth keeping is **why each of them is shaped the way it is rather than the obvious way**, and the
> three guards that failed on their first run for one and the same reason. ⛔ It is not a description of the
> two surfaces — the code carries that, at length, in place.
>
> Branch `feat/licensing-system`. Commits: **`b0ab75c`** (L9.1 — About), **`681ac95`** (L9.2 — wordmark),
> plus the closing documentation commit. ⚠ Preceded by **`d553157`**, which wrote the ratified L10
> specification into `licensing-system.md` §60 *before* L9 started — see §5.

---

## 1. What the reconnaissance found, and how much of the plan it changed

The request was: make the application name look like a product mark rather than a `Label`, prefer
**white + orange**, and make the disabled *About* row work like EmberTern's. Four measurements moved the
plan before a line was written, and three of them contradicted a premise.

**1.1 ⚠⚠ EmberTern's accent is BLUE, and orange is the colour the palette was built to eliminate.**
`AccentColor` is `#2D6BBF` in both themes, and `Colors.axaml` *overrides* `SystemAccentColor` to that blue
with its whole six-step ramp — with comments in three places naming Windows' orange-brown as the problem.
The palette's two warm colours are both **semantic**: `TransactionActiveColor` ("an open transaction /
caution-pause") and `WarningColor`. So "white + orange" could not be satisfied by reuse; it needed a **new
role**, which is the one thing that makes it legitimate rather than a violation.

**1.2 ⚠ There was no in-product precedent to copy.** EmberTern's own title bar carries **neither** the
product name nor a logo: the mark was removed on 2026-08-01 with the reason recorded in `BRANDING.md` — *"a
working surface's chrome belongs to the document, and the identity belongs in About"* — and
`BrandingPresentationTests` pins it. That is what settled two of the user's three open questions before
they were asked: a **typographic** wordmark keeps that decision intact, a bitmap would reverse it.

**1.3 `AppInfo` was unreachable, and the manager was claiming to be the product.** `AppInfo` lives in
`EmberTern.App`, which this solution must never reference. Worse, `Directory.Build.props` declares
`<Product>EmberTern</Product>` for every project, so `EmberTern.LicenseManager.exe` had been asserting that
its product was EmberTern — in its Windows file properties, not only in a window nobody had built yet.

**1.4 The notices button could not be honest.** `THIRD-PARTY-NOTICES.txt` is the *product's* file: it lists
AvaloniaEdit and the Firebird client, which the manager does not carry, and does **not** list
`Microsoft.Data.Sqlite` / `SQLitePCLRaw`, which it does. A button there would have shown a truthful-looking
list that was wrong in both directions. ⏭ The trigger for a notices file of its own is the manager being
distributed outside this company — not a redesign of the window.

---

## 2. L9.1 — About: three decisions worth keeping

### 2.1 ⭐⭐ `ManagerInfo` duplicates a MECHANISM, never a value

`ManagerInfo` is a deliberate mirror of `AppInfo`: ~30 lines of reflection over the built assembly. The
duplication is real and it is bounded, and the bound is the whole justification — **both classes read
attributes MSBuild composed from the same `Directory.Build.props`**, so they are structurally unable to
disagree about the version or the release date. What is copied is the reading, not the fact.

Three alternatives were considered and each is recorded at the type, so nobody re-derives them:

| Alternative | Why not |
|---|---|
| reference `EmberTern.App` | ⛔ this solution must never acquire the product assembly |
| `<Compile Include="..\EmberTern.App\AppInfo.cs" Link="…" />` | it would put a type in namespace `EmberTern.App` **inside** this assembly — a lie about the architecture — and it needs an edit to a product file to compile at all, because its fallback reads `UiStrings` |
| a new shared project for forty lines | an abstraction with no second reason to exist |

⭐ The repository already contains the precedent, with its reason attached: `ThemeToggleIconConverter` is
mirrored "because it lives in an assembly this solution must not reference".

### 2.2 ⭐ One csproj line closed 1.3, and it needed no branching

`<Product>EmberTern License Manager</Product>` is the **only** identity value the project overrides.
Version, `ReleaseDate`, `Company` and `Copyright` stay inherited. The consequence worth noticing is that
`ManagerInfo.Product` then needs no `if`: each assembly declares who it is, and the same reflection answers
correctly in both. ⛔ Its fallback is the **assembly name** — a technical identifier, not a word — so there
is no literal to go stale and no catalog entry to own.

### 2.3 ⚠ The date is ISO, and that is a decision against the product's own call

EmberTern renders this one date in the reader's long-date pattern, and its `AboutViewModel` argues the case
in a comment: *"a single prominent date is exactly the case for the reader's long-date pattern."* The
manager does the opposite, because `terminology.md` §4.4 and §36.2 ratify ISO as **its** date form for every
date, and one exception would have been the only non-ISO date in the application. 🔒 The user accepted it at
closure rather than by default — it was surfaced as a decision, not buried.

⭐ Recorded in `DatePresentationTests.DeliberateIsoDisplayPaths` **with that reason**, which is exactly what
that guard asks of an author: say which side of the line the date is on. The alternative — excluding the
file from the scan — would have removed the question instead of answering it.

### 2.4 What the window deliberately does not have

No `Licensed to` line, no Debug-gate marker, no notices button. The first two are facts about a
**licensed product**, and this is the tool that issues the licences; the third is §1.4. ⛔ And no dialog
skeleton: `dialog-header` / `dialog-body` / `dialog-footer` are right for a window that *asks* something and
would have produced precisely the banded Win32 "About" look the product's own window exists not to be.

---

## 3. L9.2 — the wordmark: two facts that shaped the markup

### 3.1 ⭐⭐ Two values per theme, and the number that forced it

The palette's rule is that a token exists in both `ThemeDictionaries`. The interesting part is that here
they had to **differ**, and it was cheaper to establish on paper than on screen:

| | value | on `ChromeStrongColor` |
|---|---|---|
| Dark | `#F2A65A` | **6.81:1** |
| Light | `#A8480A` | **4.84:1** |
| ⚠ *one shared value, as first considered* | `#F0A458` on `#E8EAED` | **1.72:1** |

1.72:1 is a product name that is, in practice, not there. ⭐ The threshold is **4.5:1** and not 3:1,
because the large-text exemption starts at 18 pt (or 14 pt bold) and the mark is `Text.Title` — 14 px
SemiBold, below both. ⛔ So a test pinning the two values EQUAL would have been a test forbidding the very
thing the split exists for (`feedback-shared-value-is-not-a-dependency`), and the guard asserts they
**differ** and that each clears the threshold in its own theme.

### 3.2 ⚠⚠ One `TextBlock` with three `Run`s — because of baselines

The obvious construction is two blocks side by side: the mark at `Text.Title`, the descriptor at
`Text.Compact` with its own tracking. It is wrong, and the reason is a layout fact rather than taste: **two
blocks at 14 and 11 with different line heights align by BOX, not by baseline**, so the descriptor sits
visibly off the name's foot. Inline runs share one baseline for free.

The cost is accepted knowingly and stated at the style: **`LetterSpacing` is a `TextBlock` property, not a
`Run` one**, so the tracking applies to the whole mark rather than to the descriptor alone. That is a
smaller defect than a misaligned baseline, and it is the kind of trade that has to be written down or it
looks like an oversight.

⚠ `TextWrapping="NoWrap"` is load-bearing, not tidiness: the file's implicit `TextBlock` style sets `Wrap`,
and a wordmark that wraps inside a strip one row high is **clipped**, not wrapped.

### 3.3 ⛔ The rule the new token carries

`BrandEmberBrush` is **identity, never a signal**. The palette already has two warm colours that *mean*
something, and the first way this would break is somebody reaching for "the orange one" to decorate a
control state. So the guard is on the **consumer count** across both applications' markup, not on the
wordmark alone: a third signal nobody defined cannot appear without a red test.

---

## 4. The three guards that failed on their first run, all for one reason

Every failure L9 produced was in my own new guards, and three of the four were **the same defect**:
a text-scanning guard reading a **comment**.

| Guard | What it read | Why |
|---|---|---|
| `EveryIdentityValueComesFromTheBuild` | `<Product>` from a comment | the comment beside the override explains the change by quoting the value it REPLACED |
| `TheManagerDoesNotClaimToBeTheProduct` | same | same |
| `TheAboutRowIsEnabledAndWired` | `Main.NotAvailableYet` from a comment | the comment records that the tooltip was removed |

⭐ This is gotcha **#396**'s shape, and the new half worth carrying is *where* it bites hardest: **a comment
that documents a change by naming the old value sits, by definition, immediately beside the new one** — so
a first-match regex over a project file will prefer the prose. Repaired at the source (a `MarkupOf` that
strips XML comments, the shape `XamlLocalizationTests.CodeOf` already uses), ⛔ never by weakening an
assertion. Recorded as **#412**.

The fourth failure was smaller and is worth recording as a habit rather than a rule:
`NothingButTheWordmarkPaintsWithTheBrandColour` expected **5** consumers and measured **7**, because
`<SolidColorBrush x:Key="BrandEmberBrush" Color="{StaticResource BrandEmberColor}" />` names *both* keys and
therefore matches twice. My arithmetic was wrong; the guard was right. ⭐ The arithmetic is now written out
in the test, per-file, so the next person changes a number by arguing with a stated sum rather than by
adjusting a magic one.

⚠ And `LocalizationMechanismTests.TheCatalogs_AreActuallyFound` went red as designed — it is a **tripwire**,
and a new catalog is supposed to fail it and be added on purpose.

---

## 5. One process note: the specification was written down BEFORE the stage that precedes it

L10's specification was ratified in conversation and L9 was ordered first. Writing §60 immediately — as its
own commit, before touching any L9 file — was not bookkeeping: **a ratified design that exists only in a
transcript does not survive a session boundary**, and "we return to exactly that specification" needs
something to return to. ⭐ The general habit: when a decision is accepted for a *later* stage, it is recorded
at the moment of acceptance, in the document that owns the area, not when its implementation starts.

---

## 6. Verification at closure

- ✅ Build **0 warnings / 0 errors** — License Manager **Debug and Release**, EmberTern **Debug and Release**.
  ⚠ The product had to be built and swept because L9.2 edits `Colors.axaml`, which is a product file
  (the manager links it rather than copying it).
- ✅ License Manager suite **798 / 798** (777 at entry → 790 after L9.1 → 798 after L9.2).
- ✅ EmberTern suite **9 090 / 9 092** — ⚠ both reds **pre-existing** (§49.9 / §57.8) and neither is L9's:
  `CharsetGuardSeamTests` matches a **comment** in the manager's csproj (#396 again; the comment is
  byte-identical to before L9 — only its line number moved, 105 → 125), and `DatePresentationTests` names
  `RestoreWorkflow.cs` + `StorageViewModel.cs`.
- ✅ **Liveness proved by injection in BOTH new headless files** (#374 / #391) — one injection per file, not
  one per assertion: the discarded-`Task` defect is a property of the file's shape, and the user's standing
  directive is that verification is proportionate to the stage.
- ✅ **User-confirmed visually**, Dark and Light, both surfaces. 🔒 Accepted 2026-08-22, with three decisions
  ratified on the way: no glow and no bitmap, ISO for the release date, and the ellipsis on the menu row.

---

## 7. What L9 leaves behind

| Item | Note |
|---|---|
| `BrandEmberColor` / `BrandEmberBrush` | ⛔ **Identity, never a signal.** One consumer, guarded by count. A second consumer means the rule was broken, not the catalog extended. |
| `TextBlock.display` | The `Text.Display` role's second consumer in the repository — and its comment already says a *third* would mean the rule was broken. Typography.axaml reserves that scale for an About window, which is what this is. |
| `ManagerInfo` | ⛔ Never a version literal, in either project. Two guards say so here, mirroring `AppInfoTests` — whose own sweep is scoped to `src/EmberTern.App` and therefore never watched this project. |
| ⚠ `CLAUDE.md` is over budget | Measured **825 lines** at closure against a stated ~700 target and an 800-line tripwire. ⛔ **Not fixed here** — a cleanup is its own task, and L9 added one clause on purpose (the token in the cheat-sheet). Reported to the user rather than started. |

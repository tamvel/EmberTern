# Hamburger Navigation / Application Menu — sprint design document

**Status: 🔒 SPRINT CLOSED — all five etaps DELIVERED and USER-ACCEPTED (2026-07-29), merged to `master`.**
Branch: `feat/hamburger-navigation`. Build 0/0; suite **5971** green (partitions 5917 + 54); smoke clean.

As-built, per etap: **§12** (menu + hamburger, incl. three icon QA rounds) · **§13** (About + the single version
source) · **§14** (third-party notices) · **§15** (Keyboard Shortcuts + `CommandDescriptor.Title`). The decisions
log is **§11**. Sections §1–§10 are the original analysis and design, kept as the record of *why* — including the
options that were considered and rejected, which is the part that stops a future session re-opening them.

⛔ **Closed and not to be re-litigated:** `Icon.Menu`'s geometry (§12.2d) · the 0.x versioning · About's content ·
the absence of liability wording · the decision not to give the menu's rows `CommandId`s (§7).

⚠ **ONE statement in this document is now historical: `Settings…` is no longer disabled.** Settings Center
etap 3 (2026-07-29) built the window, so the same etap enabled the row and deleted its *"Not available yet"*
tooltip — which is this document's own rule (*"a row never ships ahead of what it opens"*) reaching its other
half. §4 A / §5 / §12's disabled-row reasoning is kept verbatim as the record of why the row shipped that way;
for what the row does today see [`settings-center.md`](settings-center.md) §13. Nothing else here changed.

**Round 3:** the Keyboard Shortcuts window is **in this sprint** (etap 5), designed as the target browsing
window rather than a static list — §8.5. It surfaced the sprint's one registry change (§8.5.1).

**Round 4 — every open question is closed; etap 2 is authorised.**
**Version `1.2.0`**, the first version the project declares and from now on the single source of truth (§8.2) ·
the **Dark theme row is cut**; the titlebar button stays the only place the theme changes, and the theme moves
*additionally* into Settings when Settings exists (§4 B) · **`CommandDescriptor.Title` accepted**, text from
`UiStrings`, **no string literals in `CommandCatalog`** (§8.5.1) · a `Description` field was considered and
**declined for now** under gotcha #233 (§8.5.1a) · **column sorting is the user's, but clearing it — and first
open — always restores the canonical order** (§8.5.4).

**Ratified by the user in round 2 — do not re-litigate:** no separator between the hamburger and the rest of
the toolbar (§6) · the About window is a **product** window, not a diagnostic one — logo, name, version,
author, copyright and nothing else (§8) · the version is **read from the assembly**, never typed (§8.2) ·
library names stay off the About face; the notices live behind a secondary surface (§9.6) · **no liability,
warranty or privacy wording anywhere at this stage** — that belongs to the future EmberTern licence, not to
About (§9.7) · `Open Log File` / `Open Settings Folder` are **dropped** (§5.5).

Three questions remain open — §11.

This document is the sprint's single home, following `keyboard-manager.md`: the analysis (§1–§2), the design
(§3–§8), the licence findings (§9), the etap plan (§10). A future session starts any etap from here.

---

## 0. What this sprint is, in one line

Not "add a hamburger button". Build the **one place a user reaches EmberTern's application-level functions**
— so that Settings, Keyboard Shortcuts, About, diagnostics, documentation and licence information have a home
that does not have to be redesigned when each one arrives.

**The scope boundary, ratified by the user up front:** this is a *rarely used administrative* menu. Daily work
stays on the toolbar, the shortcuts and the context menus. A command that belongs to the active document
never appears here.

---

## 1. Method

Everything below was read out of the code or measured from a shipped artifact, not recalled. The licence
findings in §9 were taken from the `.nuspec` and licence files in the NuGet cache and from the repository's own
`ICONS.md` — nothing in §9 is from memory, and where I could not verify something from an artifact I say so.

Surfaces swept: the whole titlebar/toolbar (`MainWindow.axaml:41-322`), `CommandCatalog` + `CommandId` +
`CommandScope` + `CommandTip`, the menu style set (`ControlStyles.axaml:552-659`), `MenuMarkup.cs`, every
`MainWindowViewModel` global command, `Program.cs`, `Directory.Build.props`, all four `.csproj` files, the
branding assets, and the repository root.

---

## 2. Verified facts that constrain the design

### 2.1 There is no Settings surface anywhere in EmberTern — verified

`grep` for `SettingsDialog` / `SettingsWindow` / `SettingsTab` / `OpenSettings` across `src/` returns **no
source hit** (only two compiled third-party DLLs). Settings today are implicit and scattered: theme is a
titlebar button, Developer Mode is per-connection, grid layouts persist themselves, workspace state persists
itself. `UserSettings` + the eight DPAPI-backed store facades exist, but no UI ever presents them as
"settings".

**Consequence:** `Settings…` in this menu is a genuine placeholder for a feature that is *decided but
unbuilt* — which is exactly the case where a disabled row is honest (contrast §5.1).

### 2.2 The menu appearance already exists and needs no addition — but the *host* must be measured

Keyboard Manager etap 5 established the app's one menu appearance as a **style set**, not a control
(`ControlStyles.axaml:552-659`): `ContextMenu` chrome, 22px rows at FontSize 12, palette hover/selection, a
14px icon column, a subordinate gesture column, real separators, and — deliberately — **readable disabled
rows**, "because it is how a menu explains that an action exists but is not available right now". That last
decision is what makes §4's disabled `Settings…` work as designed rather than as a defect.

Two hosting options, and they are **not** equivalent:

| Option | Chrome reuse | Cost |
|---|---|---|
| `Button.ContextMenu`, opened on click with `Placement=BottomEdgeAlignedLeft` | **total** — the selector is literally `ContextMenu` | must confirm click-to-open + placement behaviour |
| `Button.Flyout` = `MenuFlyout` | partial — `MenuItem` selectors apply, but the presenter is `MenuFlyoutPresenter`, which no style targets, and `ContextMenu > Separator` would not match | a second chrome variant in `ControlStyles.axaml` |

**Recommendation: `ContextMenu`.** The `MenuFlyout` path would add exactly the kind of second variant the
`MessageBanner` QA round had to undo (CLAUDE.md's standing rule). ⚠ **This is a measurement obligation, not an
assumption**: gotcha #285 records that a *negative* measurement on a menu-bar `MenuItem` nearly justified
building a control the framework did not need. Etap 2 must open a real menu and confirm (a) left-click opens
it, (b) placement is under the button, (c) the existing chrome and separators apply with **zero** new style,
before any menu content is written. A menu-**bar** `Menu`/`MenuItem` host is ruled out for the same reason: its
top-level item is templated differently and has no icon or gesture part.

### 2.3 The build declares no version — verified

`Directory.Build.props` sets `TargetFramework`, `LangVersion`, `Nullable`, `ImplicitUsings`,
`TreatWarningsAsErrors`, `WarningsNotAsErrors` — and **no `<Version>`**, no `<InformationalVersion>`, no
`<Company>`, no `<Product>`, no `<Copyright>`. So today every assembly reports `1.0.0.0` by default.

**Consequence:** an About dialog cannot display a truthful version until the build declares one. This is an
open question for the user (§11.1), not something to invent.

### 2.4 EmberTern sends no data anywhere — verified, and it is worth saying out loud

`grep` for `HttpClient` / `WebRequest` / `Socket(` across `src/` returns **nothing** (the Firebird wire
protocol is inside the driver). The only file EmberTern writes outside its settings store is
`%TEMP%\EmberTern-debug.log`, on this machine (`Diagnostics/BatchTrace.cs`, `PerfTrace.cs`, `RefreshTrace.cs`,
plus the connection-attempt log). For a tool pointed at production databases that fact is a trust asset —
see §9.6.

### 2.5 The Inter font ships and is rendered — verified

`Program.cs:48` calls `.WithInterFont()`, and `Avalonia.Fonts.Inter.dll` is in the build output. So the font is
not an unused dependency that could be dropped to sidestep §9.4; it is part of the product's appearance.

### 2.6 The toolbar's first action slot is occupied by the sidebar toggle

`MainWindow.axaml:116-126`: titlebar column 1 is the action zone; it opens with a 1px divider
(`Border Width="1" Margin="4,6"`) separating brand from actions, then the sidebar toggle
(`Icon.PanelLeft`, `SidebarToggleTooltip`), then New Connection, and so on. That divider is the anchor the
hamburger's placement has to reckon with (§6).

---

## 3. Design principles

1. **Global only.** An entry qualifies if it is meaningful with no connection and no active document, or if it
   is about the *application* rather than the data. Everything else stays where it is.
2. **Rarely used by design.** The menu must not become a second navigation system. If an entry would be
   reached daily, it belongs on the toolbar — and if it is already on the toolbar, mirroring it here buys
   discoverability at the price of a second enable/disable surface to keep in step.
3. **No placeholder without a decision behind it.** A greyed row is a promise. `Settings…` is a promise the
   project has already made (§2.1); "Check for Updates" is not (§5.1).
4. **No new design system.** The etap-5 style set is the appearance, unchanged and unextended (§2.2).
5. **A gesture is written down once.** Anything shown in this menu takes its key from `CommandCatalog` via
   `{app:CommandGesture}` — never typed (gotcha #284). A command with no gesture shows an empty column, which
   is the extension's designed behaviour, rather than a hand-typed OS key.
6. **Architectural readiness lives in the command layer, not in a menu model** (§7).

---

## 4. Proposed structure — v1

Flat, with separators. **Not** submenus: at eight rows a submenu costs a hover-and-wait for no grouping the eye
does not already get from adjacency, and every submenu is one more place a future entry can be filed wrongly.
(Firefox's ☰ is the reference here, not JetBrains' File/Edit/View mirror of a menu bar — EmberTern has no menu
bar to mirror.)

The user's round-2 constraint is **"only the most important application functions"**, which settled three of
the six proposed rows: D and E are dropped (§5.5), and it moves my own recommendation on B (§11.2).

```
☰
┌──────────────────────────────────────────┐
│ ⚙  Settings…                  (disabled) │   A — placeholder, §2.1 + §2.2
├──────────────────────────────────────────┤
│ ⌨  Keyboard Shortcuts…                   │   C — a view of CommandCatalog
│ ℹ  About EmberTern…                      │   F — this sprint's deliverable
├──────────────────────────────────────────┤
│ ⏻  Exit                                  │   G — routes through the existing close guard
└──────────────────────────────────────────┘
```

Five rows, two separators: the configuration slot, the two "learn about this application" rows, and leaving.
That grouping is why `Keyboard Shortcuts` and `About` sit together — both answer a question about the app
rather than doing something to the data.

**Row by row, with the reasoning:**

**A · Settings…** — disabled, tooltip "Not available yet". Kept because the user asked for it *and* because it
is the one placeholder with a decision behind it. The etap-5 readable-disabled rule makes it legible instead
of a ghost. When Settings arrives it opens here and nothing about the menu changes.

**B · Dark theme — CUT (ratified round 4).** The titlebar button stays the only place the theme changes; when
Settings exists the theme moves *additionally* there, which is its proper home. The reasoning, kept because it
reversed my own round-1 position: the user's round-2 constraint. The theme is already a **one-click titlebar button that is always visible**, so a menu row
adds no reach — it only adds a second owner of one action (and a code-behind handler that would have to be
promoted to a command to serve both). Under "only the most important functions", a duplicate of a visible
button is the weakest row in the menu. It arrives naturally, and properly, the day `Settings…` opens: theme is
a *setting*, and its real home is the Settings window. See §11.2.

**C · Keyboard Shortcuts…** — the highest-value Help entry in the list and *architecturally almost free*:
`CommandCatalog` already holds every id, scope and gesture, and `CommandTip.Format` already renders a gesture
as text (which is why it must not be `KeyGesture.ToString()` — that would print "Ctrl+OemPeriod"). A shortcuts
window is a **read-only projection of the registry**, so it cannot drift, and it is the exact future consumer
the Keyboard Manager document names. Filed here rather than under a Help submenu because there is no submenu.
**Built in this sprint (round 3) — the full design is §8.5**, including the one thing the catalog is missing
for it (§8.5.1).

**F · About EmberTern…** — §8.

**G · Exit** — kept, because an Application Menu without it reads as incomplete, and because it must route
through `MainWindow`'s **existing** app-close flow (the three-way Save / Discard / Cancel guard plus the
transaction settle), not a second shutdown path. **No gesture is displayed**: EmberTern does not own `Alt+F4`,
so `AppExit` is declared in the catalog with no gesture and the column stays empty — showing a hand-typed
`Alt+F4` is precisely the drift gotcha #284 exists to prevent.

**Icons:** every row uses `{app:MenuIcon …}` over existing geometries where one fits. `Icon.Settings`(⚙),
`Icon.Info`(ℹ), `Icon.Power`(⏻), `Icon.Keyboard`(⌨) and a moon/sun for B may not exist yet — etap 2 checks
`IconGeometries.axaml` first and adds Lucide geometries only where nothing fits (the ICONS.md reuse rule).
The theme row can reuse whatever `ThemeToggleIconConverter` already resolves.

---

## 5. What I recommend leaving out, and why

You asked me to verify your first ideas rather than implement them. Three of them I would not ship, and one
group I would not move.

### 5.1 "Check for Updates" — omit entirely

There is no update mechanism, no distribution channel, and no decision about either. A disabled
"Check for Updates" promises a channel that does not exist, and when the channel *is* decided it will bring its
own UX (a banner? a background check? a download?) that a menu row may not be the right surface for. This is
principle §3.3: `Settings…` is decided-and-unbuilt, updates are undecided. Add the row in the sprint that adds
the mechanism.

### 5.2 "Documentation" — omit for now

EmberTern has no user documentation. `docs/` is a developer archive, partly Polish, written for whoever
implements the next milestone — pointing a user at it would be worse than pointing at nothing. The repository
is private, so a URL is not an option either. The row is cheap to add the day a user manual exists; a dead row
teaches users that this menu lies.

### 5.3 "Report Issue" — omit for now

Both remotes are development remotes (company Gitea, personal GitHub); neither is a user-facing intake, and
there are no external users yet. If support ergonomics are the real goal, the **Copy environment info** button
in About (§8.3) delivers most of the value now, and a proper intake channel can add the row later.

### 5.4 The six existing tools — do **not** move or mirror

Security Manager, Activity Monitor, Session Manager, Global Search, Script Executor and Data Import are all
**connection-scoped workspace tools**, not application functions, and all six are one toolbar click away with
a tooltip. Mirroring them here would (a) contradict "rarely used" on day one, (b) duplicate six enable/disable
conditions into a surface that has to stay in step with them, and (c) invite the question "why these six and
not the ten New-object buttons". The etap-5 consistency rule — *the same surface offers the same basic
operations whichever way you reach them* — is about one surface (a grid and its own menu), not about mirroring
the toolbar into an application menu.

**So v1 has no Tools group at all.** When one is justified it will be by a genuinely application-level tool —
the **Command Palette** and the **shortcut editor** the Keyboard Manager document already anticipates.

### 5.5 `Open Log File` / `Open Settings Folder` — dropped by user decision (round 2)

My §4 D/E proposal is withdrawn. The user's reasoning, and it is the right cut: *these are not functions an
average user looks for in an application menu*, and the menu should stay as simple as possible. They cost
nothing to add later if a support need makes the case — and the argument for them (§9 of the settings-health
audit: "where is that file?") is better answered by the health banner naming the path than by a menu row a
user has to know to look for.

---

## 6. Placement

> ⚠ **AMENDED 2026-08-01 (Branding UX sprint): the `[logo]` in this section's diagrams is GONE.** The brand
> mark was removed from the titlebar on purpose — chrome belongs to the document, not to the product's
> identity, and the mark plus its divider were spending ~40px of the most contested horizontal space in the
> window. The row now reads `[active connection] │ ☰ ⊟ ＋ …`, and with **no** connection it reads
> `☰ ⊟ ＋ …` — the whole connection block, its margin and the divider after it collapse together, so nothing
> is left standing where the mark used to be. **The hamburger's placement decision below is unchanged**: it
> is still the first button of the action zone, still unfenced. The logo now lives only in About (§9.6), which
> is what that section was already arguing it deserved.

**Decided (round 2):** `[logo] │ ☰ ⊟ ＋ …` — the hamburger is **simply the first button of the action zone**,
immediately followed by Show/Hide Connections, with **no separator between them**. Nothing else in the layout
moves.

My round-1 recommendation (hamburger on the brand side, ahead of the divider) is **withdrawn**: it would have
put a separator between the hamburger and the rest of the icons, which is exactly what the user rejected — and
the reference is right, ChatGPT and its peers do not fence the hamburger off, and it reads better. The
existing brand/action divider (§2.6) is **untouched**: it still separates brand from actions, and now simply
falls *before* the hamburger instead of after it. So this adds no `Border` and removes none.

It is a plain `Button Classes="icon"` with the same 26px slot, the same `SvgIcon` and a tooltip — no new
button variant.

---

## 7. Architecture

**The menu's readiness for the future comes from the command layer, not from a menu model.**

**⚠ Amended during etap 2 — no `CommandId` entries are added, and the reason is the registry's own admission
rule.** The round-1 plan was to declare `AppSettings`, `AppAbout`, `AppExit` and `AppKeyboardShortcuts` at
`CommandScope.Global`. Reading `CommandId`'s own contract before writing them showed that none qualifies yet:

> *"A command earns a `CommandId` only when a shared surface must speak about it — a gesture, a menu entry with
> a shown shortcut, or (later) a palette entry."*

These four have **no gesture** and their menu rows show none (§4 G), so a declared id would be a dead enum
member with no resolver, no gesture and no `Title` consumer — the dead-surface trap of gotcha #233, in the one
file the sprint is otherwise trying to keep authoritative. The rows therefore bind directly: `Settings…` is
`IsEnabled="False"`, and `Exit` is a `Click` handler calling the window's own `Close()`.

**Nothing is foreclosed.** They earn ids the moment a surface must *speak* about them — the Command Palette
being the obvious one — and that is four `Command="{Binding …}"` substitutions, not a rebuild. This is the same
discipline the user applied to `Description` in §8.5.1a: declare it when it has a consumer, not before.

Where the readiness genuinely lives is unchanged: `MainWindowViewModel.ResolveCommand` is the **existing**
Global-scope seam, so a future application command joins it without a new mechanism, and the collision
validator sees every declared gesture for free.

**What is deliberately *not* built: an `ApplicationMenuCatalog`.** A declarative menu-entry table is tempting
under "prepared for the future", but it would be a **second** declaration system beside the 32 XAML menus this
app already has, and growth is not cheaper: adding `<MenuItem Header="…" Icon="{app:MenuIcon …}"
Command="{Binding …}" />` costs exactly what adding a table row costs, and stays consistent with every other
menu in the product. This is etap 5's lesson applied one level up — *don't build the abstraction the framework
already gives you*.

The obvious objection is a future **Command Palette**, which needs to enumerate everything invocable. But a
palette enumerates **commands**, and the authority for those is already `CommandCatalog`. So the palette's
source of truth exists and this menu is a *consumer* of it, exactly like the toolbar, the tooltips and the
context menus. Nothing here has to be rebuilt for it.

**Enable/disable:** `Settings…` is `IsEnabled="False"` in XAML with a tooltip. Not a fake command with
`CanExecute => false` — that would put a lie in the command layer to produce a visual state the view can state
honestly.

---

## 8. About EmberTern — design

**Ratified in round 2: this is a product window, not a diagnostic one.** The environment block, the library
names and every disclaimer are **out**. What remains is the identity of the application and who made it — and
that is the whole design brief.

One dialog, reusing the existing dialog skeleton (`BackgroundBrush`/`ForegroundBrush` root, `Button.flat`
footer) — no new dialog pattern. Modal, fixed size (~420 × 300), not resizable, `Escape` closes, opened
`CenterOwner`.

### 8.1 The face

```
        ┌────────────┐
        │            │
        │    logo    │            ~96px, centred
        │            │
        └────────────┘

           EmberTern              20px, SemiBold
          Version 1.2.0           12px, SubtleForegroundBrush     ← read from the assembly, §8.2

       ─────────────────          hairline, BorderBrush, ~60% width

         Grzegorz Groński         12px, ForegroundBrush
   © 2026 Grzegorz Groński.       11px, SubtleForegroundBrush
      All rights reserved.

  Third-party notices                                  [ Close ]
```

- **Centred, vertical, no label column.** A two-column `Author: …` / `Copyright: …` form is what makes an
  About box look like a settings page. Five centred lines under a logo is the modern desktop idiom, and it is
  what "simple and elegant" asks for.
- **No tagline.** Round 1 proposed one; cutting it is more consistent with the brief than keeping it — the
  logo and the name already say what this is, and a marketing line on an otherwise bare window reads louder
  than it should. Trivially added later if you want it.
- **Logo** from `Assets/Branding/`, `Stretch="Uniform"` in a square slot. Etap 3 picks the file by pixel size
  so a ~96px slot is not upscaling a 26px-grade asset.
- **No mention of Claude Code or ChatGPT.** This is the author's window.
- **`All rights reserved`** is a *reservation*, not a licence grant, so it does not pre-empt the licence being
  deferred.
- **`Third-party notices`** is a discreet `Button.flat` in the footer, left of `Close` — see §9.6. Deliberately
  **not** a tab strip: a `TabControl` on a five-line window makes it look like a configuration dialog, which is
  the one thing this design is avoiding. The notices open in their own scrollable window.

### 8.2 The version is read from the assembly — never typed

**Requirement (user, round 2): releasing a new version must not touch the About window's code.** So:

1. `Directory.Build.props` gains **`<Version>1.2.0</Version>`** — ratified in round 4 as the first version the
   project declares and, from then on, **the single source of truth** — plus `<Product>`, `<Authors>` and
   `<Copyright>` for the file properties Windows shows in Explorer (free, and the same one source).
2. The window reads `AssemblyInformationalVersionAttribute` from the **entry assembly** at runtime, falling
   back to `AssemblyName.Version`.

⚠ **One trap that must be handled, not discovered later.** Since .NET 8 the SDK appends the source-revision
hash to the informational version, so `1.1.0` is emitted as **`1.1.0+9a3f2c1…`** and a naive About box would
display the hash. Two independent defences, both cheap:
`<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>` in
`Directory.Build.props`, **and** truncating at the first `+` in code. The code-side cut is what makes the
display correct even if the build property is later removed by someone who does not know why it was there.

### 8.3 Room left for what is deferred

The hairline between the name block and the author block is the obvious slot for a future **licence line**
("EmberTern Licence…"). Adding it later is one row, not a redesign — which is the point of designing this
window before the licence exists.

---

## 8.5. Keyboard Shortcuts window — design

**Promoted to a firm etap in round 3** (user reversed the deferral: the catalog exists, so building it later
costs more than building it now). Browsing only — **no editing**, which may come one day.

### 8.5.1 ⚠ The one gap between "projection only" and today's catalog — and it needs a decision

**`CommandDescriptor` has no name.** Verified: its fields are `Id` (an enum), `Scope`, `Dispatch`, `Gesture`,
`AlternateGesture`, `TabKinds` — and `CommandTip`'s own documentation states the rule deliberately:

> *"The label text stays in `UiStrings` (architecture rule #6) and is passed in — one `CommandId` serves eleven
> differently-worded Compile buttons, so the text cannot live on the descriptor. Only the gesture comes from
> the catalog."*

So a **Command** column has no source today. Two ways to satisfy "no hand-typed names", and they are not
equally good:

**(a) Derive the label from the enum name** (split PascalCase). Truly zero declaration — and the output is
poor: `FormatSql` → "Format Sql" (wrong casing, and unfixable without a per-command exception, which is a
hand-typed name wearing a different hat), `CollectionAdd` → "Collection Add" (the app calls it "New field" /
"New row"), `Go` → "Go" (meaningless stripped of its tab). A shortcuts window whose labels read worse than the
tooltips is not worth shipping.

**(b) ⭐ Recommended — add one canonical `Title` to `CommandDescriptor`, its text in `UiStrings`.** Declared
once, in the registry, beside the gesture; the window reads it and types nothing.

**Why (b) does not re-open the ratified etap-4 decision** — this matters, because that decision is recorded in
CLAUDE.md and I am not proposing to reverse it. Etap 4 rejected a single text field **for tooltips**, because
eleven Compile tooltips are host-specific *prose* ("Compile the procedure", "Compile and save the trigger").
A shortcuts catalogue needs the opposite thing: **one canonical, host-independent name** ("Compile"). Those are
two different jobs, and etap 4 only ruled out collapsing the first into the descriptor. The eleven tooltips
keep their own `UiStrings` members and keep passing them to `CommandTip.For` — nothing about them changes.
The words still live in `UiStrings` (rule #6); the descriptor holds the reference.

And it is not a single-consumer field: the **Command Palette** the Keyboard Manager document anticipates needs
exactly this same canonical name. Adding it now is building the thing that was always missing, not padding the
record for one window.

**The guard that makes it a projection rather than a parallel list:** a test asserting **every** descriptor has
a non-empty `Title`, so a command added to the catalog without one fails the suite — the same shape as etap 5's
icon-consistency test, and the reason a future command cannot silently appear in this window as a blank row.

**Accepted (round 4)**, with the user's own framing recorded because it is the clearer statement of it: a
tooltip and a canonical command name solve **two different problems** — the tooltip stays context-dependent and
keeps using `UiStrings` directly; `Title` is the one canonical name used by Keyboard Shortcuts, the future
Command Palette, and any future command search. **The text still comes from `UiStrings`; no string literal goes
into `CommandCatalog`.**

### 8.5.1a `Description` — considered, and declined for now

The user asked for a short `Description` alongside `Title` **only if it would have an immediate consumer**, and
otherwise not at all, per gotcha #233. **It would not, so it is not added — `Title` alone.**

The honest accounting: `Description`'s real consumer is the **details pane**, which §8.5.5 deliberately does not
build. The only other candidate is the row tooltip, and that would be a *manufactured* consumer — it would
oblige us to write 38 descriptions now, most of which would restate their own title ("Compile — compiles the
object"), which is noise, and noise is worse than an absent field. The row tooltip already has real content
that needs **no new field at all**: `TabKinds`, which the catalog already holds.

Nothing is lost by waiting. Adding it later is one optional record parameter with a default plus its
`UiStrings` members, and the `Title` guard test extends to cover it in one line — there is no restructuring
cost to defer.

### 8.5.2 Layout

```
┌─ Keyboard Shortcuts ─────────────────────────────────────────┐
│                                                              │
│  🔍 Search commands and shortcuts…                           │
│                                                              │
│  ┌────────────────────────────┬──────────────┬────────────┐  │
│  │ Command                    │ Shortcut     │ Scope      │  │
│  ├────────────────────────────┼──────────────┼────────────┤  │
│  │ Commit                     │ F6           │ Global     │  │
│  │ Rollback                   │ Shift+F6     │ Global     │  │
│  │ Global Search              │ Ctrl+Shift+F │ Global     │  │
│  │ Close Tab                  │ Ctrl+W       │ Global     │  │
│  │ Execute                    │ F5           │ Tab        │  │
│  │ Compile                    │ F7           │ Tab        │  │
│  │ …                          │ …            │ …          │  │
│  │ New field                  │ F3, Insert   │ Grid       │  │
│  │ Quick Fix                  │ Ctrl+.       │ Editor     │  │
│  └────────────────────────────┴──────────────┴────────────┘  │
│                                                              │
│  37 commands                                      [ Close ]  │
└──────────────────────────────────────────────────────────────┘
```

Search on top, list below, count bottom-left — as specified. Sizeable window (~720 × 560), resizable (a table
that can be widened is worth more than a fixed frame), `Escape` closes.

**The control is the shared `DataGrid`** (`Avalonia.Controls.DataGrid`, already referenced) with the app's
existing `DataGridRow` / `DataGridCell` / `DataGridColumnHeader` styles — no new table pattern, and clicking a
header to re-sort comes free. Read-only (`IsReadOnly=True`), no row editing, single selection.
Columns: Command `*`, Shortcut `Auto`, Scope `Auto`.

### 8.5.3 Which commands appear

**Only commands that have a gesture** (`Gesture is not null || AlternateGesture is not null`). It is a
*shortcuts* window; a gesture-less command has nothing to show. This also means the four hamburger ids from §7
filter themselves out with no special case.

**`Dispatch.Reserved` commands are included** — and that is deliberate, not an oversight. Reserved means
"dispatched by the control that owns it", not "internal": `F10` Step Over, `Ctrl+Space` completion, `Tab`
expand-construct and `F9` toggle-breakpoint are exactly the keys a user wants this window to tell them about.

**One row per command, not per gesture.** `CollectionAdd` (F3 + Insert) and `CollectionRemove` (F8 + Delete)
show both in one cell, so the footer count means *commands*, matching its own label.

**Every gesture is rendered by `CommandTip.Format`** — the app's one gesture→text composer. Never
`KeyGesture.ToString()`, which would print `Ctrl+OemPeriod` (its own remarks say so).

### 8.5.4 Default order and filtering

**Order:** scope rank `Global → Tab → Tree → Grid → Editor`, then by command name.

⚠ **That is the reverse of `CommandScope`'s numeric order**, which encodes *resolution precedence*
(Editor > Tree > Grid > Tab > Global) — so this window must **not** sort by the enum value. It needs its own
explicit display rank, plus a test that every `CommandScope` member has one, so adding a scope later fails the
suite instead of silently sorting to the bottom. The user's framing, worth keeping verbatim: **this is the order
for the user, not the order that falls out of the implementation.**

**Ratified (round 4): the canonical order is the resting state.** Clicking a column header sorts by that
column, as any table should — but **clearing the sort, and every first open, returns to
`Global → Tab → Tree → Grid → Editor → alphabetical`.**

Two implementation consequences, both worth writing down before etap 5 starts:

1. The canonical order is **not expressible as a single-column sort** (it is scope-rank *then* name), so it can
   never be delegated to the grid's own sorting. The view model owns the ordered collection and the grid's user
   sort is an overlay on top of it — which is also what makes "clear ⇒ canonical" fall out naturally rather than
   needing to be reconstructed.
2. ⚠ **Whether Avalonia's `DataGrid` even offers a third "unsorted" state must be measured, not assumed** — WPF
   clears a sort with Ctrl+click, and this grid's header cycle may be ascending → descending only. If there is
   no clear gesture, "first open" is the whole requirement and an explicit affordance is a separate decision, not
   a silent omission. This is the same discipline §2.2 applies to the menu host.

**Filtering:** live, case-insensitive `Contains`, over all three displayed fields — command name, **the
rendered shortcut text**, and scope name. Matching on the rendered text is what makes the shortcut column
searchable in the way people actually search it (`ctrl` finds every Ctrl binding, `f5` finds F5), and because
it is the *same string that is displayed*, what you see is what you searched.

⚠ Worth stating so nobody flags it later: `Contains` here does **not** contradict the Completion Matching
milestone's prefix-first rule. That rule governs *code completion*, which is explicitly "a prediction engine,
not a search engine". This is a search box, and substring matching is what a search box owes the user.

**Footer count** reflects the *filtered* rows, from `UiStrings` with singular/plural handling ("1 command").

### 8.5.5 Prepared for the right-hand pane — without building it

The user's requirement is that the architecture must not obstruct a future details pane (description, scope
explanation, rebinding), and that the pane may be omitted while it has nothing worth showing.

**It is omitted in v1** — an empty pane would be exactly the dead surface gotcha #233 warns about (deliberately
not shipping empty groups is the precedent). What makes it cheap later is one decision taken now: **each row is
a real `KeyboardShortcutRowViewModel`, not an anonymous tuple or a formatted string.** A details pane is a
projection of the selected row, so with a typed row and the `DataGrid`'s own selection the pane becomes a second
grid column binding to `SelectedItem` — a column, not a rewrite. No unused property is added ahead of its
consumer.

Two pieces of content are already waiting for it, and both are in the catalog: **`TabKinds`** (which tabs a
Tab-scoped command exists on) and a plain-language explanation of each **scope**. For v1 `TabKinds` can go in
the row's tooltip, which is honest and costs one binding.

### 8.5.6 Modal or not

**Recommended: modal**, consistent with every other window in the app and simplest. The honest trade-off: a
modal reference window cannot be kept open while you try the shortcut you just read, and non-modal would be
nicer — but it costs single-instance tracking and owner-lifetime handling. It is a `ShowDialog` → `Show` change
plus an instance guard if you ever want it, so nothing here forecloses it.

---

## 9. Legal / third-party — findings and proposed presentation

You asked me to verify, not guess. Everything in §9.1–§9.4 was read from a shipped artifact; §9.5 is
explicitly marked as unverified.

**Method:** the licence expression, licence file, copyright and project URL from each package's own `.nuspec`
in `C:\Users\grzegorz.gronski\.nuget\packages`, cross-checked against the assemblies actually present in
`src/EmberTern.App/bin/…/net9.0`.

### 9.1 Everything shipped is MIT — except one thing. And MIT *is* an obligation.

| Component (shipped assembly) | Version | Licence | Copyright per nuspec |
|---|---|---|---|
| Avalonia (+ Desktop, Themes.Fluent, Fonts.Inter, Skia, HarfBuzz, Win32, X11, Native, OpenGL, Vulkan, Metal, MicroCom, FreeDesktop, Dialogs, Markup.Xaml, Remote.Protocol) | 12.0.3 | **MIT** (expression) | Copyright 2013-2026 © The AvaloniaUI Project |
| AvaloniaEdit | 12.0.0 | **MIT** | Copyright 2017-2026 © The AvaloniaUI Project |
| Avalonia.Controls.DataGrid | 12.0.0 | **MIT** | Copyright 2013-2026 © The AvaloniaUI Project |
| CommunityToolkit.Mvvm | 8.4.2 | **MIT** (+ ships `License.md`, `ThirdPartyNotices.txt`) | (c) .NET Foundation and Contributors |
| DocumentFormat.OpenXml (+ .Framework) | 3.1.0 | **MIT** | © Microsoft Corporation |
| ExcelDataReader | 3.7.0 | **MIT** | ExcelDataReader developers |
| System.IO.Packaging | 9.0.18 | **MIT** (+ `LICENSE.TXT`, `THIRD-PARTY-NOTICES.TXT`) | © Microsoft Corporation |
| System.Security.Cryptography.ProtectedData | 9.0.0 | **MIT** (+ same two files) | © Microsoft Corporation |
| SkiaSharp | 3.119.4-preview.1.1 | **MIT** | © Microsoft Corporation |
| HarfBuzzSharp | 8.3.1.3 | **MIT** | © Microsoft Corporation |
| Microsoft.IO.RecyclableMemoryStream | 3.0.1 | **MIT** | © Microsoft Corporation |
| Microsoft.Extensions.DependencyInjection.Abstractions / Logging.Abstractions | (transitive) | **MIT** | © Microsoft Corporation |
| Tmds.DBus.Protocol | 0.92.0 | **MIT** | Tom Deseyn |
| MicroCom.Runtime | 0.11.4 | **MIT** | Copyright 2021 © Nikita Tsukanov |
| **FirebirdSql.Data.FirebirdClient** | **10.3.4** | **IDPL 1.0** — see §9.2 | (c) 2002-2025, FirebirdSQL |

`AvaloniaUI.DiagnosticsSupport` 2.2.1 is **Debug-only** — verified absent from `bin/Release/net9.0` (the csproj
sets `IncludeAssets=None` outside Debug), so it carries no distribution obligation. Test-only packages (xunit,
NPOI, ImageSharp, Cryptography.Xml, Avalonia.Headless) ship nothing.

**⭐ The load-bearing point: MIT is not "no obligation".** Its second paragraph requires that *"the above
copyright notice and this permission notice shall be included in all copies or substantial portions of the
Software"*. Shipping these DLLs is shipping copies. **So a third-party notices file is required, not a
courtesy** — that answers your question directly: yes, the libraries require it.

### 9.2 FirebirdSql.Data.FirebirdClient is **not** MIT — it is IDPL 1.0, and it has an explicit executable-distribution clause

Verified from the package's own `license.txt` (`<license type="file">license.txt</license>`): **Initial
Developer's Public License Version 1.0** — an MPL-1.1-derived, *file-level* copyleft licence. Two clauses bear
directly on shipping EmberTern:

- **§3.6 Distribution of Executable Versions** — you may distribute Covered Code in Executable form only if
  you *"include a notice stating that the Source Code version of the Covered Code is available under the terms
  of this License"*, and that notice *"must be conspicuously included in any notice in an Executable version,
  related documentation or collateral in which You describe recipients' rights relating to the Covered Code."*
- **§3.5 Required Notices** — *"You must also duplicate this License in any documentation for the Source Code
  where You describe recipients' rights."*

**What that means in practice:** the notices pane must (a) name the provider and IDPL 1.0, (b) carry the
licence text, and (c) state that the provider's source is available under that licence, with where to get it
(`firebirdsql.org` / the provider's repository). Because IDPL is *file-level* copyleft over the Covered Code —
here, an unmodified library — EmberTern's own source is a "Larger Work" and stays yours; §3.6 explicitly allows
distributing the executable under a licence of your choice.

**⚠ That is my reading of the licence text, not legal advice.** It is also the single item in this sprint I
would put in front of a lawyer before EmberTern is ever sold or distributed outside the company — precisely
because it is the one non-MIT dependency and the one with a source-availability clause.

### 9.3 The icon set is Lucide (ISC) — and the obligation follows the *geometries*, not the `.svg` files

From the repository's own `src/EmberTern.App/Assets/Icons/ICONS.md`: *"Source set: Lucide (lucide.dev)
(ISC license)"*. ISC requires the copyright and permission notice in all copies.

**The subtlety worth flagging:** the `.svg` files are excluded from the build output (`AvaloniaResource Remove`
in the csproj), so it is tempting to conclude nothing Lucide-derived ships. It does — the **path geometries in
`IconGeometries.axaml` are derived from those SVGs and ship inside the app**. The obligation follows the
derived work. Lucide belongs in the notices file.

### 9.4 The Inter font — the one genuine ambiguity, and it needs your decision

`Avalonia.Fonts.Inter` declares **MIT** in its nuspec and ships **no OFL text**, yet the Inter typeface itself
is published upstream under the **SIL Open Font License 1.1**, which requires the copyright notice and licence
travel with the font. The font *is* rendered (§2.5), so this is live.

I could not resolve the discrepancy from an artifact in this repository — the package carries MIT and nothing
else. **Recommended handling:** list the package as MIT *as declared*, and add one courtesy credit line —
"Inter typeface © The Inter Project Authors, used under the SIL Open Font License 1.1". Cheap, accurate, and it
satisfies the upstream intent without asserting something the package does not say.

### 9.5 Native libraries — flagged, not verified

`SkiaSharp` and `HarfBuzzSharp` are MIT **as packages**, but they bundle native binaries (Skia, HarfBuzz, and
their own dependencies) that carry their own upstream notices. **I did not verify those from an artifact** and
am not going to assert them from memory. If you want the notices file to be complete rather than merely
correct-as-far-as-verified, etap 4 should extract the third-party notices shipped in the SkiaSharp repository.
Flagged as an open item, not silently assumed.

### 9.6 Presentation — and the direct answer to "must the names be on the About face?"

**No. No licence here requires that, and I checked rather than assumed.**

- **MIT** requires the notice be *"included in all copies or substantial portions of the Software"*. It says
  nothing about placement or prominence — a notices document that ships with the application and is reachable
  from it satisfies it in full.
- **IDPL 1.0 §3.6** does use the word *conspicuously*, but read the scope: the notice must be conspicuously
  included *"in any notice in an Executable version, related documentation or collateral **in which You
  describe recipients' rights relating to the Covered Code**"*. The conspicuousness is required **inside the
  notices document**, not on every window of the product. A discreet button that opens that document, plus the
  file shipped beside the executable, is compliant.
- **ISC** (Lucide) is MIT-shaped in this respect: notice included, placement unconstrained.

So the user's instinct in round 2 was correct, and the design follows it: **the About face carries no library
names, and compliance is delivered by the notices document.**

**One file, one window, one source of truth.**

`THIRD-PARTY-NOTICES.txt` at the repository root, embedded as an `AvaloniaResource` **and** copied beside the
executable, shown verbatim in its own window (opened by the About footer button — read-only, scrollable,
selectable, monospace, `Escape` closes). Structure:

```
EmberTern uses the following third-party components.

── Component ── Version ── Licence ── Copyright ── Project ──
   (the §9.1 table, one block per component)

── Full licence texts ──
   MIT License                                     (once, covering the MIT components)
   Initial Developer's Public License Version 1.0  (FirebirdSql.Data.FirebirdClient)
   ISC License                                     (Lucide)
   SIL Open Font License 1.1                       (Inter)  ← per §9.4
```

Keeping it as a real file (rather than strings in code) means it is reviewable in a diff and updatable without
touching the UI — and it is the conventional place a reviewer looks.

### 9.7 What goes in — and what is deliberately left out

Round 2 settled this: **nothing about liability, warranty or data handling is added at this stage.**

| Item | Decision |
|---|---|
| Copyright line | **In** — §8.1, on the About face. |
| Third-party notices document | **In** — required by MIT / IDPL / ISC (§9.1–§9.3), behind the footer button (§9.6). |
| Open-source acknowledgement heading | **In** — the notices document's first line. |
| **Warranty disclaimer / limitation of liability** | **Out.** The user's clarification confirms my round-1 recommendation and states the reason better than I did: a limitation of liability for damages arising from use *is a term of the EmberTern licence*, and it belongs there — in one document, written once — not as a stray line in About. |
| **Privacy / "no telemetry" line** | **Out.** Round-1 optional suggestion withdrawn on the user's instruction: no single stray statements of this kind at this stage. §2.4 remains a verified fact worth having on record here for whoever writes the licence and the eventual user documentation. |
| EULA / acceptance flow | **Out** — deferred to the licence sprint; §8.3 leaves the slot. |
| **Firebird trademark note** | **Out of the About face; carry it in the notices document if you want it.** It is an attribution, not a liability clause, so the round-2 instruction does not strictly cover it — but it also is not *required* by anything I verified, and the About face is now deliberately bare. If a trademark line is wanted, the notices document is its natural home, and the exact wording should be taken from firebirdsql.org rather than from me (I did not verify the Foundation's preferred formulation). |

---

## 10. Etap plan

Each etap ends build 0/0, suite green, smoke clean, committable — and the visual ones await your QA, per the
standing rule that a UI change is not "done" until it has been seen in the running app.

| Etap | Scope | Notes |
|---|---|---|
| **1** | *This document.* Analysis, structure proposal, licence verification. **Revised in rounds 2–4; all questions closed.** | ACCEPTED. |
| **2** | ✅ **DONE + USER-ACCEPTED (2026-07-28)** — see §12. The button + the menu; the host measured first; placement per §6. Rows: `Settings…` (disabled, final) and `Exit` (live). **No `CommandId`s — §7 amended.** | Build 0/0; suite **5954** green (5903 + 51); smoke clean. Accepted after three icon QA rounds (§12.2a–d). |
| **3** | ✅ **DONE** — see §13. The About window (§8) + `<Version>1.2.0</Version>` in `Directory.Build.props`, the `+hash` defence, the `About EmberTern…` row. | Build 0/0; suite **5958** green (5906 + 52); smoke clean. Awaiting visual QA. |
| **4** | ✅ **DONE** — see §14. `THIRD-PARTY-NOTICES.txt` (§9.6) + the notices window behind the About footer button. §9.5 recorded in the file's own Notes rather than closed. | Build 0/0; suite **5964** green (5911 + 53); smoke clean. Awaiting visual QA. |
| **5** | ✅ **DONE** — see §15. `CommandDescriptor.Title` + its guards, then the window: search, `DataGrid`, scope-rank ordering, sort-reset, live filter, count. The `Keyboard Shortcuts…` row arrived with it. | Build 0/0; suite **5971** green (5917 + 54); smoke clean. Awaiting visual QA. |

⭐ **A row never ships ahead of what it opens.** Etap 2 delivers only `Settings…` (disabled by design, its final
state) and `Exit` (live); `About EmberTern…` and `Keyboard Shortcuts…` appear in etaps 3 and 5 *with* their
windows. This is a small revision to my own round-1 plan, and the reason is the standing rule against dead
surfaces (gotcha #233): a row that opens nothing is indistinguishable from a defect during QA, and this sprint
would have carried two of them across three etaps.

**Tests:** the same shape as etap 5's — a headless pin that the menu's items resolve their icon geometries and
their commands, plus the existing `UiStringsShortcutSourceTests` guard (no hand-typed gesture, §3.5) and the
`TheSameMenuOperationAlwaysCarriesTheSameIcon` guard, which will now also see these rows. New headless test
classes must join `HeadlessCollection` — never their own `IClassFixture` (gotcha #286).

---

## 11. Decisions log — nothing open

| Question | Round | Answer |
|---|---|---|
| Version number | 4 | **`1.2.0`** — the first version the project declares, and from now on the single source of truth (§8.2). |
| Copyright wording | 2 | `© 2026 Grzegorz Groński. All rights reserved.` |
| Toolbar placement | 2 | Hamburger is simply the first button; **no separator** between it and the rest (§6). |
| About content | 2 | Product window, not diagnostic: logo, name, version, author, copyright. No environment block (§8). |
| Library names on the About face | 2 | Out — and no licence requires them there (§9.6). |
| Liability / warranty / privacy wording | 2 | Out entirely at this stage; liability belongs to the future EmberTern licence (§9.7). |
| `Open Log File` / `Open Settings Folder` | 2 | Dropped (§5.5). |
| Dark theme row | 4 | **Cut.** Titlebar button stays the only place; theme joins Settings when Settings exists (§4 B). |
| Keyboard Shortcuts window | 3 | **In this sprint**, etap 5, as the target browsing window (§8.5). |
| `CommandDescriptor.Title` | 4 | **Accepted** — text from `UiStrings`, no literals in `CommandCatalog` (§8.5.1). |
| `CommandDescriptor.Description` | 4 | **Declined for now** — no immediate consumer, gotcha #233 (§8.5.1a). |
| Column sorting | 4 | User may sort any column; **clearing the sort and every first open restore the canonical order** (§8.5.4). |

**Etap 2 is authorised (round 4)** — and delivered; see §12.

---

## 15. Etap 5 — as built

**Build 0/0 · suite 5971 green** (partitions 5917 + 54) **· smoke clean · awaiting visual QA.** The window lists
**38 commands** — every catalog entry that has a gesture.

### 15.1 `CommandDescriptor.Title` — the registry's one canonical name

Added as a **required** positional parameter (position 2), so the compiler enumerated all 38 rows rather than
letting one slip through with a default. Text comes from `UiStrings.CommandTitle*`; **`CommandCatalog` holds no
string literal**, and `TheDescriptorTableContainsNoStringLiterals` scans the `AllDescriptors` table (comment
lines excluded — the table is heavily annotated) and fails if one appears.

⚠ **An alias class was written and deleted.** A private `T` shortening `UiStrings.CommandTitleX` to
`T.CommandTitleX` made the rows shorter at the cost of 38 lines duplicating the names — a second list to keep in
step for cosmetics. The table now references `UiStrings` directly.

Two more guards: every descriptor has a **non-empty** Title, and **titles are unique** (two commands sharing one
would be indistinguishable in a list). The Grid-scope titles are deliberately generic — *"New item in list"* —
because those three commands route through the app's one collection router, which serves fields, rows, columns,
parameters and variables; the per-collection nouns belong to the toolbar and the grid's own menu, which know
which collection they are looking at.

### 15.2 The window is a projection, and the canonical order is its resting state

`KeyboardShortcutsViewModel` holds the ordered, filtered rows; the grid's own sorting is an **overlay** on top,
which is what makes "clear the sort ⇒ canonical order" fall out rather than needing reconstruction. The scope
rank is declared once (`Global → Tab → Tree → Grid → Editor`) with a test asserting **every `CommandScope` member
has a rank** — and note the doc's earlier claim that this is "the reverse of the numeric order" was imprecise:
ascending numeric order is `Global, Tab, Grid, Tree, Editor`, which swaps Tree and Grid. An explicit rank was
required, not merely tidier.

Search matches the three displayed fields including the **rendered** shortcut text, so `ctrl` finds every Ctrl
binding and what you see is what you searched. ⚠ Substring matching means one gesture can be a prefix of
another: `Ctrl+Shift+F` finds Global Search **and** Restart debugging (`Ctrl+Shift+F5`). That is correct for a
search box — the test originally asserted a single hit and **the test was wrong, not the code**.

### 15.3 ⭐ Two real defects the measurement caught before QA

**(1) Column sorting did nothing at all.** `DataGridTextColumn` derives its sort path from the column's
`Binding`, but this project sets `AvaloniaUseCompiledBindingsByDefault`, and a compiled binding leaves the grid
without a usable path. The headers were clickable and sorted **nothing**. Fixed with an explicit
`SortMemberPath` on all three columns. The design doc had marked this area "measure, do not assume"; the
measurement is the only reason this is not a QA report.

**(2) The reset affordance re-armed itself.** It first appeared only while a sort was active, driven by the
grid's `Sorting` event — but that event also fires while the reset clears the columns, and it arrives late
enough that a "we are resetting" guard flag does not cover it, so the button reappeared the instant it had done
its job. Rather than chase the event's timing, **the affordance became stateless and is always visible**: a small
flat button in the footer costs nothing when unused and cannot lie about the grid's state. Same lesson as gotcha
#240 — never tie a control's visibility to the state its own action changes.

⚠ **Measured and deliberately not asserted:** Avalonia 12's `DataGridColumn` exposes no public sort-direction
property, so the header's direction glyph cannot be inspected from a test. That the **row order** returns to
canonical is proven; whether the glyph clears with it is owed to visual QA. `DataGridRow.GetIndex()` is also
obsolete, and `Index` is the index in the *underlying* items — so display order is read through the grid's own
selection instead.

### 15.4 What is deliberately absent

No details pane (§8.5.5) while it would be empty — the rows are typed view models, so adding it later is a
column bound to the grid's selection. No editing. `Icon.Keyboard` is verbatim Lucide; its two rows of key dots
are 4 apart, not a multiple of 1.5, and the `.svg` says why that rule does not bite here: it was written for long
parallel **rules**, where the eye compares thickness along a length.

---

## 14. Etap 4 — as built

**Build 0/0 · suite 5964 green** (partitions 5911 + 53) **· smoke clean · awaiting visual QA.**

### 14.1 Every licence text is a copy of an artefact, not a recitation

`THIRD-PARTY-NOTICES.txt` (390 lines) sits at the repository root. Sections: the components grouped by licence,
the full licence texts, and Notes for what is deliberately *not* claimed.

**MIT** was copied from a shipped artefact (`system.io.packaging/9.0.18/LICENSE.TXT`). **IDPL 1.0** (23 478
characters) was spliced verbatim out of the Firebird provider's own `license.txt` **by script**, so no character
of it passed through a transcription — including the §3.6 clause that made the file mandatory, which the tests
assert is present.

⭐ **Fetching Lucide's real LICENSE changed the content.** "Lucide is ISC" — which is what §9.3 recorded and what
I would have written from memory — is **incomplete**: Lucide's licence file carries **two** notices, ISC for
Lucide itself *and* MIT for the portions inherited from Feather (`Copyright (c) 2013-present Cole Bemis`). Both
are now reproduced. A recitation would have shipped a licence file that under-credited a copyright holder.

**Inter** is listed under the licence its package declares (MIT), with the upstream SIL OFL 1.1 credit recorded
— and the OFL text is deliberately **not** reproduced, because no artefact in this build states that it applies
(§9.4). Native Skia/HarfBuzz upstream notices are named in the Notes as an outstanding item rather than
silently omitted (§9.5). `AvaloniaUI.DiagnosticsSupport` is excluded with two reasons: it is absent from Release
output, and its package declares **no licence at all**, so stating one would be a guess.

### 14.2 One file, two destinations, and a guard that would have caught a real omission

The file is an `EmbeddedResource` **and** copied beside the executable. ⚠ **Not** an `AvaloniaResource`, and
that changed during the etap: the first attempt used one, and all three tests failed because Avalonia's asset
loader needs a live Avalonia application — so a plain text file could only be read, and only be tested, inside a
headless UI session. `GetManifestResourceStream` with an explicit `LogicalName` needs nothing. The window reads
the **embedded** copy on purpose: a document that can go missing or be edited after the build is not a notice.

Three tests, and the third is the one worth having: **`EveryShippedDependencyIsNamedInTheNotices`** parses every
`PackageReference` in `src/**/*.csproj` and fails if it is not named in the file. Adding a package is easy;
remembering the notice is not.

⭐ **Verified by planting — and the first attempt at planting was itself wrong**, which is worth recording. I
renamed `ExcelDataReader` to `ExcelDataReaderXX` in the notices and the guard passed, which looked like a broken
guard; the cause was that `ExcelDataReaderXX` still *contains* `ExcelDataReader`, so the substring check was
right and my planting was not. Deleting the lines outright made it fail by name: *"ExcelDataReader (in
EmberTern.Office.csproj)"*. Two guards this sprint have now looked correct while proving nothing, and both were
only settled by planting the violation.

### 14.3 The window

A separate scrollable window, not a tab on About: a tab strip on a five-line window makes it look like a
configuration dialog. Read-only, **selectable** (a reviewer can copy any clause), monospace — section 1 is an
aligned column layout that a proportional font would break — resizable, since licence texts are long, unlike
About's fixed composition. `Escape`/`Enter` close it.

⚠ The monospace family is the string `Cascadia Code,Consolas,Menlo,monospace`, which is now the **third** copy
of that list in the app (`HoverInfoView`, `LanguageExpansionController` have the others). Centralising it is
typography, which the backlogged app-wide UX sprint owns; noted rather than half-done here.

The About footer now holds `Third-party notices` beside `Close`. The About test's "no library names" assertion
was scoped to the window's **text blocks**, because a button that *reaches* the component list is the opposite
of putting the list on that face.

---

## 13. Etap 3 — as built

**Build 0/0 · suite 5958 green** (partitions 5906 + 52) **· smoke clean · awaiting visual QA.**

### 13.1 The version has exactly one home, and two tests keep it that way

`Directory.Build.props` gained one `PropertyGroup`: `<Version>1.2.0</Version>`,
`<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>`,
`<Product>`, `<Company>` (the author — it is the slot Windows shows in a file's properties) and `<Copyright>`.
`AppInfo` (App root, pure, no Avalonia) reads them back off the assembly; `AboutViewModel` composes the version
with its `UiStrings` label. **A release is one line in one file.**

Verified as a side effect, and worth having: the exe's own properties now read
`ProductVersion=1.2.0 · CompanyName=Grzegorz Groński · LegalCopyright=© 2026 Grzegorz Groński. All rights
reserved.` — Explorer gets the same single source for free.

**Two guards, and neither contains a version number** (`AppInfoTests` reads the expected value out of the props
file, so bumping the version needs no test change):

1. `VersionComesFromTheBuild` — `AppInfo.Version` equals the declared `<Version>`, and carries no `+`.
2. `NoVersionNumberIsHardCodedInTheApp` — the version's text appears **nowhere** under `src/`.

⭐ **The second guard failed on its first run, and it was right.** It caught the version in **my own doc
comments** in `AppInfo.cs` and `AboutViewModel.cs`, which quoted `1.2.0` as an example — including, on the next
iteration, the comment explaining the guard. Those comments would have been false after the next release. The
fix was to remove the numbers, not to narrow the guard: a stale comment teaches the next reader something
untrue, which is the same failure mode as a stale shortcut (gotcha #284), and this is a fair measure of how
easily such a copy appears even while writing the code that forbids it.

⚠ `AppInfo` reads **its own assembly**, deliberately not `Assembly.GetEntryAssembly()` — under a test host the
entry assembly is the test runner, so every one of these tests would have measured vstest's version while
passing.

### 13.2 The window

One flat surface, centred, logo-dominant — **deliberately not the app's usual dialog skeleton.** `ConfirmDialog`
and its peers open with a `PanelBrush` header band and close with a footer band, which is right for a dialog
that asks something; here it would have produced exactly the banded Win32 "About" look with a two-column
`Author:` / `Copyright:` form. So: no bands, no label column, no environment block. 400px wide, height to
content, `Escape`/`Enter` both close.

The mark is the subject: `EmberTern_logo.png` at 128px (the 256×256 transparent asset rendered from the
source master `logo.png`, which is repo-only and does not ship), then the name at
23px SemiBold, the version subtle beneath it, a **40px hairline** rather than a
full-width rule, then author and copyright. The hairline is also the slot a future licence line occupies — one
row, not a redesign (§8.3).

`AboutWindow_ShowsTheAssemblyVersionAndIdentity` closes the last link the other tests cannot: that the values
actually reach the surface through the real bindings — a correct `AppInfo` behind an unbound `TextBlock` would
satisfy everything else. It also asserts the window stays a **product** window, failing if the text ever
mentions .NET, Avalonia, Windows, Firebird or an architecture.

**`Icon.Info` is verbatim Lucide** (circle r10, stem, and a zero-length stroke whose round cap is the dot) —
no composition needed, and its 22×22 ink box matches `Copy`/`FolderPlus`.

**The `About EmberTern…` row shipped with the window**, per §10's rule that a row never ships ahead of what it
opens; the menu's separator count moved 1 → 2 and the probe asserts it.

### 13.3 QA round — four findings, and one of them exposed the guard as too weak

**(1) ⭐ There WAS a second version source, and the guard had let it through.** The status bar showed `0.1.0`
while About showed the assembly's version — the two contradicting each other on screen, which is how the user
found it. The cause was `Text="EmberTern 0.1.0"` typed into `MainWindow.axaml`. Both surfaces now read
`AppInfo` (`MainWindowViewModel.AppVersionChip`), and `StatusBarShowsTheSameVersionAsAbout` asserts the rendered
chip carries it.

⚠ **The instructive part is why `NoVersionNumberIsHardCodedInTheApp` did not catch it: it searches for the
CURRENT version's text, so a literal left over from an EARLIER one sails straight past.** A guard keyed to
today's value can only catch a copy made today. Worse, my first attempt at a shape-based guard used
`"\d+\.\d+\.\d+"` — which does **not** match `"EmberTern 0.1.0"`, because the quote is not adjacent to the
digits. It would have felt like protection while catching nothing, which is the more dangerous of the two
failures. So `NoVersionShapedLiteralCanReachTheScreen` now scans **XAML `Text=`/`Content=` attributes and
non-comment C# string literals**, was **checked against the exact removed literal**, and was **verified by
planting it back** and watching the test fail by name. Two deliberate exclusions, each reasoned: spec
references (`§9.8.1`) via a lookbehind, and comment lines — prose legitimately names the literal that was
*removed*, a historical fact that cannot go stale, while the *current* number stays banned everywhere by the
first guard. Between them: today's number appears nowhere at all, and a version shape appears nowhere it could
be displayed.

**(2) `1.2.0` was too high — now `0.5.0`.** Ratified: **1.0 arrives with the finished product and its licensing
system**, possibly preceded by a Beta suffix once EmberTern is ready for wider testing, so the number stays
under 1.0 however complete the feature set looks. `0.5.0` reads as substantial progress with beta still ahead;
the previous on-screen claim was a never-maintained `0.1.0`. It is one line in one file if you want it lower or
higher.

**(3) The author line is labelled — `Created by Grzegorz Groński`.** Unlabelled it read as an unsigned line of
text, and the name recurs in the copyright below; the label is what turns that repetition into authorship
rather than an accident. Both alternatives the user offered were viable — the label was chosen over deleting
the line because authorship is the product statement and the copyright below it is legal metadata.

**(4) A release date, under the version — `Released 29 July 2026`.** ⭐ And it obeys the same single-source rule
as the version rather than becoming a date typed into a view: there is no standard assembly attribute for it,
so `<ReleaseDate>` travels as `AssemblyMetadata` and `AppInfo` parses it strictly (ISO in the file, formatted
for display, unparseable ⇒ reported absent and the line hides rather than showing an empty label).

**Verified as a side effect:** the exe's own properties read `ProductVersion=0.5.0`, so Explorer follows the
same source. Suite **5961** green (5908 + 53).

---

## 12. Etap 2 — as built

**Build 0/0 · suite 5954 green** (partitions 5903 + 51, both with `--blame-hang`, no hang) **· smoke clean ·
USER-ACCEPTED 2026-07-28**, after three QA rounds on the icon (§12.2a–d) and none on the menu itself.

### 12.1 The measurement came first, and it settled the host

`ConnectionExpandBindingProbe.ApplicationMenu_IsTheFirstToolbarButton_AndReusesTheSharedMenuChrome` builds the
**real** `MainWindow` and proves, on the live control:

- a plain `ContextMenu` on the toolbar button carries the app's **one** menu appearance with **nothing added** —
  `BorderThickness=1`, `FontSize=12`, rows at `MinHeight=22`/`FontSize=12`, the same values
  `TheSharedStyle_AppliesToEveryContextMenuWithoutOptIn` pins for the other 32 menus. **So the `MenuFlyout`
  path, and the second `MenuFlyoutPresenter` chrome variant it would have required, was correctly rejected.**
- left-click opens it and a second click closes it (Avalonia opens a `ContextMenu` on right-click only, so the
  button's handler is what a *menu button* needs);
- the hamburger is `Children[0]` of the action zone and the element **immediately** after it is the sidebar
  toggle — the ratified "no separator of its own" (§6), now pinned structurally rather than by eye;
- `Settings…` is present-and-disabled with its tooltip, `Exit` is enabled, one real `Separator` between them,
  every row carries an icon, and **no row carries a gesture**.

⚠ **What it deliberately does not claim:** where the popup lands on screen. Headless has no real popup surface,
so `Placement` is asserted as the declared `BottomEdgeAlignedLeft` only — the actual position is owed to visual
QA. Saying so is the point; a test that appeared to cover it would be worse than one that does not.

The test lives **inside `ConnectionExpandBindingProbe`**, not in a new class, deliberately: a second headless
class is what produced the etap-5 hang, and while `HeadlessCollection` is the sanctioned fix, this etap had no
reason to spend that risk.

### 12.2 Three icons, two of them composed and labelled as such

`Icon.Menu` is Lucide `menu` exactly (three rules at y6/12/18). `Icon.Settings` and `Icon.Exit` are **composed
in the Lucide style** — 24×24, 2px stroke, round caps — and both `.svg` sources carry a comment saying so, the
precedent being `Actions/table-plus.svg` and `Actions/import.svg`. Settings is two setting tracks with a knob
each: a gear is the more expected mark, but its outline cannot be authored cleanly at a 2px stroke rendered into
a 14px icon column. Exit is a doorway with an arrow leaving through it — not `Icon.WindowClose`, which means
"close this window" chrome-side, and not a power symbol, whose near-full arc is the shape that degrades worst at
menu-icon size.

### 12.2a QA round 1 — the hamburger looked smaller than its neighbours, and it was

The user's first visual QA: the ☰ glyph read as smaller than the icons beside it — *"not the button or the
padding, the geometry itself"*. Correct, and the diagnosis was exact.

**It is not a rendering difference.** `SvgIcon`'s ControlTheme is a `Viewbox Stretch="Uniform"` around a
**fixed `Canvas Width="24" Height="24"`**, so the Viewbox scales the *Canvas*, never the path's ink. Every icon
renders at the same 24→16 scale — which means an icon looks small for exactly one reason: **its geometry fills
less of the 24×24 box.** Ink box = path extremes ±1 (half the 2px stroke, round caps):

| Icon | Ink box |
|---|---|
| `Icon.Copy` / `Icon.FolderPlus` | 22×22 / 22×19 |
| `Icon.PanelLeft` / `Icon.Trash` | 20×20 / 20×22 |
| `Icon.Menu` — **verbatim Lucide** `menu` | **18×14** |

At the rendered 16px that is 9.3px tall where `PanelLeft` is 13.3px — a **30% height deficit**, and the
shortest glyph on the bar. Lucide's own set is simply not internally consistent between a three-rule glyph and
a closed rectangle.

**Fixed by enlarging the geometry**, not by touching the control: rules widened to x3→21 and spread to
y4/12/20 → ink **20×18**, the same width as `PanelLeft` and 90% of its height. *(Superseded one round later —
see §12.2b, which raised it to a full 20×20 for a different reason.)*

### 12.2b QA round 2 — the three rules rendered differently, and it was ONE cause with two symptoms

The user's second QA: the middle rule looked **thicker**, and the bottom one looked **clipped at the right
end** — with the explicit instruction not to nudge lines again but to make the glyph mathematically symmetric
and identically rendered. The coordinates *were* already symmetric (x3→21 and y4/12/20 are both centred on 12).
Symmetry was never the problem.

**The cause is the sub-pixel phase, and it explains both symptoms at once.** The 24→16 render is a **×2/3**
scale, so a rule declared at `y` has its top edge at **2(y−1)/3** and a thickness of 1.333px. The *fractional
part* of that edge decides the anti-aliasing:

| Declared y | Rendered band | Pixel coverage | Reads as |
|---|---|---|---|
| 4 | [2.000, 3.333] | row 2 → 100%, row 3 → 33% | crisp, faint edge below |
| 12 | [7.333, 8.667] | rows 7 **and** 8 → 67% each | two grey rows — softer and **optically thicker** |
| 20 | [12.667, 14.000] | row 12 → 33%, row 13 → 100% | crisp, faint edge above; its round cap lands on yet another phase, which is the "clipped end" |

So three geometrically identical rules were drawn three different ways. **No amount of nudging could have fixed
it** — which is exactly what the user's instruction anticipated.

**The fix is arithmetic, not taste.** Equal rendering requires equal phases: `2·Δy/3 ∈ ℤ`, so **the spacing must
be a multiple of 1.5** (1.5 × 2/3 = 1). That constraint binds the *spacing* and leaves the *extent* free, which
is what round 3 then needed.

`Assets/Icons/ICONS.md` carries the rule with its coverage table as evidence: **"any icon with repeated
parallel strokes spaces them by a multiple of 1.5 in the 24-unit grid"**, and it is pinned directly by
`HamburgerRulesAllRenderIdentically`, which reads the geometry out of `IconGeometries.axaml` and asserts one
phase for all three rules — the same source-scanning idiom as the menu-icon consistency test.

### 12.2c QA round 3 — the ink box was the wrong target, and that is a lesson about method

Round 2 shipped `y3/12/21` at ink **20×20**, matching `PanelLeft` exactly. The user's verdict: now the
hamburger is **optically bigger** than its neighbours and dominates the bar as its first item — with the
instruction not to chase equal ink boxes, because *different glyphs have different visual mass and should not
be forced into an identical box*, and *"I trust what is on the screen more than perfect geometry"*.

**They are right, and the error was mine rather than the icon's: I optimised a measurable proxy in place of the
actual goal.** Equal extent is not equal weight. Three full-width rules are far denser than a thin rectangle
*outline*, so at the same 20×20 box the hamburger must read heavier — **a dense glyph needs a smaller box to
look the same size.** "Ink box == neighbour" was seductive precisely because it was checkable, which is exactly
what made it dangerous: it produced a confidently wrong answer twice, in opposite directions.

**Method changed, not just the numbers.** Rather than guess a third time at something only an eye can settle, the
candidates were rendered **side by side with the real neighbour geometries** (`PanelLeft`, `Plus`, `FolderPlus`,
`Trash`, unmodified) at the true 16px and at 6× zoom, all of them phase-consistent so only the extent varied:

| | Geometry | Ink | Note |
|---|---|---|---|
| A | x4→20, y6/12/18 | 18×14 | verbatim Lucide — round 1's "too small" |
| **B** | **x4→20, y4.5/12/19.5** | **18×17** | **shipped** |
| C | x3→21, y4.5/12/19.5 | 20×17 | wider |
| D | x3→21, y3/12/21 | 20×20 | round 2 — "too big" |
| E | x4.5→19.5, y4.5/12/19.5 | 17×17 | tightest |

**Shipped: B — ink 18×17 against the neighbours' 20×20, deliberately smaller in both axes.** Symmetric about
(12,12), Δy = 7.5, one phase (.333) for all three rules. Measured: `18, 17 @ 12, 12`.

**The pin was corrected too, because the old assertion *encoded the wrong goal*.** It demanded exact equality
with the neighbour — i.e. it would have failed the version the user actually wants. It is now a **range**: big
enough not to look lost (round 1), **strictly smaller** than a rectangle outline (round 3), centred; with the
phase invariant moved to its own test. `ICONS.md` gained the standing rule: **use the ink box to diagnose, never
as the goal — put candidates beside the real neighbours at the real size and look.**

⚠ Recorded caveat, unchanged: exactness holds at the rendered 16px, the `SvgIcon` default. A host overriding the
size re-scales the 24-unit grid and changes the phase — inherent to scaling, not fixable by coordinates.

### ⛔ 12.2d The icon is CLOSED — user-accepted 2026-07-28

*"Optycznie jest spójna z resztą toolbaru i uznajmy temat za zamknięty."* **Do not revisit `Icon.Menu`'s
geometry.** Not to "tidy" the fractional coordinates, not to align its ink box with a neighbour's, not to adopt
the upstream Lucide file — all three have been tried, and two of them shipped and were rejected. The three
tests (`ApplicationMenu_…`, `HamburgerRulesAllRenderIdentically`) plus the comments in
`IconGeometries.axaml` and `Navigation/menu.svg` exist to make each of those attempts fail loudly rather than
quietly regress the toolbar.

The two generalisations from these rounds live in `Assets/Icons/ICONS.md` and **do** apply to future icons:
spacing of repeated parallel strokes is a multiple of 1.5, and the ink box diagnoses rather than dictates.

### 12.3 Files touched

`Themes/IconGeometries.axaml` (+3 geometries) · `Assets/Icons/{Navigation/menu,Actions/settings-sliders,Actions/log-out}.svg`
(source of truth, excluded from build output) · `UiStrings.cs` (+4 members, one commented block) ·
`Views/MainWindow.axaml` (the button + its `ContextMenu`, inserted after the existing brand divider) ·
`Views/MainWindow.axaml.cs` (`OnAppMenuClick`, `OnAppMenuExitClick`) · `ConnectionExpandBindingProbe.cs` (+1 test).

**Nothing else moved** — no styles added, no `CommandId` touched, no layout re-flowed.

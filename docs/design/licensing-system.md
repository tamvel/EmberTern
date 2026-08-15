# EmberTern Licensing System — design document

**🔒 STATUS: V1 RATIFIED BY THE USER 2026-08-15 (decisions D1–D16, §0). ⭐ STAGE L1 DELIVERED — awaits the
user's confirmation. Next: L2.** Branch `feat/licensing-system`, cut from `master` at `2c3da45`.
As built: **§34**.

**This document has two parts and they have different authority:**

| Part | Content | Authority |
|---|---|---|
| **Part I — V1: Offline Licensing** (§4–§18) | Everything to be built now | ⭐ **Ratified. Build this.** |
| **Part II — V2: Online Activation** (§19–§23) | The next planned stage | 📋 **Planned, not optional.** Design direction only — not built, not started |
| **Part III — Common** (§24–§33) | Keys, threats, costs, plan, decisions | Applies to both |

⚠ **V2 is a planned next stage, not a hypothetical.** The user's target model is: admin generates a
short one-time code → customer types it into EmberTern → the backend validates and *consumes* it →
the backend issues a signed license → EmberTern stores it locally → **all further work is offline.**
V1 is designed so that V2 attaches to it, but ⛔ **V1 contains no code that only V2 would use** — see
§3, the rule that resolves those two sentences.

**Start here:** §1 (what V1 is) → §3 (why V1 carries almost nothing for V2) → §13 (license format) →
§32 (implementation plan).

---

## 0. Ratified decisions (user, 2026-08-15)

| # | Decision | Consequence in this document |
|---|---|---|
| D1 | **V1 is entirely offline.** No Firebase, no Cloudflare, no backend, no mandatory internet. | Part I contains no network code of any kind |
| D2 | **Seats are contractual in V1**, technically enforced in V2. Copying a valid `.etlic` to a second machine works, and that is accepted knowingly. | §11 |
| D3 | ⛔ **No InstallationId in V1** — not generated, not required, not bound. ⛔ **No hardware fingerprinting.** `iid` stays reserved in the format contract, unused by V1. | §13.3, §21 |
| D4 | **Long signed artifact accepted** instead of a short code. Delivered as `EmberTern.etlic`. The user must not see editable JSON; ⛔ obscurity is **not** claimed as security. ⛔ No ZIP. | §13 |
| D5 | Customer record: **name (required)**, address, first name, last name, e-mail, notes. ⛔ **No editions in V1 UI** — EmberTern has one licensed version. Format stays extensible. | §12.2, §13.2 |
| D6 | The license carries the **licensee name**; EmberTern displays it (Settings ▸ License, About). UX and a deterrent against careless sharing — ⛔ not a technical control. | §17 |
| D7 | License Manager is a **separate Avalonia application** styled like EmberTern, with the V1 feature set in §12.3. | §12 |
| D8 | ⭐ **NEW: the License Manager sends the license by e-mail** — a professional HTML message with the `.etlic` attached — **and** can always save the file locally (we sometimes install it at the customer's site ourselves). ⛔ No ZIP. | §14 |
| D9 | ⭐ **NEW: SMTP configuration with credentials encrypted via Windows DPAPI**, exactly as EmberTern protects its own secrets. ⛔ Never in code, repo, appsettings or logs. Sending goes through an abstraction so the provider can change. | §14.3 |
| D10 | **ECDSA P-256** for V1 (Q5 accepted). Ed25519 stays a possible future algorithm; `kid` selects the algorithm, enabling rotation. | §15 |
| D11 | **.NET 9 for everything**, License Manager included — environmental reason (Visual Studio 2022). Migration to .NET 10 happens for the whole product at once, later. | §12.1 |
| D12 | **V2 is the planned next stage**: one-time activation codes, activation, technical seat enforcement, code-reuse protection, activation management. ⛔ **No mandatory user accounts / login** — EmberTern is not SaaS. Firebase Authentication only ever as an auxiliary option, never the basis. | Part II |
| D13 | Cloudflare **Worker + D1** stays in the document as a recommended V2 option. ⛔ Not implemented now. | §22 |
| D14 | ⛔ **Do not over-engineer V1** to save hypothetical migration hours later. | §3 |
| D15 | ⭐ **NEW: a license is never required in a `Debug` build.** `Debug` ⇒ the gate is off; `Release` ⇒ full verification. ⛔ Not via `Debugger.IsAttached` — it must follow from the **build configuration**. ⛔ `Release` must carry no simple configuration switch that turns licensing off. Guard test required. | §16.5 |
| D16 | Open items O1–O6 resolved as recommended (§33): `maint` kept · 14-day grace · `Expired` blocks new database connections only · default seats 1 · license id in the e-mail body · delivered filename always `EmberTern.etlic`. | §33 |

---

# PART I — V1: OFFLINE LICENSING

## 1. What V1 is, in one page

A license is a **signed text artifact** — `EmberTern.etlic` — created by an offline desktop **License
Manager** that holds the only signing key. EmberTern reads it from disk, verifies the signature and the
dates locally, shows who it was issued to, and runs. **The licensing subsystem contains no HTTP client,
no socket and no DNS lookup**, which is a stronger guarantee than a policy: there is no network call to
make. This is asserted by a test (§16.2).

```
FIRST RUN                                EVERY LATER RUN
─────────                                ───────────────
no license                               read %APPDATA%\EmberTern\license.etlic
   ↓                                        ↓
Activation window                        verify signature  (kid → public key)
   ↓                                        ↓
drop EmberTern.etlic  (or paste)         check dates (with clock-rollback guard)
   ↓                                        ↓
verify → save → re-verify from disk      show licensee in About / Settings
   ↓                                        ↓
"Licensed to ACME Sp. z o.o."            EmberTern works — offline, no network
   ↓
EmberTern works
```

**Everything the customer ever does with licensing: import one file, once.**

---

## 2. Architecture

```
   ADMIN MACHINE (offline)                        CUSTOMER MACHINE
   ───────────────────────                        ────────────────

 ┌──────────────────────────────┐
 │ EmberTern License Manager    │
 │  (Avalonia, MVVM, DI, net9)  │
 │                              │
 │  customers · licenses        │
 │  issue · re-issue · extend   │
 │  search · history · export   │
 │  ⭐ send by e-mail (SMTP)    │
 │                              │
 │  ┌────────────────────────┐  │
 │  │ keystore.etkeys        │  │
 │  │  AES-256-GCM/PBKDF2    │  │
 │  │   • PRIVATE KEY  ⛔     │  │      EmberTern.etlic
 │  └────────────────────────┘  │   ┌──────────────────────┐
 │  ┌────────────────────────┐  │   │ e-mail attachment    │
 │  │ licenses.db (SQLite)   │──┼──►│  or a saved file     │
 │  │  the register of record│  │   │  or we install it    │
 │  └────────────────────────┘  │   │  on site ourselves   │
 │  ┌────────────────────────┐  │   └──────────┬───────────┘
 │  │ smtp.dat (DPAPI)       │  │              │
 │  └────────────────────────┘  │              ▼
 └──────────────────────────────┘   ┌────────────────────────────┐
                                    │ EmberTern.exe              │
                                    │                            │
                                    │  LicenseService (ONE owner)│
                                    │    └ EmberTern.Licensing   │
                                    │        • parse ETL1        │
                                    │        • kid → public key  │
                                    │        • verify signature  │
                                    │        • lv gate           │
                                    │        • nbf / exp         │
                                    │        • clock high-water  │
                                    │             ↓              │
                                    │        LicenseVerdict      │
                                    │                            │
                                    │  TrustedKeys — PUBLIC only │
                                    │  %APPDATA%\EmberTern\      │
                                    │      license.etlic         │
                                    └────────────────────────────┘

              ⛔ NO NETWORK ANYWHERE IN PART I.
```

### 2.1 The three rules

1. ⭐ **The private key exists in one file, on one machine, usable by one assembly the client does not
   reference.** Enforced by a guard test (§16.1) in the style of `CharsetGuardSeamTests`.
2. ⭐ **The client verifies; it never decides.** `LicenseVerifier.Verify(bytes) → LicenseVerdict` is a
   pure function. No other code in EmberTern asks "is the license OK?" — it reads the resolved verdict.
3. ⭐ **The signature covers the encoded bytes, never a re-parsed object.** Verify first, parse second.
   No canonical JSON, no field ordering, no round-trip. This is the JWT lesson and it serves
   Architecture rule 11.

---

## 3. ⭐ The rule that keeps V1 small while keeping V2 reachable

The brief contains two requirements that look opposed: *"V2 is a planned stage, design V1 so V2 can be
added without rebuilding"* (D12) and *"do not over-engineer V1"* (D14). They are reconciled by one
observation:

> ⭐ **V2 requires a V2 client anyway.** Online activation is code that does not exist in V1 — a customer
> can only use it after installing a build that has it. So *anything* V2 needs on the client can be
> added in the same release that adds activation. There is no population of clients that must accept a
> V2 license without also being able to talk to V2.

The only exception is **the license *format*.** A format decision cannot be renegotiated with artifacts
already in customers' hands. So:

| Kept in V1 because it is unfixable later | Deliberately NOT built in V1 |
|---|---|
| `lv` (payload version) + a strict *"refuse `lv` above what I support"* gate | ❌ InstallationId / binding (D3) |
| `kid` selecting **key and algorithm** from an append-only table | ❌ hardware fingerprinting (D3) |
| The reserved-field rule (§13.4): unknown optional fields are ignored; anything unsafe to ignore travels with an `lv` bump | ❌ certificate chains — the V2 release adds them together with the key that needs them |
| ⭐ `maint` (§13.5) — the one unused field kept, see below | ❌ `ed` / `feat` gating — one licensed version today (D5) |
| | ❌ `seatPolicy` — redundant: `iid` absent ⇒ unbound |
| | ❌ `cid` in the payload — `lid` already identifies the customer via the register |

⚠ **`maint` is the only thing kept that V1 does not use, and it is flagged so it can be overruled.**
Rationale: every other omission is fixable in the release that needs it, but a *perpetual* license sold
in 2029 would be unbounded on every client built before the gate existed. The cost is one optional field
and one comparison against `AppInfo.ReleaseDate`, which is already an assembly attribute. If you prefer
zero unused code in V1, delete it — the loss is bounded and known.

### 3.1 How V1 licenses and V2 licenses coexist

```
V1 license   lv = 1   no iid           → V1 client: ✅    V2 client: ✅
V2 license   lv = 2   iid present      → V1 client: ❌ "issued for a newer version of EmberTern"
                                          V2 client: ✅
```

⭐ **No customer is ever forced to upgrade.** The License Manager does not go away when V2 arrives: a
V1-era customer keeps receiving `lv = 1` renewals from it. The `lv` gate is what makes a V1 client refuse
a *bound* license instead of silently ignoring the binding — which is the correct failure direction.

---

## 4. First-run flow (every launch)

```
EmberTern starts
      │
      ▼
resolve the license file:
      1. %APPDATA%\EmberTern\license.etlic          ← normal case
      2. %PROGRAMDATA%\EmberTern\license.etlic      ← we installed it on site (D8)
      │
      ├── neither exists ───────────────────────► Unlicensed  → Activation window
      ▼
strip armor + whitespace, split ETL1 envelope
      │
      ├── malformed ────────────────────────────► Invalid  ⛔ file NOT deleted, NOT moved
      ▼
lv ≤ supported ?
      │
      ├── no ───────────────────────────────────► Invalid ("newer version of EmberTern")
      ▼
kid → TrustedKeys → public key + algorithm
      │
      ├── unknown kid ──────────────────────────► Invalid (same message)
      ▼
verify ECDSA P-256 signature over ("ETL1." + payloadSegment)
      │
      ├── fails ────────────────────────────────► Invalid ("may have been modified") [Copy details]
      ▼
prod == "EmberTern" ?         ├── no ──────────► Invalid ("for a different product")
      ▼
clock guard:  effectiveNow = max(systemNow, highWaterMark)
      │       systemNow < highWater − 48 h  →  warn, never block  (§16.3)
      ▼
nbf ≤ effectiveNow ?          ├── no ──────────► NotYetValid (states the start date)
      ▼
exp
      ├── effectiveNow ≤ exp ───────────────────► Valid
      │        └ exp − now ≤ 30 d ──────────────►   + ExpiringSoon banner (dismissible)
      ├── exp < effectiveNow ≤ exp + 14 d ──────► Grace — FULL function, persistent warning
      └── effectiveNow > exp + 14 d ────────────► Expired — app opens; editor, files, exports,
                                                    settings all work; ⛔ no NEW DB connections
      ▼
maint (if present) ≥ AppInfo.ReleaseDate ?
      ├── no ───────────────────────────────────► VersionNotCovered
      ▼
write highWaterMark = max(highWaterMark, systemNow)
      ▼
APP RUNS.  ⛔ ZERO NETWORK CALLS — THERE IS NO CODE TO MAKE ONE.
```

**Budget: < 5 ms**, measured (`EMBERTERN_PERF_DIAG=1`), off the path that delays the first window.

---

## 5. Activation flow

```
Customer receives EmberTern.etlic (e-mail attachment, or we install it on site)
             │
             ▼
   ┌───────────────────────────────────────────┐
   │  Activate EmberTern                       │
   │  ┌─────────────────────────────────────┐  │
   │  │   Drop your license file here       │  │  ← drag & drop
   │  │              — or —                 │  │
   │  │   [ Browse… ]   [ Paste license ]   │  │
   │  └─────────────────────────────────────┘  │
   │                            [ Activate ]   │
   └───────────────────────────────────────────┘
             │
             ▼
   the full §4 verification chain, identical code
             │
             ├── any failure ──► MessageBanner: what happened · why · what to do now
             │                    ⛔ never "License validation failed (code 7)"
             ▼
   write %APPDATA%\EmberTern\license.etlic   (atomic: temp file + replace)
             │
             ▼
   ⭐ RE-READ AND RE-VERIFY FROM DISK       ← never trust the in-memory result
             │
             ▼
   "Licensed to ACME Sp. z o.o. — valid until 2027-08-15"
             │
             ▼
   EmberTern runs.  No network was contacted at any point.
```

⭐ **The re-read is Architecture rule 11, not paranoia.** If the write half-succeeded the user must find
out now, with the file still on their desktop — not at the next launch with the e-mail deleted.

---

## 6. Renewal flow (V1)

```
   ADMIN                                             CUSTOMER
   ─────                                             ────────
License Manager
  ├ filter: "expires within 60 days"
  ├ select 23 licenses
  ├ [ Extend to … 2028-08-15 ]
  │     └ each: NEW artifact, NEW iat, SAME lid, exp = new date
  │       old artifact kept forever, marked superseded (§12.5)
  ├ 23 rows appended to audit_log
  │
  ├ [ Send ]  → review list → confirm → 23 HTML e-mails, each with its .etlic
  │            (or [ Export ] → 23 files, for manual delivery)
  │
  └────────────────────────────────────────────────►  receives the new file
                                                            │
                                                     Settings ▸ License
                                                     ▸ [ Update license ]
                                                            │
                                                     drop / paste → verified
                                                            │
                                                     replaces license.etlic
                                                     ⭐ only if lid matches
                                                        AND iat is NEWER (§16.4)
```

⛔ **There is no automatic renewal check in V1** — that would be the first line of network code. A
customer whose license is 30 days from expiry sees a banner; renewal is a human process, which at this
product's scale is a mail-merge, not a burden.

---

## 7. License states

| State | App behaviour | Message | User action |
|---|---|---|---|
| `Valid` | full | none | — |
| `Valid`, ≤ 30 d to expiry | full | Info banner, dismissible per session | renew when convenient |
| `Grace` (≤ 14 d past `exp`) | **full** | Warning banner, persistent | renew |
| `Expired` | opens; editor, files, exports, settings work; ⛔ **no new DB connections** | Error + activation entry | renew |
| `Invalid` | gated | Error + `[Copy details]` | re-import / contact us |
| `Unlicensed` | gated | Activation window | activate |
| `NotYetValid` | gated | states the start date | wait / contact us |
| `VersionNotCovered` | gated | names the covered version | install the covered build or renew maintenance |

⭐ **The 14-day grace period is a correctness requirement, not generosity.** Renewal in V1 is a human
process; an expiry that bricks the tool at midnight on day zero turns a routine purchase-order delay into
a work stoppage. ⭐ **And no state may ever prevent saving or exporting work that is already open** —
Architecture rule 11 governs licensing exactly as it governs the formatter.

---

## 8. License file location

```
1. %APPDATA%\EmberTern\license.etlic          ← per-user, written by activation
2. %PROGRAMDATA%\EmberTern\license.etlic      ← per-machine, read-only fallback
first match wins; the per-user file always shadows the machine file.
```

⭐ The `%PROGRAMDATA%` path exists because of D8: *"in some cases we will install the license at the
customer's site ourselves."* It also covers shared workstations and terminal servers. It costs about five
lines.

⛔ The license is **not** stored inside `settings.dat`: it must survive a settings reset, be copyable by
support, and be readable without EmberTern.

---

## 9. Data model — the license payload

| Field | Type | V1 | Purpose |
|---|---|---|---|
| `lv` | int | ✅ `1` | Payload version. ⭐ Client refuses `lv` above what it supports. §3.1 |
| `kid` | string | ✅ | Key id — ⭐ **selects both the public key AND the algorithm** from the client's table |
| `alg` | string | ✅ | Informational; **cross-checked against the table, never used to select**. §15.2 |
| `lid` | string | ✅ | LicenseId, 128-bit. Stable across renewals — the correlation key |
| `prod` | string | ✅ | `"EmberTern"`. Cross-product replay guard |
| `lic` | string | ✅ | ⭐ **Licensee name — the displayed one (D6).** Required |
| `seats` | int | ✅ | Contractual seat count (D2). **Displayed, never enforced in V1** |
| `iat` | RFC3339 | ✅ | Issued at. ⭐ The freshness ordering key (§16.4) |
| `nbf` | RFC3339 | ✅ | Not before |
| `exp` | RFC3339 | ✅ | Expiry |
| `maint` | RFC3339? | ⚠ reserved | Perpetual-fallback. Parsed and enforced; never emitted in V1. §3, §13.5 |
| `iid` | string? | ⛔ **V2** | InstallationId. **Absent ⇒ unbound.** A license carrying it is `lv = 2` |
| `ed`, `feat` | — | ⛔ **future** | Editions and entitlements. Not in V1 payloads, not gated by V1 clients (D5) |

⛔ **Not in the payload:** address, first name, last name, e-mail, notes, `cid`, hardware data, counters,
URLs, secrets. Those live in the License Manager (§12.2). A license is *an assertion about a customer's
rights*, not a description of a person or a computer.

### 9.1 Runtime model (client)

```csharp
// EmberTern.Licensing — pure: no Avalonia, no Firebird, no I/O, no System.Net.*
sealed record LicensePayload(int Lv, string Kid, string Lid, string Prod,
                             string Licensee, int Seats,
                             DateTimeOffset IssuedAt, DateTimeOffset NotBefore,
                             DateTimeOffset ExpiresAt, DateTimeOffset? MaintenanceUntil);

sealed record TrustedKey(string Kid, SignatureAlgorithm Algorithm, byte[] PublicKey, bool Revoked);

enum LicenseStatus  { Unlicensed, Invalid, NotYetValid, Valid, Grace, Expired, VersionNotCovered }
enum LicenseFailure { None, FileMissing, NotALicense, MalformedArmor, MalformedEnvelope,
                      MalformedPayload, UnsupportedVersion, UnknownKey, RevokedKey,
                      AlgorithmMismatch, SignatureInvalid, WrongProduct }

sealed record LicenseVerdict(LicenseStatus Status, LicenseFailure Failure,
                             LicensePayload? Payload, string? Detail);

static class LicenseVerifier { static LicenseVerdict Verify(string text, in LicenseVerificationContext c); }
                                                                        // ⭐ ONE entry point
```

⚠ **Why a closed `LicenseFailure` enum and not `MessageKey`, which is this project's ratified D‑3
currency.** `MessageKey` exists so that Core and Firebird can name a message without owning the words, and
it presumes **one** resource catalog — App's. ⭐ **A licensing verdict is rendered by two applications with
two independent catalogs** (EmberTern and the License Manager), so a key string would have to resolve in
both, and a key present in one and missing in the other fails silently — exactly the Phase-5 defect shape.
An enum is a closed set both applications map on their own terms, and the compiler can see every value.
`Detail` carries a technical token for `[Copy details]` (an unknown `kid`, a parse offset) and is ⛔ **never
rendered as prose**.

⭐ Architecture rule 12 still applies in full at the two display sites — see §17.3 for the specific trap.

---

## 10. Seats in V1 (D2)

The license states `seats: N`. EmberTern **displays** it and enforces nothing. The number is a term of
the contract, recorded in the register, carried in the signed payload, and visible in Settings ▸ License.

⭐ **This is stated honestly in the design because it will be stated honestly in support**: if a customer
copies a valid `.etlic` to a second machine, V1 works there. Nothing in V1 pretends otherwise —
no `maxInstallations` field that does not limit anything, no fingerprint that a copied profile defeats.
Technical enforcement is V2's job (§21), and it needs the backend to exist first.

---

## 11. What deters casual sharing in V1 (D6)

One thing, and it is a social mechanism, not a technical one: **the licensee name is signed into the
license and shown in the product.**

```
About              →  "Licensed to ACME Sp. z o.o."
Settings ▸ License →  licensee · seats · valid from–until · license id
```

It cannot be edited (the signature covers it) and it cannot be hidden. Handing the file to another
company means handing them a build that names yours. In B2B this is remarkably effective and it costs
one field and one label. ⛔ **It is not claimed as security anywhere in this document.**

⛔ **Not in the title bar** — the title bar is working space.

---

## 12. License Manager (D7)

### 12.1 Stack and layout (D11)

**.NET 9, Avalonia 12.1.1, MVVM, DI** — the same versions EmberTern uses, inheriting the repository's
`Directory.Build.props`. ⚠ The reason for net9 is environmental (Visual Studio 2022), not architectural;
when EmberTern moves to .NET 10 the License Manager moves with it, as one task.

```
src/
  EmberTern.Licensing/            ⭐ PURE. No Avalonia, no Firebird, no I/O, no System.Net.*
    LicenseArmor.cs                  strip/apply the -----BEGIN EMBERTERN LICENSE----- wrapper
    LicenseEnvelope.cs               parse ETL1.<payload>.<signature>
    LicensePayload.cs
    LicenseVerifier.cs               ⭐ THE one entry point
    TrustedKeys.cs                   append-only PUBLIC key table
    SignatureAlgorithm.cs
    → referenced by EmberTern.App AND EmberTern.LicenseManager

  EmberTern.Licensing.Issuing/    ⛔ NEVER referenced by EmberTern.App (guard test §16.1)
    LicenseIssuer.cs                 signing
    KeyStore.cs                      AES-256-GCM + PBKDF2 (reuses Core.Security patterns)
    KeyCeremony.cs                   key generation + verified restore
    → referenced ONLY by EmberTern.LicenseManager

  EmberTern.LicenseManager/       Avalonia desktop
    Views/ ViewModels/ Data/ Email/
    Themes/  → ⭐ LINKED from ../EmberTern.App/Themes/*.axaml, not copied

tests/
  EmberTern.Tests/                existing — gains the EmberTern.Licensing tests
                                  and PrivateKeyNeverShipsTests / LicensingMakesNoNetworkCallsTests
  EmberTern.LicenseManager.Tests/ new — Issuing, the SQLite store, e-mail composition

EmberTern.slnx                    gains src/EmberTern.Licensing
EmberTern.LicenseManager.slnx     new: Licensing + Issuing + LicenseManager + its tests
```

⭐ **Theme sharing by file link, not by moving files.**
`<AvaloniaResource Include="..\EmberTern.App\Themes\*.axaml" Link="Themes\%(Filename)%(Extension)" />`
gives one source of truth at **zero risk to EmberTern** — moving those files into a shared library would
break every `avares://EmberTern/Themes/…` URI in the app. All `CLAUDE.md` UI rules apply unchanged to
the License Manager: no hardcoded colours, tokens only, both themes, `ControlStyles.axaml` classes.

⚠ Adding tests to `EmberTern.Tests` changes the suite total, which `CLAUDE.md` treats as an acceptance
criterion. **Re-measure it; do not carry the old number forward.**

### 12.2 Customer record (D5)

```sql
CREATE TABLE customers (
  customer_id TEXT PRIMARY KEY,
  name        TEXT NOT NULL,     -- ⭐ REQUIRED — the one mandatory field
  address     TEXT,
  first_name  TEXT,
  last_name   TEXT,
  email       TEXT,              -- used by [Send license] (§14)
  notes       TEXT,              -- ⛔ admin-only, NEVER in the license payload
  created_at  TEXT NOT NULL,
  updated_at  TEXT NOT NULL
);
```

⚠ **`name` is required at the database level *and* in the UI**, because it is the value signed into every
license this customer ever receives (D6).

### 12.3 V1 feature set

| Area | Capability |
|---|---|
| Customers | create · edit · search · warn on a duplicate name |
| Licenses | create · edit terms · **change end date** · **re-issue** (new artifact, same `lid`) |
| Issuing | sign · **save `EmberTern.etlic` to disk** · copy the token · export a batch + a CSV manifest |
| Bulk | filter (expiring in N days / status) → select → **Extend to date** → batch re-issue |
| ⭐ E-mail | **[Send license]** — HTML message with the `.etlic` attached (§14) |
| Preview | inspect any issued artifact: decoded payload, signature status, the exact bytes sent |
| History | append-only `audit_log`: every mutation and every send, with actor, timestamp, before/after |
| Status | active · superseded · blocked (⚠ **blocked is bookkeeping in V1** — §26.2) |
| Backup | encrypted store export **+ a plain JSONL dump** (§30.3) |
| Keys | key ceremony · passphrase change · backup with **verified restore** |

⛔ **Not built:** editions UI (D5), invoicing, payments, CRM, a web portal, multi-operator accounts,
role-based access. The License Manager is a single-operator desktop tool.

### 12.4 Database schema

```sql
CREATE TABLE licenses (
  lid          TEXT PRIMARY KEY,
  customer_id  TEXT NOT NULL REFERENCES customers(customer_id),
  product      TEXT NOT NULL DEFAULT 'EmberTern',
  seats        INTEGER NOT NULL,
  not_before   TEXT NOT NULL,
  expires_at   TEXT NOT NULL,
  maint_until  TEXT,                  -- reserved (§3)
  status       TEXT NOT NULL,         -- active | superseded | blocked
  notes        TEXT,
  created_at   TEXT NOT NULL,
  updated_at   TEXT NOT NULL
);

-- ⭐ Every artifact ever signed. Append-only. This is what makes a lost license
--    a 5-second re-export instead of a re-issue with a new iat.
CREATE TABLE issued_artifacts (
  artifact_id  INTEGER PRIMARY KEY AUTOINCREMENT,
  lid          TEXT NOT NULL REFERENCES licenses(lid),
  kid          TEXT NOT NULL,
  issued_at    TEXT NOT NULL,
  payload_json TEXT NOT NULL,         -- exactly what was signed
  token        TEXT NOT NULL,         -- the full ETL1.… artifact, verbatim
  reason       TEXT NOT NULL          -- initial | renewal | terms-change | reissue-lost
);

-- ⭐ Append-only. No UPDATE, no DELETE, ever — enforced by a trigger, not by a ViewModel.
CREATE TABLE audit_log (
  audit_id    INTEGER PRIMARY KEY AUTOINCREMENT,
  at          TEXT NOT NULL,
  actor       TEXT NOT NULL,          -- OS user of the admin machine
  action      TEXT NOT NULL,          -- incl. 'license.sent'
  target_type TEXT NOT NULL,
  target_id   TEXT NOT NULL,
  before_json TEXT,
  after_json  TEXT,
  note        TEXT
);

CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);
```

⭐ **SQLite is the register of record, not a cache.** The signing key is offline; the register and the key
belong together. Backup is a file copy, restore is a file copy, both testable in ten seconds, and it
outlives every vendor decision in this document. `Microsoft.Data.Sqlite` — first-party, one package.

⛔ **The private key is not in this database.** It lives in `keystore.etkeys` with its own protection, so
that handing someone the `.db` for inspection leaks nothing that can sign.

### 12.5 ⭐ The safety rule

**An issued artifact is immutable and is never edited — only superseded.** Changing terms produces a
*new* artifact with a later `iat` and the same `lid`; the old row is kept forever. Consequences: the
register can always answer *"what exactly did we send this customer in 2026?"*, a lost license is
re-exportable byte-for-byte, and no code path can silently change what a customer was told they bought.
Architecture rule 11, applied to the admin side.

---

## 13. License file format (D4)

### 13.1 The artifact

`EmberTern.etlic` — UTF-8, **no BOM** (the project's existing rule for generated files, gotcha #178):

```
-----BEGIN EMBERTERN LICENSE-----
ETL1.eyJsdiI6MSwia2lkIjoiUjEiLCJhbGciOiJFUzI1Ni1QMTM2MyIsImxpZCI6IjAx
OTFmM2M0YjJhNzQxZDg5ZTBmYTIxYzdkNGUzMDU2IiwicHJvZCI6IkVtYmVyVGVybiIs
ImxpYyI6IkFDTUUgU3AuIHogby5vLiIsInNlYXRzIjo1LCJpYXQiOiIyMDI2LTA4LTE1
…
.MEUCIQD8f2rK1nT4mQpXvA7hLbYcR3sZ0uNjKdE9xWfPqTgBvwIgH2mC…
-----END EMBERTERN LICENSE-----
```

| Element | Decision | Why |
|---|---|---|
| Armor (`-----BEGIN/END…-----`) | ✅ | ⭐ **Functional, not cosmetic:** e-mail clients wrap long lines. The parser strips the armor and **all** whitespace, so a token mangled by line-wrapping still imports. A 50-year-old convention that copies and pastes safely |
| Human-readable header inside the armor | ⛔ **no** | An unsigned *"Valid until: 2099"* line would be a misinformation channel for support and the customer. **Nothing in the file may assert something the signature does not cover** |
| Payload encoding | compact JSON → **base64url** | ⭐ Satisfies D4 on its own: the file shows no editable JSON. ⛔ And that is **encoding, not protection** — it is not claimed as security anywhere |
| Signature encoding | base64url, unpadded, **fixed 64 bytes** (IEEE P1363) | §15.3 |
| Compression | ⛔ none | A second parse path and a decompression-bomb surface, to save ~150 characters |
| Encryption | ⛔ none | Integrity and authenticity are the goals. A license support can decode is a license support can debug |
| ZIP | ⛔ **never** (D4/D8) | |

The parser accepts **either** the armored file **or** a bare pasted token — same code path, whitespace
stripped. Delivered filename: `EmberTern.etlic`. Stored filename: `license.etlic` (§8).

### 13.2 Realistic size

```json
{"lv":1,"kid":"R1","alg":"ES256-P1363","lid":"0191f3c4b2a741d89e0fa21c7d4e3056",
 "prod":"EmberTern","lic":"ACME Sp. z o.o.","seats":5,
 "iat":"2026-08-15T10:00:00Z","nbf":"2026-08-15T00:00:00Z","exp":"2027-08-15T23:59:59Z"}
```

≈ 265 bytes JSON → ≈ 354 base64url characters, + `ETL1.` + `.` + 86 = **≈ 450 characters**, six wrapped
lines inside the armor. ⭐ Dropping `ed`, `feat`, `cid` and `seatPolicy` (§3) took roughly 150 characters
off the earlier draft.

### 13.3 Signing input — bytes, never objects

```
signingInput = ASCII("ETL1.") ‖ ASCII(base64url(payloadJsonUtf8))
signature    = ECDSA-P256-SHA256(privateKey, signingInput)      ← 64-byte P1363
token        = signingInput ‖ ASCII(".") ‖ base64url(signature)
```

- The magic is **inside** the signing input, so a token can never be replayed under a future envelope.
- The signature covers the **encoded** payload segment. ⚠ *Corrected in L1, because the original wording
  here — "verify first, parse second" — is not implementable:* the `kid` that selects the key lives
  **inside** the payload, so the payload must be read before anything can be verified. That is true of
  every signed-token format. ⭐ **The rule is therefore: never TRUST and never RE-SERIALISE.** Only `kid`
  and `lv` are consulted beforehand, and only to pick a key or refuse outright; the signature is computed
  over the segment exactly as it arrived; no field is acted on until it verifies. No canonical JSON, no
  key ordering, no risk that a parse/re-serialise round-trip changes the bytes — the corpus case
  `payload-reserialised-with-spaces` is what holds that shut.
- base64url is unpadded, and its decoder is **strict**: ⛔ no whitespace, no padding, no standard-base64
  `+`/`/`, and no length that is `1 mod 4`. ⚠ *Written by hand rather than calling the BCL helper, on
  purpose* — a lenient decoder lets two different texts decode to the same bytes, which is the ambiguity
  that produces signature-confusion bugs.

### 13.4 Forward-compatibility rules (binding)

1. Unknown **optional** top-level fields ⇒ **ignored**.
2. ⭐ **Any field whose *ignoring* would be unsafe travels with an `lv` bump.** `iid` is the first such
   field: a bound license is `lv = 2`, so a V1 client refuses it rather than silently ignoring the
   binding. This rule is what makes rule 1 safe.
3. Unknown `kid` ⇒ **refuse** — *"issued for a newer version of EmberTern"*.
4. `lv` above supported ⇒ **refuse**, same message.
5. ⛔ Rules 3 and 4 are the only refusals for format reasons. Everything else degrades quietly.

### 13.5 `maint` — the one reserved field V1 enforces

```
maint absent            ⇒ no constraint (every V1 license)
maint present and
  AppInfo.ReleaseDate > maint  ⇒ VersionNotCovered
```

`AppInfo.ReleaseDate` already exists as an assembly attribute fed by `Directory.Build.props`, so the gate
is a comparison. ⚠ Flagged in §3 as the single piece of unused-in-V1 code, kept only because it cannot be
retrofitted onto clients already in the field. **Overrule it freely.**

---

## 14. ⭐ Sending the license by e-mail (D8 — new in V1)

### 14.1 The flow

```
License Manager ▸ a license ▸ [ Send license ]
        │
        ▼
   ┌──────────────────────────────────────────────────────┐
   │  Send license                                        │
   │  To:       biuro@acme.pl        (from the customer)  │
   │  Subject:  Your EmberTern license                    │
   │  Attach:   EmberTern.etlic  (≈1 KB)                  │
   │  ┌────────────────────────────────────────────────┐  │
   │  │  ⟨rendered HTML preview⟩                       │  │
   │  └────────────────────────────────────────────────┘  │
   │                          [ Cancel ]  [ Send ]        │
   └──────────────────────────────────────────────────────┘
        │
        ▼  ⭐ explicit confirmation — always, including in bulk
   send → result recorded in audit_log ('license.sent', recipient, outcome)
        │
        ├── success ──► "Sent to biuro@acme.pl"
        └── failure ──► the SMTP error verbatim + [ Save .eml instead ]
```

⭐ **`[Save license to disk]` is always available and never depends on e-mail** (D8): in some cases we
install the license at the customer's site ourselves, and in others their mail server rejects
attachments. E-mail is a delivery convenience, never the only way out.

⭐ **Bulk sending shows the full recipient list and requires one explicit confirmation**, then reports per
message. ⛔ No silent bulk send.

### 14.2 Message content (D8)

An HTML message with a plain-text alternative (some corporate clients strip HTML), styled to match
EmberTern, containing:

1. a greeting addressed to the **customer name**;
2. one short paragraph about EmberTern;
3. the **validity period** — *"valid from 15 August 2026 to 15 August 2027"*;
4. the **seat count**, as the contractual term it is;
5. **activation instructions** — three sentences: start EmberTern, drop the attached file on the
   activation window, done;
6. a note that **`EmberTern.etlic` is attached**, and that it should be kept — it can be re-imported at
   any time;
7. contact details for questions.

⛔ **The e-mail body never repeats a claim it could get wrong.** Dates come from the same payload that
was signed, rendered once, so the message and the license cannot disagree. ⛔ No tracking pixel, no
telemetry, no read receipt.

⚠ The message template is a **localizable resource in the License Manager**, not a string in code — the
customer's language may not be the admin's.

### 14.3 SMTP configuration and secret storage (D9)

```
EmberTern.LicenseManager/Email/
   ILicenseEmailSender.cs        Compose(...) → SendAsync(...) → SendResult
   SmtpLicenseEmailSender.cs     System.Net.Mail.SmtpClient  (host, port, TLS, user, password)
   EmlFileEmailSender.cs         ⭐ writes a ready .eml the admin opens in their own mail client
   SmtpSettings.cs               host · port · TLS mode · from-address · from-name · username
   SmtpSettingsStore.cs          ⭐ DPAPI (CurrentUser) — the SAME mechanism EmberTern uses
```

- ⛔ **The password is never in code, in the repository, in `appsettings`, or in any log.** It is written
  only into `%APPDATA%\EmberTern License Manager\smtp.dat`, encrypted with **Windows DPAPI, CurrentUser
  scope**, exactly as EmberTern protects connection passwords (`DpapiSecretProtector`,
  `EncryptionSchemes.Dpapi`). ⚠ DPAPI CurrentUser is deliberately **not** portable across machines or
  accounts — which is correct for a credential and must be documented in the UI, as EmberTern already
  does for connection profiles.
- ⛔ **Log redaction is a requirement, not a habit**: any diagnostic that echoes the SMTP settings masks
  the password, in the style of `LogConnectionAttempt`.

⚠ **Architecture rule 2 says "no interfaces without two concrete implementations", and this design has
two real ones** — `SmtpLicenseEmailSender` and `EmlFileEmailSender`. The `.eml` writer is not a
rule-satisfying stub: it is the fallback for the very likely case below, and it is what an admin who
prefers to send from Outlook will actually use.

⚠⚠ **Measure this before L6, it is the one real unknown in V1:** `System.Net.Mail.SmtpClient` supports
basic authentication over TLS but **not OAuth2 / XOAUTH2**. Many corporate tenants (Microsoft 365,
Google Workspace) have disabled SMTP basic auth. If the sending mailbox lives on such a tenant, the
options are an app password (where permitted), a relay that accepts basic auth, MailKit for OAuth2, or an
HTTP mail API. ⭐ **`ILicenseEmailSender` exists precisely so that choice is a new class, not a rebuild**
— and the `.eml` path works regardless of the answer. ⛔ Whatever is chosen, the dependency lives in the
License Manager only; **EmberTern gains nothing.**

---

## 15. Cryptography (D10)

### 15.1 ECDSA P-256 — confirmed, with the three caveats worth writing down

The user asked whether P-256 has a critical problem. **It does not.** Three things are nonetheless worth
recording so they are not discovered later:

1. ⭐ **Signatures must be fixed-length P1363 (`r‖s`, 64 bytes), never DER.** DER is variable-length and
   drags an ASN.1 parser into the verification path — extra surface for zero benefit. .NET supports this
   directly: `ECDsa.SignData/VerifyData(…, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`.
2. ⚠ **ECDSA leaks the private key if a signing nonce is reused or biased.** This is the standard ECDSA
   footgun and it is bounded here: signing happens only in the License Manager, on one machine, a few
   times a month, using .NET's platform CSPRNG. Nothing in the design signs attacker-chosen data at
   volume. ⛔ Do not hand-roll signing; use `ECDsa` as provided.
3. **Signature malleability (`s` vs `n−s`) is irrelevant**, because signatures are never used as
   identifiers or deduplication keys. `lid` is the identity.

**What it buys:** zero third-party cryptography in the shipped client. With `TreatWarningsAsErrors`
escalating `NU1902`/`NU1903` (gotcha #278), a CVE in a client-side crypto package would fail EmberTern's
build — on the one code path that should be boring and stationary for a decade.

### 15.2 ⭐ The `kid` rule

```
The client holds an APPEND-ONLY table:

   kid   algorithm            public key        revoked
   ───   ──────────────────   ───────────────   ───────
   R1    ECDSA-P256-SHA256    30 59 30 13 …     false

VERIFICATION
   1. read kid from the payload
   2. look it up                        ← unknown kid ⇒ REFUSE, never guess
   3. THE TABLE ENTRY dictates the algorithm
   4. cross-check payload.alg == the entry's algorithm  ⇒ mismatch = REFUSE
   5. verify

⛔ payload.alg NEVER chooses an algorithm or a key.
⛔ There is no "none" algorithm and no path where a missing signature verifies.
⛔ There is no "try every key" fallback.
```

This one mechanism covers **key rotation, algorithm migration (including a future Ed25519) and the V2
backend's own key** — which is why it is the only future-facing machinery kept in V1 (§3).

### 15.3 Key rotation

| Case | Trigger | Old licenses | Action |
|---|---|---|---|
| **Scheduled** | hygiene, every 3–5 years | ✅ keep working forever | add `R2` to `TrustedKeys` in release *N*; start signing with `R2` only once *N* is widely deployed |
| **Algorithm change** | e.g. adopting Ed25519 | ✅ keep working | identical — the table entry names the algorithm |
| **Compromise** ☠ | key stolen | ❌ must die | (1) new key + reissue **every** live license; (2) *only then* ship a release marking the old `kid` revoked — that release refuses honest old licenses too, hence the ordering |

⛔ **`TrustedKeys` entries are never removed and never edited** — only appended and flagged. A key removed
from the table is a population of licenses that stopped working. `EncryptionSchemes.cs` documents the same
discipline for persisted identifiers in this codebase.

---

## 16. Hardening in V1

### 16.1 ⭐ `PrivateKeyNeverShipsTests` — written in L2, before there is a real key

1. `EmberTern.App`'s transitive assembly closure contains **no** `EmberTern.Licensing.Issuing`.
2. No type in `EmberTern.Licensing` exposes a signing operation or a private-key parameter.
3. No file under `src/EmberTern.App` or `src/EmberTern.Licensing` matches `PRIVATE KEY`, `.etkeys`, or a
   base64 blob of private-key length.
4. `EmberTern.App`'s published output contains no `*.etkeys` / `*.pem` / `*.key`.

⭐ The test exists so the rule survives whoever wrote it — and `TreatWarningsAsErrors` makes a violation a
build failure, not a review comment.

### 16.2 ⭐ `LicensingMakesNoNetworkCallsTests` — written in L1

The licensing assemblies' referenced-type closure contains nothing from `System.Net.*`. This is the
machine-checkable form of D1, and it will still be true in 2031 when nobody remembers this conversation.

### 16.3 Clock-rollback guard

```
settings.dat (already DPAPI-encrypted, versioned, migration-aware)
   └── LicenseClockHighWater : DateTimeOffset

every start:   effectiveNow = max(systemNow, highWater)
               systemNow < highWater − 48 h  →  warn, non-blocking
on exit:       highWater = max(highWater, systemNow)
```

⭐ **In V1 the expiry date is the *entire* enforcement mechanism, so leaving the clock unguarded makes it
a no-op.** That is why this small piece stays despite the simplification pass.

- **48 h tolerance**, because time zones, DST, VM suspends, dead CMOS batteries and travelling laptops are
  all normal.
- ⛔ **Warns, never blocks.** A user legitimately fixing a badly wrong clock must not be locked out of
  their tool. Architecture rule 11 governs here too.
- ⭐ Reuses `settings.dat` rather than inventing a store — DPAPI-per-user makes casual editing hard, and
  the versioned header, migration path and refuse-on-unreadable `Save` are already implemented and tested.

### 16.4 The freshness rule for replacing a license

```
Install the incoming license ONLY IF:
      signature verifies
  AND prod matches
  AND ( there is no local license
        OR ( incoming.lid == local.lid AND incoming.iat > local.iat )
        OR the user explicitly confirmed replacing a DIFFERENT lid )
```

⭐ Makes renewal idempotent, and makes an accidental re-import of last year's file a no-op instead of a
downgrade. The explicit-confirmation branch exists because moving a machine to a different license is
legitimate.

### 16.5 ⭐ The Debug / Release gate (D15)

**⭐ The rule, stated precisely: `Debug` disables the *block*, not the *licensing*.** Verification runs
identically in both configurations, the verdict is computed, displayed and logged the same way; the only
difference is whether an absent or invalid verdict prevents the application from being used.

```csharp
// EmberTern.App/Licensing/LicensingPolicy.cs  —  the ONLY place this distinction exists
internal static class LicensingPolicy
{
#if DEBUG
    internal const bool GateEnabled = false;
#else
    internal const bool GateEnabled = true;
#endif
}
```

| Property | How it is achieved |
|---|---|
| Follows the build configuration, not the debugger | `DEBUG` is a compile-time symbol the SDK defines for the `Debug` configuration only. ⛔ `Debugger.IsAttached` is never consulted — it is a *runtime* fact an attacker controls, and it would also make a `Release` build behave differently under a profiler |
| ⭐ **No bypass code exists in a `Release` binary** | `GateEnabled` is a `const`, so the compiler folds `if (LicensingPolicy.GateEnabled)` and eliminates the dead arm. There is nothing to patch back on, because there is nothing there |
| ⛔ No configuration switch in `Release` | No setting, no environment variable, no command-line argument, no file influences it. The only input is which configuration was compiled |
| ⭐ **The bypass is in the gate, never in the verifier** | `EmberTern.Licensing` tells the truth in every configuration. ⚠ **This is load-bearing:** the suite runs in `Debug`, so a bypass inside the verifier would make the entire tamper corpus vacuous — every licensing test would pass while proving nothing |

**Guard tests (land in L4, with the gate):**

1. **Runtime pair** — `#if DEBUG` asserts `GateEnabled == false`, `#else` asserts `true`. Running the
   suite in `Release` therefore proves the `Release` arm, which a `Debug`-only run never can.
2. **Source structure** — `LicensingPolicy.cs` contains exactly one `#if DEBUG` / `#else` / `#endif`, the
   `#else` arm is `true`, and nothing else in the file writes `GateEnabled`.
3. **No runtime switch** — no file under `src/EmberTern.App` mentions `GateEnabled` alongside an
   environment variable, a preference, a settings read or a command-line argument.
4. ⭐ **No `DefineConstants` smuggling** — neither `Directory.Build.props` nor any `.csproj` adds `DEBUG`
   to a non-`Debug` configuration. Without this one, all three tests above stay green while the bypass
   ships; it is the cheapest and least obvious of the four.

⚠ **Consequence for L4's acceptance:** the gated states (`Unlicensed`, `Invalid`, `Expired`) can only be
seen blocking in a **`Release`** build. `CLAUDE.md` already requires building both configurations before
asking for a visual check; here it becomes a verification requirement, not just hygiene. ⭐ The licensing
*screens* remain reachable in `Debug` from Settings ▸ License, showing the real verdict, so the flow
itself is developable without switching configuration — only the blocking behaviour needs `Release`.

⚠ **One Debug-only marker is proposed for L4**: the About window appends *"Debug build — licensing gate
off"* to the version line. Rationale: without it, a developer seeing EmberTern start without a license
cannot tell whether the gate is off by design or broken. ⭐ It is developer-facing text that can never
reach a user (users receive `Release` builds), in the same class as `%TEMP%\EmberTern-debug.log`, so
Architecture rule 12 is not engaged. Flagged here so it can be overruled.

---

## 17. UX

### 17.1 The customer's entire relationship with licensing

```
install → launch → "Activate EmberTern" → drop the file → "Licensed to ACME Sp. z o.o."
        → …then nothing, for a year.
```

Once a year: an unobtrusive banner 30 days before expiry, and a new file in the inbox.

### 17.2 Surfaces

| Surface | Content |
|---|---|
| **Activation window** | first run only; drop target + Browse + paste; errors in a `MessageBanner` — ⭐ the IDE's ONE message surface, never a locally styled coloured `TextBlock` |
| **Settings ▸ License** | a new Settings Center category: licensee, seats, valid from–until, license id, `[Update license]`, `[Copy license id]` |
| **About** | *"Licensed to …"*, beside the version `AppInfo` already reads |
| **Expiry banner** | `MessageBanner` on the main surface: Info ≤ 30 d, Warning in grace, Error when expired |

⛔ Nowhere else. No startup modal for a valid license. No nag screens. No "buy now".

### 17.3 ⚠⚠ Localization — the highest-risk part of this feature

Every string goes through `Strings.resx` + `Strings.pl.resx`, resolved at display time (Architecture rule
12). **The Phase-5 charset-guard defect has exactly this shape and will repeat if it is not planned
against:** the failure mode is not a missing entry but a *perfect entry that nothing reads*, because the
message was wrapped on its way out and the display site read `ex.Message`. Licensing is the same shape —
a verdict produced deep in a pure library, surfaced by App.

⭐ **A licensing message is finished only when it has been seen rendered in Polish, or pinned by a test
that resolves it through the path the UI actually uses** (`ErrorText`), as `CharsetGuardLocalizationTests`
does.

⭐ Every failure message answers three questions: **what happened · why · what to do now.** Not
*"License validation failed (code 7)."*

### 17.4 Terminology

Checked against `docs/design/terminology.md` and enforced by `TerminologyTests`. Proposed: **Activate** /
*Aktywuj*, **Update license** / *Aktualizuj licencję*, **Send license** / *Wyślij licencję* (License
Manager only). To be verified against the norm during L4 — not assumed here.

### 17.5 Privacy

⭐ **V1 sends nothing, ever** — no telemetry, no check-in, no phone-home, not even an optional one. The
only outbound traffic anywhere in V1 is the License Manager's SMTP connection, from the admin's machine,
initiated explicitly by the admin.

---

## 18. V1 scope — the complete list

✅ ETL1 signed license format: armored `.etlic`, base64url payload, 64-byte P1363 ECDSA P-256 signature
✅ `lv` gate · `kid` → key + algorithm · append-only `TrustedKeys`
✅ Local verification: signature · product · `nbf` / `exp` · `maint` (reserved) · clock high-water
✅ Eight-state licensing state machine, 14-day grace, EN + PL
✅ ⭐ `Debug` disables the block but not the licensing; `Release` has no bypass code at all (§16.5)
✅ Activation by file drop, Browse or paste; atomic write; re-verify from disk
✅ `%APPDATA%` + `%PROGRAMDATA%` resolution order
✅ Licensee name displayed in About and Settings ▸ License
✅ Seats as a contractual, displayed number
✅ License Manager: customers (name required, address, first/last, e-mail, notes) · licenses · issue ·
   re-issue · change end date · group extend · search · filter · preview · immutable history ·
   save `.etlic` · encrypted backup + JSONL escape hatch
✅ ⭐ Send license by e-mail: HTML + plain text, `.etlic` attached, preview and explicit confirm,
   `.eml` fallback, send recorded in the audit log
✅ ⭐ SMTP settings with the password under Windows DPAPI, behind `ILicenseEmailSender`
✅ Offline key ceremony, encrypted keystore, verified restore, rotation procedure
✅ Guard tests: private key never ships · no network in licensing · tamper corpus · audit immutability
⛔ **Zero network code in EmberTern. Zero cloud accounts. €0. No payment card anywhere.**

---

# PART II — V2: ONLINE ACTIVATION (planned next stage)

📋 **Planned, not optional (D12). Not started, not implemented, not scheduled here.** This part exists so
that V1's format decisions are known to be compatible with it — nothing in Part I is built for it except
what §3 lists.

## 19. The target model (D12)

```
1. admin generates a SHORT ONE-TIME activation code   →  ETRN-4K7P-9WQX-2M8D
2. customer types it into EmberTern
3. EmberTern contacts the backend                     ← the only online moment
4. backend validates the code
5. code is CONSUMED / assigned to this activation     ← reuse protection
6. backend issues a signed license (lv = 2, iid set)
7. EmberTern stores it locally
8. all further work is offline — exactly as in V1
```

⛔ **No mandatory user accounts or login.** EmberTern is not SaaS; a customer must not need an account to
run a program they bought. The activation code is the credential. Firebase Authentication may be
considered later as an *auxiliary* option (e.g. a self-service customer portal), ⛔ never as the basis of
licensing.

⭐ **Why the code must be short here and cannot be short in V1:** a short code carries no signature and no
data — it is a *lookup handle*, which requires something to look it up in. **Short code ⟺ backend.** That
is the whole architectural fork, and it is why V1's artifact is long (§13.2) and V2's code is short.

## 20. What V2 adds

| Capability | Note |
|---|---|
| One-time activation codes | generated by the License Manager, consumed by the backend |
| Online activation | the flow in §19 |
| **Technical seat enforcement** | the reason V2 exists (§21) |
| Code-reuse protection | a consumed code cannot activate a second installation |
| Activation management | list, inspect and **release** activations; block with real effect on new activations |
| Self-service renewal | the client asks the backend instead of waiting for an e-mail |
| Offline activation requests | `.etreq` → License Manager → the **same** signed license artifact — ⛔ one licensing model, two couriers |

⭐ **EmberTern still works offline after activation.** ⛔ No periodic online check as the base model — the
strict-offline decision (D1) survives into V2 for everything except the activation moment itself.

## 21. Seat enforcement and device binding (V2 only)

```
license (backend)              activations (backend)
  lid                            lid           FK
  seats        5                 iid           PK with lid
  status       active            fingerprint   hashed components
                                 activated_at
                                 released_at   NULL = occupying a seat
```

Activation, in one transaction:

```
1. unknown / already-consumed code        → refuse
2. status != active                       → refuse
3. this iid already active                → RE-ISSUE the same seat (idempotent; retries are free)
4. active activations < seats             → take a seat
5. otherwise → FINGERPRINT RECLAIM:
      an active row matching ≥3 of 6 hashed components
        → release it (reason 'fingerprint-reclaim'), take its seat
      else → refuse, naming the admin contact
6. sign lv = 2 with iid set;  append an audit row
```

⭐ **Step 5 is the difference between a licensing system and a support burden.** The most common real
event — *"I reinstalled Windows and now it says no seats left"* — resolves itself, without an admin,
without weakening the limit for anyone actually running a sixth machine.

### 21.1 ⚠ The binding model when it arrives (the V1 analysis that still holds)

When binding is built, bind to a **random `InstallationId`**, not to hardware. Measured against what
actually happens to a developer's workstation:

| Event | SMBIOS UUID | CPU id | Volume id | **Random InstallationId** |
|---|---|---|---|---|
| RAM / GPU / PSU upgrade | stable | stable | stable | **stable** |
| SSD replaced, image restored | stable | stable | ❌ changes | **stable** |
| Motherboard replaced (warranty) | ❌ **changes** | stable | stable | **stable** |
| Windows reinstalled | stable | stable | ❌ changes | ❌ changes → re-activate |
| Dev VM cloned 10× | ❌ **identical — abuse invisible** | identical | identical | ❌ identical |

**The hardware triple punishes the honest customer and fails to detect the dishonest one.** A random id
does neither. Hardware signals belong **only** on the server, **only** hashed and salted, and **only** to
answer *"is this probably the same machine, reinstalled?"* for the reclaim in step 5.

⛔ **None of this is built in V1 (D3),** and V1 generates no InstallationId at all — because with an
offline license the user can copy the id along with the file, so it would add work and complexity for
appearance rather than protection.

## 22. Backend options for V2 (D13)

**Recommended direction: Cloudflare Worker + D1.** Kept as a direction, not a decision — it will be
re-evaluated when V2 starts, against the free-tier terms of that day.

| | Worker + D1 ⭐ | Worker + Firestore | Firestore direct | Something else |
|---|---|---|---|---|
| Can sign a license | ✅ | ✅ | ❌ **rules have no cryptography** | — |
| Can enforce seats | ✅ | ✅ | ❌ forgeable (§22.2) | — |
| Clouds / secrets to manage | 1 / 1 | 2 / 2 | ☠ service account in the client | — |
| .NET client SDK | n/a | n/a | ❌ none for desktop | — |
| Runaway-cost risk | none (no card) | none | none | must be verified |
| Verdict | **recommended** | rejected — §22.1 | **impossible** — §22.2 | re-evaluate at V2 |

### 22.1 Why not Firestore behind the Worker

It contributes nothing D1 does not, and costs a second cloud account, a second private key (a Google
service-account key minting RS256 JWTs for OAuth2), two subrequests per activation, and no SQL. D1 is
SQLite; the License Manager's register is SQLite. Same shape, one vendor.

### 22.2 Why Firestore cannot be the backend on its own

- **Security Rules cannot sign** — so Firestore can only distribute licenses something else signed, i.e.
  it is a file host with a daily quota, and it can never bind a license to a device.
- **No Firebase client SDK exists for .NET desktop.** `Google.Cloud.Firestore` is the *server* SDK and
  authenticates with a service account, which must never ship in a client (server SDKs bypass Rules
  entirely).
- **Rules cannot count**, so seat limits are unexpressible; the counter workaround is defeated by a
  client that simply never writes its activation document.
- **Spark has no rate limiting and no WAF**, so anonymous reads are a free denial-of-service on our own
  customers: exhausting the daily quota costs the attacker nothing and blocks every real activation until
  the UTC reset.
- **Cloud Functions require Blaze**, which is excluded.

### 22.3 ⚠ Firebase App Check does not apply (correcting the earlier suggestion)

App Check attests that a request comes from a genuine instance of your app using **platform**
attestation: reCAPTCHA (web), Play Integrity (Android), DeviceCheck / App Attest (Apple). **There is no
Windows-desktop provider.** The remaining path — a *custom* provider — requires a trusted server to mint
App Check tokens, i.e. it presupposes the backend it was meant to protect, and can only authenticate a
secret shipped in the client, which is not a secret. **Drop it from consideration.**

### 22.4 Cost and abuse posture (to re-verify at V2)

Cloudflare Workers Free requires **no payment method**; exceeding the daily request cap refuses requests
until the UTC reset and **never produces an invoice**. ⭐ **But "zero cost risk" is not "zero risk":** the
same cap is a denial-of-service lever. That is acceptable here **only because the backend is off the
product's critical path** — activations are rare and every already-activated customer is unaffected. ⛔ It
would not be acceptable in a lease/periodic-check model, which is the main reason §23 rejects one.

⚠ The one item that costs money: a **domain** (~€10–15/yr) if a zone-level WAF rate-limiting rule is
wanted, since `*.workers.dev` is not behind zone rules. Not usage-based, and the product wants a domain
anyway.

## 23. ⛔ Rejected: managed-offline / lease

A lease (`offlineAllowedUntil`, refreshed on contact) is the only mechanism that makes revocation
effective against a machine that never connects. It buys real revocation and real re-counting. It costs
**the product stopping when a service we do not control is unreachable** — moving the backend onto the
critical path of every customer's daily work. For a developer tool whose users sit behind corporate
networks and VPNs, that is a support burden out of proportion to the piracy it prevents.

**Rejected. Revisit only on a concrete incident, not a preference.** The format supports it additively
(an `offlineUntil` field older clients ignore), so nothing is foreclosed.

---

# PART III — COMMON

## 24. Key management

### 24.1 The key ceremony — performed once, documented, rehearsed

```
1. Generate the ECDSA P-256 key pair on the admin machine, offline.
2. Encrypt the private key into keystore.etkeys:
      AES-256-GCM under PBKDF2-SHA256(passphrase, random salt, ≥600 000 iterations)
   ⭐ Reuse EmberTern.Core.Security's PassphraseProtector / EncryptionSchemes patterns —
      the project already has a reviewed implementation of exactly this shape.
3. Passphrase: ≥6 diceware words, generated, never typed from memory,
   stored in a password manager AND on paper in a sealed envelope.
4. Back up keystore.etkeys to TWO offline media in TWO physical locations.
5. ⭐ VERIFY THE RESTORE — on a different machine, from the backup, sign a test
   license and verify it. A backup that has never been restored is a hypothesis.
6. Record the public key + kid in EmberTern.Licensing.TrustedKeys and ship it.
7. Record the ceremony date, kid and public-key fingerprint in Appendix A.
```

### 24.2 Where the private key must never be

⛔ EmberTern · the installer · any repository (a `.gitignore` entry is not protection — the keystore lives
outside the working tree entirely) · any cloud-sync folder · any CI system · any screenshot · any chat
message · any unencrypted backup.

⚠ The **passphrase** protects the keystore; the **DPAPI-protected SMTP password** (§14.3) is a different
secret with a different scheme, deliberately. Do not merge them: one must be portable for backup, the
other must not be portable at all.

## 25. Threat model

### 25.1 Assets, in order of value

1. **The private signing key.** Its compromise is the only unrecoverable event.
2. The register (`licenses.db`) — recoverable from backup; ⭐ **its loss does not stop a single license in
   the field from working.**
3. The SMTP credential — a mail account, not a licensing asset, but a real one.
4. Customers' license artifacts — they authorise only what was purchased.

### 25.2 Trust boundaries

```
┌── TRUSTED ────────────────────┐        ┌── UNTRUSTED ─────────────────────┐
│ admin machine                 │        │ customer machine                 │
│  private key · licenses.db    │        │  EmberTern process               │
│  smtp.dat                     │        │  license.etlic                   │
└───────────────────────────────┘        └──────────────────────────────────┘
        ▲ full authority                       ▲ ⭐ verifies only. Holds no secret.
                                                 Can lie about the clock and the disk.
```

⭐ **The client is untrusted and the design never pretends otherwise** — which is why it holds nothing
worth stealing: a public key and a signed statement. Nothing on one customer's machine can harm another.

### 25.3 ⚠ The limit no design fixes — stated so nobody re-litigates it

EmberTern is a .NET application. A single `brfalse` → `br` patch disables every check in this document.
Obfuscation, control-flow flattening and anti-debug tricks raise the effort from *minutes* to *an
afternoon*, at the cost of debuggability, crash-report quality, antivirus false positives and startup
time — for a niche B2B product whose pirate was never going to buy.

⭐ **The purpose of this system is to make honest use easy and accidental over-deployment visible, not to
make dishonest use impossible.** Every euro past that line buys nothing. The one measure worth
considering is **Authenticode signing of the released binaries**, which is about supply-chain trust and
SmartScreen rather than licensing, and is a separate paid decision at the installer stage.

## 26. Attack analysis

| # | Attack | V1 result | Why |
|---|---|---|---|
| 1 | Edit `exp` / licensee / seats in the file | ❌ blocked | signature covers the encoded payload (§13.3) |
| 2 | Swap in another customer's signature | ❌ blocked | signature is over *this* payload |
| 3 | `alg` set to `none` or something weak | ❌ blocked | algorithm comes from the `kid` table; no `none` exists |
| 4 | Invent a `kid` | ❌ blocked | unknown `kid` ⇒ refuse, no fallback |
| 5 | Downgrade `lv` to dodge a future check | ⚠ bounded | the client's minimum supported `lv` rises with any mandatory new semantics (§13.4) |
| 6 | Present a V2 bound license to a V1 client | ❌ blocked | `lv = 2` ⇒ refuse; ⭐ fails in the safe direction |
| 7 | Roll the system clock back | ⚠ mitigated | §16.3 |
| 8 | Copy `EmberTern.etlic` to another machine | ✅ **works** | ⭐ **accepted knowingly (D2)**; V2 addresses it |
| 9 | Edit the armor to claim a different date | ❌ harmless | ⭐ nothing outside the signature is ever read (§13.1) |
| 10 | Re-import last year's file | ❌ no-op | `lid`-equal / `iat`-greater rule (§16.4) |
| 11 | Steal the admin laptop | ⚠ serious | keystore is AES-256-GCM under a passphrase not stored on that machine; rotation §15.3 |
| 12 | Read the SMTP password off the admin disk | ⚠ mitigated | DPAPI CurrentUser — requires that user's logon context |
| 13 | Intercept the e-mail carrying the license | ⚠ real, bounded | the attacker gets a license naming someone else's company; ⛔ mail is not a secure channel and the design never assumes it is |
| 14 | Decompile to extract the public key | ✅ works, ⭐ irrelevant | it is public |
| 15 | Patch the binary | ✅ **works** | out of scope by decision (§25.3) |

## 27. Cost analysis

| Item | Cost | Card required |
|---|---|---|
| Signing, storage, register (V1) | €0 | no |
| Delivery by e-mail | ⚠ an existing mailbox — no new cost | no |
| Runtime cost per activation | €0 | no |
| **V1 total** | **€0 / year** | **no** |
| V2: Cloudflare Workers + D1 free tiers | €0, requests refused past the cap, **never an invoice** | **no** |
| ⚠ Optional: domain for a zone WAF rule (V2) | €10–15 / yr, **not usage-based** | yes, at the registrar |
| ⚠ Optional: Authenticode certificate | €200–400 / yr — **not licensing**, deferred to the installer stage | yes |

⛔ **Nothing in V1 requires a payment card, a cloud account, or a plan upgrade. Firebase Blaze is not
required in any variant this document recommends, including V2.**

⭐ **The principle that outlives the numbers:** do not depend on a quota, depend on the absence of a
billing relationship. Every free-tier figure here is dated **2026-08-15** and must be re-verified before
V2; none of them carries a design decision.

## 28. Upgrade and migration

- **`lv` (payload version)** rises only when a *mandatory* semantic changes. Adding an optional field does
  not bump it — that is what §13.4 rule 1 is for.
- **`ETL1` (envelope)** is reserved for a change to the envelope itself. Expected frequency: **never.**
- **V1 → V2**: additive. V1 licenses stay valid forever; V2 licenses are `lv = 2` and require a V2 client,
  which the customer necessarily has if they activated online. ⭐ **No customer is forced to upgrade** —
  the License Manager keeps issuing `lv = 1` renewals for V1-era customers.
- **Removing the backend later**: delete the calls. The product returns to V1 and every license in the
  field keeps working.

### 28.1 The 10-year test

> *Can a customer install EmberTern from an archive in 2036, with the vendor gone, the domain expired and
> every cloud provider in this document bankrupt, and use their license?*

**Yes** — the artifact is self-contained, the public key ships in the binary, verification is local, and
nothing in the path resolves a name or opens a socket. ⭐ **This is the single most important property of
the V1-first decision, and no design with a mandatory backend can answer that question with a yes.**

## 29. Recovery

| Scenario | Recovery | Preparation |
|---|---|---|
| Customer lost their license file | re-export the **exact artifact** from `issued_artifacts` | the table exists (§12.4) |
| Customer's disk died / new machine | re-send the same file | — |
| Admin lost `licenses.db` | restore from the encrypted backup; ⭐ **licenses in the field are unaffected** | scheduled backup |
| Admin lost the keystore passphrase | ☠ **unrecoverable** — nothing can ever be issued or renewed again | paper copy, sealed, off-site |
| Private key leaked | rotate (§15.3, compromise row): reissue everything, *then* ship the revocation | rehearsed procedure |
| SMTP account changed / blocked | re-enter settings; ⭐ `.eml` fallback and manual delivery always work | §14.3 |
| License Manager will not start | ⭐ artifacts are plain text in a SQLite file readable by any tool | schema in §12.4 |
| Vendor ceases operations | every issued license works to its `exp` | §28.1 |

### 29.1 The escape hatch

The License Manager exports **plain JSONL** alongside the encrypted backup: one line per customer, license
and artifact. If the tool is ever unbuildable, the register is still readable by `cat`. ~30 lines, and it
buys the register's independence from its own application — the same reasoning that keeps the payload
JSON rather than binary.

## 30. Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | Keystore passphrase lost | low | ☠ catastrophic | §24.1 ceremony, verified restore, paper off-site |
| 2 | Private key leaked | very low | ☠ severe | rotation §15.3 |
| 3 | ⚠ **SMTP basic auth blocked by the mail tenant** | **medium–high** | medium | ⭐ measure before L6; `.eml` fallback + `ILicenseEmailSender` (§14.3) |
| 4 | Licensing strings ship untranslated or unread | **medium** | medium | §17.3 — the exact defect Phase 5 shipped; test through `ErrorText` |
| 5 | Licence copied to a second machine | **certain** | low | accepted (D2); V2 addresses it |
| 6 | A binary patch circulates | low | low–medium | accepted (§25.3) |
| 7 | Renewal-by-e-mail does not scale | low in V1 | low | V2's self-service renewal |
| 8 | Clock guard locks out an honest user | low | high | warn-never-block, 48 h tolerance (§16.3) |
| 9 | License Manager becomes a second product to maintain | medium | medium | scope discipline §12.3 — ⛔ no invoicing, no CRM, no accounts |
| 10 | Startup cost regresses | low | medium | §4 budget < 5 ms, measured |
| 11 | Suite total changes and hides a lost test | medium | medium | re-measure the total (§12.1); `CLAUDE.md`'s acceptance rule |

## 31. What is deferred, and what would trigger it

| Item | Stage | Trigger |
|---|---|---|
| One-time codes · online activation · seat enforcement · activation management | **V2 — planned** | the first public distribution has settled and seat enforcement matters commercially |
| Offline `.etreq` activation requests | V2 | only meaningful once online activation exists |
| Self-service renewal | V2 | — |
| Editions and feature entitlements | future | a second licensed version of EmberTern actually exists |
| Perpetual licenses | future | a pricing decision; `maint` is already enforced (§13.5) |
| Trials | future | a trial is just a short signed license — no new mechanism |
| Floating / concurrent licenses | future | needs a lease; same objection as §23 |
| Managed-offline lease | ⛔ rejected | a concrete incident, not a preference |
| MailKit / an HTTP mail API | on measurement | risk #3 materialises |
| Authenticode signing | installer stage | separate paid decision |

## 32. Implementation plan

One stage per session, in the project's established rhythm: complete + tested + verified before the next
begins. ⛔ Per the standing directive, a stage touching UI is reported as *"implementation done — awaits
user confirmation"*, never *"fixed"*, until it has been seen in the running app.

| Stage | Deliverable | Exit criteria |
|---|---|---|
| **L1** | `EmberTern.Licensing` — armor, ETL1 envelope, payload, `TrustedKeys`, `LicenseVerifier`, `lv` gate. Pure, zero dependencies | build 0/0; **tamper corpus** (≥ 40 mutated artifacts, each refused for the *right* reason); `LicensingMakesNoNetworkCallsTests`; round-trip tests |
| **L2** | `EmberTern.Licensing.Issuing` — `KeyStore`, `LicenseIssuer`, `KeyCeremony`. ⭐ `PrivateKeyNeverShipsTests` lands here | a ceremony performed with a **test** key; sign → verify across the assembly boundary; guard tests green |
| **L3** | License Manager: skeleton (Avalonia/MVVM/DI, linked themes), SQLite + migrations, customers, licenses, issue, **save `EmberTern.etlic`** | a license issued end to end; `audit_log` immutability trigger proven; UI passes the `CLAUDE.md` UI Review Checklist in **both** themes |
| **L4** | ⭐ **EmberTern integration** — `LicenseService`, the §4 state machine, `LicensingPolicy` (§16.5), Activation window, Settings ▸ License, About line, banners, **EN + PL** | all eight states reachable and verified **in the running app**, Polish included; ⚠ the gated states verified in a **`Release`** build (§16.5); the four Debug-gate guard tests green; startup budget measured; the loop closes end to end |
| **L5** | License Manager depth: search, filters, **group extend**, re-issue, artifact preview, history view, encrypted backup + JSONL | extend 20 licenses in one operation; restore from backup on a second machine |
| **L6** | ⭐ **E-mail**: `ILicenseEmailSender`, SMTP + `.eml`, DPAPI settings store, HTML + text template (localizable), preview, explicit confirm, send audit | ⚠ **measure the SMTP auth question first** (risk #3); a real message delivered and read on a real client |
| **L7** | Hardening and closing: clock high-water, `%PROGRAMDATA%` fallback, `maint` gate, real key ceremony, public key shipped, documentation | full suite green (**total re-measured**); `docs/history/` entry, gotchas, one line in `docs/current-state.md`; both remotes pushed after acceptance |

**Shape:** L1, L2, L5, L6, L7 one session each; L3 and L4 two each. ⚠ Shape, not commitment.

⭐ **L4 is deliberately early** — before the License Manager's depth and before e-mail — so the end-to-end
loop (issue → deliver → activate → run) is provable at the earliest possible moment, and everything after
it is refinement of something already known to work.

## 33. Resolved minor decisions (D16)

🔒 All closed as recommended, on the user's instruction to decide anything that is purely implementational
under the principle of minimal complexity. None of the six changes the security posture, the UX shape or
V2's feasibility; had one done so, it would have been referred back rather than decided here.

| # | Question | 🔒 Decided |
|---|---|---|
| **O1** | Keep `maint` in V1 (the one unused field, §3 / §13.5)? | **Kept** — it cannot be retrofitted onto clients already in the field, and it costs one optional field plus one comparison |
| **O2** | Grace period | **14 days** (§7) |
| **O3** | What `Expired` blocks | **New database connections only** — editor, files, exports and settings stay usable (Architecture rule 11) |
| **O4** | Default `seats` for a new license | **1**, explicit, never blank |
| **O5** | License id in the e-mail body | **Yes** — it is already inside the attachment, and support asks for it first |
| **O6** | Delivered filename | **Always `EmberTern.etlic`** — matches D4 and keeps a customer name out of a filename that travels by e-mail |

---

## 34. L1 — as built (2026-08-15)

✅ **Delivered and green.** `src/EmberTern.Licensing` — 9 files, **zero package references, zero project
references**. Build **0/0 in both Debug and Release**; suite **8 972** (was 8 853; +119, and the arithmetic
matches exactly, so nothing dropped out of discovery). Tamper corpus: **59 cases**, requirement was ≥ 40.

**Surface:** `LicenseArmor` · `LicenseEnvelope` · `LicensePayload` · `Base64Url` (internal) ·
`SignatureAlgorithm` / `SignatureAlgorithmIds` · `TrustedKey` / `TrustedKeyTable` / `TrustedKeys` ·
`LicenseStatus` / `LicenseFailure` / `LicenseVerdict` / `LicenseVerificationContext` · `LicenseVerifier` ·
`LicenseConstants`.

**Six decisions taken during implementation, none of which change the design:**

1. ⭐ **`LicenseFailure` lost three members** (`NotYetValid`, `Expired`, `VersionNotCovered`). Those states
   are fully described by `LicenseStatus`, and carrying them twice would have created two sources for one
   fact. `Failure` is now non-`None` only for `Invalid` and `Unlicensed`.
2. ⚠ **§13.3's "verify first, parse second" was wrong and is corrected in place** — see the amended bullet.
3. **`Base64Url` is hand-written** for strictness (§13.3), not for want of a BCL helper.
4. **`TrustedKeyTable` validates every key at construction** and throws. A malformed entry is a bug in
   *our* table, and reporting it to a user as an invalid licence would send them chasing a good file.
5. **`LicensePayload.WriteJson` lives in the shared assembly** although only the issuer calls it — so
   field names and the timestamp shape have one definition shared by EmberTern, the License Manager and
   the tests. ⛔ The verifier never calls it.
6. **Two guard tests beyond the brief**: the licensing assembly references no other EmberTern assembly,
   and no third-party assembly at all. The second is what decision D10 actually bought, so it is worth a
   test rather than a comment.

⚠ **One thing to know before L2:** `TrustedKeys.Production` is **empty**, so a build today refuses every
licence with `UnknownKey`. That is correct for L1 and is asserted by
`LicenseVerifierTests.TheShippedTrustedKeyTableIsStillEmptyAtThisStage` — a test written as a **reminder**,
to be rewritten (⛔ not deleted) when L2's ceremony produces `R1`.

---

## Appendix A — key register (filled by the key ceremony, §24.1)

| kid | Algorithm | Public key (SPKI, base64url) | Ceremony date | Revoked |
|---|---|---|---|---|
| `R1` | ECDSA-P256-SHA256 | *(pending L2)* | — | — |

⛔ Entries are appended and flagged, never removed or edited (§15.3).

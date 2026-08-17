# EmberTern Licensing System — design document

**🔒 STATUS: V1 RATIFIED BY THE USER 2026-08-15 (decisions D1–D16, §0). ✅ L1 ACCEPTED. ✅ L2 ACCEPTED.
✅ L3 ACCEPTED (2026-08-15, after a two-round UI review — §36.5). ✅ L4a ACCEPTED. ✅ **L4b ACCEPTED**
(2026-08-15, after a UI review that produced the status-bar correction — §38.5). ⏭ **Next: L5.** Branch
`feat/licensing-system`, cut from `master` at `2c3da45`.
As built: **§34** (L1), **§35** (L2), **§36** (L3), **§37** (L4a), **§38** (L4b).

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

⭐⭐ **`EmberTern.Licensing.Issuing` is NOT in `EmberTern.slnx`, and `EmberTern.Tests` does not reference
it.** This was strengthened during L2 from "App must not reference it" to "the client's solution must not
contain it", and the difference is worth stating: with the issuer absent from that solution, its assembly
is absent from the folder `EmberTern.dll` is built into — so *"it does not ship"* stops being a claim
about intent and becomes an observable fact about a directory. `EmberTern.Licensing` is in both solutions
on purpose: sharing the format by project rather than by a package is what stops the verifier and the
issuer from ever disagreeing about what an ETL1 artifact is.

⚠ **Consequence: there are two test commands from L2 on**, and this is inherent to shipping two
applications, not a partitioning of one suite:

```bash
dotnet test EmberTern.slnx
```
```bash
dotnet test EmberTern.LicenseManager.slnx
```

`CLAUDE.md`'s *"the suite runs as ONE command"* rule exists because partitioning **EmberTern's own** suite
hid two defects for months. It does not speak to a second product, and ⛔ it must not be used as an
argument for pulling the issuer back into the client's solution.

⭐ **Theme sharing by file link, not by moving files.** One source of truth at **zero risk to EmberTern**
— moving those files into a shared library would break every `avares://EmberTern/Themes/…` URI in the app.

⚠⚠ **MEASURED CORRECTION (L3). This paragraph originally said "link `Themes/*.axaml`". Only FOUR of the
nine are linkable**, and the boundary is not a matter of taste — the others bind to types the License
Manager does not have and must not acquire:

| File | Linked? | Why |
|---|---|---|
| `Colors` · `Tokens` · `Typography` · `FluentBridge` | ✅ | pure resource dictionaries, zero type references |
| `IconGeometries.axaml` | ❌ | `ControlTheme`s for `controls:SvgIcon` / `DebuggerIcon` / `CreateIcon` |
| `ControlThemes.axaml` | ❌ | the `CheckBox` template instantiates `controls:SvgIcon` |
| `SearchableComboBox.axaml` · `PickerTemplates.axaml` | ❌ | `EmberTern.App` + `EmberTern.Core.Metadata` |
| `ControlStyles.axaml` | ❌ | all of the above, plus AvaloniaEdit, plus DataGrid, plus `avares://EmberTern/Assets/…` |

⭐ **So the License Manager brings its own `Themes/LicenseManagerStyles.axaml`, and the split is "one
palette, two style layers" rather than "two palettes".** ⛔ That file may not define a single colour —
every brush it paints with is a `{DynamicResource}` into the linked `Colors.axaml`, and
`LicenseManagerThemeTests` fails the build otherwise. That test is what keeps the distinction real rather
than aspirational; every other `CLAUDE.md` UI rule applies unchanged.

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

## 35. L2 — as built (2026-08-15)

✅ **Delivered and green.** `src/EmberTern.Licensing.Issuing` — 6 files, zero package references, one
project reference (the shared format). New `EmberTern.LicenseManager.slnx` and
`tests/EmberTern.LicenseManager.Tests`. Builds **0/0 in Debug and Release, both solutions**.
Suites: EmberTern **8 978** (was 8 972; +6 guards), License Manager **46**.

**Surface:** `KeyStore` · `KeyStoreEntry` · `KeyStoreFailure` / `KeyStoreException` · `IssuingKey` ·
`LicenseTerms` / `IssuedLicense` / `LicenseIssuer` · `KeyCeremony` (`CeremonyResult`,
`RestoreVerification`).

### 35.1 ⭐ The guards were proved by watching each one fail

⭐⭐ **A green guard nobody has seen go red is not evidence.** Each of the six
`PrivateKeyNeverShipsTests` was verified by injecting the violation it exists to catch, observing the
failure, and reverting:

| Injected violation | Test that fired |
|---|---|
| Issuing added to `EmberTern.slnx` | `TheEmberTernSolutionDoesNotContainTheIssuingProject` |
| `ProjectReference` from `EmberTern.App` to Issuing | `NoProjectInTheEmberTernSolutionReferencesIssuing` |
| …and the resulting assembly in the output | `TheShippedOutputContainsNoIssuingAssembly` |
| `SignData` written into a shipped source file | `NoShippedSourceUsesAPrivateKeyOrSigningApi` |
| a `.pem` dropped into the build output | `TheShippedOutputContainsNoKeyMaterial` |
| a public `ECDsa`-returning member on `EmberTern.Licensing` | `ThePublicApiOfTheVerifierCannotSign` |

⭐ Six tests, six *different* violations — none of them catches another's. That redundancy is the design:
the private key is the one asset whose compromise is unrecoverable (§25.1).

### 35.2 Five decisions taken during implementation

1. ⭐ **`IssuingKey.Sign` is `internal`.** The only way to obtain a signature is to ask
   `LicenseIssuer.Issue` for a licence, so every signature the system emits has passed the same validation
   and self-check. A public `Sign(byte[])` would be a signing oracle wearing a helpful name.
2. ⭐⭐ **`LicenseIssuer.Issue` verifies its own output** through the real `LicenseVerifier` against the
   key's own public half, and throws rather than returning an artifact it cannot prove is good.
   Architecture rule 11 at the source: the alternative to catching a key or format fault here is catching
   it in a customer's inbox. ⚠ It asserts the artifact *authenticates*, **not** that it is currently
   `Valid` — demanding `Valid` would make a post-dated licence unissuable.
3. ⭐ **`KeyCeremony.VerifyRestore` takes the *expected* public key.** Without it the operation would only
   prove a backup holds **a** working key, and a backup of the **wrong** key passes that check while being
   exactly as useless as no backup. It returns a report and never throws — a failed restore verification
   is a finding to act on, not a crash.
4. **`KeyStore` works on bytes, never on a path.** Whose disk and which folder is the License Manager's
   business; keeping I/O out makes every failure state reachable in a test.
5. **`KeyCeremony.FormatTrustedKeyEntry` generates the paste-ready C#.** Transcription is where a ceremony
   goes wrong: a public key is 120-odd base64 characters nobody proof-reads, and one altered character
   produces a build that refuses every licence forever.

### 35.3 One finding owed to `docs/gotchas.md` at L7

⚠ **`Utf8JsonWriter`'s default encoder escapes `+` as `+`**, so a base64 value read back with
`GetString()` does **not** appear verbatim in the file text. Two keystore tests did a text `Replace` on
that value, matched nothing, mutated nothing — and reported *"no exception was thrown"*. ⭐ **The failure
mode is the dangerous one: a correct product wearing a red test, which invites "fixing" the product.**
The rule: edit JSON as JSON (`JsonNode`), never as text. Record it at L7 with the rest of the
documentation closure.

### 35.4 Still open going into L3

⚠ **`TrustedKeys.Production` is still empty and the REAL ceremony has not been performed.** L2's exit
criterion was a ceremony with a *test* key, which the tests perform on every run. The real one — a real
passphrase, two offline backups, a verified restore from each, and the public key pasted into
`TrustedKeys.Production` — is **L7**, deliberately: doing it now would mean carrying a production private
key through five more stages of development for no benefit.

| kid | Algorithm | Public key (SPKI, base64url) | Ceremony date | Revoked |
|---|---|---|---|---|
| `R1` | ECDSA-P256-SHA256 | *(pending the real ceremony — L7)* | — | — |

⛔ Entries are appended and flagged, never removed or edited (§15.3).

---

## 36. L3 — as built (2026-08-15)

✅ **Delivered, reviewed by the user over two rounds, and accepted 2026-08-15.**
`src/EmberTern.LicenseManager` — 17 files (4 view models, 2 windows, 3 services, 2 data files, 1 style
file, the app and its entry point). Builds **0/0 in Debug and Release, both solutions**. Suites: License
Manager **102** (was 46 at L2; +56), EmberTern **8 979** (was 8 978; +1 — the new charset-domain guard,
and the arithmetic matches exactly, so nothing left discovery).

⚠ **The suite grew by 15 during the UI review, and five of those are not new coverage — they are five
tests that already existed and did not work** (§36.5, the discarded `Task`). The honest reading of
87 → 102 is *+10 new guards, +5 resurrected*.

**Exit criteria, each met and each pinned by a test rather than by this paragraph:**

| L3 criterion | Where it is proved |
|---|---|
| a licence issued end to end | `IssuingWorkflowTests` — ceremony → keystore → register → issue → `EmberTern.etlic` on disk → read back → **verified by `EmberTern.Licensing`, the assembly the customer runs** |
| `audit_log` immutability trigger proven | `LicenseRegisterTests.TheHistoryCannotBeRewritten` + `AnIssuedArtifactCannotBeEditedOrDeleted` — both reach **past** the register's own API, because a trigger only the register's methods respect is a convention, not a trigger |
| UI passes the `CLAUDE.md` UI Review Checklist in both themes | 12 items in `LicenseManagerThemeTests`, 9 in `LicenseManagerWindowTests`; ✅ the judgement items were reviewed by the user in the running application over two rounds (§36.5) and accepted |

### 36.1 The measured correction the stage produced

⚠⚠ **§12.1 said "link `Themes/*.axaml`". Only FOUR of the nine are linkable** — the table is in §12.1,
amended in place. The other five bind to types the License Manager does not have and must not acquire
(`SvgIcon`, AvaloniaEdit, DataGrid, `EmberTern.Core.Metadata`, `avares://EmberTern/Assets/…`). ⭐ So the
shape is **one palette, two style layers**, not two palettes: `Themes/LicenseManagerStyles.axaml` may not
define a single colour, and `LicenseManagerThemeTests` fails the build if it ever does.

### 36.2 Six decisions taken during implementation

1. ⭐⭐ **The charset seam guard was narrowed by DOMAIN, not by an exception list.** `CharsetGuardSeamTests`
   forbade `CreateCommand()` / `CommandText =` / `AddWithValue` anywhere under `src/` — which was the right
   rule while "everything under `src/`" and "code that can reach the Firebird driver" were the same set.
   They stopped being the same set: the License Manager talks to SQLite, whose provider transliterates
   nothing. ⭐ **What makes this a boundary rather than a loophole is that the precondition is re-checked
   mechanically on every run** — `TheExcludedProjectsGenuinelyCannotReachTheFirebirdDriver` walks each
   excluded project's `ProjectReference` graph and fails if any of them ever gains a path to Firebird.
   ⚠ Verified by injection: a `ProjectReference` to `EmberTern.Firebird` added to the License Manager
   makes it fail naming both hops, and removing it makes it green again.
2. **Dependency injection by constructor, no container.** Every collaborator is passed in, which is what
   makes the view models testable without a window. A container would add a package, a registration list
   and an indirection to build four objects that are trivial to build. ⚠ Flagged for the user's override,
   since the brief listed "DI" as a technology — this *is* dependency injection, the wiring rather than
   the framework.
3. **Dates are typed as ISO text, not picked from a calendar.** A date picker is a templated control with
   a flyout, i.e. the largest theming surface in the application, introduced in the first stage that has
   any UI at all. `2027-08-15` is unambiguous and is what an administrator reads off a purchase order. A
   picker is an L5 refinement, not a correctness gap.
4. ⭐ **The expiry the operator types runs to the END of that day** (`23:59:59`), not to its midnight.
   Storing midnight expires a licence at the start of the day the invoice says the customer owns.
5. ⭐ **Recording happens BEFORE the file is saved, and a cancelled Save-As leaves the artifact recorded.**
   A signed licence the register does not know about is the one state from which it can no longer answer
   *"what did we send this customer?"* — and the file is always re-exportable from the stored token.
6. **The register's own message surface is `MessageHostViewModel` + `Border.message`.** EmberTern's
   `MessageBanner` lives in `EmberTern.App.Controls` and cannot be referenced from here; the *rule* it
   embodies — a message is never a loose coloured `TextBlock` — is carried over intact.

### 36.3 ✅ What only the user could judge — done, twice

Four UI Review Checklist items are judgements: the complete set of states (normal · hover · active ·
disabled · focus), and whether the two windows actually *read* correctly in Light and in Dark. The
title-bar **Light / Dark** button switches live, which is what makes that a one-click check in the main
window rather than something a screenshot has to promise.

⭐⭐ **This is the part that earned its place.** The judgement items were not a formality: the user's first
look produced §36.5's list, and the second look produced the one defect the first had missed — every
field's content pinned to the top of its box. Neither was reachable by any test that existed, and the
second one was invisible to the first guard written for it. ⛔ The lesson is not "write more tests" — it is
that a stage with a UI is not finished until a person has looked at it.

```powershell
src\EmberTern.LicenseManager\bin\Debug\net9.0\EmberTern.LicenseManager.exe
```

⚠ First run performs the **ceremony** (a test passphrase is fine — this is not the production key, §35.4)
and writes `%APPDATA%\EmberTern License Manager\`.

### 36.5 ⭐⭐ The first-run screen — user review, and what it found (2026-08-15)

The user rejected the L3 UI after looking at the first-run window, and asked for a full manual review of
it. **The review found more than the report did**, including one defect that made every headless test in
this suite worthless.

⭐⭐ **THE ONE THAT MATTERS MOST: all five L3 headless tests were vacuous.**
`HeadlessUnitTestSession.Dispatch` returns a `Task`. Written as `public void X() => _session.Dispatch(…)`
— which compiles, because a method call is a statement expression — **the `Task` is discarded, xUnit never
awaits it, and no assertion inside the lambda can fail the test.** So the L3 claim *"2 checklist items
proved headless"* was false: those two items were never checked. ⭐ Caught by injecting `Assert.Fail` into
a headless body and watching the run report success; EmberTern's own headless tests `await` correctly.
⛔ Never write one of these as `void`. Every guard below was then verified **red** before being accepted.

**What the user reported, and what each turned out to be:**

| Report | Cause |
|---|---|
| the form has no coherent vertical rhythm | THREE independent causes, below |
| "Create signing key" is not aligned to the form | it stood on `Size.Control` (24) — the **field** height — instead of `Size.ControlProminent` (28), the height Tokens.axaml names for a dialog-footer action. Fields and actions are two independent ladders, and the action was on the wrong one |
| the storage path must not be on this screen | agreed and removed, view **and** view model (§36.4) |

**The three causes of the broken rhythm, each a wrong or missing role rather than a wrong number:**

1. ⭐⭐ **The proximity rule was inverted.** The form used a uniform `StackPanel Spacing`, which
   *cannot* express it: it puts the same gap between a caption and its field as between two fields. With
   `Margin.LabelGap` also applying, caption→field measured **8** and field→next-caption **6** — so the eye
   attached every caption to the field **above** it. `Margin.LabelGap`'s own comment in Tokens.axaml
   states the rule and records the identical user report from Product Polish M2b.
2. ⭐ **Typography roles were consumed without their line height.** A role is family + size + weight +
   **line height**; L3 took two thirds of each, leaving every text block on the font default. That *is*
   what a ragged baseline grid is, and it is invisible in a screenshot of one label.
3. ⭐ **`field-label` was on `Text.Caption` (10 px)** — the smallest grade in the product, reserved for a
   shortcut chip — instead of `Text.Application` (12 px), which is what EmberTern's own `field-label`
   uses. Two grades below the value it names made the whole form read as fine print.

⭐⭐ **A SECOND REVIEW ROUND FOUND THE ONE THE FIRST ROUND MISSED: every field's text and password dots
were pinned to the TOP of the box.** `Pad.Control` is `8,0` — the vertical padding is deliberately zero,
because a single-line field takes its height from `Size.Control` and one thing must own a size. ⚠ But zero
padding only *centres* if something says where the content goes, and the framework default is `Stretch`.
L3 copied the padding token and left `VerticalContentAlignment` behind; EmberTern's own base `TextBox`
style carries it, for exactly this reason. Measured: presenter **22 px tall around 14.17 px of text** with
the setter absent, **15 px** with it present, sitting 5 above / 4 below in a 24 px field.

⚠ The multi-line variant needs `Top` **the moment the base style centres**, or the fix reintroduces
EmberTern's own §18.11 defect (a short note hanging in the middle of a fifteen-line box) in the main
window's three multi-line fields.

⚠⚠ **The first version of the guard for this measured the wrong box and passed with the defect in place.**
Under `Stretch` the presenter *fills* the field, so "is the presenter centred?" answered yes while the
glyphs sat on its top edge. The presenter has to be shown to be sized to its **text** before its position
means anything. ⭐ Twice in this review a plausible measurement proved nothing — the discipline that caught
both was injecting the defect and requiring the guard to go red.

**Also found, none of it reported:**

- ⚠ **A dark label on the accent fill** whenever a primary button carries an explicit `<TextBlock>` child
  — the shape *every* EmberTern primary button takes (icon + label + shortcut chip). Measured: `#1B1D1F`
  on blue in Light, `#D4D4D4` in Dark. ⚠⚠ The first version of this finding was **wrong** and the
  injection caught it: with a *string* `Content` the ContentPresenter sets Foreground as a **local value**,
  which outranks every style setter, so the window as it ships was never affected. The style is right, the
  first explanation of it was not.
- **No focus state at all**, in either variant, and no `:pressed`. The passphrase field is reached by Tab.
- **The action geometry was declared twice**, once per variant — the drift EmberTern's single
  `Button.primary, Button.flat` style exists to prevent, already written down.
- **`VerticalContentAlignment` was missing** on the base `Button`, while `Pad.Button` is `12,0` — the zero
  is deliberate because height comes from `MinHeight`, which makes the centring load-bearing.
- **`Button.flat` used `ControlOutlineBrush`**, a token whose own comment names its two consumers
  (CheckBox, RadioButton) and the contrast measurement it exists for. A button takes `BorderBrush`.
- **The width floor reached chrome**, which `Size.ActionMinWidth` explicitly forbids — 100 px buttons for
  a two-word label in the title strip.
- **Two buttons literally marked `IsDefault`**; both answer Enter regardless of visibility.
- **The window had no icon.** Now EmberTern's own `.ico`, **linked not copied**, through `ApplicationIcon`
  (the Win32 resource: Explorer, file properties, taskbar) *and* one `<Style Selector="Window">` setter
  (title bar, Alt+Tab) — the same one-setter rule `CLAUDE.md` states for EmberTern. ⛔ No new artwork.

**Structure.** The window is now the **banded dialog skeleton** EmberTern uses for every window that asks
the user for something (`TextPromptDialog`, `ConfirmDialog`): a `PanelBrush` header carrying the `h1`, a
body carrying the form, a `PanelBrush` footer carrying the action, one gutter (`Pad.Dialog`) through all
three so the heading, the fields and the button stand on one left edge. ⭐ The `h1` now states the **task**
("Create the signing key" / "Unlock the keystore") — product identity is carried by the title bar and, as
of this pass, by the icon beside it.

⚠ **One path deliberately stays on screen:** the keystore filename inside the *error* message for a
damaged or foreign file. That is a diagnostic naming the offending file, not ambient infrastructure.

⭐ **RATIFIED LIMIT (user, 2026-08-15): the first-run screen is Dark-only, and that does not block L3.**
`App.axaml` bootstraps `Dark` exactly as EmberTern does, and the theme toggle lives in the main window's
chrome — which is reachable only after unlocking. So a real operator never sees first run in Light. ⛔ The
bootstrap value was **not** to be flipped just to obtain a screenshot of that one state. Both themes are
covered headlessly; a stored theme preference for the License Manager is an L5 question, if it is one at
all.

⏭ **Not this screen, found while reviewing it — the main window owes its own pass:** `Border.rail` takes
`Border.Rail`, which is the **status-bar rail** role (a 2 px TOP edge as a state signal), not a side
separator; the chrome buttons are `.flat` where EmberTern would use `Button.icon`; and the Seats box is
still an `int` bound to text.

### 36.4 Known limits, deliberately left to a later stage

- **Only `active` is ever written.** `LicenseStatuses` carries `superseded` and `blocked`, and nothing in
  L3 sets either — superseding is what L5's re-issue does, and `blocked` is bookkeeping in V1 anyway
  (§26.2). ⛔ Recorded rather than removed: the values are persisted verbatim, so the vocabulary is
  append-only.
- **The Seats box is an `int` bound to text**, so a non-numeric keystroke leaves the previous valid value
  in place with nothing said. Bounded — the value can never *become* garbage — but it is silent, and a
  validated numeric field is an L5 item alongside the date picker.
- ⚠ **`LicenseRegister.Read` / `ReadOne` always run outside a transaction.** Nothing in L3 reads inside
  one (every read happens before its `BeginTransaction`), and `Microsoft.Data.Sqlite` would throw rather
  than read stale data if that changed. Worth knowing before L5 adds bulk operations.
- **Search, filters, group extend, re-issue, artifact preview, backup, e-mail** are L5/L6 by plan, not
  omissions.
- ⏭ **Where the two files live has no surface yet.** Removed from first run (§36.5) and not replaced: it
  belongs on an administrative surface — an "Open data folder" action or a storage section — which is L5.

---

## 37. L4a — as built (2026-08-15)

✅ **The mechanism, with no UI.** `src/EmberTern.App/Licensing/` — 5 files. Builds **0/0 in Debug and
Release, both solutions**. Suite: EmberTern **9 029** (was 8 979; **+50**), License Manager **102**
(untouched). ⭐ The **`Release` run is part of the acceptance, not hygiene** — it is the only thing that can
prove the gate's `Release` arm, and a `Debug`-only run never can.

**Surface:** `LicensingPolicy` · `LicenseLocation` · `LicenseStore` · `LicenseService`
(`LicenseInstallOutcome` / `LicenseInstallResult`) · `LicenseText`. Plus
`UserSettings.LicenseClockHighWater` in Core, `EmberTern.App` → `EmberTern.Licensing`, and 18 EN + 18 PL
resource entries.

### 37.1 The three decisions the user ratified before implementation

| Decision | As built |
|---|---|
| **Option A** — no development key | `TrustedKeys.Production` stays empty until L7. ⭐ `LicenseService` takes a `TrustedKeyTable` parameter defaulting to it, so the app always uses production while tests prove the whole chain. ⛔ No second key to maintain in the client |
| **`Expired` blocks every attachment opener**, Test connection included | `AllowsNewDatabaseConnections` is one predicate over the whole domain. ⚠ Measured and recorded: `CreateDebugSessionAsync` and `CreateImportSessionAsync` both hard-require `IsConnected`, so gating the connect path genuinely closes the domain rather than the reported cases. ⏭ Wiring the call sites is L4b |
| **Debug marker in About** | Recorded; About is a UI surface, so it lands in L4b with the rest of them |

### 37.2 ⚠⚠ The same trap fired THREE times in one session — it is a pattern, not an accident

**A guard that matches source TEXT fires on the prose that documents its own rule.**

1. `LicensingPolicy.cs` documents *"not a setting, not an environment variable, not a command-line
   argument"* — and the guard forbidding runtime inputs matched its own doc comment.
2. `EmberTern.App.csproj` explains that the **issuer** is deliberately absent — and L2's ratified
   `NoProjectInTheEmberTernSolutionReferencesIssuing` matched the sentence naming it.
3. L3 hit the identical thing in `LicenseManagerThemeTests` (§36.5).

⭐ **The fix is always to strip comments, never to reword the documentation** — a guard that fires on its
own rule is one that gets suppressed, and a suppressed guard reads as coverage while providing none.
⚠ Loosening a rule #11-class guard demanded proof it still bites: a real `ProjectReference` to Issuing was
injected and the guard fired naming the project, before the change was accepted.

### 37.3 Two guards that proved nothing until they were made to fail

- ⚠ **`AnAlteredLicenceIsRefused` first tampered by a text `Replace` of the licensee's name** — which
  appears nowhere in the file, because the payload is base64url. It mutated nothing, the licence verified
  perfectly, and the test reported the absence of a failure as a success. Now `LicenseFixtures.Tamper`
  edits the **encoding**. ⭐ Identical in shape to the keystore finding in L2 (§35.3): editing an encoded
  artifact as text silently edits nothing.
- ⚠ **The runtime-input guard was a false positive on `LicenseService.cs`**, whose offence was reading
  `_settings.Load()` for the clock high-water twenty lines from an unrelated use of the gate. ⭐ A rule
  bounded by *"appears in the same FILE"* is not bounded by anything; it was restated positively — the
  policy file has exactly one input. ⚠ It had been failing unnoticed because the test filter used
  (`~License`) never matches `Licensing…`.

### 37.4 Decisions taken during implementation

1. ⭐ **`LicenseStore.Install` takes the verifier as a parameter and returns the verdict it read BACK FROM
   DISK.** The re-read is not something a caller can forget, because there is no way to write without it.
   Design §5 calls this Architecture rule 11 rather than paranoia: a half-succeeded write must be found
   *now*, with the file still on the user's desktop.
2. ⭐ **`NotYetValid` and `Expired` are accepted for storage**, only unusable artifacts are rejected.
   Refusing to store a post-dated renewal would make renewing early impossible — which is exactly when a
   well-organised customer renews.
3. **`AppInfo.ReleaseDate` is a `DateOnly`**, widened at UTC midnight for the `maint` comparison, so a
   licence whose `maint` falls on the release date covers that build rather than missing it by hours.
4. ⭐ **`RecordClock` goes through `ApplicationSettingsStore.Update`**, which takes the cross-process lock
   and reads under it — ⛔ never `Load()` → mutate → `Save()`, the shape that was measured turning a
   transient read failure into 89 writes of DEFAULTS.
5. **A failed settings read yields no high-water mark rather than a default one.** The guard is then merely
   absent for that session, which is the safe direction: the alternative is inventing an instant and
   enforcing it.
6. ⭐ **The verdict maps to words in ONE place (`LicenseText`), and every value is pinned in BOTH
   languages** through the path the UI will use. The eleven `LicenseFailure` values collapse onto four
   answers, because what the user needs is *"wrong kind of file"* vs *"this was altered"* vs *"this build
   cannot read it"* vs *"wrong product"* — eleven sentences would be eleven ways of saying those four.

### 37.5 ⏭ What L4a deliberately did NOT do

⛔ No UI: no Activation window, no Settings ▸ License, no About line, no banner — all L4b. ⛔ The connection
gate is a **predicate**, not yet wired into the call sites, because refusing a connection needs the sentence
that explains why. ⛔ No network code, no `iid`, no fingerprint, no `seats` enforcement, no production
ceremony.

⚠ **A real licence file verifies as `Invalid / UnknownKey` in every configuration today** — `TrustedKeys.Production`
is empty until L7. That is correct and deliberate, and `ALicenceSignedByAKeyThisBuildDoesNotKnowIsRefused`
records it so the next reader does not diagnose it as a defect.

---

## 38. L4b — as built (2026-08-15)

✅ **The surfaces, and the gate wired to them.** Builds **0/0 in Debug and Release, both solutions**. Suite:
EmberTern **9 081** (was 9 029; **+52**), License Manager **102** (untouched). ⭐ The **`Release` run is again
part of the acceptance**: it is the only thing that can prove the refusals, because the gate is a compile-time
`const` — and it earned its place immediately, by rejecting four `Assert.Throws` calls that compiled fine in
`Debug`.

**New surface:** `LicensedConnections` · `LicenseBlockedException` · `LicenseActivationWindow` (+ view model) ·
`LicenseSettingsViewModel` + the Settings ▸ Licence page · the About licence line and Debug marker · the
main-window licence banner. Plus `LicenseService.AllowsConnecting` / `InstallPath`,
`LicenseText.ConnectionRefused` / `SeverityOf` / `Day`, a `license` category in `SettingsCatalog`, and **40 EN
+ 40 PL** resource entries.

**Tests: +52**, in five files — `LicensingConnectionSeamTests` (the seam guard), `LicenseGateTests` (what the
licence prevents, with `#if DEBUG` pairs), `LicenseActivationTests` (the §5 flow), `LicenseSurfaceLocalizationTests`
(rule 12 through the bound properties, EN + PL) and `LicenseSurfaceViewTests` (the surfaces as they render,
every `Dispatch` awaited).

### 38.1 ⭐⭐ The decision that shaped the stage: a SEAM, not four checks

The gate could have been four `if`s at the four call sites. It is instead one file every opener goes through,
guarded by `LicensingConnectionSeamTests`, for the reason the charset guard exists: **a check written at each
call site is a check the fifth call site forgets, silently, with a green build.**

- ⛔ **Never call `ConnectAsync` / `TestConnectionAsync` / `CreateDebugSessionAsync` /
  `CreateImportSessionAsync` outside `src/EmberTern.App/Licensing/LicensedConnections.cs`.** Use
  `OpenAsync` / `TestAsync` / `OpenDebugSessionAsync` / `OpenImportSessionAsync`.
- ⭐ The seam **throws** rather than returning false: every opener returns something the caller uses, so a
  `false` would need four return shapes and the one a caller ignored would open the attachment anyway.
- ⚠ **The guard's bound is written down rather than overclaimed.** Three members have names nothing else uses
  and are matched outright; `ConnectAsync` is not unique (`MainWindowViewModel.ConnectAsync` legitimately
  forwards the user's gesture), so it is matched on a receiver ending in `service`. A future receiver named
  otherwise would slip past — the three unique members are what actually close the domain.
- ⭐ **Verified RED four ways** before being accepted green: a probe calling all four openers from
  `MainWindowViewModel` (each pattern named its own line), a `Guard()` deleted from `TestAsync`, an
  `ex.Message` reintroduced at a refusal site, and an `Assert.Fail` injected into a headless body.

### 38.2 `AllowsConnecting` — read off §7, not invented

`Expired` denies new connections while everything else keeps working; the four `IsBlocked` states are *gated*,
which is strictly stronger and therefore also denies them. Stated once as
`AllowsConnecting => AllowsNewDatabaseConnections && !IsBlocked` so a caller cannot satisfy one half and miss
the other. ⛔ L4a's two predicates are unchanged.

### 38.3 ⚠⚠ The Phase-5 shape, closed at the source

`LicenseBlockedException` deliberately carries the **verdict**, and its own `Message` is an untranslated
developer breadcrumb. Every refusal site renders `LicenseText.ConnectionRefused(ex.Verdict)` at display time.

⭐ **`NoRefusalSite_RendersTheExceptionMessageInsteadOfTheVerdict` is the guard §17.3 asked for**, and it
carries an **anti-vacuity assertion** (`sites >= 3`): a regex that silently matched nothing would report
perfect compliance forever — the exact shape of L4a's finding where a tampering test mutated nothing and
reported the absence of a failure as a success (§37.3).

⭐ The refusal is **one short whole sentence per state** from the catalog — see §38.5 for why it is not the
long composition it started as.

### 38.4 Decisions taken during implementation

1. **British *licence* throughout the English catalog.** The 18 entries L4a shipped spell it that way;
   consistency inside the product outranks the US spelling used in this document's headings.
2. ⭐ **The activation window's three gestures feed ONE buffer.** A drop and a Browse read the file into the
   paste box, so `Activate` has exactly one thing to act on and the user can see what they are installing.
   Three sources feeding three code paths is how a paste comes to be verified by different code from a drop.
3. **The window closes on `IsActivated`, not on the button press** — closing on the press would hide a failed
   write behind a dismissed dialog.
4. ⭐ **Settings ▸ Licence is reachable in every state, including the blocked ones**, and two tests say so. It
   is the way *out* of `Expired` and `Unlicensed`; a gate that also hid the screen for fixing the licence
   would be a trap.
5. **Only the 30-days notice is dismissible.** Grace and expiry describe something the user must act on, and a
   banner they can dismiss is one they do not see the second time.
6. ⚠ **A comfortably valid licence shows nothing at all** — no startup modal, no nag, no "you are licensed"
   confirmation (§17.1).
7. **The blocked states open the activation window over the main window; `Expired` does not.** A modal there
   would take away the editing, saving and exporting §7 guarantees.
8. ⚠ **The two main-window banners share row 1 through a `StackPanel`.** An unreadable `settings.dat` and an
   expired licence are independent facts and can both be up; two children of one `Auto` grid cell would simply
   overlap.
9. ⚠ **`AboutWindow`'s `Margin` baseline went 7 → 9, deliberately** (`product-polish.md` §11.1: *"nazwij,
   którą z dwóch rzeczy robisz"*). About is a documented one-off composition where every child carries its own
   vertical gap, the catalog has no "gap above" role, and §11.1 states outright that `Margin` is too contextual
   to be a role. The two new occurrences are the licensee line and the Debug marker, in the same idiom as the
   other seven.
10. **The activation window is a growing dialog** — measured, not assumed: the banner appears on the first
    failed attempt and Replace appears when a different `lid` is offered. `ScrollViewer` first, then
    `GrowingDialogBehavior.Attach`.

### 38.5 ⚠⚠ The status-bar correction — and a measurement that proved nothing

**User review of the running app, 2026-08-15.** `ConnectionRefused` originally returned the verdict's full
`Explain` plus a second sentence repeating what to do — **~250 characters landing in the STATUS BAR**. Seen
running it was a technical dump stretched across the window, and it repeated word for word what the banner
above it and the activation window were already saying.

⭐ **The fix is a division of labour, not a shorter string:** the **status bar says what is BLOCKED**; the
**banner and the activation window say what to DO**. `ConnectionRefused` now returns one short sentence chosen
by state, in a switch mirroring `Headline`'s exactly. ⛔ Do not re-compose it from `Explain`.

⚠ **One sentence per state, not one generic line.** An expired licence and one this build cannot read call
for different actions; a single sentence covering both would say neither.

#### ⚠⚠ The first version of the fit test passed on the very sentence the user had just reported as cut

It compared the label's `Bounds.Width` against its own `DesiredSize.Width` — and **a horizontal `StackPanel`
hands its children their full desired width unconditionally**, so those two numbers are equal by construction,
for any text of any length. The overflow does not shrink the label; it runs off the end of the flexible
column, which is why the screenshot showed text cut at the window edge rather than ellipsised, despite
`TextTrimming="CharacterEllipsis"` being set.

⭐ **The property that decides what the user sees is the width of column 1 of the status-bar grid**, so that
is what the test compares against now. Verified RED on the reported sentence: it needs **2 854 px** against
**1 081 px** of column at a 1280 px window — 2.6× over. The shipped sentences measure **841 px (EN)** and
**874 px (PL)**, leaving ~200 px of headroom, which is roughly 35 characters of connection name in column 0
before it starts to bite.

⭐ **Generalises past licensing:** *a "does it fit" test must measure against the container that constrains
the element, never against the element's own bounds — a child of a horizontal `StackPanel`, a `ScrollViewer`
or any unconstrained panel always "fits" itself.* Same family as the L2/L4a findings where editing an encoded
artifact as text mutated nothing and the test reported the absence of a failure as a success.

### 38.6 ⚠ What the acceptance actually rested on — stated so nobody over-reads it

§32's exit criterion for L4 reads *"all eight states reachable and verified in the running app"*. What
happened is worth recording precisely, because it is not the same sentence:

- ⭐ **The user ran the application and reviewed it**, in both configurations. That review is what found the
  status-bar defect (§38.5) — a defect no test in this stage had any chance of catching, because every one of
  them was about *what the text says*, not *how much room it has*.
- ⚠ **The states seen by hand were the unlicensed / gated ones.** `Valid`, `Grace` and `ExpiringSoon` were
  **not** walked in the running app, and they cannot be: `TrustedKeys.Production` is empty until L7, so no
  licence anyone can produce today verifies as usable in a shipped build. They are proven by tests against the
  fixture's own key table, end to end through the real `LicenseVerifier`.
- ⏭ **So one line of §32's criterion is deferred to L7, deliberately**, and it is the line that needs the real
  key ceremony to be true at all: *seeing* a valid licence run. ⛔ Do not record L4b as having verified it.

### 38.7 ⏭ What L4b deliberately did NOT do

⛔ **The clock-rollback warning has no surface.** `ClockLooksRolledBack` is computed and used by
`EffectiveNow`, exactly as L4a built it, but §7's banner table lists only the expiry states and this stage did
not invent a ninth. ⛔ No network code, no `iid`, no fingerprint, no seats enforcement, no production ceremony.
⛔ The License Manager's own main-window review stays where L3 left it.

⚠ **A real licence still verifies as `Invalid / UnknownKey` in every configuration** — `TrustedKeys.Production`
is empty until L7. Correct, deliberate, and unchanged by this stage.

### 38.8 ⏭ Owed to `CLAUDE.md` at the cleanup

The seam rule in §38.1 is the same class as the charset seam's (*"never create a command outside
`FirebirdCommandGuard`"*), which lives in `CLAUDE.md`'s driver-gotchas section. It is **not** written there yet:
`CLAUDE.md` stands at ~813 lines against its own ~800 threshold, and the user ratified that its cleanup is a
separate task which L4b must not expand. ⏭ Add one line for it when that cleanup runs.

---

## 39. L5.0 — as built (2026-08-16)

⭐ **The data layer only — no UI, by instruction.** `EmberTern.LicenseManager` gains schema **v2**, a
cross-customer licence query, history by subject, an integrity check, and the atomic issuing batch.
Builds **0/0 in Debug and Release, both solutions**. License Manager suite **139** (was 102: **+37**).

### 39.1 ⭐⭐ R1/R2 — how a batch cannot leave a signed artifact unrecorded

The stage existed to answer one question: *how do twenty signatures and twenty rows commit so that a
failure anywhere leaves neither half a batch nor an artifact the register does not know about?*

**The resolution is that signing is not a side effect.** `LicenseIssuer.Issue` is a pure function of key,
terms and clock — it writes no file and no row. What is irreversible is not the signature, it is the
**moment an artifact leaves the process**. So the invariant is stated as: *no artifact may be delivered
until its row is committed*, and the operation is ordered to make the alternative unreachable:

| Phase | Where | Property |
|---|---|---|
| 1 — sign everything | `IssuingWorkflow.IssueBatch` | pure; a throw leaves the register untouched and produces nothing anyone can hold |
| 2 — record everything | `LicenseRegister.ApplyIssueBatch` | ONE SQLite transaction: terms, artifacts, pointers, history, batch line — all or none |
| 3 — deliver | `IssuingWorkflow.SaveArtifact`, later and separately | reads the **stored** token, so every delivery path starts at a committed row |

⛔ The naive shape — sign-and-record one licence at a time — was rejected: it is forty transactions, and
an interruption at ten leaves ten customers extended, ten not, and no way to tell which half is which.

⚠ **Both halves were proved by injected defect, not by reasoning.** Per-unit transactions instead of one
turned **3** tests red including `AFaultInTheMiddleOfABatchLeavesTheRegisterExactlyAsItWas`; recording
inside the signing loop turned **2** red including `AFailedSIGNATUREInTheMiddleOfABatchRecordsNothingAtAll`.
Both were then restored and re-verified green.

### 39.2 ⭐⭐ D‑A resolved: `superseded` lives on the artifact, but not as a column

The user ratified that `current` / `superseded` is a fact about **`issued_artifacts`**. It cannot be a
column there: that table aborts every `UPDATE` and every `DELETE` by trigger, and L3 proved it by reaching
past the register's own API. A mutable status column would have meant relaxing a rule-#11-class guarantee
to hold bookkeeping.

⭐ **So the two facts are separated by their lifetimes.** The bytes are written once
(`issued_artifacts`, untouched); *which* artifact is current is rewritten on every re-issue
(`license_current_artifact`, one row per `lid`, updated in the same transaction that appends). The status
is **projected** from the join, and the **`artifact_status` view** exposes the same projection to any SQL
tool — §29's recovery row promises the register stays readable when this application will not start, and
a projection only our C# knows how to compute would have broken that promise quietly.

⛔ **`LicenseStatuses.Superseded` was removed.** It could never be written — a re-issue keeps the same
`lid`, so the licence *row* is never replaced. ⚠ This does not breach the append-only vocabulary rule:
nothing ever persisted it, so no stored row can carry it.

### 39.3 The freshness guard, and the L3 test it caught

`RefuseAnArtifactThatIsNotFresher` rejects an artifact whose `iat` does not come after the current one's.
⭐ EmberTern installs a replacement only when `incoming.iat > local.iat` (§16.4) and the issuer truncates
`iat` to whole seconds — so a double-click, or a batch retried at once, would otherwise be **recorded as
delivered** while every client silently declines the file.

⚠⚠ **It immediately failed a test L3 shipped green.** `ArtifactsAccumulateNewestFirstAndAreAudited`
appended two artifacts stamped at the same instant — a state no client would accept. The test was
corrected, not the guard. ⭐ A guard added after the fact is worth most exactly when it contradicts
existing green tests; the reflex to "fix the guard so the suite passes again" is what would have kept the
defect.

### 39.4 Decisions taken during implementation

1. **The free-text match runs in memory, the structured filters run in SQL.** ⚠ Measured, not stylistic:
   SQLite's `LIKE` and `lower()` are case-insensitive for **ASCII only**, by documented design — so
   `łódzka` would not find `Łódzka` in a register whose customers are Polish companies. .NET's
   `OrdinalIgnoreCase` applies Unicode case folding and does. ⭐ `ThePlainSqlEquivalentWouldHaveMissedIt`
   pins the *premise* rather than our code, so if SQLite ever changes, someone is told.
2. **Timestamps are compared as text, and that is sound rather than lucky** — every stamp is written
   through `LicensePayload`'s fixed-width UTC format, so lexicographic and chronological order coincide.
3. **`CheckIntegrity` reports, never repairs, and never throws.** The caller decides what a problem means:
   a list view warns, ⏭ a restore (L5.5) refuses. ⛔ A register that quietly fixes its own history is one
   whose history cannot be trusted. Its three corruption tests **inject** the damage past the API — a
   check proved only against states its own writer can produce is a check of the writer.
4. **`SaveLicenseCore` / `AppendArtifactCore` are the single owners of a write**, shared by the single
   issue and every batch unit, so the two can never disagree about what a licence update means.
5. **Reads are threaded with their transaction.** §36.4 recorded this as *"worth knowing before L5 adds
   bulk operations"*; it is now load-bearing — `Microsoft.Data.Sqlite` **throws** rather than reading
   stale data when a command's transaction does not match the connection's active one.
6. **The v1 → v2 upgrade is tested against the schema L3 actually shipped**, read off the `SchemaV1`
   constant by reflection rather than retyped, and backfills each licence's newest artifact as current.

### 39.5 ⏭ What L5.0 deliberately did NOT do

⛔ No UI of any kind — search, filters, the licences view, group extend and re-issue are L5.1–L5.4.
⛔ No backup, no JSONL, no restore (L5.5). ⛔ No date picker, no Seats validation, no data-folder surface
(L5.1+). ⛔ Nothing was committed — L5.0 awaits the user's acceptance.

---

## 40. L5.1 — as built (2026-08-16)

⭐ **Search and filters, as a second VIEW.** The main window gains a **Licences** view listing every
licence across every customer, narrowed by free text and three filters, with a way back to the customer
that owns the row. Builds **0/0 in Debug and Release, both solutions**. License Manager suite **171**
(was 139: **+32**). ⏭ **Implementation done — awaits the user's visual confirmation**, per the standing
directive: nothing with a UI is "fixed" until it has been seen in the running application.

### 40.1 ⭐ Two views, because there are two questions

The customer panel L3 built answers *"what does THIS customer have?"* — the operator arrives with a
name. The question this stage serves is *"who lapses next month?"*, which has no customer to start
from. ⛔ Per the user's ratified decision, licence filters were **not** folded into the customer detail
panel: at fifty customers a panel organised around one name cannot answer a question about all of them.

`LicenseBrowserViewModel` is a **separate** view model for the same reason, not for size. Two organising
principles in one class is how a view model becomes the place every later feature is added.

### 40.2 ⭐⭐ The view switch is two buttons, not a `TabControl`

⚠ **The same reasoning L3 used to decline a date picker.** `ControlStyles.axaml` — where EmberTern's own
`TabItem.bottom-tab` and `TabItem.sub-tab` live — **is not linkable** (it binds to AvaloniaEdit, DataGrid
and `EmberTern.App.Controls`; §12.1). A `TabControl` here would fall back to Fluent's own `TabItem`, i.e.
a fresh set of normal / hover / selected / disabled / focus decisions to repin, taken on for a two-item
switch.

⭐ It still **reads** as tabs because it consumes the tab vocabulary the token layer already carries —
`Size.Row.Tab`, `Pad.Tab`, `Radius.Tab` — so it moves with EmberTern's real tabs instead of drifting from
them. The current tab is painted `SurfaceRaisedBrush`, which the token cheat-sheet names for exactly this
consumer: a current tab **floats** above its strip, which is a different job from a row being selected in
a list (and in Light the two are opposites).

⚠ The strip is its own container and deliberately **not** nested inside `Border.chrome`: that context
style already declares a toolbar height for its children, and two context styles racing over one button
is settled by declaration order rather than by intent.

### 40.3 Decisions taken during implementation

1. **The "Issuing" filter is a dropdown, not a checkbox** — `ControlThemes.axaml`, which carries
   EmberTern's hand-written `CheckBox` template, is not linkable either, so a checkbox here would be
   Fluent's. ⭐ It also buys an option a checkbox cannot express: *"issued at least once"*.
2. ⭐ **A filter's narrowing is DATA, not a delegate** (`FilterOption` and its three subtypes), so
   `BuildQuery` is public and a test asserts that *"Expiring within 30 days"* produces the query it
   claims to — rather than inferring it from what came back.
3. ⭐⭐ **"Expiring within 30 days" sets `ExpiresFrom` as well as `ExpiresBefore`.** Without the lower
   bound the renewal list silently includes everyone who lapsed last year — a list an operator stops
   trusting after one phone call. Pinned by `ExpiringWithinThirtyDaysDoesNotIncludeWhatAlreadyLapsed`.
4. **The selection survives a keystroke.** The list is rebuilt on every filter change, so the selected
   object is a different instance; without re-finding it by `lid`, typing one more character clears the
   detail strip the operator is reading.
5. **The browser re-reads on entering the view**, not on every mutation — in L5.1 the view is read-only
   and unreachable while editing, so "fresh whenever it is looked at" is both sufficient and the only
   rule that cannot fall out of step with a mutation added later.
6. **"Never issued" outranks the expiry date** in a row's standing: saying *"expires in 300 days"* about
   a file that was never sent is the more misleading of the two true statements.
7. ⭐ **`HeadlessTheme` was extracted** when a second headless class was about to make a second copy of
   *"how you switch the theme in a test"*. Two copies of a one-line helper is how two classes end up
   testing two different things while appearing to test one.
8. ⚠ **`TextBox.Watermark` is obsolete in Avalonia 12.1** — it is `PlaceholderText`. Caught by the build,
   which is what `TreatWarningsAsErrors` is for; `FluentBridge` repins the placeholder brush in both
   dictionaries, so it is themed without further work.

### 40.4 ⚠ The guards were proved by injected defect, four of them

| Injection | Went red |
|---|---|
| the shared `ItemTemplate` removed from one dropdown | `AFilterDropdownShowsItsLabelAndNotTheShapeOfItsRecord` — and the failure printed `StatusFilter { Label = Any status, Status =  }`, i.e. **gotcha #370 exactly**: a template that stops matching raises no binding error, it silently renders `ToString()` |
| `Button.view-tab.active` paint removed | `TheCurrentTabIsPaintedAsRaisedAndTheOtherIsNot` (both themes) — the class was still set and still meant nothing |
| selection preservation removed from `Refresh` | `TheSelectionSurvivesTheNextKeystroke` |
| `ExpiresFrom` dropped from the forward window | `ExpiringWithinThirtyDaysDoesNotIncludeWhatAlreadyLapsed` + `TheExpiryFilterIsReadableAsAQueryBeforeItRuns` |

⭐ The tab test asserts the **realised brush**, not the class: a class that is set and painted by nobody
looks exactly like a class that works, in every test that checks the class.

### 40.5 ⚠ A divergence worth stating: the License Manager is English-only

`CLAUDE.md` architecture rule 12 requires every user-visible string to go through the localization
mechanism in every supported language. **The License Manager has no such mechanism** — L3 shipped it with
English literals, and L5.1 followed that convention rather than inventing a second catalog mid-stage.
⏭ Whether this single-operator admin tool is ever localized is the user's decision, not a defect this
stage should have fixed silently.

### 40.6 ⏭ What L5.1 deliberately did NOT do

⛔ No bulk selection and no bulk action — L5.4. ⛔ No artifact preview or history view — L5.2. ⛔ No
re-issue — L5.3. ⛔ No date picker, no Seats validation, no data-folder surface: the user placed all three
in L5, and they belong with the licence FORM (L5.3) and with backup (L5.5) rather than with a read-only
search. ⛔ Nothing committed — L5.1 awaits acceptance.

---

## 41. L5.1 — the QA pass (2026-08-16)

⭐ Six points raised by the user after looking at the running application, plus the enabling change they
all turned out to depend on. Builds **0/0 in Debug and Release, both solutions**. License Manager suite
**195** (was 171: **+24**); EmberTern **9 087** (was 9 081: **+6**). ⏭ **Awaits the user's visual
confirmation** — nothing with a UI is finished until a person has looked at it.

### 41.1 ⭐⭐ The enabler: `IconGeometries.axaml` was split so the icons could be SHARED

Three of the six points (the top bar, the copy action, the message strip) needed EmberTern's icons, and
the dictionary could not be linked: it ended with three `ControlTheme`s bound to `controls:SvgIcon`,
`DebuggerIcon` and `CreateIcon`. **Those 164 lines were the only type reference in an otherwise pure
catalogue of 86 `StreamGeometry` resources.**

They moved to `EmberTern.App/Themes/IconControlThemes.axaml`; the geometry half is now linked into the
License Manager. ⛔ **No geometry was copied**, and the License Manager still does not — and must not —
reference `EmberTern.App`. What it reproduces instead is the *render path* (Viewbox → 24×24 Canvas →
stroked Path), as STRUCTURE, which its style layer is allowed to hold. ⚠ Stroke, not fill: the glyphs are
Lucide, and Avalonia's built-in `PathIcon` fills its data.

⚠ **The split created an ordering dependency that compiles perfectly when reversed** — `CreateIcon`'s
theme resolves `{StaticResource Icon.Play}`, and `StaticResource` sees only what is already merged. Three
new guards in `IconGeometriesSplitTests` hold it: the geometry file stays type-free, the merge order is
declared, and **every `Icon.*` key the application references resolves in the live resource system**.

### 41.2 ⚠⚠ Two findings the new guard produced, both pre-existing

1. **The guard's first version covered markup only, and missed C#.** Renaming `Icon.Sun` out of the
   dictionary did NOT turn it red, because the theme toggle resolves its glyph from a string literal in
   `ThemeToggleIconConverter`. Dozens of keys are referenced that way. ⭐ Found by injection, not by
   review — a markup-only scan was covering roughly half the real surface while reading as complete.
2. ⭐ **`Icon.Name` does not exist.** `SqlCompletionData.cs` asks for it for `SqlCompletionKind.Column`
   and for locals; the only occurrence of that key in the dictionary is inside the header COMMENT showing
   how to add a geometry. So column and local completion items render with no icon. ⛔ Not fixed here —
   choosing a glyph is a design decision — and recorded in `docs/current-state.md` as the guard's single
   `KnownMissing` entry, with a note that a second entry would mean the rule is wrong.

### 41.3 The six points, as resolved

| # | Point | Resolution |
|---|---|---|
| QA‑1 | The top bar did not read as EmberTern | `Size.TitleBar` strip; the signing key demoted to `hint` (provenance, not a control); the theme toggle is now the **same icon EmberTern uses**, showing the ACTION — Sun while Dark is active, Moon while Light |
| QA‑2 | `Identifier` and `Licence id` looked editable | Both are now a `SelectableTextBlock` value plus a copy action. ⭐ A read-only `TextBox` states "generated" by refusing input, which is the one way of saying it that still invites the input |
| QA‑3 | The customer rail had a fixed width | `GridSplitter`, EmberTern's own shape (`Width="4"`, painted `BorderBrush`), bounded 200–480 |
| QA‑4 | The message strip was weak in Light, with an empty band beneath | ⭐ **Measured cause of the band: `Margin.SectionGap` is `0,0,0,16`** — a BOTTOM margin on a strip docked to the bottom edge. Removed. Severity now travels on a **stripe + glyph + text** (EmberTern's `MessageBanner` recipe) instead of the border colour alone, which in Light is a hairline against a nearly-document-coloured panel |
| QA‑5 | The licences list was thin | Contact person (searchable) and the **register's own** status |
| QA‑6 | Dates demanded a format | `CalendarDatePicker` — pick or type |

### 41.4 ⛔ Two vocabularies of status, kept apart

`LicenseStatus` in `EmberTern.Licensing` is the CLIENT'S VERDICT about an artifact (Valid · Grace ·
Expired · NotYetValid · Invalid · VersionNotCovered), produced by `LicenseVerifier` and by nothing else.
`LicenseStatuses` in the register is administrative bookkeeping about a licence ROW (active · blocked).

⭐ **The list column shows the register's stored value; the client verdict stays on the SELECTION**, where
"Inspect latest" already runs the real verifier. Ratified by the user, and it is a cost decision as well
as a principled one: a verdict per row is an ECDSA verification per row, i.e. hundreds of signature checks
on every keystroke. ⛔ Nothing in the UI invents a licensing state — what it computes for itself is
arithmetic on a date.

### 41.5 The domain did not move with the date picker

⭐ A chosen day is still read as a **UTC calendar day**, and the expiry still runs to the **end** of it.
`LicenseTermsDateTests` pins both, plus the case that proves the second rule is load-bearing: a licence
starting and ending on the same day is legal precisely because the expiry runs to 23:59:59 — under
midnight-to-midnight it would be an empty interval and get refused.

⚠ Empty is now the only date fault reachable in the view model: text that does not parse never becomes a
`SelectedDate`. It is refused rather than defaulted — a licence quietly starting today because a field was
blank is a term nobody agreed to.

### 41.6 ⏭ Recorded, not fixed

- ⚠ **The calendar flyout is not repinned to our palette**: `FluentBridge.axaml` carries **zero**
  `Calendar*` keys, so `CalendarDatePicker`'s popup shows Fluent's own accent — the brown/orange this
  project fights everywhere else. ⭐ **EmberTern has the identical gap** in `DebuggerTabView` and
  `ExecuteProcedureDialog`, so adopting the control makes the License Manager consistent with the product
  *including this blemish*. Ratified by the user as its own design-system item; ⛔ deliberately out of
  this pass, because repinning `Calendar*` is work in the product's bridge, for both applications at once.
- ⚠ **The License Manager is English-only** (§40.5) — unchanged by this pass.
- ⚠ **`DatePresentationTests` gained a third recorded category**, `DeliberateIsoDisplayPaths`. The guard
  scans all of `src/`, so it reaches the License Manager, where ISO is the ratified date form (§36.2) and
  matches what the register stores. ⛔ Recorded WITH ITS REASON rather than excluded — the guard's whole
  point is that an author must say which side of the line a date is on. ⚠⚠ This also exposed a process
  error: **L5.1 was reported without running the EmberTern suite**, on the reasoning that no changed file
  belonged to that solution. The reasoning was wrong — several EmberTern guards scan the whole tree.

---

## 42. L5.1 QA follow-up — done, and what remains (2026-08-16)

⭐ A second visual QA round. **P0 closed, P1 not started.** Builds **0/0 in Debug and Release, both
solutions**. License Manager **206** (was 195: **+11**); EmberTern **9 087**, unchanged (this round
touched no product file). ⛔ **Nothing committed.**

### 42.1 ⭐⭐ P0.1 — the "Licences view shows only the last customer's licence" defect: ROOT CAUSE FOUND

**It was not a query defect, and not a filter leaking from the customers view.** Both hypotheses are now
refuted by tests that stay in the suite. The register's cross-customer query was always right.

⭐ **The licence FORM was not cleared when a new customer was started.** `NewCustomer` emptied the
customer fields, the licence LIST and the selection — but left `LicenseId` sitting in the form. So an
operator who added a second customer and pressed **Save terms** without first pressing **New licence**
wrote *the previous customer's licence id* with the new `customer_id`: the row was **re-parented**, not
created. One licence where there should have been two, and the first customer silently lost theirs.

⚠ That is a rule-#11-class defect — data moved, quietly, with no error. Fixed on both levels:

1. **The register refuses it outright.** `SaveLicenseCore` now throws `RegisterIntegrityException` if a
   save would change an existing licence's `customer_id`. ⭐ A licence's customer is part of its identity:
   every artifact ever signed for it carries that customer's NAME (D6), so re-parenting the row would make
   the register stop agreeing with the files it has already delivered.
2. **`ClearLicenseForm()`** — one owner for "what a blank licence form looks like", called by
   `NewCustomer`. Clearing a form is a habit; refusing the write is the guarantee.

`SecondCustomerRegressionTests` reproduces the operator's exact click order and pins six properties,
including the two hypotheses that turned out to be wrong (so nobody re-investigates them).

### 42.2 ⭐ P0.2 — the License Manager draws its own window

Windows drew a title bar reading *"EmberTern License Manager"* and the application drew a second bar
reading the same thing directly beneath it — two bars that looked like two different programs. The window
now extends its client area exactly as EmberTern has since M3.1: `ExtendClientAreaToDecorationsHint`,
`ExtendClientAreaTitleBarHeightHint="-1"`, `WindowDecorations="BorderOnly"`, drag by the bar, double-tap
to maximise (with the guard that keeps a double-click on a BUTTON from also maximising), and three
caption buttons wearing EmberTern's own `Icon.Window*` glyphs through the shared dictionary.

⭐ The maximise glyph shows what the click will DO and is driven by `WindowState` itself, so it is correct
however the state changed — including a Windows snap gesture the application never saw as a click.

### 42.3 ⚠⚠ NOT DONE — the exact remaining scope for the next session

⛔ **Do not re-do the recon; it is all here.**

| # | Item | State | Notes for whoever picks it up |
|---|---|---|---|
| **P1‑a** | **Spacing rhythm** — `Identifier` + value + copy icon, `Seats` + `Valid from`, `First name` + `Last name`, the banner glyph + its text, and other neighbours that touch | ⛔ **not started** | ⚠ The user's instruction is explicit: ⛔ no ad-hoc `Margin="…"` on individual controls. First check the roles that already exist (`Space.*`, `Margin.FieldGap`, `Margin.LabelGap`, `Margin.InlineGap`, `Margin.SectionGap`, `Pad.*`); if a role is genuinely missing, ADD the role rather than scattering numbers. ⚠ New roles would go in `EmberTern.App/Themes/Tokens.axaml`, which is SHARED with the product — that needs the user's agreement, exactly as the icon split did |
| **P1‑b** | **Uniform control sizes** — `Seats` is visually shorter than the date pickers; neighbours in one row differ in height; actions in one row differ without a reason | ⛔ **not started** | ⚠ Fix in the BASE STYLE, never per control. Suspected cause: `CalendarDatePicker` carries its own Fluent `MinHeight` of 32 while `TextBox` sits on `Size.Control` (24) — EmberTern records this exact conflict in `ControlStyles.axaml` around its `DataGridCell CalendarDatePicker` style and deliberately leaves FORM pickers alone. So the decision is: give the License Manager a `CalendarDatePicker` metric style on `Size.Control`, and MEASURE whether the setter actually beats the template's own value (§16's "a setter cannot beat a local value" trap) |
| **P1‑c** | **Double-click a licence → Inspect** in the customers view | ⛔ **not started** | Single click already selects. Wire `DoubleTapped` on the licences `ListBox` to the existing `InspectLatestCommand` — ⛔ no new screen. ⚠ A licence with no artifact must explain WHY the preview is unavailable; `InspectLatest` already warns *"This licence has never been issued."*, so the work is the gesture, not the message. Testable headlessly through the command |
| **P2** | Remaining cosmetics | ⛔ not started | ⛔ Do not start before P1 is closed (user's instruction) |

### 42.4 Standing facts the next session should not rediscover

- ⚠ **The calendar flyout is not repinned** — `FluentBridge` has zero `Calendar*` keys, and EmberTern has
  the same gap in its own two pickers. Ratified as a separate design-system item, in the product.
- ⚠ **`Icon.Name` does not exist**, so column/local completion items in EmberTern render with no icon.
  Recorded in `docs/current-state.md`; the guard holds it as its single `KnownMissing` entry.
- ⚠ **`TabStripPresentationTests` loses to the documented Avalonia headless race** on a full parallel run
  on this machine — measured this session: identical stack, fails identically with the new test class
  REMOVED, and **9 087/9 087 green with collection parallelism off**. ⛔ Parallelism-off is a diagnostic
  only; as a fix it was already measured and rejected.
- ⚠ **Every EmberTern guard scans the whole `src/` tree**, the License Manager included. ⛔ Never report a
  License Manager stage without running the EmberTern suite — that error was made once already (§41.6).

---

## 43. L5.1 QA follow-up — P1 as built (2026-08-17)

⭐ **P1 closed: spacing, uniform control sizes, double-click to Inspect.** Builds **0/0 in Debug and
Release, both solutions**. License Manager **223** (was 206: **+17**); EmberTern **9 087**, unchanged
(this round touched no product file). ✅ **Accepted by the user 2026-08-17 after looking at the running
application** — *"elementy są już prawidłowo rozstawione"*. Committed together with P0 as one logical
commit; ⛔ **not pushed** — the user holds the push.

⭐⭐ **No token was added and `Tokens.axaml` was not touched.** Both P1‑a and P1‑b turned out to be
misapplications of roles that already exist, not gaps in the catalogue — so the shared file the user
asked to be warned about never came into it.

### 43.1 ⭐⭐ P1‑a — the gaps were not missing; ONE rule was applied to the wrong owner, five times

**Measured first, on a laid-out window, before anything was changed.** Every multi-column row reported
`ColumnSpacing = 0` and this pattern of realised distances:

| Row | 0→1 | 1→2 |
|---|---|---|
| Name │ Identifier | **0** | — |
| First name │ Last name │ E-mail | **0** | 8 |
| Seats │ Valid from │ Valid until | **0** | 8 |
| value │ Copy | **0** | — |
| stripe │ glyph │ message | **0** | **16** |
| Search │ Status │ Expiry │ Issuing │ Clear | **0** | 8 / **0** / 8 |
| licences list row (6 columns) | **0** everywhere | |

⭐ **The cause is a single sentence.** `Margin.InlineGap` is `0,0,8,0`, and `Tokens.axaml` states its
contract in its own comment — *"jej właścicielem jest element PO LEWEJ"*. The License Manager hung it on
the **second** column of every row, i.e. on the element to the **right** of the gap, so the 8 px landed
*after* the pair instead of *between* it. Hence the alternating 0 / 8 rhythm, and hence the 16 in the
message strip, where the glyph's right margin and the text's own left margin both paid into the same gap
while the one that was missing had no owner at all.

⛔ **So "add a margin here and there" would have been wrong twice over** — it would have added a second
owner to gaps that already had one, and left the rule that produced all of them intact.

**The fix is that the gap belongs to the container.** `ColumnSpacing` / `RowSpacing`, which the product
already does this way (`SettingsWindow.axaml`: *"Gaps come from RowSpacing / ColumnSpacing, so no cell
carries a margin of its own"*). All seven `Margin.InlineGap` uses are gone; the window now has **15**
spaced grids and **three** spacings, each meaning something different and each read off the existing
scale:

| Role | Token | What it separates |
|---|---|---|
| the default | `Space.Md` (8) | two INDEPENDENT things — field ↔ field, caption ↔ action, column ↔ column |
| compound | `Space.Sm` (6) | parts of ONE element — the severity glyph and its message |
| attached | `Space.Xs` (4) | a value and an affordance that acts on THAT value — an id and its Copy button |

⚠ The message strip was restructured rather than re-margined: the stripe still reaches three edges
(`Padding` stays 0) and a **content grid inside it** carries one `Pad.Cell` inset plus the glyph→text
gap. The `Border.message TextBlock` margin was **deleted** — a second owner is what produced the
reported spacing in the first place.

### 43.2 ⭐ P1‑b — the setter DOES beat Fluent here, and it was measured before it was written

`GetDiagnostic(MinHeightProperty)` reported the picker's **32** arriving at priority **`Style`** —
Fluent's own setter, **not** a template-local value. That is the whole question §16 poses, and the answer
here is the favourable one: our style layer merges into `Application.Styles` after `FluentTheme`, so a
peer setter wins. ⭐ The proof was already standing next to it — the base `TextBox` style beats Fluent's
32 by exactly this mechanism, which is why `Seats` measured 24.

⚠⚠ **But the outer setter alone produced 26, not 24** — the case the user asked to be stopped and shown.
It did not need a decision, because measurement found the cause: the base `TextBox` style also reaches
`PART_TextBox` **inside** the template and hands it `MinHeight` 24, which the template then insets by its
own 1 px margin. One more setter — the inner box gives up its own floor — and the row measures **24 / 24
/ 24**.

⚠⚠ **§16's trap did appear, just not where it was expected.** The first version of that inner style also
set `Padding` and `BorderThickness` to 0. **Both were measured to change nothing**: Fluent hands
PART_TextBox those two as template-bound LOCAL values. They were removed rather than left in looking
effective — three of four setters on that element are silently inert, and a dead setter reads exactly
like a live one.

⛔ The hybrid is untouched: pick from the calendar, type by hand, empty/unparseable refused (§41.5).

### 43.3 ⭐⭐ A defect the domain rule found that the user's list did not contain

The height sweep reported the **"Issuing" filter at 34 px** beside two identical dropdowns at 24. The
cause was not the dropdown: its `<TextBlock>` had rendered the label on **two lines** (17 + 17). The base
`TextBlock` style sets `TextWrapping="Wrap"` — correct for a caption, a hint and a message, and wrong the
moment it reaches a control's content presenter, where a value that does not fit must lose its tail
rather than gain a row and push its neighbours out of line.

⚠ **A/B'd before it was written up**, because P1‑a had just narrowed those columns by 8 px and was the
obvious suspect: with the filter row reverted to its exact pre-P1‑a shape the label still wrapped and the
dropdown still measured 34. ⭐ **The defect pre-dates P1‑a.** Narrowing the column would have made it
worse, and hiding behind that would have left a live defect labelled "not mine".

⛔ Fixed by what the text IS, not by which dropdown was seen: `ComboBox TextBlock, ComboBoxItem TextBlock`
trims. Fixing the three filters would have been an exception list with a defect waiting behind it.

### 43.4 P1‑c — the gesture, and only the gesture

`DoubleTapped` on the customer view's licences list runs **`InspectLatestCommand`** — the same command
the button runs. ⛔ No second Inspect, no new screen. ⚠ Guarded on the ROW rather than on the list:
`DoubleTapped` bubbles from the empty space below the last item too, and re-opening the previously
selected licence because the operator double-clicked past the end is a preview nobody asked for.

⭐ The never-issued case is the one the test leans on: a gesture wired to "open the preview" instead of to
the command would have to answer it itself, and the honest failure mode of a copy is silence.

### 43.5 ⚠⚠ Every guard was proved by injected defect — five injections, five reds

⛔ Nothing in these three files asserts that a property was SET. The P1‑a defect would have passed such a
test perfectly: `Margin.InlineGap` **was** set, on every row, and produced a gap of zero.

| Injection | Went red | Reported |
|---|---|---|
| the Seats row reverted to its pre-P1‑a shape | `SeatsDoesNotRunIntoTheFirstDateField` + the sweep | `0 px`, the original symptom |
| the inner `PART_TextBox` `MinHeight` setter removed | 4 tests, incl. the mechanism test | `26` |
| the dropdown trimming style removed | `ADropdownLabelTooLongForItsBox…` + the sweep | `34` |
| the outer `CalendarDatePicker` `MinHeight` removed | 3 tests | `32` |
| `DoubleTapped` unwired / the row guard removed | 2 and 1 respectively | — |

⭐ **Two of the three new files carry a rule bounded by the DOMAIN, not by the six rows the user
photographed**: `NoTwoNeighboursInAnyRowOfThisWindowTouch` and
`NoNeighbourInAFormRowIsTallerThanAnyOtherByMoreThanTheLadderGap`. The second one is what found §43.3 —
an exception list would not have.

⚠ **The sweep also produced a finding that had to be JUDGED rather than fixed**: it reported the customer
rail and the detail pane each 0 px from the `GridSplitter`. That zero is correct — a separator that does
not touch what it separates is a line floating in a gutter — so the rule is stated positively (the
distance is between two pieces of **content**; a splitter is not content) rather than as an exception.

### 43.6 What was measured and deliberately NOT changed

- ⭐ **The four licence actions are already uniform at 28 px.** The reported "różne wymiary" is *width*
  — 146 / 194 / 194 / 206 — and that is `Size.ActionMinWidth` being a **floor** above which the label
  decides. A recorded product decision, not drift. The test asserts height and deliberately does not
  assert width.
- ⚠ `Seats` carries a literal `Width="80"`. A content-driven width has no token role, and inventing one
  for a single field is the kind of role that lies. ⏭ Left as it is, recorded here.
- ⚠ The calendar FLYOUT is still not repinned (§41.6, §42.4) — product work, both applications at once.

### 43.7 ✅ Two gotchas filed (on acceptance, 2026-08-17)

Both went into the **Avalonia UI** section of `docs/gotchas.md` as **#375** and **#376**: the directional
spacing token hung on the wrong owner, and the base `TextBlock` `Wrap` reaching inside control templates.
⛔ Neither was promoted into `CLAUDE.md`'s short "Live gotchas" list — that list's bar is *"would bite
almost any session"*, and these two bite a session doing UI work. Keeping them out is the tripwire
working, not an omission.

⚠⚠ **Filing them exposed a third thing, and it was not in the plan.** The gotchas TOC carried per-section
counts *"recomputed 2026-07-25"* and then hand-maintained — so **every figure in it was wrong, all of them
low**: "General engineering" read 44 against a true **133**, "SQL lexing" 8 against **22**, "Avalonia UI"
84 against **93**. ⭐ Precisely the failure `CLAUDE.md` describes where it refuses to write the total down.
The block was **re-derived** rather than incremented, and now carries the command that re-derives it plus
a ⛔ against adding one to a figure. `CLAUDE.md`'s own measured line moved 363/#374 → **365/#376**.

### 43.8 ⏭ What remains, and where the branch stands

⛔ **P2 NOT started, by instruction, and it must not start before the user accepts it in a new session.**
⛔ L5.2 (artifact preview / history surface), L5.3 (re-issue), L5.4 (bulk), L5.5 (backup) all unchanged
and unstarted.

⭐ **P0 and P1 are ONE commit**, and deliberately so: P1 is a QA pass over the surfaces P0 built, and P0
alone never stood as an accepted state — the two were reviewed together and are indivisible as a unit of
history. The commit also carries the L5.1 work and its first QA round (§40–§42), which had likewise never
been committed.

⛔ **Not pushed.** The user holds the push; `origin` is the only remote on this clone and it was not
touched. ⏭ On the work machine the company Gitea is synced by hand, later (§0 of
`docs/current-state.md`).

⚠ Carried forward unresolved, all recorded rather than fixed: the `Calendar*` flyout is still not repinned
in `FluentBridge` (product work, both applications at once); `Icon.Name` still does not exist; the
License Manager is still English-only (§40.5); `Seats` still carries a literal `Width="80"`.

---

## 44. L5.2 — as built (2026-08-17)

⭐ **The issuing history and the artifact preview.** A licence's every issue is listed, chronologically,
with the current one unmistakable and the earlier ones intact; selecting one shows what was signed into it
and what EmberTern would say about it today. Builds **0/0 in Debug and Release, both solutions**. License
Manager **249** (was 223: **+26**); EmberTern **9 087**, unchanged. ✅ **Accepted by the user 2026-08-17
after looking at the running application** — the history, the current-issue mark and the preview all
confirmed in both themes. Committed on `feat/licensing-system`; ⛔ **not pushed** — the user holds the
push.

⭐⭐ **No schema change, no new model, no new register method.** L5.0 had already built everything the data
side needed — `issued_artifacts` append-only, `license_current_artifact` as the pointer, the `status`
projection, `GetArtifacts` newest-first, `GetCurrentArtifact`, and an `IssuingWorkflow.Inspect` /
`SaveArtifact` pair that both already take **any** artifact rather than only the newest. L5.2 is a
surface over facts that were already true, which is what the L5.0/L5.1 split was for.

### 44.1 The two sources, and why neither could do the other's job

| Shown | Source | Why not the other one |
|---|---|---|
| Licensee · seats · validity · product · algorithm | `LicensePayload.TryParse` over the **stored payload** | These are what was SIGNED, which is not what the licence row says today — the difference is the whole reason a support call happens. And the parse works even for an artifact the verifier refuses, which is exactly the artifact being asked about |
| "What EmberTern would say about it today" | the real `LicenseVerifier`, through `IssuingWorkflow.Inspect` | ⛔ Never recomputed from the dates above. An administrative tool that answers *"would this be accepted?"* with its own arithmetic will eventually disagree with the product, in front of a customer (§41.4) |
| current / superseded | the register's projection over `license_current_artifact` | ⛔ Never "the newest row". §44.4 records what happened when that was tested |

⚠ Re-encoding the stored payload string to bytes for the parser is **lossless rather than lucky**: the
register holds `Encoding.UTF8.GetString(issued.PayloadJson)`, a decode of the exact signed bytes.

### 44.2 ⭐⭐ The presentation rule: the mark is ADDITIVE

The operator's real question is *"did re-issuing overwrite what I sent them before?"*, and the answer is a
property of the schema. So the panel says it twice — once in words (*"3 issues on record, all kept … earlier
ones were superseded, never overwritten or deleted"*) and once in its shape.

⛔ **An earlier issue must not be dimmed, struck through, greyed or otherwise shown as removed.** It was
really delivered, to a customer who may still be running it, and the append-only trigger exists so the
register can still answer for it. ⭐ Hence the asymmetry only ever ADDS: the current artifact gains a chip
(`ConnectedBrush` — a state, not an accent, so it does not compete with the primary action); every other
row keeps full-strength content and simply says "superseded" in the subtle grade any secondary fact uses.

⚠ Pinned as pixels, in both themes: the earlier row's content brush, font size and effective opacity are
asserted **equal to the current row's**, and its text asserted not struck. A claim about how something
looks cannot be held by a test that reads a property.

### 44.3 Decisions taken during implementation

1. **A third view model** (`ArtifactHistoryViewModel`), for §40.1's reason rather than for size: the
   licence card is a FORM over terms — singular, editable — and this is a LEDGER of artifacts — plural,
   ordered, immutable. Two organising principles in one class is how a view model becomes the place every
   later feature is added, and L5.3 will build on this one.
2. ⭐ **`VerdictText` extracted** the moment the detail pane became a second consumer of a mapping that
   lived inside `InspectLatest`. Two switches over one enum is how a message strip and a detail panel end
   up describing one artifact two different ways, with no way to tell which is the application's opinion.
3. ⭐ **`InspectLatest` gained a second half rather than a sibling command**: it now selects the artifact
   it is describing. So the message and the panel always name the same release, and P1-c's double-click
   still runs the one Inspect — no parallel path that would have to re-answer "never issued" itself.
4. **"Export latest…" is untouched.** The new "Export this issue…" answers a different question —
   *"send them THIS one"* versus *"send them their file"* — and collapsing them would make the common
   case depend on a selection the operator did not make. ⛔ Both go through `IssuingWorkflow.SaveArtifact`,
   so there is one writer, one `licence.exported` audit action, and one guarantee that a re-export never
   becomes a re-issue with a new `iat` (§16.4).
5. ⛔ **No delete and no edit, and that is asserted** — `issued_artifacts` aborts UPDATE and DELETE by
   trigger, so a command offering either would be an invitation to a stack trace. A test walks the view
   model's generated commands and fails if one appears.
6. **The panel is visible even when the licence was never issued.** A surface that disappears cannot say
   *"nothing was ever sent"*, and that is the state an operator most needs told.
7. ⭐ **`LicenseHistory` was DELETED from `ShellViewModel`.** Once the history card carried the summary,
   the licence card was rendering the identical sentence from a second property. One owner.
8. **The token is shown as one trimmed line plus its delivered size, with the whole of it on the copy
   action.** ⚠ The size is measured over `LicenseArmor.Wrap(token)` — the armored form that is actually
   written to the file — not the raw token's length.

### 44.4 ⚠⚠ An injected defect that stayed GREEN, and what it exposed

⭐⭐ **The most useful thing this stage measured.** The claim *"current comes from the register's pointer,
never from the ordering"* was covered by a test comparing the marked row against `GetCurrentArtifact`.
Replacing the projection with *"the newest row wins"* turned **nothing** red.

The reason is structural: a re-issue appends an artifact and moves the pointer **in one transaction**, so
in every scenario reachable through the API the two answers are the same row. The test could not have
failed. ⭐ The repair was to build the unreachable state — repoint `license_current_artifact` at the
**oldest** artifact with raw SQL, exactly as §39.4's corruption tests inject damage past the API — after
which the injection goes red properly.

⚠⚠ **The general lesson is now gotcha #378:** *"I injected the defect and it went red"* proves a guard;
*"I injected the defect and it stayed green"* proves nothing, and the first question is whether the wrong
implementation is distinguishable in any reachable state at all.

A second injection failed the same way for a different reason: dimming the row with `Opacity="0.5"` inside
the `DataTemplate` left the opacity assertion green, because it measured **upward from the row container**
while the dimming sat below it. Effective opacity is a product along the whole chain, so the measurement
now starts at the ink. ⛔ Not filed as its own gotcha — it is an instance of the standing QA rule about
measuring against the thing that actually governs the element, and the test carries the note.

### 44.5 The guards, and the injections that proved them

| Injection | Went red |
|---|---|
| `current` decided by the newest row instead of the pointer | `TheCurrentMarkFollowsThePointerEvenWhenItIsNotTheNEWESTArtifact` — ⚠ only after the guard was rewritten; see §44.4 |
| `Opacity="0.5"` on the row template | `AnEarlierIssueIsNotDimmedStruckThroughOrOtherwiseShownAsRemoved` (both themes) — ⚠ only after the measurement moved to the ink |
| the chip repainted `PanelBrush` instead of `ConnectedBrush` | `TheCurrentIssueWearsTheChipAndTheEarlierOnesDoNot` (both themes) |
| export writing `Artifacts[0]` instead of the selection | `ExportingTheSELECTEDIssueWritesThatIssueAndNotTheNewestOne` |
| `InspectLatest` no longer selecting the current artifact | `InspectLatestOpensTheArtifactItIsTalkingAbout` + the double-click test |
| the history not clearing its selection on load | `ReloadingKeepsTheOperatorLookingAtTheIssueTheyHadOpen` |

⚠ A test-authoring defect was also found and fixed on the way: the export assertion compared the written
file against the **raw** token, which is never a contiguous substring of it — `LicenseArmor.Wrap` breaks
the token into 64-character lines. It now compares against the armored form, which is what the customer
receives.

### 44.6 ⏭ What L5.2 deliberately did NOT do

⛔ No re-issue (L5.3), no bulk selection or batch renewal (L5.4), no backup / JSONL / restore (L5.5).
⛔ `FluentBridge`'s missing `Calendar*` keys untouched — product work, both applications at once.
⛔ `Icon.Name` untouched. ⛔ No new control type: the panel is the existing `Border.card` + `ListBox` +
`field-label` + `SelectableTextBlock.value` + Copy vocabulary, on the P1 spacing rhythm, with the AppBar
and both themes unchanged.

### 44.7 ⏭ Where the branch stands after L5.2

⭐ **L5.2 is its own commit**, unlike P0+P1: those were one unit because P1 was a QA pass over the surfaces
P0 had just built and neither had ever stood as an accepted state on its own. L5.2 was accepted in its own
right, on top of an accepted and pushed `2531576`, so it commits alone.

⛔ **Not pushed.** `origin` is the only remote on this clone and it was not touched. ⏭ The company Gitea is
synced by hand from the work machine, later (§0 of `docs/current-state.md`).

⏭ **Next: L5 QA P2** (remaining cosmetics), then **L5.3** (re-issue), which is the first consumer of
`ArtifactHistoryViewModel` beyond reading. ⛔ Neither starts without the user's go-ahead.

⚠ Carried forward unresolved, all recorded rather than fixed and all unchanged by this stage: the
`Calendar*` flyout is not repinned in `FluentBridge`; `Icon.Name` does not exist; the License Manager is
English-only (§40.5); `Seats` carries a literal `Width="80"`.

---

## 45. L5.3 — as built (2026-08-17)

⭐ **Re-issue of a single licence, with an explicit reason.** The first stage in which the License Manager
changes a licence's standing rather than reading it — and it does so without touching one byte of what was
already issued. Builds **0/0 in Debug and Release, both solutions**. License Manager **279** (was 249:
**+30**); EmberTern **9 087**, unchanged. ✅ **Accepted by the user 2026-08-17 after looking at the running
application** — the reason picker, the re-issue steer, the note field, the reasons in words on the history
and the re-sized `Seats` field, in both themes.

⭐ **Committed on `feat/licensing-system` as its own commit**, on top of an accepted `e3c746c` (L5.2).
⛔ **Not pushed** — the user holds the push; `origin` is the only remote on this clone and it was not
touched. ⏭ The company Gitea is synced by hand from the work machine, later (§0 of
`docs/current-state.md`).

⭐⭐ **No schema change, no register change, no second signing path.** `issued_artifacts` is untouched and
still append-only; `license_current_artifact` still moves in the same transaction that appends;
`LicenseIssuer` is still the only thing that signs. L5.0 had already built every mutation this stage needed.

### 45.1 ⭐⭐ The stage began as a feature and turned out to be a REPAIR

The plan was "add re-issue and a reason dictionary". Reading the code first found that
`IssueRequest.Reason` already carried the contract in a comment —

> *One of `IssueReasons`. ⛔ Chosen by the operator, never inferred from a diff.*

— while the only production path did exactly the opposite:

```csharp
var reason = _register.GetArtifacts(SelectedLicense.LicenseId).Count == 0
    ? IssueReasons.Initial
    : IssueReasons.Renewal;                    // ShellViewModel, before L5.3
```

⚠ **Two of the four vocabulary values were unreachable by any code path.** `terms-change` and
`reissue-lost` were declared, documented and never written — a `grep` for `IssueReasons.` found them only
in tests. Every re-issue was filed as a *renewal* whether or not an expiry had ever moved, and
`issued_artifacts.reason` is append-only, so every one of those rows is wrong permanently.

⭐ That reframes the stage: the reason picker is not a convenience, it is what makes the column mean
anything. It also explains why the picker could not simply default to something sensible — a default
reproduces the inference with a control in front of it (§45.6 records the injection that proved this).

### 45.2 ⭐⭐ The governing rule: refuse what can be DISPROVED, never what cannot be judged

The four reasons are not alike, and treating them alike was the trap to avoid.

| Reason | What it asserts | Can the register check it? | So |
|---|---|---|---|
| `initial` | there is no earlier artifact | **yes**, exactly | not offered at all after the first issue; not a choice before it (D‑2) |
| `renewal` | the expiry moved | **yes** — against the signed payload | refused when the expiry is provably unchanged |
| `terms-change` | something other than the expiry moved | **yes** | refused when nothing else provably differs |
| `reissue-lost` | the customer lost their file | **no. Never.** | ⛔ never refused — only steered (D‑6) |

⛔ **A rule that refused `reissue-lost` would be guessing about a person**, which is the habit this stage
exists to remove. ⭐ The boundary is therefore stated positively (`CLAUDE.md` UI rule 11): a reason is
refused **only** when the register can produce the artifact that contradicts it.

⚠⚠ **The corollary that is easy to get backwards: `CanCompare == false` means UNKNOWN, not UNCHANGED.**
When the stored payload will not parse, every refusal is switched off rather than on. A payload the parser
refuses is precisely the artifact a support call is about, and blocking a re-issue there would turn a
display problem into an operational one on the day the operator can least afford it.

### 45.3 ⭐ Where the judgement lives, and where it deliberately does not

`IssueReasonPolicy` (App-side service), consuming `IssueChange` (pure comparison).

- ⛔ **Not in `LicenseRegister`.** The register records what it is told, verbatim and append-only. Teaching
  the one component whose job is *"never lose what happened"* to also hold an opinion about it is how a
  history stops being a history. It would also have meant parsing a previous payload inside the writer.
- ⛔ **Not in `IssuingWorkflow`.** The workflow signs and records; tests legitimately issue with arbitrary
  reason text to exercise storage, and several existing ones do. The judgement belongs where the
  operator's choice is made.
- ⭐ **`IssueChange` compares on the SIGNED WIRE FORM**, through `LicensePayload.FormatTimestamp` — the same
  function the issuer's truncation and the register's storage already go through. Two values that render
  to the same timestamp produce byte-identical payloads, so a sub-second difference is not a change any
  artifact could ever show. ⛔ A second rounding rule here is how the two would drift.
- ⭐ **The diff is taken against `GetCurrentArtifact` — the POINTER, never `Artifacts[0]`.** §39.2's
  authority, and §44.4's lesson about what that costs to prove (see §45.6).

⭐ **The licensee counts as a term.** It is signed into the artifact (D6), so a re-issue after a company is
renamed genuinely changes what the customer holds, though no date and no number moved.

### 45.4 D‑6 — the steer away from a re-issue nobody needs

Choosing *"Re-issue — lost file"* reveals a card naming **"Export this issue…"**, the action that re-sends
the delivered artifact byte for byte without a new `iat`. ⛔ **Advice, not a block:** the Issue action stays
enabled and a chosen re-issue is recorded normally. A test asserts both halves — that the advice appears and
names the cheaper action, and that it never takes the decision away.

### 45.5 Decisions taken during implementation

1. **The optional note (D‑4) rides on `audit_log.note`, appended to the generated summary.** No column, no
   model, nothing extra to back up or migrate. ⚠ Appended rather than instead of: the summary is what lets
   the audit answer *"on what terms?"* without joining anything. It is cleared after each issue — a remark
   is about ONE artifact, and carrying it forward would attach last week's ticket number to this week's row.
2. **`ReasonText`, shaped exactly like `VerdictText`** — one mapping, two consumers (the picker and the
   history). ⭐ An unrecognised value is shown **verbatim**, not as "unknown": the column can only grow, so a
   register written by a later version must stay readable here, and the raw value is always more
   informative than our word for not recognising it.
3. **`IssueReasonOption` keeps the persisted value and the label as separate fields**, so a reworded caption
   can never start writing a fifth value into an append-only column.
4. ⭐ **The picker is refreshed on the same path as the history** (`OnSelectedLicenseChanged`), so
   *"this licence has been issued"* cannot be true for one and false for the other.
5. ⚠ **The diff is measured again at the moment of issuing**, not reused from when the choices were built —
   the operator presses **Save terms** in between, which is exactly what a renewal requires.
6. **D‑8 — `Seats` lost its literal `Width="80"` and gained NO number in its place.** `Tokens.axaml` has no
   width role for a small numeric input and one was not invented for a single field, so the three form
   fields became equal columns and the control takes its width from its context (`CLAUDE.md` UI rule 10).
   ⏭ The **other** `Width="80"`, on the licences-list Seats column, is deliberately left: it is one of five
   sibling literal column widths (130 / 80 / 90 / 170 / 150) and changing one alone would make the row
   inconsistent. That set is a column-width question of its own, not this stage's.

### 45.6 ⚠⚠ Every guard proved by injected defect — 23 injections, 23 reds, and TWO of them found real holes

A harness applied each defect, built, ran the specific guard, and reverted. ⭐ It was worth writing: two
guards that looked fine **stayed green under their own injection**, which proves nothing at all (#378).

| The hole | Why it was vacuous | Repair |
|---|---|---|
| *"issuing without choosing a reason is refused"* | The injection hard-coded `renewal`, which the **policy** then refused — so the test was passing on a different guard's behaviour, and a default would have shipped undetected | A second test injects `reissue-lost`, the one reason the policy never refuses. Now nothing but the absence of a default can keep it green |
| *"the diff follows the pointer, not the newest row"* | It called `IssueChange.Between` with an artifact **it had fetched itself**, so repointing the shell's own lookup could not affect it — the same shape §44.4 recorded, one layer out | Rewritten to drive the **shell**, which is the code that has to choose the artifact |

⚠ A third injection could not be run as written: swapping the picker template's `x:DataType` was rejected at
**compile** time, because Avalonia's compiled bindings resolve `{Binding Label}` against the declared type.
⭐ That is a stronger outcome than a red test — the #370 shape cannot ship in a compiled template — but it
is not a proof of the guard, so the guard was proved with an injection that compiles (binding `Value`, the
persisted vocabulary, instead of `Label`). Filed as gotcha #380.

Injections that went red as intended, in one line: a supplied default reason · the operator's choice
ignored · renewal not required to be a renewal · terms-change not required to change terms · reissue-lost
refused · an unreadable payload read as "unchanged" · licensee dropped from the diff · seats dropped from
the diff · the diff taken against the newest row · timestamps compared raw · the note not reaching the
workflow · the note replacing the summary · the note surviving into the next issue · the history showing
the raw value (twice: view model and realised row) · a fifth vocabulary value · a reason losing its
explanation · an unknown reason mapped to "Unknown" · the picker rendering the persisted value · the picker
enabled before the first issue · the advice never appearing · the advice not naming the export · `Seats`
getting its literal width back.

### 45.7 ⚠ Two test-infrastructure findings, both pre-existing, both now gotchas

1. **#379 — `LicensesViewTests` identified its subjects as "the first `ComboBox` in the window".** Adding a
   dropdown to the CUSTOMERS view failed two tests in the LICENCES view, neither of them about the new
   control. The three filter dropdowns are now **named** and selected by name, exactly as
   `CustomerLicenses` and `ArtifactHistory` already were. ⛔ Not a weakening: the guard keeps its full
   strength and stops depending on the window's inventory.
2. **#380 — a `ComboBox` popup is not a descendant of its window in a headless test**, so an assertion over
   the opened dropdown's items sweeps zero elements and passes vacuously. The picker guard reads the
   selection box instead, one choice at a time, which exercises the same `ItemTemplate`.

⚠ A third, smaller one, recorded in the test rather than as a gotcha: `ManagerFixture.SaveLicense` stores
`NotBefore` at 09:00 while the FORM stores a whole UTC day, so a fixture-built licence differs from its own
form round-trip in the start date. A diff test read that as a terms change it had not set up. Every diff
test now normalises through **Save terms** once, which is also what an operator's licence always went
through.

### 45.8 ⚠ The Release run and the documented race — what was and was not proved

The full `EmberTern` suite in **Release** failed **`TabStripPresentationTests`** twice, then went green
**9 087/9 087 with no change**. Alongside: it passes in isolation, and Debug ran 9 087/9 087 green
throughout. §42.4 records this exact test as this machine's usual victim of the documented Avalonia
headless session race.

⚠⚠ **Stated honestly: the STACK was not captured** — the failure did not recur once output was being
written to a file. `CLAUDE.md` identifies that race by its stack and not by the test's name, so the
attribution here rests on the surrounding evidence (green in isolation, green in Debug, green on re-run
without a change, and §42.4's prior measurement of the same test) rather than on the signature itself.
⛔ Nothing in this stage touches a product file.

### 45.9 ⏭ What L5.3 deliberately did NOT do

⛔ No bulk selection and no batch renewal (L5.4). ⛔ No backup / JSONL / restore (L5.5). ⛔ No e-mail (L6).
⛔ No production key ceremony (L7). ⛔ `FluentBridge`'s missing `Calendar*` keys untouched — product work,
both applications at once. ⛔ `Icon.Name` untouched. ⛔ The License Manager is still English-only (§40.5);
the reason vocabulary went through the existing text mechanism and started no localization stage. ⛔ No new
control type and no new token: the picker is a `ComboBox` on the existing `field-label` rhythm, and
`Tokens.axaml` was not touched.

⚠ Carried forward unresolved and unchanged: the `Calendar*` flyout is not repinned in `FluentBridge`;
`Icon.Name` does not exist; the License Manager is English-only; the licences-list `Seats` column still
carries a literal width, as one of five siblings (§45.5 point 6).

### 45.10 ⏭ Where the branch stands after L5.3

`feat/licensing-system` now carries, in order: `efbe180` (L5.0) · `2531576` (L5.1 + both QA rounds) ·
`e3c746c` (L5.2) · the L5.3 closing commit. ⭐ **`origin` is at `2531576`**, so the last two commits are
local only — the user holds the push and it covers both at once.

⏭ **Next: L5.4** (bulk selection and batch renewal), which is the first consumer of `IssueBatch` from a
surface, then **L5.5** (backup / JSONL / restore). ⛔ Neither starts without the user's go-ahead, and L5.4
begins in a NEW session (one milestone per session).

⭐ **What L5.4 inherits and must not rebuild:** `IssuingWorkflow.IssueBatch` already signs everything before
recording anything and commits the whole operation as ONE transaction (§39.1); `IssueRequest` already
carries `Reason` **and** `TermsChanged`, both of which L5.3 has now given real meaning on the single path.
⚠ `IssueReasonPolicy` was written against ONE licence — a batch asks the same question per licence, so the
open design question for L5.4 is whether one reason covers a whole batch or each licence answers for
itself. ⛔ Do not decide it by making the policy lenient.

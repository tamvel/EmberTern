# EmberTern Licensing System — design document

**📋 STATUS: DESIGN ONLY — NOTHING IMPLEMENTED, NOTHING RATIFIED.** This document is the analysis the
user asked for before any code is written. Branch `feat/licensing-system`, cut from `master` at
`2c3da45`. No production code is touched by this commit.

**Start here:** §1 (Executive Summary) → §3 (the option comparison that produces the recommendation) →
**§33 (the open decisions, Q1–Q14)**. Everything between §4 and §32 is the detail behind those three.

⚠ **This document contradicts three premises in the brief.** They are argued, not smuggled: the backend
is **not** in V1 (§3, §31), **Firebase is dropped entirely** (§3.1, §10), and the license binds to a
**random InstallationId rather than a hardware fingerprint** (§15). The brief invited exactly this
("nie bój się go zakwestionować"), and each is a ratifiable decision in §33, not a fait accompli.

⚠ **Every free-tier figure in this document is dated 2026-08-15 and MUST be re-verified before
implementation.** They are quoted for the comparison, not depended upon — see §24.0 for the principle
that keeps the architecture safe when the numbers move.

---

## 1. Executive Summary

### 1.1 The recommendation in one paragraph

**Ship V1 with no backend at all.** A license is a small, cryptographically signed, self-describing text
artifact issued by an offline desktop **License Manager** that holds the only signing key. EmberTern
verifies it locally, in-process, with zero network code in the licensing subsystem — which is the
strongest possible guarantee of the offline-first requirement (§2 of the brief): not "we don't call the
network", but *"there is no network call to make"*. The activation UX is **paste the license or drop the
file → done**. Seats are contractual in V1 and technically enforceable in V2 **without reissuing a single
license**, because the format carries the fields from day one. If and only if seat enforcement or short
typed keys prove necessary in practice, V2 adds a **Cloudflare Worker + D1** activation service — same
format, same trusted-key table, same client verifier. **Firebase is not part of the recommendation in any
variant.**

### 1.2 The five findings that drive it

1. ⭐ **Firestore cannot issue a license, so Variant A cannot exist as described.** Security Rules have no
   cryptography — they cannot produce a signature. A Firestore-only design can only *distribute*
   licenses that something else already signed, which means the signing tool is the real backend and
   Firestore is a file host with a quota. See §3.1 / §10.
2. ⭐ **There is no Firebase client SDK for .NET desktop, and Firebase App Check has no Windows-desktop
   attestation provider.** `Google.Cloud.Firestore` is the *server* SDK: it authenticates with a service
   account, which must never ship in a distributed client. Gemini's App Check suggestion (§17 of the
   brief) does not apply to this platform at all — its providers are web/reCAPTCHA, Android/Play
   Integrity and Apple/DeviceCheck, plus a *custom* provider that itself requires a trusted server. See
   §10.2, §21.6.
3. ⭐ **The scale is three orders of magnitude below the free tier, in the wrong direction to justify a
   service.** EmberTern is a niche tool for Firebird developers. A realistic first year is tens to low
   hundreds of activations — i.e. a few hundred requests *per year* against a budget of 100 000 *per
   day*. Standing up, securing, monitoring and keeping alive a public API for 5–10 years to serve
   ~1 request/day is the expensive decision, not the cheap one. The cost that matters here is
   **maintenance and blast radius, not hosting.**
4. ⭐ **The backend's only irreplaceable capability is seat enforcement.** Everything else the brief
   wants from it — activation, renewal, offline activation, revocation-on-next-issue, edition/feature
   changes — is achievable with a signed file and a desktop tool. So the whole backend question reduces
   to one business question: *is "5 seats" a contractual number or a technically enforced one?* That is
   **Q1**, and it is the single most consequential decision in this document.
5. ⭐ **Answering "yes" to Q1 later costs nothing, if the format is designed for it now.** The payload
   carries `seats`, `seatPolicy` and an optional `iid` (InstallationId) from V1. A V1 license simply has
   no `iid` and is therefore unbound; a V2 license has one and is bound. Both verify in the *same* client
   code, shipped in V1. This is the property that makes deferring the backend safe rather than merely
   cheap.

### 1.3 What the user gets, in the brief's own terms

| The brief asked for | V1 delivers | Notes |
|---|---|---|
| Offline-first, strict | ✅ absolutely — no network code exists | §25 |
| No internet on normal startup | ✅ by construction | §25 |
| First activation = enter key → done | ✅ paste/drop → done | but the "key" is ~560 chars, not `XXXX-XXXX` — §14.5, **Q2** |
| Signed, versioned, offline-verifiable license | ✅ | §13, §14 |
| Tamper-proof against date/customer/edition edits | ✅ | §22 |
| Private key never in the client | ✅ enforced by a build guard test | §19.4 |
| Editions + features, extensible | ✅ features are the gate, edition is a label | §9.3 |
| Seat limits 1/2/5/… | ⚠ **contractual only in V1**, enforced in V2 | **Q1** |
| Separate License Manager desktop app | ✅ | §17 |
| Group renewal, blocking, history, export | ✅ | §17.3 |
| Offline activation for internet-less customers | ✅ — in V1 it is the *only* mode, so the problem vanishes | §8 |
| Deactivation / seat release | ⚠ admin-side bookkeeping in V1; technical in V2 | §20 |
| Zero runaway-cost risk | ✅ €0, no card, no account, no vendor | §23 |
| Works in 10 years with no backend alive | ✅ by construction | §26.4 |

### 1.4 What V1 deliberately does not do

Blocking a license already in the field. Counting installations. Short typed keys. Self-service renewal
without the admin. Telemetry of any kind. Stopping a determined attacker who patches the binary (§21.5 —
this is out of scope on purpose, and no amount of engineering changes that).

---

## 2. Recommended architecture

### 2.1 Components

```
┌───────────────────────────────────────────────────────────────────────┐
│  ADMIN MACHINE (offline, single operator)                             │
│                                                                       │
│   EmberTern License Manager  (Avalonia desktop, MVVM, DI)             │
│     ├── licenses.db            SQLite — the register of record        │
│     ├── keystore.etkeys        AES-256-GCM under a passphrase         │
│     │      └── ROOT private key  ⛔ never leaves this file            │
│     └── EmberTern.Licensing.Issuing   (signing — never shipped)       │
└───────────────────────────────────────────────────────────────────────┘
                                    │
                    signed license artifact (.etlic / a text token)
                    delivered by e-mail, portal, USB stick, anything
                                    │
                                    ▼
┌───────────────────────────────────────────────────────────────────────┐
│  CUSTOMER MACHINE                                                     │
│                                                                       │
│   EmberTern.exe                                                       │
│     ├── EmberTern.Licensing   (verify only — PUBLIC keys only)        │
│     │     └── TrustedKeys: append-only table  kid → pubkey + alg      │
│     └── %APPDATA%\EmberTern\                                          │
│           ├── license.etlic       the artifact, verbatim              │
│           ├── installation.id     random 128-bit id, first run        │
│           └── settings.dat        ← clock high-water mark lives here  │
└───────────────────────────────────────────────────────────────────────┘

              NO NETWORK ANYWHERE IN THIS DIAGRAM.
```

### 2.2 The four rules that make it work

1. ⭐ **The private key exists in exactly one file, on exactly one machine, and the code that can use it
   exists in exactly one assembly that the client does not reference.** Enforced by a guard test
   (§19.4), in the style this project already uses for `CharsetGuardSeamTests` and
   `FluentBridge_ContainsNoLocalValues`.
2. ⭐ **The client verifies; it never decides.** Verification is a pure function: bytes in, a
   `LicenseVerdict` out. One owner, one call site, no branch anywhere else in the app asks "is the
   license OK?" — they ask the resolved verdict.
3. ⭐ **Entitlements gate behaviour; edition is a display label.** An unknown edition string is harmless;
   an unknown feature string is ignored. This is what makes a license issued in 2031 readable by a client
   built in 2026 (§26).
4. ⭐ **The signature covers the encoded bytes, never a re-parsed object.** No canonicalisation, no
   round-trip, no JSON-ordering dependency. This is the JWT lesson and it is non-negotiable (§13.4).

### 2.3 How V2 attaches without breaking anything

```
V1 license:  { ..., "seats": 5, "seatPolicy": "contractual", "iid": null   }   ← unbound
V2 license:  { ..., "seats": 5, "seatPolicy": "bound",       "iid": "…"    }   ← bound to one install
```

Same envelope, same `kid` table, same verifier, same client build. The V2 backend is a **new issuer**, not
a new system. The client change needed to *accept* a V2 license is **zero** — it is written in V1.

---

## 3. Comparison of all considered variants

Six variants were considered, not the three in the brief — B was split because "Worker + Firestore" and
"Worker + D1" differ enough to be separate decisions, and D (static distribution) is new.

| | **A** Firestore direct | **B1** Worker + Firestore | **B2** Worker + D1 | **C** No backend ⭐ | **D** C + static pull | **E** Managed-offline lease |
|---|---|---|---|---|---|---|
| Can issue a *signed* license | ❌ **no** — rules have no crypto | ✅ | ✅ | ✅ (offline tool) | ✅ | ✅ |
| Works offline-first strictly | ⚠ yes but pointless | ✅ | ✅ | ✅✅ by construction | ✅ | ❌ violates §21 |
| Short typed key possible | ⚠ | ✅ | ✅ | ❌ (§14.5) | ❌ | ✅ |
| Seat enforcement | ❌ forgeable (§10.4) | ✅ | ✅ | ❌ contractual | ❌ contractual | ✅ |
| Self-service renewal | ⚠ | ✅ | ✅ | ❌ admin sends a file | ✅ | ✅ |
| Secrets held by a third party | service acct in client ☠ | 2 clouds, 2 secrets | 1 cloud, 1 secret | **none** | none | 1–2 |
| .NET client SDK exists | ❌ hand-rolled REST | n/a | n/a | n/a | n/a | n/a |
| Runaway-cost risk | none (Spark denies) | none (no card) | none (no card) | **none** | none | none |
| Availability risk on the product's critical path | none | none | none | **none** | none | ❌ **yes** |
| Code to maintain for 10 yrs | client REST + rules | Worker + rules + 2 SDK surfaces | Worker + SQL | **desktop tool only** | + an upload step | Worker + client scheduler |
| Blast radius if backend is compromised | licenses editable | signing key stolen | signing key stolen | **n/a** | n/a | signing key stolen |
| Survives the vendor disappearing | ⚠ | ⚠ | ⚠ | ✅✅ | ✅ (degrades to C) | ❌ product stops |
| **Verdict** | **rejected** | **rejected** | deferred to V2 | ⭐ **V1** | optional V1.5 | rejected for now |

### 3.1 Why A (Firestore + Security Rules) is rejected

Not because `allow read: if true` is distasteful — because the variant cannot do the job.

- **Rules cannot sign.** Firestore Security Rules are a boolean expression language over the request and
  the document. There is no `crypto.sign`. So the license Firestore hands out must already be signed by
  something else — and that something else is the License Manager, i.e. Variant C with a paid file host
  bolted on.
- **Rules therefore cannot bind a license to a device**, because binding requires signing the
  InstallationId *at activation time*, which is after issuance.
- **The client would need service-account credentials or hand-rolled REST.** `Google.Cloud.Firestore` is
  the server SDK (§10.2). The only client-shaped path is Firebase Auth anonymous sign-in + the Firestore
  REST API, hand-written, maintained by us, for a decade.
- **An anonymously readable Firestore is a free denial-of-service on our own customers.** With no billing
  attached, exceeding the daily read quota does not produce a bill — it produces *denied reads*, for
  everyone, until UTC midnight. Spark has no rate limiting and no WAF. One script can spend our 50 000
  daily reads in minutes and block every legitimate activation that day, at zero cost to the attacker.
  Rules can restrict *who*, but anonymous auth is free and unlimited, so "who" is anybody.
- **Seat counting in rules is forgeable** — see §10.4 for the full argument, including why the
  `increment(1)`-only trick does not survive a client that simply deletes its own activation document.

### 3.2 Why B1 (Worker + Firestore) is rejected in favour of B2 (Worker + D1)

If a Worker is fronting the data store anyway, Firestore contributes nothing D1 does not, and costs:

- a **second cloud account** and a second free-tier ToS to track for 10 years;
- a **second private key** — a Google service-account key, stored as a Worker secret, used to mint RS256
  JWTs for OAuth2 token exchange on every cold path;
- **two subrequests per activation** (token endpoint + Firestore REST) against the Free plan's
  per-request subrequest budget, plus cross-cloud latency;
- no SQL, so "which licenses expire in the next 30 days" becomes application code.

D1 is SQLite. The License Manager's local store is SQLite. The schemas can literally be the same file
shape. That is a real longevity property, not a coincidence to be clever about.

### 3.3 Why C wins V1

Every requirement in the brief's own priority order:

1. **Security** — the attack surface is one file on one machine. There is no public endpoint, no secret
   in anyone else's cloud, no token, no rate limit to tune, no dependency whose compromise mints
   licenses. The signing key never touches a network-connected process.
2. **No runaway cost** — €0, and not "€0 because a quota stops it", but €0 because nothing is running.
3. **Offline-first** — not a policy, a fact about the code.
4. **User convenience** — drop a file / paste a token. Compared to a typed short key this is *worse to
   describe* and *better to do*: no typos, no "the key is case-sensitive", no server outage on the one
   day the customer is installing.
5. **10-year evolvability** — the format, not a service, is the long-lived asset. §26.
6. **Maintenance simplicity** — one desktop app, no operations.

### 3.4 Why D (static pull-renewal) is worth having, and why it is not in V1

The one real friction in C is renewal: the admin must send a file every year. D removes it for **zero
backend code and zero secrets**: the License Manager uploads the signed artifact to static hosting as
`…/l/<licenseId>.etlic`; EmberTern, only when the user clicks *Check for renewal* (or, with explicit
opt-in, within N days of expiry), issues one `GET` for a static file. If it verifies, is for the same
`lid`, and has a later `iat`, it replaces the local copy.

Why it is safe: there is no compute, no database, no secret, and no write path. The URL is a bearer
artifact protected by 128 bits of `lid` entropy. Worst case someone downloads signed licenses they
already possess. Static hosting on Cloudflare Pages / R2 / GitHub Pages is free and has no per-request
compute bill.

Why not V1: it introduces the first line of network code and the first privacy question (§28.6), and
until there are enough customers for annual renewal to be a burden, it is unbuilt code. It is scoped as
**L7** (§30) and is a strictly additive change.

### 3.5 Why E (managed offline / lease) is rejected — for now

The brief already prefers strict offline; this documents the consequence so the decision is informed.

A lease (`offlineAllowedUntil`, refreshed on contact) is the *only* mechanism that makes revocation
effective against a machine that never connects. It buys: real revocation, real seat re-counting, real
theft response. It costs: **the product stops working when a service we do not control is unreachable**
— which moves the backend onto the critical path of every customer's daily work and inverts finding #3
above. For a developer tool whose users are frequently on locked-down corporate networks and VPNs, that
is a support burden out of all proportion to the piracy it prevents. **Rejected. Revisit only if a
concrete incident justifies it** — and note that the format supports it additively: a future
`offlineUntil` field ignored by old clients, honoured by new ones.

---

## 4. Architecture diagram (recommended, V1)

```
   ADMIN                                                CUSTOMER
   ─────                                                ────────

 ┌─────────────────────────────┐
 │ License Manager (Avalonia)  │
 │                             │
 │  1. create customer         │
 │  2. create license          │
 │     (edition, features,     │
 │      seats, dates)          │
 │  3. SIGN  ──────────────┐   │
 │                         │   │
 │  ┌──────────────────────▼─┐ │
 │  │ keystore.etkeys        │ │
 │  │  AES-256-GCM /         │ │
 │  │  PBKDF2 passphrase     │ │
 │  │   • ROOT private key   │ │
 │  └────────────────────────┘ │
 │                             │
 │  4. store artifact  ─────┐  │
 │  ┌───────────────────────▼┐ │
 │  │ licenses.db (SQLite)   │ │      ETL1.<payload>.<sig>
 │  │  customers             │ │  ┌──────────────────────────┐
 │  │  licenses              │ │  │  e-mail / portal / USB   │
 │  │  issued_artifacts  ────┼─┼──►  (any channel, it is     │
 │  │  audit_log             │ │  │   integrity-protected)   │
 │  └────────────────────────┘ │  └────────────┬─────────────┘
 └─────────────────────────────┘               │
                                               ▼
                                 ┌──────────────────────────────┐
                                 │ EmberTern.exe                │
                                 │                              │
                                 │  LicenseService (ONE owner)  │
                                 │    ├ read %APPDATA%\...      │
                                 │    ├ EmberTern.Licensing     │
                                 │    │   • parse envelope      │
                                 │    │   • select key by kid   │
                                 │    │   • verify signature    │
                                 │    │   • check nbf/exp       │
                                 │    │   • check iid (if any)  │
                                 │    │   • check clock h-mark  │
                                 │    └ LicenseVerdict          │
                                 │         │                    │
                                 │         ├ Valid → app runs   │
                                 │         ├ Grace → banner     │
                                 │         ├ Expired → gate     │
                                 │         └ Invalid → gate     │
                                 │                              │
                                 │  TrustedKeys (PUBLIC only)   │
                                 │    R1 → pubkey, ES256, …     │
                                 └──────────────────────────────┘
```

---

## 5. Activation diagram (V1)

```
User receives license.etlic (or the token text)
             │
             ▼
   ┌─────────────────────────────────────────┐
   │  Activation window                      │
   │  ┌───────────────────────────────────┐  │
   │  │  Drop the license file here       │  │   ← drag & drop target
   │  │        — or —                     │  │
   │  │  [ paste the license text …    ]  │  │   ← multiline, monospace
   │  └───────────────────────────────────┘  │
   │                        [ Activate ]     │
   └─────────────────────────────────────────┘
             │
             ▼
   parse envelope  ── malformed ──► "This does not look like an
             │                       EmberTern license." (localized)
             ▼
   select key by kid ── unknown ──► "Issued for a newer version of
             │                       EmberTern. Please update."
             ▼
   verify signature ── fails ─────► "The license could not be verified.
             │                       It may have been modified." [Copy details]
             ▼
   check prod == EmberTern ─ no ──► "This license is for a different product."
             │
             ▼
   check nbf / exp ── expired ────► "This license expired on <date>."
             │                       (offer [Contact us] — mailto, no network)
             ▼
   check iid (only if present) ───► V2 only; absent in V1
             │
             ▼
   WRITE %APPDATA%\EmberTern\license.etlic   (atomic: temp + replace)
             │
             ▼
   ⭐ RE-READ AND RE-VERIFY FROM DISK       ← never trust the in-memory result
             │
             ▼
   "Licensed to ACME Sp. z o.o. — Professional, 5 seats, until 2027-08-15"
             │
             ▼
   EmberTern runs.  No network was contacted at any point.
```

⭐ **The re-read step is not paranoia — it is Architecture rule 11.** If the write half-succeeded, the
user must learn now, at the activation screen with the artifact still in the clipboard, not at the next
launch with the source gone.

---

## 6. First-run diagram (every launch, V1)

```
EmberTern starts
      │
      ▼
read %APPDATA%\EmberTern\license.etlic
      │
      ├── file absent ──────────────────────────► state: Unlicensed
      │                                            → Activation window
      │                                            → app features gated (§28.3)
      ▼
verify signature (kid → public key → algorithm)
      │
      ├── invalid ──────────────────────────────► state: Invalid
      │                                            ⛔ file is NOT deleted, NOT moved
      │                                            → Activation window + reason
      ▼
read clock high-water mark from settings.dat
      │
      ├── now < highWater − 48h ────────────────► warn; use effectiveNow = highWater
      │                                            (never blocks — §22.6)
      ▼
check nbf ≤ effectiveNow
      │
      ├── not yet valid ────────────────────────► state: NotYetValid
      ▼
check exp
      │
      ├── effectiveNow ≤ exp ───────────────────► state: Valid
      │        └── exp − now ≤ 30 d ────────────►   + ExpiringSoon banner (dismissible)
      │
      ├── exp < effectiveNow ≤ exp + 14 d ──────► state: Grace
      │                                            full function + persistent banner
      │
      └── effectiveNow > exp + 14 d ────────────► state: Expired
                                                   app opens, editor/files/export work,
                                                   ⛔ no NEW database connections
      │
      ▼
check maint (perpetual-fallback, §14.4) against AppInfo.ReleaseDate
      │
      └── build newer than maint ───────────────► state: VersionNotCovered
                                                   → "this build requires maintenance
                                                      until <date>; install <version>"
      ▼
write clock high-water mark = max(highWater, now)
      ▼
APP RUNS.  ⛔ ZERO NETWORK CALLS ON THIS PATH — IN V1 THERE IS NO CODE TO MAKE ONE.
```

**Budget:** the whole path is one small file read, one ECDSA/Ed25519 verification (~0.1 ms) and one
settings write. Target **< 5 ms**, measured, and it must not be on the UI thread's critical path before
the window shows (the project already cares about startup cost — `EMBERTERN_PERF_DIAG=1`).

---

## 7. Renewal diagram

### 7.1 V1 — admin-driven (the only mode)

```
   ADMIN                                            CUSTOMER
   ─────                                            ────────
License Manager
  │
  ├─ filter: "expires within 60 days"
  ├─ select 23 licenses
  ├─ [ Extend to … 2028-08-15 ]
  │     └─ for each: new artifact, new iat,
  │        SAME lid, SAME cid, exp = new date
  ├─ audit_log gets 23 rows
  ├─ [ Export selected ] → 23 × .etlic + a CSV of e-mails
  │
  └─ mail merge ──────────────────────────────────►  receives the new file
                                                          │
                                                          ▼
                                                   Settings ▸ License
                                                   ▸ [ Update license ]
                                                          │
                                                   drop / paste → verified
                                                          │
                                                   replaces license.etlic
                                                   (⭐ only if iat is NEWER
                                                    and lid matches — §22.8)
```

### 7.2 V1.5 — static pull (Variant D, optional)

```
Admin: [ Publish ] → uploads <licenseId>.etlic to static hosting
                                             │
Customer: Settings ▸ License ▸ [ Check for renewal ]     ← user-initiated
                                             │
                                   GET https://…/l/<lid>.etlic
                                             │
                        ┌────────────────────┴──────────────────┐
                        │ 404 / offline / any error             │
                        │   → "No update available."            │
                        │   ⛔ never an error dialog, never      │
                        │      blocks anything                  │
                        └────────────────────┬──────────────────┘
                                             │ 200
                                   verify signature, lid, iat > local iat
                                             │
                                   replace license.etlic
```

### 7.3 V2 — self-service (deferred)

`POST /v1/renew { lid, iid }` → the Worker re-checks status and seats and returns a freshly signed,
bound license. Identical client-side handling to 7.2.

---

## 8. Offline activation diagram

⭐ **In V1 this section is trivial, because V1's *only* activation mode is offline.** The brief's §19
concern — that ordinary users must not see an "Activate offline" option — **does not arise**: there is
one path and it is the offline one. This is a genuine simplification, not a dodge.

The diagram below is therefore the **V2** design, retained here so V2 does not invent a second licensing
system (the brief's explicit requirement).

```
CUSTOMER (no internet)                         ADMIN
──────────────────────                         ─────
EmberTern
  Settings ▸ License ▸ Advanced
  ▸ [ Create activation request ]     ← service mode, not on the main path
        │
        ├ writes request.etreq:
        │    { key, iid, fingerprint[], product, appVersion, createdAt }
        │    ⛔ unsigned — it carries no authority, it is an ORDER FORM
        │
        └────── e-mail / USB ───────────────────►  License Manager
                                                     ▸ Import activation request
                                                     ▸ validates key, status, seats
                                                     ▸ records the activation row
                                                     ▸ SIGNS a license with iid set
                                                            │
      EmberTern  ◄──────── .etlic ────────────────────────────┘
        ▸ [ Import license ]
        ▸ ⭐ EXACTLY THE SAME verification path as online activation
        ▸ iid in the license must equal the local installation.id
```

⭐ **One model, two couriers.** The online path's HTTPS response body and the offline path's e-mailed
file are byte-for-byte the same artifact. There is no second format, no second verifier, no second
trust decision.

---

## 9. Data model

### 9.1 The license payload (what the client sees)

| Field | Type | Req. | Purpose / rules |
|---|---|---|---|
| `lv` | int | ✅ | License payload version. Client supports ≤ N; `> N` ⇒ refuse with *"update EmberTern"*. §26.1 |
| `kid` | string | ✅ | Key id. ⭐ **Selects both the public key AND the algorithm** from the client's table. §13.3 |
| `alg` | string | ✅ | Informational. **Cross-checked against the table, never used to select.** §13.3 |
| `lid` | string | ✅ | LicenseId — 128-bit, lowercase hex/ULID. Stable across renewals. The renewal correlation key. |
| `cid` | string | ✅ | CustomerId — stable across all licenses of one customer. |
| `prod` | string | ✅ | `"EmberTern"`. Guards against cross-product replay. |
| `ed` | string | ✅ | Edition — **a display label only**. Unknown values are harmless. §9.3 |
| `feat` | string[] | ✅ | Entitlements — **the only behavioural gate**. Unknown entries ignored. §9.3 |
| `seats` | int | ✅ | Contractual seat count (V1) / enforced (V2). Always displayed. |
| `seatPolicy` | string | ✅ | `contractual` \| `bound`. Tells the client whether `iid` is meaningful. |
| `iid` | string? | — | InstallationId this license is bound to. **Absent ⇒ unbound.** V2 only. §15 |
| `iat` | RFC3339 | ✅ | Issued at. ⭐ The **freshness ordering key** — a replaced license must have a later `iat`. §22.8 |
| `nbf` | RFC3339 | ✅ | Not before. |
| `exp` | RFC3339? | — | Expiry. **Absent ⇒ perpetual.** §14.4 |
| `maint` | RFC3339? | — | Perpetual-fallback: builds released after this date are not covered. §14.4 |
| `cust` | object | ✅ | `{ name (required), company? }`. **Displayed** — the social deterrent. §22.3 |
| `chain` | string[]? | — | 0 or 1 issuer certificate. Reserved for V2. §20.2 |

⛔ **`note` (uwagi), `email`, `first`, `last` are NOT in the payload.** Admin notes may contain internal
remarks; personal fields make a shared license file a personal-data leak and add nothing the client uses.
They live in the License Manager database (§18.2). **Q4** asks whether `email` should be included anyway.

### 9.2 What is deliberately *not* in the payload

Serial numbers, MAC addresses, disk ids, CPU ids, a hardware hash, a machine name, a user name, an IP,
any counter, any URL, any secret. A license is an **assertion about a customer's rights**, not a
description of a computer.

### 9.3 Edition vs Features — the rule that buys 10 years

```
edition = "professional"          ← a word on the About screen
features = ["debugger",           ← what the code actually asks
            "import.xlsx",
            "export.xlsx",
            "trace",
            "security-manager"]
```

⭐ **Code never asks `if (edition == "professional")`.** It asks
`_license.HasFeature(Features.Debugger)`. Consequences:

- adding an edition is **data**, not a release;
- a customer can be given one feature outside their edition without inventing an edition;
- an old client meeting a new edition string degrades to *"it shows an unfamiliar word"*, not
  *"it refuses to start"*;
- an old client meeting a new feature string ignores it — which is correct, because a feature it does
  not have cannot be gated by it.

Feature ids are **append-only, kebab-case, and never reused** — exactly the discipline
`EncryptionSchemes` already documents for persisted identifiers in this codebase.

### 9.4 Client-side runtime model

```csharp
// EmberTern.Licensing — pure, no Avalonia, no Firebird, no I/O
sealed record LicensePayload(...);                       // §9.1, immutable
sealed record TrustedKey(string Kid, SignatureAlgorithm Alg, byte[] PublicKey,
                         DateTimeOffset? RetiredAt, bool Revoked);
enum LicenseStatus { Unlicensed, Invalid, NotYetValid, Valid, Grace,
                     Expired, VersionNotCovered, WrongInstallation }
sealed record LicenseVerdict(LicenseStatus Status, LicensePayload? Payload,
                             MessageKey Reason, object[] ReasonArgs);
static class LicenseVerifier { static LicenseVerdict Verify(...); }   // ⭐ ONE entry point
```

⭐ `MessageKey` + args, resolved by App — this is the existing Core↔App localization contract (D‑3),
not a new one. **Architecture rule 12 applies in full: every one of these reasons is a `Strings.resx`
+ `Strings.pl.resx` pair, and the Phase-5 lesson applies — a reason is finished only when it has been
seen rendered in Polish, or pinned by a test that resolves it through the path the UI actually uses.**

---

## 10. Firestore model — *if Firebase were used* (it is not recommended)

Delivered because the brief asks for it; §10.4 is the finding that matters.

### 10.1 Collections

```
/customers/{customerId}          { name, company, email, createdAt }
/licenses/{licenseKey}           { customerId, lid, edition, features[], seats,
                                   notBefore, expiresAt, status, maxDevices,
                                   signedArtifact }              ← pre-signed elsewhere
/licenses/{licenseKey}/activations/{installationId}
                                 { fingerprint[], deviceName, activatedAt, lastSeenAt }
/audit/{autoId}                  { actor, action, target, at, details }
```

### 10.2 The .NET access problem (measured, not assumed)

`Google.Cloud.Firestore` — the only mature .NET Firestore library — is the **server** SDK. It
authenticates via Application Default Credentials, i.e. a service-account JSON key. Shipping that key in
EmberTern would hand every customer full read/write on the whole project, bypassing Security Rules
entirely (server SDKs are not subject to rules). **This alone disqualifies Variant A as usually
imagined.** The only client-shaped alternative is Firebase Auth anonymous sign-in plus hand-written calls
to the Firestore REST API — code we would own and maintain for a decade, replacing an SDK we do not.

### 10.3 Security Rules sketch

```javascript
rules_version = '2';
service cloud.firestore {
  match /databases/{db}/documents {

    match /licenses/{key} {
      // A license is a bearer document: knowing the key is the only credential.
      allow get:    if request.auth != null;         // anonymous auth
      allow list:   if false;                        // ⛔ never enumerable
      allow create, delete: if false;                // only the admin path may create
      allow update: if false;                        // ⛔ clients never touch the license doc

      match /activations/{iid} {
        allow get:    if request.auth != null;
        allow list:   if false;
        allow create: if request.auth != null
                      && !exists(/databases/$(db)/documents/licenses/$(key)/activations/$(iid))
                      && get(/databases/$(db)/documents/licenses/$(key)).data.status == 'active';
        allow update: if request.auth != null
                      && request.resource.data.diff(resource.data)
                             .affectedKeys().hasOnly(['lastSeenAt']);
        allow delete: if false;                      // ⛔ a client may not free its own seat
      }
    }

    match /audit/{doc}  { allow read, write: if false; }
    match /customers/{c}{ allow read, write: if false; }
  }
}
```

### 10.4 ⭐ Why these rules still cannot enforce seats

This is the finding, and it is not fixable by writing better rules:

1. **Rules cannot count.** There is no aggregation in Security Rules; you cannot express *"the number of
   documents under `activations/` is below `maxDevices`"*. The usual workaround is a counter field on the
   parent, guarded by `request.resource.data.count == resource.data.count + 1` — but the parent is
   `allow update: if false` for exactly the reason in the brief's §12, and opening it re-opens
   everything.
2. **Even with a counter, the client controls whether to report.** Nothing forces an installation to
   create its activation document. A modified client simply skips it and uses a license that reads
   `seats: 1` on ten machines. The counter measures *honest* installations only.
3. **Enumeration and quota exhaustion remain.** `allow list: if false` prevents enumeration of documents,
   but anonymous auth is free and unlimited, so key-guessing attempts are free to the attacker and each
   one costs *us* a read against a 50 000/day cap shared with every real customer.
4. **No rate limiting exists on Spark.** There is no per-IP throttle, no WAF, no Cloud Armor. The only
   throttle is the quota itself, and hitting it is the attacker's goal, not their obstacle.

**Conclusion: seat enforcement requires a trusted compute layer. Firestore + Rules is not one.**

---

## 11. Security Rules — verdict

**Not needed, because Firebase is not in the recommended architecture.** §10.3 is retained as the
reference implementation should Q3 be decided against this recommendation. If Firebase is retained for
some other reason (e.g. an existing project), the only defensible shape is: **Firestore reachable by
nothing except a Worker service account, all client rules `if false`** — i.e. the database is private
infrastructure and the Worker is the entire public surface. That is Variant B1, which §3.2 rejects on
complexity, not on security.

---

## 12. Worker API — the V2 design (deferred, not V1)

Recorded now so V1's format decisions are compatible with it.

### 12.1 Public surface — three endpoints, no authentication, hard-throttled

```
POST /v1/activate
  { key, iid, fingerprint: [h1..h6], product, appVersion, os }
  200 { license: "ETL1.…" }
  404 { error: "unknown-key" }          ← identical timing to 200 (§22.9)
  409 { error: "no-seats", seats: 5, used: 5 }
  410 { error: "blocked" }
  429 { error: "rate-limited", retryAfter }

POST /v1/renew
  { lid, iid }                          → same responses

GET  /v1/health                         → 200 {"ok":true}   (static, for monitoring)
```

⛔ **No admin endpoints on the public Worker.** The License Manager does not talk to the Worker at all —
it talks to **D1 directly through Cloudflare's D1 REST API**, using a scoped API token stored in the
admin's encrypted keystore. The blast radius of the public API is therefore bounded by what those three
endpoints can do, and none of them can change a license's terms.

### 12.2 Signing inside the Worker, without exposing the root key

⭐ **The Worker never holds the root key.** It holds an *issuer* key; the root key (offline, in the
License Manager) has signed a certificate binding that issuer key to a validity window. The client trusts
the root and validates a one-level chain (§20.2).

```
root private key      → offline, keystore.etkeys, never on a network-connected machine
issuer private key    → Cloudflare Worker secret (wrangler secret put ISSUER_KEY)
issuer certificate    → root-signed, embedded in every license as chain[0]
client                → ships the ROOT public key only
```

If the Worker is compromised: the attacker can mint licenses until the issuer certificate expires or a
client update revokes that issuer — **but the root key, and therefore every future issuer, is untouched.**
Compare with the naive design, where a Worker compromise is a full key compromise requiring a client
update, a new key, and reissuing every license in the field.

### 12.3 Abuse controls (in the order they should be built)

1. **The daily cap itself** — Workers Free stops running past the cap. No bill (§24.2).
2. **Per-key attempt counter in D1** — 10 failed attempts per key per hour ⇒ 429. This is the one that
   actually stops key brute-force, and it costs one row write.
3. **Per-IP token bucket** — Workers' native rate-limiting binding, or a KV/D1 counter. Coarse, cheap.
4. **A Cloudflare zone with a free WAF rate-limiting rule.** ⚠ Requires a **custom domain**;
   `*.workers.dev` subdomains are not behind zone WAF rules. This is the one item that costs money — a
   domain registration, ~€10–15/year, **not usage-based** (§23.3).
5. ⛔ **Not built:** nonce/challenge/request-signing. The brief says do not over-complicate, and it is
   right: a request-signing scheme whose key ships in the client is decoration, and one whose key does
   not requires the activation the scheme is protecting. **Firebase App Check is not applicable at all**
   (§21.6).

### 12.4 Answering the brief's §11 directly

> *Czy w naszym konkretnym przypadku Worker Free rzeczywiście daje nam praktycznie zerowe ryzyko kosztów?*

**Cost risk: yes, effectively zero** — Workers Free requires no payment method, and exceeding the daily
request cap results in requests being refused until the UTC reset, never in an invoice. There is no
overage-billing mode to accidentally enable.

**But "zero cost risk" is not "zero risk."** The same cap is a denial-of-service lever: an attacker who
burns 100 000 requests blocks every legitimate activation for the rest of the day, for free.
⭐ **This is acceptable in this architecture, and only in this architecture** — because activations are
rare and every already-activated customer is unaffected (they never contact the service). **In Variant E
(lease) the identical attack would stop the product for every customer.** The backend must stay off the
product's critical path; that property, not the request cap, is what makes the risk tolerable.

---

## 13. Ed25519 signature model

### 13.1 The audit's recommendation is right about the shape, and worth re-examining on the primitive

GPT Terra recommended Ed25519 with the private key outside the client. **The shape is correct and
adopted in full.** The primitive deserves one question the audit did not ask: *what does the verifier
cost us for ten years?*

### 13.2 The dependency argument

| | **Ed25519** | **ECDSA P-256 (IEEE P1363)** |
|---|---|---|
| In the .NET 9 BCL | ❌ **no** | ✅ `ECDsa.VerifyData(…, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)` |
| Client dependency needed | BouncyCastle (managed) or NSec (native libsodium) | **none** |
| Signature size | 64 B | 64 B |
| Verify cost | ~50 µs | ~100 µs (both irrelevant here) |
| Signing footguns | none (deterministic) | nonce reuse leaks the key — mitigated: signing is ours, low volume, platform RNG |
| Misuse surface | very low | low |

⚠ **The project-specific cost is concrete, not theoretical.** `Directory.Build.props` sets
`TreatWarningsAsErrors=true`, which escalates NuGet's `NU1902`/`NU1903`, so **a direct `PackageReference`
with a published advisory fails the build** (gotcha #278). Adding BouncyCastle to the *distributed
client* means that for the next decade, a BouncyCastle CVE breaks EmberTern's build until the version is
bumped — on the one code path we most want to be boring and stationary. A native dependency (NSec) is
worse: it adds a per-RID binary to a currently pure-managed client.

⚠ Verify against the actual target SDK before ratifying: **as of .NET 9 there is no BCL Ed25519.** If a
later .NET adds one, adopting it is a **non-event** — a new `kid` whose table entry names the new
algorithm. That is precisely what §13.3 buys.

**Recommendation (Q5): ECDSA P-256 + SHA-256, fixed 64-byte P1363 signatures, for V1.** Not because
Ed25519 is worse — it is a better primitive — but because *the client's job is verification only*, where
P-256 is beyond reproach, and because zero third-party crypto in the shipped binary is itself a security
property. **This contradicts the audit and the user should decide it explicitly.**

### 13.3 ⭐ The `kid` rule — the JWT lesson, applied

```
The client holds an append-only table:

   kid   algorithm            public key            retiredAt     revoked
   ───   ──────────────────   ───────────────────   ──────────    ───────
   R1    ECDSA-P256-SHA256    30 59 30 13 06 07…    (none)        false

VERIFICATION:
   1. read kid from the payload
   2. look it up in the table            ← unknown kid ⇒ REFUSE, do not guess
   3. THE TABLE ENTRY dictates the algorithm
   4. cross-check payload.alg == entry.algorithm   ⇒ mismatch = REFUSE
   5. verify

⛔ payload.alg is NEVER used to choose an algorithm or a key.
⛔ There is no "none" algorithm, and no code path where an empty/absent
   signature can be treated as valid.
⛔ There is no fallback to "try every key".
```

### 13.4 ⭐ The signing input — bytes, never objects

```
signingInput = ASCII("ETL1.") ‖ ASCII(base64url(payloadJsonUtf8))
signature    = Sign(rootOrIssuerPrivateKey, SHA256(signingInput))
token        = signingInput ‖ ASCII(".") ‖ base64url(signature)
```

- The magic is **inside** the signing input, so a token can never be replayed under a future envelope
  generation.
- The signature covers the **encoded** payload segment. The verifier verifies first and parses second.
  There is no canonical-JSON requirement, no key-ordering dependency, no risk that a parse/re-serialise
  round-trip changes the bytes. This directly serves Architecture rule 11.
- base64url is unpadded (`Base64Url` is in the .NET 9 BCL).

---

## 14. License file format

### 14.1 The artifact

```
ETL1.eyJsdiI6MSwia2lkIjoiUjEiLCJhbGciOiJFUzI1Ni1QMTM2MyIsImxpZCI6IjAxOTFm…
….ZmM0ZDciLCJjdXN0Ijp7Im5hbWUiOiJBQ01FIFNwLiB6IG8uby4ifX0.MEUCIQD8f2rK1nT…
```

One line, no whitespace, ASCII only. Stored as `license.etlic` (UTF-8, **no BOM** — the project already
has this rule for generated `.sql`, gotcha #178). The file's *content* is the whole license; the file is
a container of convenience. Pasting the text and dropping the file are the same operation.

| Element | Decision | Why |
|---|---|---|
| `ETL1` magic | Envelope generation. `ETL2` = a breaking envelope change only | Payload evolution uses `lv`; the envelope should almost never change |
| Payload encoding | **compact JSON, UTF-8, base64url** | Readable in 10 years with `base64 -d`, without our code. A CBOR/binary payload saves ~25 % and costs forensic legibility — the wrong trade for a longevity artifact |
| Signature encoding | base64url, unpadded, fixed 64 B | |
| Compression | ⛔ **none in V1** | Adds a second parse path and a decompression-bomb surface to save ~150 chars |
| Encryption | ⛔ **none** — matches the brief's §5 | Integrity and authenticity are the goals. A license the customer can read is a license support can debug |

### 14.2 Size, measured on a realistic payload

```
{"lv":1,"kid":"R1","alg":"ES256-P1363","lid":"0191f3c4…","cid":"c-0042",
 "prod":"EmberTern","ed":"professional","feat":["debugger","import.xlsx",
 "export.xlsx","trace","security-manager"],"seats":5,"seatPolicy":"contractual",
 "iat":"2026-08-15T10:00:00Z","nbf":"2026-08-15T00:00:00Z",
 "exp":"2027-08-15T23:59:59Z","cust":{"name":"ACME Sp. z o.o."}}
```

≈ 380 bytes JSON → 507 base64url chars + `ETL1.` + `.` + 86 chars ≈ **≈ 600 characters.**

### 14.3 ⚠ What 600 characters means for the brief's dream UX

The brief wants *"wpisz klucz"*. 600 characters is **not typeable**; it is *pasteable* and it is
*droppable*. This is the honest cost of "no backend", and it is a real cost, so it is **Q2**:

- ⭐ **A short key (`ETRN-XXXX-XXXX-XXXX`) cannot be verified offline.** It carries no signature and no
  data — it is only a *lookup handle*, which requires something to look it up in. **Short key ⟺ backend.
  There is no third option.** This is the cleanest architectural fork in the whole design.
- Precedent for the long form is strong in this market: JetBrains offline activation codes, Sublime, IDA,
  Navicat and Beyond Compare all hand the user a long blob or a file.
- Practically: the customer receives an e-mail with an attachment and one sentence — *"drag this file
  onto the EmberTern activation window."* No typos, no case sensitivity, no transcription support
  tickets. It is arguably a **better** experience than typing 19 characters; it is only a worse
  *slogan*.

### 14.4 Perpetual licenses and perpetual-fallback

`exp` absent ⇒ perpetual. For a perpetual product the useful limit is on *versions*, not time:

```
maint = "2027-08-15"        ← maintenance/updates covered until this date
AppInfo.ReleaseDate         ← already an assembly attribute in Directory.Build.props
                              (AssemblyMetadata "ReleaseDate"), already read back by AppInfo

if (maint is not null && AppInfo.ReleaseDate > maint)  ⇒  VersionNotCovered
```

⭐ **This works today with no new infrastructure** because product identity already has one source and
the release date is already a first-class assembly attribute. The gate is ~5 lines, costs nothing while
unused, and would be expensive to retrofit into an already-issued licence population. **Build it in V1,
leave it unused.**

### 14.5 Forward-compatibility rules (binding)

1. Unknown **top-level** payload fields ⇒ **ignored**.
2. Unknown **feature** strings ⇒ ignored.
3. Unknown **edition** ⇒ displayed verbatim, gates nothing.
4. Unknown **`kid`** ⇒ **refuse**, with *"issued for a newer version of EmberTern"*.
5. `lv` greater than supported ⇒ **refuse**, same message.
6. ⛔ Rules 4 and 5 are the only refusals. Everything else degrades.

---

## 15. InstallationId / fingerprint model

### 15.1 ⚠ Why Gemini's BIOS + CPU + partition triple is rejected

Evaluated against the events that actually happen to a paying developer's workstation:

| Event | SMBIOS UUID | CPU id | Partition/volume id | Windows MachineGuid | **Random InstallationId** |
|---|---|---|---|---|---|
| RAM upgrade | stable | stable | stable | stable | **stable** |
| SSD replaced, image restored | stable | stable | ❌ **changes** | stable | **stable** |
| GPU / PSU / case swap | stable | stable | stable | stable | **stable** |
| Windows reinstalled | stable | stable | ❌ changes | ❌ changes | ❌ changes (re-activate) |
| Motherboard replaced (warranty) | ❌ **changes** | stable | stable | stable | **stable** |
| New laptop, profile migrated | ❌ changes | ❌ changes | ❌ changes | ❌ changes | ❌ changes |
| Dev VM cloned 10× | ❌ **identical** — abuse invisible | identical | identical | identical | ❌ identical |
| Corporate imaging / sysprep | varies | stable | changes | ⚠ may be identical fleet-wide | changes per install |

**Two failure modes, and they point the same way.** The hardware triple *punishes* the honest customer
(a warranty motherboard swap locks them out) and *fails to detect* the dishonest one (a cloned VM looks
identical). A random InstallationId does neither: hardware changes are invisible to it, and cloning is
equally invisible to both — so the hardware triple buys nothing and costs support tickets.

Note also that reading SMBIOS/CPU identifiers on Windows means WMI or P/Invoke — a startup cost, an
antivirus-heuristic surface, and a privacy question — on the one code path required to be fast and
boring.

### 15.2 The recommended model

```
InstallationId
  · random 128 bits, generated once at first run
  · %APPDATA%\EmberTern\installation.id   (plain text, one line)
  · ⭐ if the file is missing it is REGENERATED, never inferred from hardware
  · it is an identifier, not a secret, and not a fingerprint

MachineFingerprint          ← V2 only; NEVER in the license; NEVER gates startup
  · six independently hashed weak signals, each SHA-256(salt ‖ value):
        MachineGuid · SMBIOS UUID · CPU brand+cores · primary volume serial
        · Windows install date · hostname
  · sent only during activation, stored only server-side
  · ⭐ used ONLY to answer "is this probably the same machine, reinstalled?"
        → threshold match: ≥ 3 of 6 components agree ⇒ auto-release the old seat
  · salted + hashed so no raw hardware identifier ever leaves the machine
```

### 15.3 The local check the brief's §15 asks for

```
signature valid?   ← always
   ↓
lid/prod correct?  ← always
   ↓
iid matches?       ← ONLY IF the license carries one (seatPolicy == "bound")
   ↓                  V1 licenses carry none, so this is a no-op in V1
date valid?        ← always
```

⭐ **Hardware never appears in this chain.** A bound license compares one random id against one random
id. That is the whole device check, and it is why a warranty repair does not create a support ticket.

### 15.4 Honesty about what this achieves

Copying `%APPDATA%\EmberTern\` wholesale copies the InstallationId with it, so a bound license can be
cloned by anyone willing to copy a directory. **This is the same limitation the audit already stated and
it is unfixable without hardware binding, which §15.1 shows costs more than it buys.** The mechanism
raises casual sharing from *"e-mail them the file"* to *"deliberately copy a profile directory"*, and —
in V2 — makes the abuse *visible* on the admin side. That is the achievable goal.

---

## 16. Seat model

### 16.1 V1 — contractual

The license states `seats: 5`. EmberTern **displays** it (About, Settings ▸ License) and enforces
nothing. The number is a term of the contract, recorded in the License Manager, printed on the license,
and visible to whoever opens the About window. For a B2B tool sold to named companies this is how most
of the market operates, and it costs nothing to change later.

### 16.2 V2 — enforced

```
license (D1)                        activations (D1)
  lid            PK                   lid          FK
  seats          5                    iid          PK (with lid)
  seat_policy    'bound'              fp1..fp6     hashed components
  status         active               device_name  user-supplied, cosmetic
                                      activated_at
                                      released_at  NULL = occupying a seat
```

Activation algorithm, inside one D1 transaction:

```
1. look up the key             → unknown ⇒ 404 (constant-time-ish, §22.9)
2. status != active            → 410
3. this iid already active     → RE-ISSUE the same seat (idempotent — retries are free)
4. count active activations
   4a. < seats                 → take a seat
   4b. == seats                → try FINGERPRINT RECLAIM:
          if some active row matches ≥3 of 6 components
             → mark it released (reason: 'fingerprint-reclaim'), take its seat
          else → 409 no-seats, with a message naming the admin contact
5. sign a license with iid set, seatPolicy = 'bound'
6. append an audit row
```

⭐ **Step 4b is the difference between a licensing system and a support burden.** The single most common
real event — *"I reinstalled Windows and now it says no seats left"* — resolves itself, silently and
correctly, without an admin, without a phone call, and without weakening the limit for anybody who is
actually running a sixth machine.

### 16.3 Seat release (§20 of the brief)

- **Admin, in License Manager** — select an activation, Release, with a mandatory reason; it becomes an
  audit row. This is the primary path and always available.
- **Automatic fingerprint reclaim** — §16.2 step 4b.
- ⛔ **No user-facing "deactivate this computer" button in V1 or V2.** The brief already rules it out,
  and it is also the mechanism by which a seat limit becomes decorative.
- **A stale-seat policy is deliberately not built**: without a lease (§3.5) there is no `lastSeen`, so
  "release seats unused for 180 days" is unimplementable. Correctly so — it would require the telemetry
  the brief rejects.

---

## 17. License Manager

### 17.1 ⚠ Two challenges to the brief's stack choice

**(a) .NET 10 — recommend .NET 9 instead, for V1.** The brief specifies .NET 10. Consider what a second
TFM costs *this* repository: `Directory.Build.props` pins `net9.0` for every project and is documented as
the single source of product identity; Avalonia is pinned at 12.1.1 with **two deliberate version
mismatches** that already carry written justifications (`docs/design/avalonia-12.1.1-update.md`, gotcha
#321). A second app on a second TFM means a second props file, a second Avalonia resolution to keep in
step, and two SDKs on the build machine — to gain nothing V1 needs. **Recommend: License Manager on
`net9.0`, and move both applications to .NET 10 together, deliberately, as one task.** (**Q6**)

**(b) Same repository, separate solution.** Recommend `EmberTern.LicenseManager.slnx` alongside
`EmberTern.slnx`, sharing `Directory.Build.props`. Rationale: the licensing format library must be shared
by source, not by a published package, and the two must never drift. ⚠ **Risk to record:** if EmberTern
is ever open-sourced, `EmberTern.Licensing.Issuing` and `keystore.etkeys` must be extractable — which the
project layout below already allows, since issuing is one self-contained project. (**Q7**)

### 17.2 Project layout

```
src/
  EmberTern.Licensing/            ⭐ PURE. No Avalonia, no Firebird, no I/O, no network.
    LicenseEnvelope.cs               parse/serialise ETL1
    LicensePayload.cs
    LicenseVerifier.cs               ⭐ THE one entry point
    TrustedKeys.cs                   append-only PUBLIC key table
    SignatureAlgorithm.cs
    Features.cs                      append-only feature id constants
    → referenced by EmberTern.App AND EmberTern.LicenseManager

  EmberTern.Licensing.Issuing/    ⛔ NEVER referenced by EmberTern.App.
    LicenseIssuer.cs                 signing
    KeyStore.cs                      AES-256-GCM + PBKDF2 (reuses Core.Security patterns)
    KeyCeremony.cs                   root key generation + backup verification
    → referenced ONLY by EmberTern.LicenseManager

  EmberTern.LicenseManager/       Avalonia desktop, MVVM, DI
    Views/ ViewModels/ Data/
    Themes/  →  ⭐ LINKED, not copied, from ../EmberTern.App/Themes/*.axaml
```

⭐ **Theme sharing by file link, not by moving files.** A `<AvaloniaResource Include="..\EmberTern.App\
Themes\*.axaml" Link="Themes\%(Filename)%(Extension)" />` gives one source of truth with **zero risk to
EmberTern** — moving those files into a shared library would break every `avares://EmberTern/Themes/…`
URI in the app, which is exactly the kind of change this project's rules exist to prevent. All UI styling
rules in `CLAUDE.md` apply unchanged to the License Manager: no hardcoded colours, tokens only, both
themes, `ControlStyles.axaml` classes.

### 17.3 Functional scope

**V1 (must have)**

| Area | Capability |
|---|---|
| Customers | create · edit · search · merge-guard on duplicate name |
| Licenses | create · duplicate-as-template · edit terms · **re-issue** (new artifact, same `lid`) |
| Issuing | sign · export `.etlic` · copy token to clipboard · export a batch + a CSV manifest |
| Terms | edition · features (checkbox list from `Features`) · seats · `nbf` / `exp` / `maint` |
| Bulk | filter (expiring in N days / edition / status) → select → **Extend to date** → batch re-issue |
| Status | active · blocked · superseded (⚠ **blocked is bookkeeping in V1** — §21.4) |
| History | append-only `audit_log`, every mutation, with actor + timestamp + before/after |
| Export | full store → encrypted `.etlmbackup`; plus a **plain JSONL** dump (§27.3) |
| Keys | key ceremony wizard · passphrase change · backup + **verified restore** |

**V2 (with the backend)**

Activation browser (who activated what, when, from where) · release a seat · block with effect · import
`.etreq` offline activation requests · D1 sync.

**⛔ Explicitly not built**: invoicing, payments, CRM, e-mail sending, a web portal, multi-operator
accounts, role-based access. The License Manager is a single-operator desktop tool. If a second operator
is ever needed, that is when the register moves to D1 — not a reason to build accounts now.

### 17.4 The safety rule the License Manager needs

⭐ **An issued artifact is immutable and is never edited — only superseded.** Changing a license's terms
produces a *new* artifact with a later `iat` and the same `lid`; the old row is marked `superseded` and
kept forever. Consequences: the register can always answer *"what exactly did we send this customer in
2026?"*, a lost license is re-exportable byte-for-byte, and there is no code path that can silently
change what a customer was told they bought. This is Architecture rule 11 applied to the admin side.

---

## 18. License Manager local database

### 18.1 Recommendation: SQLite, and it is the **register of record** — not a cache

The brief asks whether a local database is needed and floats "Firebase, or Firebase + cache". The
recommendation inverts that: **the local SQLite file is the master, and any future backend is a
mirror of it.** Reasons:

1. The signing key is offline by design (§19). The register and the key belong together — an issuing
   record that lives somewhere the issuer cannot reach is a record that will drift.
2. It works with no internet by construction, which the brief requires of the License Manager (§23).
3. Backup is a file copy. Restore is a file copy. Both are testable in ten seconds.
4. It outlives every vendor decision in this document.
5. `Microsoft.Data.Sqlite` is a first-party, single-package, no-native-hassle dependency, and the file
   format is the most durable one in the industry.

⚠ A JSON-file store was considered and rejected: group renewal, filtering and an append-only audit log
are all queries, and hand-rolled querying over JSON is how a tool becomes unmaintainable in year three.

### 18.2 Schema

```sql
CREATE TABLE customers (
  customer_id   TEXT PRIMARY KEY,        -- 'c-0042'
  name          TEXT NOT NULL,           -- ⭐ REQUIRED (the brief's one mandatory field)
  company       TEXT,
  first_name    TEXT,
  last_name     TEXT,
  email         TEXT,
  notes         TEXT,                    -- ⭐ admin-only; NEVER in the license payload
  created_at    TEXT NOT NULL,
  updated_at    TEXT NOT NULL
);

CREATE TABLE licenses (
  lid           TEXT PRIMARY KEY,
  customer_id   TEXT NOT NULL REFERENCES customers(customer_id),
  product       TEXT NOT NULL DEFAULT 'EmberTern',
  edition       TEXT NOT NULL,
  features_json TEXT NOT NULL,
  seats         INTEGER NOT NULL,
  seat_policy   TEXT NOT NULL,           -- contractual | bound
  not_before    TEXT NOT NULL,
  expires_at    TEXT,                    -- NULL = perpetual
  maint_until   TEXT,
  status        TEXT NOT NULL,           -- active | blocked | superseded
  notes         TEXT,
  created_at    TEXT NOT NULL,
  updated_at    TEXT NOT NULL
);

-- ⭐ Every artifact ever signed. Append-only. This is what makes a lost
--    license a 5-second re-export instead of a re-issue with a new iat.
CREATE TABLE issued_artifacts (
  artifact_id   INTEGER PRIMARY KEY AUTOINCREMENT,
  lid           TEXT NOT NULL REFERENCES licenses(lid),
  kid           TEXT NOT NULL,
  issued_at     TEXT NOT NULL,
  payload_json  TEXT NOT NULL,           -- exactly what was signed
  token         TEXT NOT NULL,           -- the full ETL1.… artifact, verbatim
  reason        TEXT NOT NULL            -- initial | renewal | terms-change | reissue-lost
);

-- ⭐ Append-only. No UPDATE, no DELETE, ever. Enforced by a trigger.
CREATE TABLE audit_log (
  audit_id      INTEGER PRIMARY KEY AUTOINCREMENT,
  at            TEXT NOT NULL,
  actor         TEXT NOT NULL,           -- OS user of the admin machine
  action        TEXT NOT NULL,
  target_type   TEXT NOT NULL,
  target_id     TEXT NOT NULL,
  before_json   TEXT,
  after_json    TEXT,
  note          TEXT
);

CREATE TABLE schema_meta (key TEXT PRIMARY KEY, value TEXT NOT NULL);  -- 'version'
```

⭐ The `audit_log` immutability is enforced in the database (`CREATE TRIGGER … BEFORE UPDATE … RAISE
(ABORT, …)`), not in the ViewModel. A history that the application can rewrite is not a history.

### 18.3 What is *not* in this database

**The private key.** It lives in `keystore.etkeys`, a separate file with a separate protection scheme, so
that "back up the register" and "back up the key" are two decisions with two different risk profiles —
and so that handing the `.db` to someone for inspection leaks nothing that can sign.

---

## 19. Cryptographic key management

### 19.1 The key ceremony (documented, scripted, and performed once)

```
1. Generate the ROOT key pair on the admin machine, offline.
2. Encrypt the private key into keystore.etkeys:
      AES-256-GCM under PBKDF2-SHA256(passphrase, random salt, ≥600 000 iterations)
   ⭐ Reuse EmberTern.Core.Security's PassphraseProtector / EncryptionSchemes patterns —
      the project already has a reviewed implementation of exactly this shape.
3. Passphrase: ≥ 6 diceware words, generated, never typed from memory, stored in a
   password manager AND on paper in a sealed envelope.
4. Back up keystore.etkeys to TWO offline media in TWO physical locations.
5. ⭐ VERIFY THE RESTORE — on a different machine, from the backup, sign a test
   license and verify it. A backup that has never been restored is a hypothesis.
6. Record the public key + kid in EmberTern.Licensing.TrustedKeys and ship it.
7. Record the ceremony date, the kid, and the fingerprint of the public key in this
   document's appendix.
```

### 19.2 Where the private key must never be

⛔ EmberTern · the installer · any repository (a `.gitignore` entry is not protection — the keystore
lives outside the working tree entirely) · any cloud sync folder · any CI system · any screenshot · any
chat message · any unencrypted backup.

### 19.3 If a backend is ever added (V2)

The root key **still** never leaves the admin machine. The Worker gets an *issuer* key with a root-signed
certificate (§12.2, §20.2). The blast radius of any cloud compromise is bounded to one issuer's validity
window, and recovery does not require replacing the root of trust.

### 19.4 ⭐ The guard test — `PrivateKeyNeverShipsTests`

Written in L1, before there is a key to protect. In the style of this project's existing structural
guards (`CharsetGuardSeamTests`, `FluentBridge_ContainsNoLocalValues`, `AppInfoTests`):

1. `EmberTern.App`'s transitive assembly closure contains **no** `EmberTern.Licensing.Issuing`.
2. No type in `EmberTern.Licensing` exposes a signing operation or a private-key parameter.
3. No file under `src/EmberTern.App` or `src/EmberTern.Licensing` matches `PRIVATE KEY`, `.etkeys`, or a
   base64 blob of private-key length.
4. The published output of `EmberTern.App` contains no `*.etkeys` / `*.pem` / `*.key`.

⭐ The test exists so that the rule survives the person who wrote it. **`TreatWarningsAsErrors=true`
means a violation is a build failure, not a review comment.**

---

## 20. Key rotation

### 20.1 The three cases, which are not the same

| Case | Trigger | Old licenses | Action |
|---|---|---|---|
| **Scheduled rotation** | hygiene, every 3–5 years | ✅ keep working forever | add `R2` to `TrustedKeys` in release *N*; start signing with `R2` only after release *N* is widely deployed; mark `R1` `retiredAt` (stop *issuing*, never stop *verifying*) |
| **Algorithm migration** | e.g. adopting Ed25519 when the BCL has it | ✅ keep working | identical to above — the `kid` table entry names the algorithm (§13.3) |
| **Compromise** ☠ | key stolen | ❌ **must die** | (1) new key `R2` + reissue **every** live license; (2) only then ship a release marking `R1` `revoked = true`; (3) that release refuses every `R1` license, including honest ones — hence the ordering |

### 20.2 The certificate chain (reserved in V1, used in V2)

```
chain: [ base64url( ETC1.<issuerCertPayload>.<rootSignature> ) ]

issuerCertPayload = { "kid":"I1", "alg":"ES256-P1363", "pub":"<base64url>",
                      "nbf":"…", "exp":"…", "iss":"R1" }

Client rules — deliberately rigid, because chain flexibility is where chain bugs live:
  · at most ONE element. Two or more ⇒ refuse.
  · the certificate is verified with a ROOT key from the table (never from the chain).
  · the license is verified with the certificate's key.
  · the license's iat must lie inside the certificate's nbf/exp.
  · ⛔ no path building, no cross-signing, no delegation of delegation.
```

⭐ **Implement and test chain verification in V1 even though V1 issues no chains.** A client shipped in
2026 must be able to verify a license issued by a 2028 backend — otherwise adding the backend forces a
mandatory client upgrade for every existing customer, which is the exact class of migration this
document exists to avoid.

### 20.3 The append-only rule

`TrustedKeys` entries are **never removed and never edited**, only appended and flagged. A key removed
from the table is a population of licenses that stopped working. `EncryptionSchemes.cs` already documents
this discipline for persisted identifiers in this codebase — same rule, same reason.

---

## 21. Threat model

### 21.1 Assets, in order of value

1. **The root private key.** Its compromise is the only unrecoverable event. Everything else is a bad
   day.
2. The License Manager register (`licenses.db`) — recoverable from backup; its loss does not stop any
   license in the field from working (§27.2).
3. Customers' license artifacts — bearer-ish, but they authorise only what was purchased.
4. Revenue from customers who would otherwise pay.

### 21.2 Actors

| Actor | Capability | In scope? |
|---|---|---|
| Honest customer with new hardware | replaces a machine | ✅ **primary design constraint** |
| Careless sharer | e-mails the file to a colleague | ✅ deterred socially (§22.3), enforced in V2 |
| Determined pirate | patches the binary | ❌ **out of scope — §21.5** |
| Forger | wants to mint licenses | ✅ blocked by key secrecy |
| Vandal / botnet (V2 only) | floods the API | ✅ §12.3, §24.2 |
| Insider / laptop thief | steals the admin machine | ✅ encrypted keystore + rotation plan |
| Curious customer | reads the license file | ✅ **allowed** — it is not secret |

### 21.3 Trust boundaries

```
┌── TRUSTED ──────────────┐   ┌── SEMI ─────┐   ┌── UNTRUSTED ─────────────────┐
│ admin machine           │   │ Worker (V2) │   │ customer machine             │
│ root private key        │   │ issuer key  │   │ EmberTern process            │
│ licenses.db             │   │ D1          │   │ license.etlic, installation.id│
└─────────────────────────┘   └─────────────┘   └──────────────────────────────┘
        ▲ full authority          ▲ delegated,      ▲ ⭐ verifies only. Holds no
                                    time-boxed        secret. Can be lied to about
                                                      the clock and the filesystem.
```

⭐ **The client is untrusted, and the design never pretends otherwise.** That is why it holds no secret
worth stealing: a public key, a random id, and a signed statement. There is nothing on a customer's
machine whose theft harms another customer.

### 21.4 ⚠ The consequence the brief already accepted (§21 of the brief)

**Strict offline means a blocked license keeps working on a machine that never connects, until `exp`.**
This is not a bug to be engineered around; it is the arithmetic of offline verification. In V1 `blocked`
therefore means:

- the customer receives **no renewal**, so the license dies at `exp` — the real enforcement;
- no new activation succeeds (V2);
- the register records the block and the reason.

⛔ **Do not add a "kill switch" that phones home.** That is Variant E and it costs the product's
independence from a service we do not control (§3.5).

### 21.5 ⚠ The limit that no design fixes — stated plainly so nobody re-litigates it

EmberTern is a .NET application. A single `brfalse` → `br` patch in `LicenseService` disables every check
in this document. Obfuscation, control-flow flattening, checksum self-verification and anti-debug tricks
raise the effort from *minutes* to *an afternoon*, at the cost of debuggability, crash-report quality,
antivirus false positives, and startup time — for a product with a niche B2B audience where the pirate
was never going to buy.

⭐ **The purpose of this system is to make honest use easy and accidental over-deployment visible, not to
make dishonest use impossible.** Every euro spent past that line buys nothing. The one measure worth
considering is **Authenticode signing of the released binaries** — which is about supply-chain trust and
SmartScreen, not licensing, and is a separate paid decision (§23.3).

### 21.6 ⚠ Firebase App Check does not apply here (correcting Gemini's §17 suggestion)

App Check attests that a request comes from a genuine instance of *your* app, using platform attestation:
reCAPTCHA Enterprise (web), Play Integrity (Android), DeviceCheck / App Attest (Apple). **There is no
Windows-desktop attestation provider.** The remaining option, a *custom* provider, requires a trusted
server that mints App Check tokens — i.e. it presupposes the backend it was meant to protect, and can
only authenticate a secret shipped in the client, which is not a secret. **App Check is not applicable to
a .NET desktop application and should be dropped from consideration.**

---

## 22. Attack analysis

| # | Attack | Result | Why |
|---|---|---|---|
| 1 | Edit `exp` in the license file | ❌ blocked | signature covers the encoded payload (§13.4) |
| 2 | Edit customer name / edition / features / seats | ❌ blocked | same |
| 3 | Swap in another customer's signature | ❌ blocked | signature is over *this* payload |
| 4 | Set `alg` to `none` or a weak algorithm | ❌ blocked | algorithm comes from the `kid` table; no `none` exists (§13.3) |
| 5 | Invent a `kid` | ❌ blocked | unknown `kid` ⇒ refuse, no fallback |
| 6 | Downgrade `lv` to dodge a new check | ⚠ limited | the client's minimum supported `lv` rises with the checks it must apply — new mandatory semantics require an `lv` bump *and* a minimum |
| 7 | Roll the system clock back | ⚠ mitigated | §22.6 |
| 8 | Copy `license.etlic` to another machine | ✅ **works in V1** | accepted (§16.1); V2 binds via `iid` |
| 9 | Copy the whole `%APPDATA%\EmberTern` profile | ✅ works even in V2 | accepted (§15.4) |
| 10 | Delete `license.etlic` to restart a trial | ✅ works | trials are out of V1 scope; if added, the high-water mark limits naive replays |
| 11 | Brute-force a license key (V2) | ❌ blocked | 128-bit `lid`; plus per-key throttling (§12.3) |
| 12 | Enumerate static license URLs (V1.5) | ❌ infeasible | 128-bit `lid`; and success yields a license the holder already has |
| 13 | MITM the activation response (V2) | ❌ harmless | TLS, and the payload is signed — a modified response fails verification |
| 14 | Replay an old activation response | ⚠ harmless | it is an *older* license; §22.8 refuses to install a lower `iat` |
| 15 | Flood the API (V2) | ⚠ DoS only, **never a bill** | §12.4 |
| 16 | Steal the admin laptop | ⚠ serious | keystore is AES-256-GCM under a passphrase not stored on that machine; rotation plan §20.1 |
| 17 | Compromise the Worker (V2) | ⚠ contained | issuer key only; root untouched (§12.2) |
| 18 | Patch the binary | ✅ **works** | out of scope by decision (§21.5) |
| 19 | Point EmberTern at a fake update server (V1.5) | ❌ harmless | a fetched license must still verify against a trusted key |
| 20 | Decompile to extract the public key | ✅ works, ⭐ **irrelevant** | it is public; possessing it grants nothing |

### 22.6 Clock rollback — the mitigation, and its deliberate gentleness

```
settings.dat  (already DPAPI-encrypted, already versioned, already migration-aware)
   └── LicenseClockHighWater : DateTimeOffset

on every start:  effectiveNow = max(systemNow, highWater)
                 if systemNow < highWater − 48h  →  warn, non-blocking
on clean exit / periodically:  highWater = max(highWater, systemNow)
```

- **48 hours of tolerance** because time zones, DST, VM suspends, dead CMOS batteries and travelling
  laptops are all normal, and a licensing system that punishes a flat battery is worse than one that
  loses a week of expiry enforcement.
- ⛔ **It warns; it never blocks.** A user who legitimately fixes a badly wrong clock must not be locked
  out of their tool. Architecture rule 11 governs here too.
- ⭐ It reuses `settings.dat` rather than inventing a store: DPAPI-per-user makes casual editing hard, the
  file already has a versioned header and a migration path, and the `Save` that refuses over an unreadable
  file is already implemented and tested.

### 22.8 The freshness rule for replacing a license

```
Install the incoming license ONLY IF:
    signature verifies
  AND  prod matches
  AND  ( there is no local license
         OR ( incoming.lid == local.lid AND incoming.iat > local.iat )
         OR the user explicitly confirmed replacing a DIFFERENT lid )
```

⭐ The `lid`-equal / `iat`-greater rule makes renewal idempotent and makes an accidental re-import of
last year's file a no-op instead of a downgrade. The explicit-confirmation branch exists because
transferring a machine to a different license is legitimate and must not be impossible.

### 22.9 Timing on the V2 activation endpoint

`unknown-key` and `blocked` must not be distinguishable by response time, or the endpoint becomes a key
oracle. In practice: do the D1 lookup, then a fixed-cost path to the response. This is cheap and worth
doing; it is not worth building constant-time cryptography for.

---

## 23. Cost analysis

### 23.1 Recommended architecture (V1)

| Item | Cost | Card required |
|---|---|---|
| Signing | €0 | no |
| Distribution (e-mail) | €0 | no |
| Storage (SQLite on the admin machine) | €0 | no |
| Runtime cost per activation | €0 | no |
| **Total** | **€0 / year** | **no** |

### 23.2 V2, if built

| Item | Free tier | Overage behaviour | Card |
|---|---|---|---|
| Cloudflare Workers | 100 000 req/day | requests refused until UTC reset | **no** |
| Cloudflare D1 | 5 GB · 5 M row-reads/day · 100 k row-writes/day | queries refused | **no** |
| Workers KV (optional) | 100 k reads / 1 k writes per day | refused | **no** |
| Static hosting (V1.5) | Pages / R2 free tiers | throttled | **no** |
| **Total** | **€0 / year** | **never an invoice** | **no** |

### 23.3 ⚠ The only items that cost money — both optional, neither usage-based

| Item | ~Cost | Needed for | Recommendation |
|---|---|---|---|
| Domain registration | €10–15 / yr | a Cloudflare **zone**, which is required for the free WAF rate-limiting rule (`*.workers.dev` is not behind zone rules) | buy it when V2 is built — the product wants a domain anyway |
| Authenticode code-signing certificate | €200–400 / yr (OV) | SmartScreen, supply-chain trust — **not licensing** | **deferred**; a separate decision at the installer stage |

⛔ **Nothing in the recommended path requires a payment card, a billing account, or a plan upgrade.**
Firebase Blaze is not required in any variant that this document recommends, including V2.

---

## 24. Free-plan limits analysis

### 24.0 ⭐ The principle that outlives the numbers

**Do not depend on a limit; depend on the absence of a billing relationship.** Free-tier quotas will move
over ten years — some up, some down, some replaced. The architectural property worth relying on is
binary and stable: *"there is no payment method attached, so the worst case is refusal, not an invoice."*
Every figure below is dated **2026-08-15** and must be re-verified before implementation; none of them
appears in a design decision, only in a comparison.

### 24.1 Firebase Spark (as quoted in the brief — consistent with our understanding)

| Resource | Limit | On exceeding |
|---|---|---|
| Firestore document reads | 50 000 / day | denied until reset |
| Firestore writes | 20 000 / day | denied |
| Storage | 1 GiB | writes denied |
| Egress | 10 GiB / month | denied |
| **Cloud Functions** | ⛔ **not deployable on Spark** | — |
| Rate limiting / WAF | **none** | — |

⭐ The last two rows are the finding: **no compute** (so no signing) and **no throttle** (so the quota is
the only limit, and exhausting it is the attack).

### 24.2 Cloudflare Workers Free

| Resource | Limit (2026-08-15) | On exceeding |
|---|---|---|
| Requests | 100 000 / day (UTC reset) | Worker stops; error returned; **no charge** |
| CPU per invocation | ~10 ms | invocation terminated |
| Subrequests per request | ~50 | request fails |
| Secrets | supported (`wrangler secret put`) | — |
| Rate-limiting binding | available (verify status) | — |
| Zone WAF rate-limiting rule | 1 free rule ⚠ **zone only, not `*.workers.dev`** | — |

**Sufficiency for our load:** an activation is one signature (~0.1 ms CPU) plus 1–2 D1 queries — an order
of magnitude inside the CPU budget. At a realistic few hundred activations per *year*, the daily cap is
overprovisioned by roughly **5 orders of magnitude**. The cap exists to stop an attacker, not us.

### 24.3 Cloudflare D1 free

5 GB total storage · ~5 M rows read/day · ~100 k rows written/day. Our data is thousands of rows.
Non-binding by five orders of magnitude.

---

## 25. Offline mode

### 25.1 What "strict offline" means here, precisely

**In V1, `EmberTern.Licensing` and `LicenseService` contain no HTTP client, no socket, no DNS lookup and
no reference to any networking namespace.** This is stronger than a policy — it is a property that can be
asserted by a test:

⭐ **`LicensingMakesNoNetworkCallsTests`** — the licensing assemblies' referenced-type closure contains
nothing from `System.Net.*`. Written in L1. It is the machine-checkable form of the brief's central
requirement, and it will still be true in 2031 when nobody remembers this conversation.

### 25.2 The states, end to end

| State | App | Banner | User action |
|---|---|---|---|
| `Valid` | full | none | none |
| `Valid` + expiry ≤ 30 d | full | info, dismissible per session | renew when convenient |
| `Grace` (≤ 14 d past `exp`) | **full** | warning, persistent, not dismissible | renew |
| `Expired` | opens; editor, files, export, settings work; ⛔ **no new DB connections** | error + activation entry | renew |
| `Invalid` | gated | error + `[Copy details]` | re-import / contact |
| `Unlicensed` | gated | activation window | activate |
| `NotYetValid` | gated | states the start date | wait / contact |
| `VersionNotCovered` | gated | names the covered version | install the covered build or renew maintenance |
| `WrongInstallation` (V2) | gated | explains the binding | re-activate |

⭐ **A 14-day grace period is not generosity — it is a correctness requirement.** Renewal in V1 is a human
process (an admin sends a file); an expiry that bricks the tool at midnight on day zero turns a routine
purchase-order delay into a work stoppage. And per Architecture rule 11, no state may ever prevent a user
from saving or exporting work that is already open.

### 25.3 License file location

```
1. %APPDATA%\EmberTern\license.etlic                ← per-user, the normal case
2. %PROGRAMDATA%\EmberTern\license.etlic            ← per-machine fallback, read-only
                                                       (shared workstations, terminal servers,
                                                        admin-deployed installs)
first match wins; the per-user file always shadows the machine file.
```

⛔ The license is **not** stored in `settings.dat`: it must survive a settings reset, be copyable by
support, and be readable without EmberTern.

---

## 26. Upgrade / migration

### 26.1 Payload version (`lv`)

`lv` rises only when a **mandatory** semantic changes — a new field that an older client must not ignore.
Adding an optional field does not bump `lv`; that is what rule §14.5(1) is for. A client refusing `lv > N`
must say so in words the user can act on: *"This license was issued for a newer version of EmberTern."*

### 26.2 Envelope version (`ETL1`)

Reserved for a change to the envelope itself (encoding, signing input, segment layout). Expected
frequency: **never**. If it happens, clients accept both and the License Manager issues the older form
until the new client is deployed.

### 26.3 Backend migrations

- **Adding a backend (V1 → V2):** additive. Old licenses remain valid; new ones carry `iid` and a chain.
  ⭐ **Zero client changes are required to accept V2 licenses, because V1's verifier already handles
  both.** This is the payoff of §20.2.
- **Changing the backend (Cloudflare → anything):** the client only knows a URL and a trusted key. Change
  both in a release; already-activated customers are unaffected because they never call it.
- ⭐ **Removing the backend entirely:** delete the calls. The product returns to V1 and every license in
  the field keeps working. **This is the design's most important long-term property**, and it exists
  because V1 was built first.

### 26.4 The 10-year test

> *Can a customer install EmberTern 0.6 from an archive in 2036, with the vendor gone, the domain
> expired, Cloudflare bankrupt and Firebase discontinued, and use their license?*

**Yes** — the artifact is self-contained, the public key ships in the binary, verification is local, and
nothing in the path resolves a name or opens a socket. **No variant with a mandatory backend can answer
that question with a yes.**

---

## 27. Recovery

| Scenario | Recovery | Preparation required |
|---|---|---|
| Customer lost their license file | License Manager re-exports the **exact artifact** from `issued_artifacts` | the table exists (§18.2) |
| Customer's disk died, new machine | V1: re-send the same file. V2: fingerprint reclaim or admin release | §16.2 |
| Admin lost `licenses.db` | restore from the encrypted backup; ⭐ **licenses in the field are unaffected** | scheduled backup |
| Admin lost the passphrase | ☠ **unrecoverable** — cannot issue or renew anything again | paper copy, sealed, off-site |
| Root key leaked | rotate (§20.1, compromise row): reissue everything, *then* ship the revocation | rehearsed procedure |
| License Manager will not start | ⭐ the artifacts are plain text in a SQLite file readable by any tool | schema documented here |
| Vendor ceases operations | every issued license keeps working to its `exp`; perpetual ones forever | §26.4 |

### 27.3 The escape hatch

The License Manager exports **plain JSONL** alongside the encrypted backup: one line per customer,
license and artifact. If the tool is ever unbuildable, the register is still readable by `cat`. This
costs ~30 lines and buys the whole register's independence from its own application — the same reasoning
that keeps the license payload JSON rather than binary (§14.1).

---

## 28. UX

### 28.1 The customer's entire relationship with licensing

```
install → launch → "Activate EmberTern"
                    [ drop the file here, or paste the license ]
                    [ Activate ]
                                → "Licensed to ACME Sp. z o.o."
                                → …then never again, for a year.
```

Once a year: an unobtrusive banner 30 days before expiry, and a new file in the inbox.

### 28.2 Surfaces

| Surface | Content |
|---|---|
| **Activation window** | first-run only; drop target + paste box + Activate; errors in a `MessageBanner` (⭐ the IDE's ONE message surface — never a locally styled coloured `TextBlock`) |
| **Settings ▸ License** | a new Settings Center category: who it is licensed to, edition, features, seats, dates, `[Update license]`, `[Copy license id]`, and — behind Advanced — the V2 service-mode actions |
| **About** | "Licensed to …" line, near the version `AppInfo` already reads |
| **Expiry banner** | `MessageBanner` on the main surface; Info at ≤30 d, Warning in grace, Error when expired |

⛔ No licensing UI anywhere else. No modal on startup for a valid license. No nag screens. No "buy now".

### 28.3 What "gated" means

Gated ⇒ the app opens and the window is usable, but **no new database connection can be established**.
The editor, saved queries, settings, exports and everything already open continue to work. Rationale:
Architecture rule 11 — a licensing state must never be able to destroy or trap the user's work — and
basic decency toward a paying customer whose renewal is three days late.

### 28.4 Error messages

Every string in this system is a `Strings.resx` + `Strings.pl.resx` pair resolved at display time
(Architecture rule 12). ⚠ **The Phase-5 charset-guard lesson applies directly and is the highest-risk
part of this feature's localization**: the failure mode is not a missing entry but a *perfect entry that
nothing reads*, because the message was wrapped in an exception and the display site read `ex.Message`.
Licensing has exactly that shape — a verification failure deep in a pure library, surfaced by App. **Each
reason must be seen rendered in Polish, or pinned by a test that resolves it through the path the UI
actually uses** (`ErrorText`), as `CharsetGuardLocalizationTests` does.

⭐ Every failure message answers three questions: *what happened · why · what to do now.*
Not *"License validation failed (code 7)."*

### 28.5 Terminology

Action names must be checked against `docs/design/terminology.md` and will be enforced by
`TerminologyTests`. Proposed: **Activate** / *Aktywuj*, **Update license** / *Aktualizuj licencję*,
**Release seat** / *Zwolnij stanowisko* (License Manager only). To be verified against the norm during
L4, not assumed here.

### 28.6 Privacy

V1 sends nothing, ever. V1.5's renewal check reveals to the hosting provider that a given `lid` is in
use — therefore it is **user-initiated or explicitly opted into**, disclosed in plain language, and
never automatic on startup. V2's activation sends the fingerprint **hashes** only, never raw hardware
identifiers, and only at activation.

---

## 29. Risks

| # | Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|---|
| 1 | Passphrase or key lost | low | ☠ **catastrophic** | §19.1 ceremony, verified restore, paper backup off-site |
| 2 | Key leaked | very low | ☠ severe | rotation §20.1; two-tier hierarchy in V2 limits blast radius |
| 3 | 600-character "key" rejected as bad UX | **medium** | medium | **Q2** — decide before L1; the alternative is a V1 backend |
| 4 | Contractual seats prove insufficient commercially | medium | medium | **Q1**; V2 is additive by design (§2.3) |
| 5 | A binary patch circulates | low | low–medium | accepted by decision (§21.5) |
| 6 | Renewal-by-e-mail does not scale | medium | low | V1.5 static pull (§3.4); it is ~80 lines |
| 7 | Free tiers change (V2) | **high over 10 yrs** | low | §24.0 — depend on "no card", not on the number |
| 8 | License Manager becomes a second product to maintain | medium | medium | scope discipline §17.3; ⛔ no invoicing, no CRM, no accounts |
| 9 | Clock mitigation locks out an honest user | low | high | warn-never-block, 48 h tolerance (§22.6) |
| 10 | Licensing strings ship untranslated or unread | **medium** | medium | §28.4 — the exact defect Phase 5 shipped; test through `ErrorText` |
| 11 | Startup cost regresses | low | medium | §6 budget < 5 ms, measured with `EMBERTERN_PERF_DIAG=1` |
| 12 | Two TFMs / two Avalonia versions drift | medium | medium | **Q6** — recommend one TFM for V1 |

---

## 30. Implementation plan, by stage

One stage per session, in the project's established rhythm: complete + tested + verified before the next
begins.

| Stage | Deliverable | Exit criteria |
|---|---|---|
| **L0** | ⭐ **Ratify §33 (Q1–Q14).** No code. | Decisions written into §33 as ratified |
| **L1** | `EmberTern.Licensing` — envelope, payload, `TrustedKeys`, `LicenseVerifier`, chain verification (unused), `Features`. Pure, zero deps. | Build 0/0; a **tamper corpus** (≥ 40 mutated tokens, each refused for the *right* reason); `LicensingMakesNoNetworkCallsTests`; round-trip tests |
| **L2** | `EmberTern.Licensing.Issuing` — `KeyStore`, `LicenseIssuer`, `KeyCeremony`. ⭐ **`PrivateKeyNeverShipsTests` lands here.** | A key ceremony performed for a *test* key; sign → verify across the assembly boundary; guard tests green |
| **L3** | License Manager: app skeleton (Avalonia/MVVM/DI, linked themes), SQLite store + migrations, customers, licenses, issue, export `.etlic` | Issue a license end to end; `audit_log` immutability trigger proven; UI passes the `CLAUDE.md` UI Review Checklist in **both** themes |
| **L4** | EmberTern integration: `LicenseService`, startup state machine, Activation window, Settings ▸ License, About line, banners, **EN + PL strings** | All nine states reachable and verified **in the running app**, Polish included; startup budget measured; ⛔ per the standing directive this stage is *"implementation done — awaits user confirmation"*, never "fixed" |
| **L5** | License Manager admin depth: search, filters, **group extend**, block, re-issue, history view, encrypted backup + JSONL export | Extend 20 licenses in one operation; restore from backup on a second machine |
| **L6** | Hardening: clock high-water, `%PROGRAMDATA%` fallback, `maint` gate, threat-model tests, the real key ceremony, public key shipped | Full suite green (total matches, per `CLAUDE.md`); documentation: `docs/history/` entry + gotchas + `current-state.md` row |
| **L7** | *(optional)* V1.5 static pull-renewal | User-initiated only; failure is silent and non-blocking |
| **L8** | *(deferred, only on demand)* Worker + D1 activation service, short keys, seat enforcement, offline `.etreq` flow | Decided separately, against real usage |

**Estimated shape:** L1–L2 one session each; L3 and L4 two each; L5–L6 one each. L1–L6 is the complete
V1. ⚠ Estimates are shape, not commitment.

---

## 31. What V1 contains

✅ ETL1 signed license format, versioned, forward-compatible, chain-capable
✅ Local verification: signature · `kid`/algorithm · product · `nbf`/`exp` · `maint` · optional `iid`
✅ Nine-state licensing state machine with a 14-day grace period, EN + PL
✅ Activation by file drop or paste; atomic write and re-verify
✅ Clock-rollback high-water mark (warn, never block)
✅ `%APPDATA%` + `%PROGRAMDATA%` resolution order
✅ Editions as labels, features as the gate
✅ Seats as a contractual, displayed number
✅ Perpetual and perpetual-fallback licenses
✅ License Manager: customers, licenses, issuing, export, search, filters, group extend, block, immutable
   history, encrypted backup + JSONL escape hatch
✅ Offline key ceremony, encrypted keystore, verified restore, rotation procedure
✅ Guard tests: private key never ships · no network in licensing · tamper corpus · audit immutability
⛔ **Zero network code. Zero cloud accounts. Zero cost. No payment card anywhere.**

## 32. What is deferred

| Item | Stage | Trigger to build it |
|---|---|---|
| Static pull-renewal | L7 | renewal-by-e-mail becomes a chore |
| Worker + D1 activation service | L8 | **Q1 answered "enforced"**, or short keys become a hard requirement |
| Short typed keys | L8 | ties to the backend by necessity (§14.3) |
| Technical seat enforcement + fingerprint reclaim | L8 | evidence of over-deployment |
| Offline `.etreq` activation requests | L8 | only meaningful once online activation exists |
| Managed-offline lease | ⛔ rejected | a concrete incident, not a preference (§3.5) |
| Trials / time-limited evaluation | future | a trial is just a short signed license — no new mechanism |
| Floating / concurrent licenses | future | needs a lease; same objection as §3.5 |
| Authenticode signing | installer stage | separate paid decision (§23.3) |
| Firebase, in any role | ⛔ **rejected** | §3.1, §10.4, §21.6 |

---

## 33. Open decisions — for the user to ratify (nothing below is decided)

| # | Question | Recommendation | Consequence if decided otherwise |
|---|---|---|---|
| **Q1** ⭐ | Are seats **contractual** or **technically enforced** in V1? | **Contractual.** V2 is additive and needs no reissue. | "Enforced" pulls L8 into V1: a public API, a cloud account and a Worker-held key, for a limit most B2B customers respect anyway |
| **Q2** ⭐ | Is a ~600-character pasted/dropped license acceptable, or is a short typed key required? | **Acceptable** — file drop is better than typing (§14.3) | A short key **requires** the backend in V1. There is no offline short key |
| **Q3** | Drop Firebase entirely? | **Yes** (§3.1, §10.4, §21.6) | Keeping it means hand-rolled REST, no signing, no seat enforcement, and a free DoS surface |
| **Q4** | Include the customer's `email` in the license payload? | **No** — `name` (+ optional `company`) only | Including it makes a shared license file a personal-data leak for no functional gain |
| **Q5** ⭐ | ECDSA P-256 (BCL, zero deps) or Ed25519 (BouncyCastle in the shipped client)? | **P-256 for V1**, with `kid`-selected algorithms so Ed25519 is a later non-event | Ed25519 adds a third-party crypto dependency to the client, and with `TreatWarningsAsErrors` a future CVE in it breaks the build (gotcha #278). ⚠ **This contradicts the GPT Terra audit — decide it consciously** |
| **Q6** | License Manager on `net9.0` or `net10.0`? | **`net9.0` for V1**; move both apps together later | Two TFMs ⇒ a second props file and a second Avalonia resolution to keep in step with two documented version mismatches |
| **Q7** | Same repository (separate `.slnx`) or a separate repository? | **Same repo, separate solution** | A separate repo means the shared format library travels as a package or a copy; a copy will drift |
| **Q8** | Grace period after expiry: 14 days? | **14 days**, full function, persistent warning | 0 days turns a purchase-order delay into a work stoppage |
| **Q9** | What does `Expired` block? | **New database connections only** — the rest of the app stays usable | Blocking everything risks trapping open work (Architecture rule 11) |
| **Q10** | Build the `maint` perpetual-fallback gate in V1 even though nothing uses it? | **Yes** — ~5 lines now, expensive to retrofit across an issued population | |
| **Q11** | Build chain verification in V1 even though V1 issues no chains? | **Yes** — otherwise adding a backend later forces every customer to upgrade first | |
| **Q12** | Static pull-renewal (V1.5) in scope now? | **No** — build it when e-mail renewal becomes a chore | |
| **Q13** | Should EmberTern display the customer name in the **title bar**, or only in About and Settings? | **About + Settings** — the title bar is working space | A title-bar attribution is a stronger deterrent but a permanent cost to the workspace |
| **Q14** | Product identity in the payload: `"EmberTern"` only, or a product family from day one? | **`"EmberTern"`**, checked strictly | A family field costs nothing later — `prod` is already a string |

---

## Appendix A — key register (to be filled by the key ceremony, §19.1)

| kid | Algorithm | Public key (SPKI, base64url) | Ceremony date | Retired | Revoked |
|---|---|---|---|---|---|
| `R1` | *(pending Q5)* | *(pending L2)* | — | — | — |

## Appendix B — feature id register (append-only, §9.3)

| Feature id | Meaning | Introduced |
|---|---|---|
| *(to be defined in L1 against the actual `CLAUDE.md` feature inventory)* | | |

⛔ Once an id is shipped it is never renamed and never reused — the same discipline
`EncryptionSchemes.cs` documents for persisted scheme identifiers.

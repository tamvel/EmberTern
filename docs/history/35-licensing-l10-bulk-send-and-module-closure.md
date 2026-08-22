# 35 — Licensing L10: bulk send, administrative removal, and the module's closure (2026-08-22)

> **Why this file exists.** L10 delivered the last capability the licensing module needed — sending many
> licences by e-mail — and then a run of user-found repairs that reshaped what "remove" means in the
> register. ⛔ It is not a description of the bulk-send design: `design/licensing-system.md` §60 is the
> ratified specification and §61 is the as-built. What is kept here is **what the work discovered**: five
> places where a measurement contradicted a written premise, and the defects that only appeared once a real
> operator used the thing.
>
> Branch `feat/licensing-system`. Commits: **`65ea50c`** (L10.1), **`96120e9`** (L10.2), **`1e73897`**
> (L10.3), **`c53afe6`** (L10.4), **`8ec0ce6`** (SMTP timeout + window state), **`83553b5`** (L10.5),
> **`94641a9`** (customer removal, panels, icon), **`ce42fe1`** (licence removal), **`99b3a87`** (customer
> retirement), plus the closing documentation commit.

---

## 1. The shape L10 was built in, and why it held

§60 was ratified **before** L9 started (`d553157`), and the six steps it laid out were executed in order
with no renegotiation. That is worth recording because it is unusual here: most stages in this project
changed shape after their first measurement. This one did not, and the reason is that §60's own
reconnaissance had already done the measuring — it recorded that the batch renewal has no active-licence
filter, that `audit_log` has no index for a per-licence query, that four needed icons already existed, and
that `ProgressBar` has no repinned keys in `FluentBridge`. Each of those would otherwise have been
discovered mid-implementation and moved the work.

⭐ **The one thing the plan did not predict was where the defects would come from.** Every design-level
question was answered by the specification; every defect that reached the user came from a place the
specification did not think about — a BCL promise that is not kept, a folded panel, and the word "remove".

---

## 2. Three models, and the invariant that made the report honest

The bulk send is deliberately **three types**, not one growing one: `BulkSendPlan` (what we intend),
`BulkSendProgress` (what is happening), `BulkSendResult` (what happened). The user's requirement — *"nie
udawajmy, że wysłano 40, kiedy 37 się udało"* — became the invariant `Planned == Sent + Failed + Skipped +
NotAttempted`, asserted automatically across all four terminal states.

⭐ **The counts are read off the attempt list rather than accumulated.** That is what makes the invariant
hold on the SCREEN as well as in the model: a card that kept its own counters would be a second accounting
of the same run, and the two would disagree the first time a path was added.

⚠ **`Completed` counts finished attempts, and a `Waiting` snapshot legitimately carries a count one higher
than the `Sending` before it** — the pacing happens *after* an attempt finished. The first draft of the
progress test asserted the opposite and was wrong; the property that matters is that nothing is counted
before it finishes.

### 2.1 K1 — stop on the first failure, and the enum value that had no producer

The user chose **K1** (stop after the first refusal) over continuing, in those words: *"Bezpieczeństwo i
przewidywalność są ważniejsze."* The first redaction of §60.7 listed **five** conclusions including
`CompletedWithErrors`.

⛔ **Under K1 that value has no producer.** A run that had a failure ends `StoppedAfterError`; there is no
path to "finished, with errors". An enum value nothing can produce is the dead-surface trap (#233) in the
one place a reader most needs to trust the list, so the specification was corrected to **four** and the
value will only return together with K2, if K2 is ever ratified. ⭐ That is the honest order: a value and
its producer arrive together.

---

## 3. What the measurements contradicted

**3.1 ⚠⚠ `SmtpClient.Timeout` does not bound `SendMailAsync`.** The class had declared
`Timeout = 30_000` since L6 with a comment explaining why a wrong host must not read as a frozen
application — and the property governs the *synchronous* `Send`, which this application never calls. A
probe against a black-holed address with `Timeout = 3 000` took **21 078 ms**; against the worse
configuration (implicit TLS on port 465, where the server waits for a ClientHello while the client waits
for an SMTP banner) nothing bounded the wait at all. ⭐ The same probe measured the fix: the
`CancellationToken` overload **does** interrupt a connect going nowhere (2 995 ms against a 3 000 ms
token). Gotcha **#414**.

⭐ The second half of the user's report — *"nawet ponowne wejście w Ustawienia nie pomagało"* — was not a
second defect. `ShellViewModel` builds `SettingsViewModel` once, so `IsTesting` left `true` survives
closing the window; the `finally` was there all along and never ran.

**3.2 The flood-fill tolerance for the new icon is 4, not 12.** `BRANDING.md` documents the OS-icon
pipeline and says explicitly that the tolerance must be measured per source. Measured here: background
`rgb(12,13,15)`, border noise max **4**, the artwork's darkest pixel at distance **5** — a window of 4–5,
where the product's source had 4–19 and used 12. ⛔ Copying 12 would have flooded through the shield's
near-black interior and eaten the middle out of the mark.

⚠ And a second step the documented pipeline did not have: this source carries a handful of **single
pixels** of compression noise outside the tolerance, invisible, which inflated the bounding box from
1006 × 850 to 1209 × 1113 — ~30 % of the icon spent on nothing. Removed by **connected component**, ⛔ never
by a row/column density threshold, which would also clip the shield's own apex.

**3.3 The licences view could not carry a third card.** L10.5's card, added the obvious way, took the
results grid — the list licences are ticked IN — down to two rows on a 720 px window. `BatchRenewalViewTests`
measured it in the same run the card landed. The first answer was a shared ceiling; the answer the user
accepted was **two independent disclosure panels, closed by default**, using the idiom the product already
has (chevron + title, ⛔ no `Expander`).

---

## 4. What only a real operator found

Three reports, and each one changed a decision rather than a line.

**4.1 The panels.** *"Zdecydowanie zbyt stłoczony."* The measurement above says the same thing
arithmetically; the report is what made it a design change rather than a ceiling constant.

**4.2 Two identical icons are two applications you cannot tell apart.** The License Manager had referenced
`EmberTern.ico` across the project boundary since L3, on the reasoning that it is the same product's admin
side rather than a second brand. ⭐ **The reasoning was sound and the result was wrong** — and it is only
wrong once the two are open side by side, one of them holding the signing key. `LicenseManagerThemeTests`
now fails the build if the two files ever become identical again.

**4.3 ⚠⚠ Removing every licence did not make the customer removable.** This is the most instructive of the
three, because the inconsistency was created by the fix immediately before it. §5 has it.

---

## 5. What "remove" means in this register, and how it got there

The register is append-only where it matters: `audit_log` and `issued_artifacts` both abort every UPDATE
and DELETE by trigger. Everything below follows from that and from two foreign keys, and **none of it is a
policy** — each branch was measured against a live database.

| row | measured | therefore |
|---|---|---|
| licence, never issued | `DELETE` succeeds | deleted |
| licence, ever issued | `SQLITE_CONSTRAINT_FOREIGNKEY` 19/787 from `issued_artifacts` | retired |
| customer, no licence rows | `DELETE` succeeds | deleted |
| customer, only retired licences | **`SQLITE_CONSTRAINT_FOREIGNKEY` 19/787 from `licenses`** | retired |
| customer, ≥ 1 active licence | — | refused, naming the count |

⭐ **Retirement is a `retired_at` COLUMN, never a status.** `LicenseStatuses` describes the AGREEMENT
(`active` / `blocked`, and §26.2 records that `blocked` is bookkeeping — a licence in the field keeps
working); retirement is an administrative fact about the REGISTER, orthogonal to it. A retired licence was
active or blocked when it was retired and that stays true. Folding them together would put a retired row in
the Blocked filter and make every reader that switches on the status learn a value answering a different
question. The customer record has no status vocabulary at all, so inventing one there would have been the
same wrong-role trap one table over.

### 5.1 ⚠⚠ The inconsistency the licence fix created, and the fix that was wrong

`ce42fe1` gave licences retirement. `CountLicenses` counted every row, retired included, because that is
what the foreign key sees. So an operator who removed a customer's every licence saw an **empty list** and
still could not remove the customer — with a message naming licences they could no longer see.

⛔ **The obvious repair — make `CountLicenses` ignore retired rows — is the wrong one**, and the user said
so before it was attempted: the counter would read zero, the application would attempt a `DELETE`, and
SQLite would refuse it on a row nobody was ever shown. A pre-flight check that hands the failure to the
database is worse than no check at all.

⭐ **The repair is two counts that answer two questions.** `CountActiveLicenses` decides whether the
operation is ALLOWED — it is what the operator can see, so it is the only number a refusal may talk about.
`CountLicenses` decides which operation it IS — the foreign key's own view, and the only number that may
authorise a `DELETE`. The delete branch is taken **only when the row count is zero**, which makes "counter
said 0, database said no" unreachable rather than unlikely.

### 5.2 What retirement had to reach

Retiring a row is not one line. Four other things had to learn about it, and one of them is a rule-#11
matter:

- `GetLicenses` / `QueryLicenses` and `GetCustomers` exclude retired rows **always** — no
  `IncludeRetired` flag, because every caller of them is an operation and a retired row must be unreachable
  to all of them. That one place is what keeps a retired licence out of the batch renewal and the bulk send.
- `GetAllLicenses` / `GetAllCustomers` are the unfiltered reads, and they exist for exactly two consumers:
  the JSONL escape hatch and the backup's counts. ⚠ **The export writes `retiredAt`** — a row that came back
  from that file without it would silently return to the active register.
- `SaveCustomer` and `SaveLicense` refuse a retired row, and `SaveLicense` separately refuses a new licence
  for a retired CUSTOMER — a licence save does not pass through `SaveCustomer`, and the foreign key would
  happily allow it because the customer row is still there.
- Schema **3** (licence) and **4** (customer), each one nullable `ALTER TABLE ADD COLUMN` with no backfill.

---

## 6. Gotchas this stage owes

Recorded in `docs/gotchas.md`: **#414** (`SmtpClient.Timeout` does not bound the async path), **#415**
(python `open(path, "w")` truncates on OPEN — it emptied `RegisterRecords.cs`), **#416** (restoring a file
with `os.replace` keeps the BACKUP's mtime, so the incremental build skips it — three full suite runs spent
on a "failure" that was an artefact of the restore; plus its sibling, a non-unique string replace that hit
the wrong branch of the migration ladder), **#417** (`Progress<T>` delivers asynchronously; and awaiting a
`ConfigureAwait(true)` command inside a headless dispatch deadlocks) and **#418** (comparing objects by
`ToString` where the type does not override it is a vacuous assertion).

⭐ **#416 is the one worth reading twice**, because it is a lesson about the *method* rather than about the
code: injecting a defect to prove a guard is alive is a technique this project relies on, and the restore
step is where it can quietly lie to you.

---

## 7. Where the module stands

**CLOSED.** L1–L10 delivered; the production key `R1` exists and ships its public half; a real licence has
been seen `Valid` in a `Release` build; bulk delivery works end to end and the user has confirmed it, along
with the removal of licences and customers, against their own register.

Suite **929 / 929**, both configurations **0 warnings / 0 errors**, at **`99b3a87`**.

⏭ **Two things deliberately left, both ratified:** the clock-rollback WARNING has no surface (the
enforcement does — decision C2, backlog), and the ceremony's two key backups were verified on one machine,
so portability is confirmed at the first real migration (§35.4). ⚠ The company mailbox is still unmeasured
(§48.1) — a tenant refusing basic auth is a NEW CLASS behind `ILicenseEmailSender`, ⛔ not a defect.

Authority for everything the module now does: **`design/licensing-system.md`** — §0 (ratified D1–D16),
§34–§59 (as built through L9), §60 (the ratified L10 specification) and **§61** (the as-built closure).

using System;

namespace EmberTern.LicenseManager.Data;

/// <summary>A customer. ⭐ <see cref="Name"/> is the only required field — it is what gets signed.</summary>
public sealed record CustomerRecord
{
    /// <summary>Stable identity, e.g. <c>c-0042</c>.</summary>
    public required string CustomerId { get; init; }

    /// <summary>
    /// ⭐ REQUIRED, at the database level and in the UI, because it is the value signed into every licence
    /// this customer will ever receive and displayed in their copy of EmberTern (decision D6).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>Postal address.</summary>
    public string? Address { get; init; }

    /// <summary>Contact first name.</summary>
    public string? FirstName { get; init; }

    /// <summary>Contact last name.</summary>
    public string? LastName { get; init; }

    /// <summary>Where the licence is sent (L6).</summary>
    public string? Email { get; init; }

    /// <summary>⛔ Administrative notes. Never travel in a licence — see §9.2.</summary>
    public string? Notes { get; init; }

    /// <summary>When the record was created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>The terms of one licence, as the register holds them.</summary>
public sealed record LicenseRecord
{
    /// <summary>The <c>lid</c>. Stable across renewals.</summary>
    public required string LicenseId { get; init; }

    /// <summary>Who it belongs to.</summary>
    public required string CustomerId { get; init; }

    /// <summary>Which product.</summary>
    public required string Product { get; init; }

    /// <summary>Contractual seats (D2).</summary>
    public required int Seats { get; init; }

    /// <summary>Start of validity.</summary>
    public required DateTimeOffset NotBefore { get; init; }

    /// <summary>End of validity.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Perpetual-fallback boundary. Nothing sets it in V1.</summary>
    public DateTimeOffset? MaintenanceUntil { get; init; }

    /// <summary>
    /// <c>active</c> · <c>blocked</c>. ⚠ <c>blocked</c> is bookkeeping in V1 — a
    /// licence already in the field keeps working until it expires (§26.2), and pretending otherwise in
    /// the UI would be the one lie this register must not tell.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>⛔ Administrative notes. Never travel in a licence.</summary>
    public string? Notes { get; init; }

    /// <summary>When created.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When last changed.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// One artifact that was actually signed and handed out.
///
/// <para>⭐ This table is why a lost licence is a five-second re-export rather than a re-issue with a new
/// <c>iat</c>, and why the register can always answer <i>"what exactly did we send this customer?"</i>
/// with the bytes rather than with a reconstruction (§12.5).</para>
/// </summary>
public sealed record IssuedArtifactRecord
{
    /// <summary>Row identity.</summary>
    public long ArtifactId { get; init; }

    /// <summary>Which licence.</summary>
    public required string LicenseId { get; init; }

    /// <summary>Which key signed it.</summary>
    public required string KeyId { get; init; }

    /// <summary>The <c>iat</c> it carries.</summary>
    public required DateTimeOffset IssuedAt { get; init; }

    /// <summary>The exact JSON that was signed.</summary>
    public required string PayloadJson { get; init; }

    /// <summary>The full <c>ETL1.…</c> token, verbatim.</summary>
    public required string Token { get; init; }

    /// <summary><c>initial</c> · <c>renewal</c> · <c>terms-change</c> · <c>reissue-lost</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>
    /// ⭐ <c>current</c> or <c>superseded</c> — see <see cref="ArtifactStatuses"/>.
    ///
    /// <para>⚠ <b>Projected on read, never stored on this row.</b> The row is immutable by database
    /// trigger; the pointer that decides which artifact is current lives in
    /// <c>license_current_artifact</c> and is rewritten in the same transaction that appends a newer
    /// artifact. <see langword="null"/> means "not read from the register" — the shape of a record built
    /// in memory on its way to being appended, which has no position in a history it is not in yet.</para>
    /// </summary>
    public string? Status { get; init; }
}

/// <summary>One line of history. ⛔ Append-only, enforced by a database trigger.</summary>
public sealed record AuditEntry
{
    /// <summary>Row identity.</summary>
    public long AuditId { get; init; }

    /// <summary>When.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>Which operating-system user of the administrator machine.</summary>
    public required string Actor { get; init; }

    /// <summary>What happened, e.g. <c>customer.created</c>, <c>licence.issued</c>.</summary>
    public required string Action { get; init; }

    /// <summary><c>customer</c> · <c>licence</c> · <c>key</c>.</summary>
    public required string TargetType { get; init; }

    /// <summary>Which one.</summary>
    public required string TargetId { get; init; }

    /// <summary>State before, when it existed.</summary>
    public string? BeforeJson { get; init; }

    /// <summary>State after.</summary>
    public string? AfterJson { get; init; }

    /// <summary>A free-text remark.</summary>
    public string? Note { get; init; }
}

/// <summary>
/// The audit actions a READER has to name. Persisted verbatim, so append-only.
///
/// <para>⚠⚠ <b>Deliberately NOT the whole vocabulary, and that is the point of the doc comment.</b>
/// The register and its workflows write around twenty actions and every one of them is a literal at its
/// own single write site — which is correct, because a value written in exactly one place has exactly
/// one owner. A name arrives HERE only when something has to <b>read back</b> what another component
/// wrote, because that is the moment the string acquires a SECOND owner and becomes the hand-typed
/// derived value gotcha #284 is about.</para>
///
/// <para>⛔ So this is not a refactoring target: moving the other actions here would create the second
/// owner it exists to prevent.</para>
/// </summary>
public static class AuditActions
{
    /// <summary>
    /// A licence artifact left this application by e-mail and the server accepted it.
    /// </summary>
    /// <remarks>
    /// ⭐ Written by <c>LicenceDelivery</c>, read by <see cref="LicenseRegister.GetLastSentAt"/>. ⚠ The
    /// two are proved to agree BEHAVIOURALLY — a test performs a delivery and asserts the register sees
    /// it — rather than by comparing this constant against a literal, which would only prove that two
    /// strings match and not that the path works.
    /// ⛔ It says the SERVER ACCEPTED the message; it never says the customer received it.
    /// </remarks>
    public const string LicenceSent = "licence.sent";
}

/// <summary>
/// The audit target types a READER has to name. Persisted verbatim, so append-only.
/// </summary>
/// <remarks>
/// ⭐ Same rule, and same narrow scope, as <see cref="AuditActions"/>: a name arrives here only when
/// something reads back what another component wrote. ⚠ <c>customer</c>, <c>batch</c> and <c>key</c>
/// are deliberately absent — nothing reads them yet.
/// </remarks>
public static class AuditTargets
{
    /// <summary>A licence. ⭐ The <c>target_id</c> beside it is the <c>lid</c>.</summary>
    public const string Licence = "licence";
}

/// <summary>The reasons an artifact gets issued. Persisted verbatim, so append-only.</summary>
public static class IssueReasons
{
    /// <summary>The first artifact for a licence.</summary>
    public const string Initial = "initial";

    /// <summary>The expiry moved.</summary>
    public const string Renewal = "renewal";

    /// <summary>Something other than the expiry changed.</summary>
    public const string TermsChange = "terms-change";

    /// <summary>The customer lost their copy. ⚠ Prefer re-EXPORTING the stored artifact over this.</summary>
    public const string ReissueLost = "reissue-lost";
}

/// <summary>
/// Licence statuses. Persisted verbatim, so append-only.
///
/// <para>⭐⭐ <b><c>superseded</c> was here in L3 and is gone as of L5, deliberately.</b> It could never
/// be written: a re-issue keeps the same <c>lid</c>, so the licence ROW is never replaced — only its
/// newest artifact is. The value described something that happens one level down, where it now lives as
/// <see cref="ArtifactStatuses.Superseded"/>, projected from <c>license_current_artifact</c>. ⚠ Removing
/// it does not breach the append-only vocabulary rule: nothing ever persisted it, so no stored row can
/// carry it and no reader can encounter it.</para>
/// </summary>
public static class LicenseStatuses
{
    /// <summary>Current.</summary>
    public const string Active = "active";

    /// <summary>⚠ Bookkeeping in V1 — see <see cref="LicenseRecord.Status"/>.</summary>
    public const string Blocked = "blocked";
}

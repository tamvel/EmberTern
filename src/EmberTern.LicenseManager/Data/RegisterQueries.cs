using System;
using System.Collections.Generic;

namespace EmberTern.LicenseManager.Data;

/// <summary>
/// An artifact's position in its licence's history.
///
/// <para>⭐⭐ <b>This is NOT a column on <c>issued_artifacts</c>, and that is the whole design.</b> The
/// artifact's bytes are immutable — <c>issued_artifacts</c> carries a trigger that aborts every UPDATE
/// and every DELETE, and L3 proved it by reaching past the register's own API. <i>Which</i> artifact is
/// currently the one a customer should be holding is a different fact with a different lifetime: it
/// changes on every re-issue. Storing it as a mutable column would have meant relaxing that trigger,
/// i.e. trading a rule-#11-class guarantee for a piece of bookkeeping.</para>
///
/// <para>So the two facts live apart: the bytes in <c>issued_artifacts</c> (append-only), the pointer in
/// <c>license_current_artifact</c> (one row per <c>lid</c>, rewritten in the same transaction that
/// appends the new artifact). The status below is <b>projected</b> from the join, and the
/// <c>artifact_status</c> view exposes the same projection to any SQL tool that opens the file without
/// this application (§29 — the register must stay readable when the tool will not start).</para>
///
/// <para>⚠ <c>superseded</c> says nothing about cryptography. A superseded artifact still verifies
/// perfectly and still works in the field until its own <c>exp</c>; it is simply no longer the newest one
/// we issued for that licence.</para>
/// </summary>
public static class ArtifactStatuses
{
    /// <summary>The newest artifact issued for this licence — what the customer should be holding.</summary>
    public const string Current = "current";

    /// <summary>Replaced by a later artifact with the same <c>lid</c>. ⭐ Kept forever, never deleted.</summary>
    public const string Superseded = "superseded";
}

/// <summary>
/// What to look for when listing licences across every customer.
///
/// <para>⭐ The register had no cross-customer query before L5 — <c>GetLicenses</c> takes a customer and
/// the UI was built around one. Filtering "everything expiring in the next 30 days" is not a view over
/// that; it is a different question, and it is the one bulk operations are selected from.</para>
///
/// <para>⚠ <b><see cref="Text"/> is matched in memory, not in SQL, and the reason is measured rather than
/// stylistic.</b> SQLite's <c>LIKE</c> and <c>lower()</c> are case-insensitive for ASCII only — by
/// documented design, not by configuration — so <c>ŁÓDŹ</c> would not match <c>Łódź</c> in a register
/// whose customers are Polish companies. .NET's <see cref="StringComparison.OrdinalIgnoreCase"/> applies
/// Unicode case folding and does. The structured filters below stay in SQL where the indexes are; the
/// text match runs over what they return, which for a single-operator tool holding hundreds of licences
/// costs nothing.</para>
/// </summary>
public sealed record LicenseQuery
{
    /// <summary>Everything, newest expiry last.</summary>
    public static LicenseQuery All { get; } = new();

    /// <summary>
    /// Free text: matches the customer's name, e-mail or identifier, or the licence id. Case-insensitive
    /// including diacritics. <see langword="null"/> or blank matches everything.
    /// </summary>
    public string? Text { get; init; }

    /// <summary>The licence row's status (<see cref="LicenseStatuses"/>), or <see langword="null"/> for any.</summary>
    public string? Status { get; init; }

    /// <summary>Only licences expiring strictly before this instant. ⭐ This is the "expiring soon" filter.</summary>
    public DateTimeOffset? ExpiresBefore { get; init; }

    /// <summary>Only licences expiring at or after this instant — pair it with <see cref="ExpiresBefore"/> for a window.</summary>
    public DateTimeOffset? ExpiresFrom { get; init; }

    /// <summary>
    /// <see langword="true"/> for licences with no artifact yet, <see langword="false"/> for those with
    /// one, <see langword="null"/> for both. ⚠ A licence whose terms were saved but never issued is the
    /// one an operator forgets.
    /// </summary>
    public bool? NeverIssued { get; init; }

    /// <summary>
    /// How many rows to return at most. A guard against an accidental full-table read in a list view,
    /// not paging — V1 has no paging and does not need it.
    /// </summary>
    public int Limit { get; init; } = 500;
}

/// <summary>
/// One row of the cross-customer licence list: the terms, who they belong to, and where its issuing
/// history stands — everything the list view and a bulk selection need, without a second round trip.
/// </summary>
public sealed record LicenseSummary
{
    /// <summary>The licence terms as the register holds them.</summary>
    public required LicenseRecord License { get; init; }

    /// <summary>⭐ The name that gets signed into every artifact for this licence (D6).</summary>
    public required string CustomerName { get; init; }

    /// <summary>The customer's contact address, when there is one. ⏭ L6 sends here.</summary>
    public string? CustomerEmail { get; init; }

    /// <summary>The contact person's first name, when recorded.</summary>
    public string? CustomerFirstName { get; init; }

    /// <summary>
    /// The contact person's last name, when recorded.
    ///
    /// <para>⭐ Carried on the SUMMARY, not fetched per row: an operator looking for "the licence
    /// Kowalski called about" has a person, not a company, and a list that cannot answer that sends them
    /// to the customers view to translate the name first.</para>
    /// </summary>
    public string? CustomerLastName { get; init; }

    /// <summary>How many artifacts have ever been signed for this licence.</summary>
    public required int ArtifactCount { get; init; }

    /// <summary>When the newest one was signed, or <see langword="null"/> if it has never been issued.</summary>
    public DateTimeOffset? LastIssuedAt { get; init; }

    /// <summary>The <c>artifact_id</c> currently marked <see cref="ArtifactStatuses.Current"/>, if any.</summary>
    public long? CurrentArtifactId { get; init; }

    /// <summary>True when the licence has terms but no artifact — saved and forgotten.</summary>
    public bool NeverIssued => ArtifactCount == 0;
}

/// <summary>
/// What slice of the history to read.
///
/// <para>⭐ L3's <c>GetAudit</c> answered only <i>"the newest 200 lines of everything"</i>, which is the
/// one shape a support question never takes. The question is always about a subject: <i>what happened to
/// THIS licence</i>, <i>what did we ever do for THIS customer</i>.</para>
/// </summary>
public sealed record AuditQuery
{
    /// <summary>The newest entries, whatever they are about.</summary>
    public static AuditQuery All { get; } = new();

    /// <summary><c>customer</c> · <c>licence</c> · <c>batch</c> · <c>key</c>, or <see langword="null"/> for any.</summary>
    public string? TargetType { get; init; }

    /// <summary>Which subject, or <see langword="null"/> for all of that type.</summary>
    public string? TargetId { get; init; }

    /// <summary>A specific action, e.g. <c>licence.issued</c>. <see langword="null"/> for any.</summary>
    public string? Action { get; init; }

    /// <summary>How many entries at most, newest first.</summary>
    public int Limit { get; init; } = 200;
}

/// <summary>
/// One licence's share of a batch: the artifact that was signed for it, and — when the operation changed
/// them — the terms to store alongside it.
///
/// <para>⭐⭐ <b>The unit deliberately carries a SIGNED artifact rather than the instructions for making
/// one.</b> Signing is a pure function of key, terms and clock: it writes nothing and touches nothing, so
/// a failure while signing leaves the register exactly as it was. Recording is the step that cannot be
/// half-done. Keeping them in this order — sign everything first, then commit everything once — is what
/// makes "a signed artifact with no register row" unreachable rather than merely unlikely. See
/// <see cref="LicenseRegister.ApplyIssueBatch"/>.</para>
/// </summary>
public sealed record LicenseIssueUnit
{
    /// <summary>The signed artifact to append. ⭐ Its <c>Token</c> is already final and verified.</summary>
    public required IssuedArtifactRecord Artifact { get; init; }

    /// <summary>
    /// The licence terms to save with it, or <see langword="null"/> when this issue changed nothing about
    /// the terms. ⚠ <see langword="null"/> matters: it keeps a plain re-issue from writing a
    /// <c>licence.updated</c> history line that says nothing changed.
    /// </summary>
    public LicenseRecord? UpdatedTerms { get; init; }

    /// <summary>
    /// ⭐ The terms in one sentence — <i>"Licensed to ACME, 5 seat(s), until 2028-01-01."</i> — written
    /// onto this licence's own <c>licence.issued</c> audit line.
    ///
    /// <para>⭐⭐ <b>It is required because a batch used to be a second-class citizen of the audit.</b>
    /// The single issuing path has always written this sentence; a batch wrote only <c>"batch &lt;id&gt;"</c>,
    /// so the one thing the summary exists for — letting the audit answer <i>"on what terms?"</i> without
    /// joining anything — was exactly the thing twenty licences at a time lost. Making it required rather
    /// than optional is deliberate: an optional field is one a later caller omits, and the gap returns
    /// silently.</para>
    ///
    /// <para>⚠ The sentence is composed by <see cref="IssuingWorkflow"/>, which knows the customer;
    /// <see cref="LicenseRegister"/> appends the batch marker to it and never invents it. ⛔ The register
    /// must not derive it from the payload — that would make the component whose job is "record what you
    /// are told" start paraphrasing.</para>
    /// </summary>
    public required string Summary { get; init; }
}

/// <summary>What a batch actually did, once it is committed and therefore true.</summary>
/// <param name="BatchId">Correlates every history line the operation wrote.</param>
/// <param name="Artifacts">The stored artifacts, with their identities and <c>current</c> status.</param>
public sealed record IssueBatchResult(string BatchId, IReadOnlyList<IssuedArtifactRecord> Artifacts);

/// <summary>
/// A structural problem found in the register itself, rather than in what an operator asked of it.
///
/// <para>⚠ Distinct from an ordinary argument fault on purpose: this one means the FILE disagrees with
/// itself, which is the condition a restore has to refuse on (⏭ L5.5) and an operator has to be told
/// about rather than shielded from.</para>
/// </summary>
public sealed class RegisterIntegrityException : Exception
{
    /// <summary>Creates the exception.</summary>
    public RegisterIntegrityException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner cause.</summary>
    public RegisterIntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Required by the exception design guidelines.</summary>
    public RegisterIntegrityException()
        : base("The register is inconsistent.")
    {
    }
}

/// <summary>
/// The stored pointer that decides which artifact a licence's customer should be holding.
///
/// <para>⭐ One row per <c>lid</c> in <c>license_current_artifact</c>, rewritten in the same transaction
/// that appends a newer artifact. ⚠ This is the record as STORED — <see cref="SetAt"/> is when the
/// pointer last moved, which is a different fact from the artifact's own <c>iat</c> and is the one an
/// export would otherwise lose.</para>
/// </summary>
public sealed record CurrentArtifactPointer
{
    /// <summary>Which licence.</summary>
    public required string LicenseId { get; init; }

    /// <summary>Which artifact is current. ⭐ References <c>issued_artifacts.artifact_id</c>.</summary>
    public required long ArtifactId { get; init; }

    /// <summary>When the pointer was last moved here.</summary>
    public required DateTimeOffset SetAt { get; init; }
}

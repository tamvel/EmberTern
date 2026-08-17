using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Licensing;
using EmberTern.LicenseManager.Data;
using EmberTern.LicenseManager.Services;

namespace EmberTern.LicenseManager.ViewModels;

/// <summary>
/// One line of a licence's issuing history, already in the words the operator reads.
///
/// <para>⚠ No Avalonia types (Architecture rule 1) — the view turns <see cref="IsCurrent"/> into a chip,
/// and everything else here is a string.</para>
/// </summary>
public sealed record ArtifactListItem
{
    private const string StampFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>The register row, verbatim. ⭐ The item PRESENTS it and never restates it.</summary>
    public required IssuedArtifactRecord Artifact { get; init; }

    /// <summary>When it was signed, to the second — the <c>iat</c> the artifact itself carries.</summary>
    public required string IssuedAt { get; init; }

    /// <summary>Why it was issued: <c>initial</c> · <c>renewal</c> · <c>terms-change</c> · <c>reissue-lost</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>Which key signed it.</summary>
    public required string KeyId { get; init; }

    /// <summary>The register's own row number, which is also the issuing order.</summary>
    public required string Ordinal { get; init; }

    /// <summary>
    /// ⭐ Whether this is the artifact <c>license_current_artifact</c> points at.
    ///
    /// <para>⛔ Read from the register's projection (<see cref="ArtifactStatuses"/>), never decided here
    /// by comparing dates. The pointer is the authority on which artifact is current; a view that
    /// recomputed it from "the newest one" would be right until the day it was not, and would then
    /// disagree with the <c>artifact_status</c> view that §29's recovery path promises.</para>
    /// </summary>
    public required bool IsCurrent { get; init; }

    /// <summary>
    /// What this artifact's standing is, in words.
    ///
    /// <para>⭐⭐ "Superseded" is deliberately a NEUTRAL statement of fact, not a warning and not a
    /// deletion. An earlier release was really sent, to a real customer, who may still be running it —
    /// the whole reason <c>issued_artifacts</c> is append-only is so the register can still answer for
    /// it. ⛔ Nothing in this application may present it as removed, replaced or invalid.</para>
    /// </summary>
    public required string Standing { get; init; }
}

/// <summary>
/// A licence's issuing history, and the detail of whichever artifact the operator is looking at.
///
/// <para>⭐ A separate view model from <see cref="ShellViewModel"/> for the reason §40.1 gave when
/// <see cref="LicenseBrowserViewModel"/> was split out: it organises around a different question.
/// The licence form answers <i>"what are the terms?"</i>; this answers <i>"what have we actually sent,
/// and what exactly was in it?"</i> — a question about artifacts, which are immutable, plural and
/// ordered, where the terms are singular and editable.</para>
///
/// <para>⛔⛔ <b>It is READ-ONLY over the history, and that is a design guarantee rather than a stage
/// boundary.</b> There is no delete, no edit, no "clean up old artifacts": <c>issued_artifacts</c>
/// aborts every UPDATE and DELETE by trigger (§39.2), and a surface offering an action the database
/// refuses would be an invitation to a stack trace. Export writes a copy OUT; nothing here writes back.</para>
/// </summary>
public sealed partial class ArtifactHistoryViewModel : ObservableObject
{
    private const string StampFormat = "yyyy-MM-dd HH:mm:ss";
    private const string DateFormat = "yyyy-MM-dd";

    private readonly LicenseRegister _register;
    private readonly IssuingWorkflow _workflow;
    private readonly SigningSession _session;

    private string? _licenseId;

    /// <summary>Creates the history over a register, sharing the shell's workflow and unlocked key.</summary>
    public ArtifactHistoryViewModel(
        LicenseRegister register, IssuingWorkflow workflow, SigningSession session)
    {
        _register = register ?? throw new ArgumentNullException(nameof(register));
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    /// <summary>Every artifact ever issued for the licence, newest first.</summary>
    public ObservableCollection<ArtifactListItem> Artifacts { get; } = [];

    /// <summary>Which one the operator is looking at.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ArtifactListItem? _selectedArtifact;

    /// <summary>Whether there is an artifact to show detail for.</summary>
    public bool HasSelection => SelectedArtifact is not null;

    /// <summary>Whether this licence has ever been issued.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _hasArtifacts;

    /// <summary>
    /// ⭐ The empty state is a STATED fact, not an absent list. "Nothing here" and "never issued" look
    /// identical when a panel simply disappears, and only one of them is information.
    /// </summary>
    public bool IsEmpty => !HasArtifacts;

    /// <summary>One line summarising the whole history, for the operator who is not reading the list.</summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    // ── The selected artifact, in detail ─────────────────────────────────────────────────────────────

    /// <summary>What EmberTern would say about this artifact TODAY, from the real verifier.</summary>
    [ObservableProperty]
    private string _verdict = string.Empty;

    /// <summary>The verdict's severity, so the view can paint it without interpreting the words.</summary>
    [ObservableProperty]
    private MessageSeverity _verdictSeverity = MessageSeverity.Info;

    /// <summary>The name signed into this artifact — which may differ from the customer's name today.</summary>
    [ObservableProperty]
    private string _licensee = string.Empty;

    /// <summary>Contractual seats, as signed.</summary>
    [ObservableProperty]
    private string _seats = string.Empty;

    /// <summary>The validity window, as signed.</summary>
    [ObservableProperty]
    private string _validity = string.Empty;

    /// <summary>Product and payload version, as signed.</summary>
    [ObservableProperty]
    private string _product = string.Empty;

    /// <summary>Which key signed it, and with which algorithm.</summary>
    [ObservableProperty]
    private string _signedWith = string.Empty;

    /// <summary>The size of the delivered token, in bytes.</summary>
    [ObservableProperty]
    private string _tokenSize = string.Empty;

    /// <summary>The full <c>ETL1.…</c> token, verbatim — copyable, never edited.</summary>
    [ObservableProperty]
    private string _token = string.Empty;

    /// <summary>The exact JSON that was signed, as the register stored it.</summary>
    [ObservableProperty]
    private string _payloadJson = string.Empty;

    // ── Loading ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the history for a licence, or clears it when there is no licence selected.
    ///
    /// <para>⚠ Re-read on every call rather than cached: an issue, a re-export and a batch all change what
    /// this shows, and a cache would need every one of them to remember it exists.</para>
    /// </summary>
    public void Load(string? licenseId)
    {
        _licenseId = licenseId;

        var previous = SelectedArtifact?.Artifact.ArtifactId;

        Artifacts.Clear();
        SelectedArtifact = null;

        if (string.IsNullOrEmpty(licenseId))
        {
            HasArtifacts = false;
            Summary = string.Empty;
            return;
        }

        foreach (var artifact in _register.GetArtifacts(licenseId))
        {
            Artifacts.Add(Present(artifact));
        }

        HasArtifacts = Artifacts.Count > 0;
        Summary = BuildSummary();

        // ⭐ The operator keeps looking at the artifact they were looking at. The list is rebuilt from the
        //    register on every load, so the item is a different instance even when it is the same row —
        //    matching on identity rather than on reference is what stops a re-export from closing the
        //    detail the operator had open. (§40.3 point 4 learned this on the licences list.)
        if (previous is { } id)
        {
            SelectedArtifact = Artifacts.FirstOrDefault(a => a.Artifact.ArtifactId == id);
        }
    }

    /// <summary>Re-reads the history for whichever licence is loaded.</summary>
    public void Reload() => Load(_licenseId);

    /// <summary>
    /// Selects the artifact the register marks current, and returns it.
    ///
    /// <para>⭐ Used by "Inspect latest" so that the command and the history show the SAME artifact —
    /// otherwise the detail pane and the message could name two different releases. ⛔ Keyed on the
    /// register's pointer, not on <c>Artifacts[0]</c>.</para>
    /// </summary>
    public ArtifactListItem? SelectCurrent()
    {
        SelectedArtifact = Artifacts.FirstOrDefault(a => a.IsCurrent);
        return SelectedArtifact;
    }

    private ArtifactListItem Present(IssuedArtifactRecord artifact)
    {
        var isCurrent = string.Equals(
            artifact.Status, ArtifactStatuses.Current, StringComparison.Ordinal);

        return new ArtifactListItem
        {
            Artifact = artifact,
            IssuedAt = artifact.IssuedAt.ToString(StampFormat, CultureInfo.InvariantCulture),
            Reason = artifact.Reason,
            KeyId = artifact.KeyId,
            Ordinal = "#" + artifact.ArtifactId.ToString(CultureInfo.InvariantCulture),
            IsCurrent = isCurrent,
            Standing = isCurrent ? "current" : "superseded",
        };
    }

    private string BuildSummary()
    {
        if (Artifacts.Count == 0)
        {
            return "Never issued. Nothing has been sent to the customer for this licence.";
        }

        // ⭐ Says the append-only guarantee out loud. The operator's question behind this whole surface is
        //    "did re-issuing overwrite what I sent them before?" — and the answer is a property of the
        //    schema, so it is worth stating rather than leaving to be inferred from a list of rows.
        var kept = Artifacts.Count == 1
            ? "1 issue on record"
            : $"{Artifacts.Count} issues on record, all kept";

        return $"{kept}. The current file is the one marked below; earlier ones were superseded, " +
               "never overwritten or deleted.";
    }

    partial void OnSelectedArtifactChanged(ArtifactListItem? value)
    {
        if (value is null)
        {
            Verdict = string.Empty;
            VerdictSeverity = MessageSeverity.Info;
            Licensee = Seats = Validity = Product = SignedWith = TokenSize = string.Empty;
            Token = PayloadJson = string.Empty;
            return;
        }

        var artifact = value.Artifact;

        Token = artifact.Token;
        PayloadJson = artifact.PayloadJson;

        // ⭐ The delivered size, measured from the token that would actually be written — not from the
        //    string's character count. `SaveArtifact` writes UTF-8 with no BOM, and the armor is what
        //    goes in the file, so this is the number the customer receives.
        TokenSize = string.Create(CultureInfo.InvariantCulture,
            $"{System.Text.Encoding.UTF8.GetByteCount(LicenseArmor.Wrap(artifact.Token))} bytes as delivered");

        // ⭐⭐ TWO SOURCES, EACH ANSWERING WHAT ONLY IT CAN.
        //
        //    The FIELDS come from the stored payload through `LicensePayload.TryParse` — the same parser
        //    the client uses — so they are readable even for an artifact the verifier refuses, which is
        //    exactly the artifact a support call is about.
        //
        //    The VERDICT comes from the real `LicenseVerifier`, through the workflow's own Inspect. ⛔ It
        //    is never recomputed here from the dates above: "would EmberTern accept this today?" is the
        //    product's opinion, and an administrative tool that answers it with its own arithmetic will
        //    eventually disagree with the product in front of a customer.
        // ⚠ Re-encoding the stored string is LOSSLESS rather than lucky: the register holds
        //    `Encoding.UTF8.GetString(issued.PayloadJson)` — a decode of the exact bytes that were
        //    signed — so encoding it back reproduces them. ⛔ Do not "improve" this into storing the
        //    bytes separately; the JSON in the register is the signed payload, and one representation
        //    is the point.
        if (LicensePayload.TryParse(
                System.Text.Encoding.UTF8.GetBytes(artifact.PayloadJson), out var payload, out _))
        {
            Licensee = payload.Licensee;
            Seats = payload.Seats == 1 ? "1 seat" : $"{payload.Seats} seats";
            Validity =
                $"{payload.NotBefore.ToString(DateFormat, CultureInfo.InvariantCulture)} → " +
                $"{payload.ExpiresAt.ToString(DateFormat, CultureInfo.InvariantCulture)}";
            Product = $"{payload.Product}, payload v{payload.Version}";
            SignedWith = $"{payload.KeyId} · {payload.AlgorithmId}";
        }
        else
        {
            // ⚠ Stated, not hidden. A payload the parser cannot read is the single most interesting row in
            //    the register, and blanking the fields would present it as an ordinary one.
            Licensee = Seats = Validity = Product = "— unreadable payload —";
            SignedWith = artifact.KeyId;
        }

        var verdict = _workflow.Inspect(_session, artifact);
        var described = VerdictText.Describe(verdict);
        Verdict = described.Text;
        VerdictSeverity = described.Severity;
    }
}

using System;
using System.Globalization;
using EmberTern.Core.Diagnostics;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of one <see cref="TransactionInfo"/> + its
/// <see cref="TransactionHealthEntry"/> for the Transactions grid. Immutable — rebuilt each poll.</summary>
public sealed class TransactionRowViewModel
{
    private readonly TransactionInfo _t;
    private readonly TransactionHealthEntry _h;

    public TransactionRowViewModel(TransactionInfo transaction, TransactionHealthEntry health, string sessionLabel, DateTime referenceTime)
    {
        _t = transaction;
        _h = health;
        SessionLabel = sessionLabel;
        AgeValue = _t.StartedAt is { } start ? Math.Max(0, (referenceTime - start).TotalSeconds) : 0;
        AgeText = _t.StartedAt is not null ? DiagnosticsFormat.Age(AgeValue) : string.Empty;
    }

    // Numeric sort keys (gotcha #42).
    public double AgeValue { get; }
    public long GcImpactValue => _h.GcImpact;

    public long TransactionId => _t.TransactionId;
    public long AttachmentId => _t.AttachmentId;
    public string SessionLabel { get; }

    // --- Health dot (always on — never blank; tooltip explains the state). Mirrors the
    // Sessions grid Health column so the two grids read the same way. ---
    public bool IsGcBlocker => _h.IsGcBlocker;
    public bool IsLong => _h.IsLong && !_h.IsGcBlocker;

    public string HealthBrushKey =>
        _h.IsGcBlocker ? "DangerIconBrush"        // 🔴 the OAT gatekeeper — blocking GC
        : IsLong ? "WarningBrush"                 // 🟠 long-running
        : "SubtleForegroundBrush";                // ⚪ normal transaction

    public string HealthTooltip =>
        _h.IsGcBlocker ? UiStrings.SessionManagerTxHealthGcBlocker
        : IsLong ? UiStrings.SessionManagerTxHealthLong
        : UiStrings.SessionManagerTxHealthNormal;

    // --- columns ---
    public string TransactionIdText => _t.TransactionId.ToString(CultureInfo.InvariantCulture);
    public string StateText => _t.IsActive ? "Active" : "Idle";
    public string AgeText { get; }
    public string IsolationText => _t.IsolationMode;
    public string ReadOnlyText => _t.ReadOnly ? "Yes" : "No";
    public string GcImpactText => _h.GcImpact.ToString("N0", CultureInfo.CurrentCulture);

    public string ToTsv() => string.Join('\t',
        TransactionIdText, SessionLabel, StateText, AgeText, IsolationText, ReadOnlyText, GcImpactText);
}

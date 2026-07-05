using System;
using EmberTern.App;
using EmberTern.App.ViewModels;
using EmberTern.Core.Diagnostics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The final-polish pass on the Session Manager: the Transactions grid Health dot (every visible
/// column communicates — no blank stripe), the Sessions grid Health dot, and the transaction-gap
/// GAUGE (scale-before-alarm — a small/normal gap must not look alarming, and the gauge's severity
/// can never contradict the Health-Bar verdict).
/// </summary>
public class SessionManagerPolishTests
{
    private static readonly DateTime Now = new(2026, 7, 4, 12, 0, 0, DateTimeKind.Utc);

    private static TransactionRowViewModel TxRow(bool gcBlocker, bool isLong)
    {
        var tx = new TransactionInfo { TransactionId = 100, AttachmentId = 7 };
        var h = new TransactionHealthEntry(100, GcImpact: 0, IsGcBlocker: gcBlocker, IsLong: isLong, Severity: null);
        return new TransactionRowViewModel(tx, h, "7", Now);
    }

    // ---- Task 1: Transactions grid — always-on Health dot (never blank) ----

    [Fact]
    public void TxHealth_GcBlocker_IsRed()
    {
        var r = TxRow(gcBlocker: true, isLong: true);
        Assert.Equal("DangerIconBrush", r.HealthBrushKey);
        Assert.Equal(UiStrings.SessionManagerTxHealthGcBlocker, r.HealthTooltip);
    }

    [Fact]
    public void TxHealth_Long_IsAmber()
    {
        var r = TxRow(gcBlocker: false, isLong: true);
        Assert.Equal("WarningBrush", r.HealthBrushKey);
        Assert.Equal(UiStrings.SessionManagerTxHealthLong, r.HealthTooltip);
    }

    [Fact]
    public void TxHealth_Normal_IsSubtle_NotBlank()
    {
        var r = TxRow(gcBlocker: false, isLong: false);
        Assert.Equal("SubtleForegroundBrush", r.HealthBrushKey);   // a dot, never empty
        Assert.Equal(UiStrings.SessionManagerTxHealthNormal, r.HealthTooltip);
    }

    // ---- Sessions grid — Health dot (self / system / risk) ----

    private static SessionRowViewModel Session(bool self, string host, SessionRisk risk)
    {
        var s = new SessionInfo { AttachmentId = 5, IsSelf = self, Host = host };
        var h = new SessionHealthEntry(5, risk, 0, null);
        return new SessionRowViewModel(s, h, Now);
    }

    [Fact]
    public void SessionHealth_Self_IsInfoBlue()
        => Assert.Equal("InfoIconBrush", Session(self: true, "10.0.0.1", SessionRisk.None).HealthBrushKey);

    [Fact]
    public void SessionHealth_SystemInternal_NoHost_IsSubtle()
        => Assert.Equal("SubtleForegroundBrush", Session(self: false, host: "", SessionRisk.None).HealthBrushKey);

    [Fact]
    public void SessionHealth_HealthyUser_IsGreen()
        => Assert.Equal("SuccessIconBrush", Session(self: false, "10.0.0.1", SessionRisk.None).HealthBrushKey);

    [Fact]
    public void SessionHealth_GcBlocker_IsRed()
        => Assert.Equal("DangerIconBrush", Session(self: false, "10.0.0.1", SessionRisk.GcBlocker).HealthBrushKey);

    // ---- Task 2: transaction-gap gauge — scale-before-alarm ----

    private const long Danger = 10_000; // == SessionHealthOptions.Default.LargeGapThreshold

    [Fact]
    public void Gauge_SmallNormalGap_IsCalm_NotAlarming()
    {
        // The reported scenario: a 59-transaction gap while the Health Bar says Healthy.
        var (frac, key, status) = SessionManagerTabViewModel.ResolveGapGauge(59, Danger);
        Assert.Equal("SubtleForegroundBrush", key);                 // grey, never permanent orange
        Assert.Equal(UiStrings.SessionManagerGapStatusHealthy, status);
        Assert.True(frac < 0.01);                                   // a barely-there sliver
    }

    [Fact]
    public void Gauge_ApproachingBudget_IsWatch()
    {
        var (_, key, status) = SessionManagerTabViewModel.ResolveGapGauge(6_000, Danger);
        Assert.Equal("WarningBrush", key);
        Assert.Equal(UiStrings.SessionManagerGapStatusWatch, status);
    }

    [Fact]
    public void Gauge_AtOrOverBudget_IsCritical_AndFull()
    {
        var (frac, key, status) = SessionManagerTabViewModel.ResolveGapGauge(25_000, Danger);
        Assert.Equal("DangerIconBrush", key);
        Assert.Equal(UiStrings.SessionManagerGapStatusCritical, status);
        Assert.Equal(1.0, frac);                                   // clamped full
    }

    [Fact]
    public void Gauge_SeverityAgreesWithEngineThreshold()
    {
        // Below LargeGapThreshold the engine reports no GC risk — the gauge must not be red there.
        var (_, key, _) = SessionManagerTabViewModel.ResolveGapGauge(Danger - 1, Danger);
        Assert.NotEqual("DangerIconBrush", key);
    }
}

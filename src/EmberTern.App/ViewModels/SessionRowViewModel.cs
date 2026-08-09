using System;
using System.Globalization;
using EmberTern.Core.Diagnostics;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of one <see cref="SessionInfo"/> + its
/// <see cref="SessionHealthEntry"/> for the Sessions grid + Session Details. Immutable —
/// rebuilt each poll.</summary>
public sealed class SessionRowViewModel
{
    private readonly SessionInfo _s;
    private readonly SessionHealthEntry _h;

    public SessionRowViewModel(SessionInfo session, SessionHealthEntry health, DateTime referenceTime)
    {
        _s = session;
        _h = health;
        ConnectedText = session.ConnectedAt is { } at
            ? DiagnosticsFormat.Age(Math.Max(0, (referenceTime - at).TotalSeconds))
            : string.Empty;
    }

    public long AttachmentId => _s.AttachmentId;
    public bool IsSelf => _s.IsSelf;
    public long? ActiveStatementId => _s.ActiveStatementId;
    public string CurrentStatement => _s.CurrentStatement;
    public bool HasStatement => !string.IsNullOrEmpty(_s.CurrentStatement);
    public SessionRisk Risk => _h.Risk;

    // --- Health state (the Health column always shows a dot; also drives the Session Details verdict) ---
    // A Firebird internal attachment (Cache Writer / Garbage Collector) has no remote endpoint —
    // classify it grey so those rows read as "system", not "healthy user session".
    public bool IsSystem => !IsSelf && string.IsNullOrEmpty(_s.Host);

    public string HealthBrushKey =>
        IsSelf ? "InfoIconBrush"                    // 🔵 EmberTern's own
        : IsSystem ? "SubtleForegroundBrush"        // ⚪ Firebird internal
        : _h.Risk switch
        {
            SessionRisk.GcBlocker => "DangerIconBrush",       // 🔴
            SessionRisk.LongTransaction => "WarningBrush",    // 🟠
            _ => "SuccessIconBrush",                          // 🟢 healthy user session
        };

    public string HealthTooltip =>
        IsSelf ? UiStrings.SessionManagerHealthSelf
        : IsSystem ? UiStrings.SessionManagerHealthSystem
        : _h.Risk switch
        {
            SessionRisk.GcBlocker => UiStrings.SessionManagerHealthGcRisk,
            SessionRisk.LongTransaction => UiStrings.SessionManagerHealthWarning,
            _ => UiStrings.SessionManagerHealthHealthy,
        };

    // --- columns ---
    public string IdText => IsSelf
        ? _s.AttachmentId.ToString(CultureInfo.InvariantCulture) + UiStrings.SessionRowSelfSuffix
        : _s.AttachmentId.ToString(CultureInfo.InvariantCulture);

    public string User => _s.User;
    public string Role => _s.Role;
    public string Application => _s.ApplicationName;
    public string Host => _s.Host;
    public string StateText => _s.IsActive ? "Active" : "Idle";
    public string ActiveTxText => _h.ActiveTransactionCount.ToString(CultureInfo.InvariantCulture);
    public string OldestTxText => DiagnosticsFormat.Age(_h.OldestTransactionAgeSeconds);

    /// <summary>Time since the attachment connected (empty when unknown).</summary>
    public string ConnectedText { get; }

    // Activity breakdown (lifetime totals since connect) for the Session Details Activity section.
    public string SequentialReadsText => _s.SequentialReads.ToString("N0", CultureInfo.CurrentCulture);
    public string IndexedReadsText => _s.IndexedReads.ToString("N0", CultureInfo.CurrentCulture);
    public string InsertsText => _s.Inserts.ToString("N0", CultureInfo.CurrentCulture);
    public string UpdatesText => _s.Updates.ToString("N0", CultureInfo.CurrentCulture);
    public string DeletesText => _s.Deletes.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>One-line session verdict + dot for the Session Details General section — the same
    /// health state as the grid Health column.</summary>
    public string RiskText => HealthTooltip;
    public string RiskBrushKey => HealthBrushKey;

    /// <summary>Cumulative records touched since the session connected (not a rate — V1 shows the
    /// lifetime total; the inter-poll rate + heavy classification are a V2 feature).</summary>
    public string LoadText => _s.Load.ToString("N0", CultureInfo.CurrentCulture);

    // Numeric sort keys (SortMemberPath targets — the display strings sort lexically; gotcha #42).
    public int ActiveTxValue => _h.ActiveTransactionCount;
    public double OldestTxValue => _h.OldestTransactionAgeSeconds ?? 0;
    public long LoadValue => _s.Load;

    /// <summary>Free-text filter target (user / application / host).</summary>
    public string FilterKey => (_s.User + " " + _s.ApplicationName + " " + _s.Host).ToLowerInvariant();

    /// <summary>TSV for the copy action (columns match the grid, self marker included).</summary>
    public string ToTsv() => string.Join('\t',
        IdText, User, Application, Host, StateText, ActiveTxText, OldestTxText, LoadText);
}

using System.Globalization;
using EmberTern.Core.Diagnostics;

namespace EmberTern.App.ViewModels;

/// <summary>Read-only projection of one <see cref="SessionInfo"/> + its
/// <see cref="SessionHealthEntry"/> for the Sessions grid. Immutable — rebuilt each poll.</summary>
public sealed class SessionRowViewModel
{
    private readonly SessionInfo _s;
    private readonly SessionHealthEntry _h;

    public SessionRowViewModel(SessionInfo session, SessionHealthEntry health)
    {
        _s = session;
        _h = health;
    }

    public long AttachmentId => _s.AttachmentId;
    public bool IsSelf => _s.IsSelf;
    public long? ActiveStatementId => _s.ActiveStatementId;
    public string CurrentStatement => _s.CurrentStatement;
    public bool HasStatement => !string.IsNullOrEmpty(_s.CurrentStatement);
    public SessionRisk Risk => _h.Risk;

    // --- risk stripe (one of three, DynamicResource brushes — mirrors the trace gutter) ---
    public bool IsGcBlocker => _h.Risk == SessionRisk.GcBlocker;
    public bool IsLongTransaction => _h.Risk == SessionRisk.LongTransaction;
    public bool IsHeavyRisk => _h.Risk == SessionRisk.Heavy;

    // --- columns ---
    public string IdText => IsSelf
        ? _s.AttachmentId.ToString(CultureInfo.InvariantCulture) + " · self"
        : _s.AttachmentId.ToString(CultureInfo.InvariantCulture);

    public string User => _s.User;
    public string Role => _s.Role;
    public string Application => _s.ApplicationName;
    public string Host => _s.Host;
    public string StateText => _s.IsActive ? "Active" : "Idle";
    public string ActiveTxText => _h.ActiveTransactionCount.ToString(CultureInfo.InvariantCulture);
    public string OldestTxText => DiagnosticsFormat.Age(_h.OldestTransactionAgeSeconds);

    public bool IsHeavy => _h.IsHeavy;
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

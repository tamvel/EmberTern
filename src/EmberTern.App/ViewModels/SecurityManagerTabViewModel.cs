using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using EmberTern.Core.Metadata;
using EmberTern.Core.Security;
using EmberTern.Firebird;

namespace EmberTern.App.ViewModels;

/// <summary>
/// The unified Security Manager workspace tab: four panes — Users (server-level),
/// Roles, Membership, Privileges. Not a singleton — a tab is keyed by the context
/// object it was opened from (a user or a role), so multiple contexts coexist; the
/// context only sets the initial sub-tab + selection. All changes apply immediately
/// (each GRANT/REVOKE/CREATE/ALTER executes + auto-commits via the autonomous DDL
/// executor — admin tooling, no buffered "compile" step).
/// </summary>
public partial class SecurityManagerTabViewModel : ViewModelBase
{
    public const int UsersTabIndex = 0;
    public const int RolesTabIndex = 1;
    public const int MembershipTabIndex = 2;
    public const int PrivilegesTabIndex = 3;

    private readonly FirebirdSecurityReader _security;
    private readonly FirebirdMetadataReader _metadata;
    private readonly FirebirdDdlExecutor _ddl;
    private readonly MetadataObject? _context;

    public SecurityManagerTabViewModel(
        FirebirdSecurityReader security,
        FirebirdMetadataReader metadata,
        FirebirdDdlExecutor ddl,
        MetadataObject? context = null)
    {
        _security = security;
        _metadata = metadata;
        _ddl = ddl;
        _context = context;

        Users = new SecurityUsersPaneViewModel(this);
        Roles = new SecurityRolesPaneViewModel(this);
        Membership = new SecurityMembershipPaneViewModel(this);
        Privileges = new SecurityPrivilegesPaneViewModel(this);

        ActiveSubTabIndex = context?.Kind == MetadataObjectKind.Role ? PrivilegesTabIndex : UsersTabIndex;
    }

    public SecurityUsersPaneViewModel Users { get; }
    public SecurityRolesPaneViewModel Roles { get; }
    public SecurityMembershipPaneViewModel Membership { get; }
    public SecurityPrivilegesPaneViewModel Privileges { get; }

    /// <summary>All grantees (users ∪ roles) — shared by the Membership and
    /// Privileges panes' grantee pickers. Rebuilt on every load. Same instance for
    /// the lifetime of the tab (only Clear+Add), so picker bindings stay stable.</summary>
    public ObservableCollection<GranteeOptionViewModel> Grantees { get; } = new();

    [ObservableProperty]
    private int _activeSubTabIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    [ObservableProperty]
    private bool _isLoading;

    // Dialog/confirmation seams — the view fulfils these (it owns the windows).
    public event Func<ConfirmRequest, Task<bool>>? ConfirmationRequested;
    public event Func<UserInfo?, Task<UserEditResult?>>? UserEditRequested;
    public event Func<Task<string?>>? NewRoleRequested;

    internal FirebirdSecurityReader Security => _security;
    internal FirebirdMetadataReader MetadataReader => _metadata;

    private Task? _loadTask;
    public Task EnsureLoadedAsync(CancellationToken ct = default) => _loadTask ??= LoadAsync(ct);

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        _loadTask = null;
        await EnsureLoadedAsync(ct).ConfigureAwait(true);
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = null;
        // Sequential — FbConnection services one command at a time (gotcha #31).
        // Per-step isolation (gotcha #24): a SEC$USERS permission failure (non-admin
        // login) must not blank the Roles / Membership / Privileges panes too. The
        // first error is surfaced; the rest still load.
        await SafeLoadAsync(() => Users.LoadAsync(ct)).ConfigureAwait(true);
        await SafeLoadAsync(() => Roles.LoadAsync(ct)).ConfigureAwait(true);
        RebuildGrantees();
        await SafeLoadAsync(() => Membership.LoadAsync(ct)).ConfigureAwait(true);
        await SafeLoadAsync(() => Privileges.LoadAsync(ct)).ConfigureAwait(true);
        ApplyContextSelection();
        IsLoading = false;
    }

    private async Task SafeLoadAsync(Func<Task> step)
    {
        try
        {
            await step().ConfigureAwait(true);
        }
        catch (MetadataReadException ex)
        {
            ErrorMessage ??= ex.Message;
        }
    }

    private void RebuildGrantees()
    {
        Grantees.Clear();
        foreach (var u in Users.Items)
            Grantees.Add(new GranteeOptionViewModel(new GranteeRef(u.UserName, GranteeType.User)));
        foreach (var r in Roles.Items)
            Grantees.Add(new GranteeOptionViewModel(new GranteeRef(r.Name, GranteeType.Role)));
    }

    private void ApplyContextSelection()
    {
        if (_context is null) return;
        if (_context.Kind == MetadataObjectKind.User)
        {
            Users.SelectByName(_context.Name);
        }
        else if (_context.Kind == MetadataObjectKind.Role)
        {
            Roles.SelectByName(_context.Name);
            Privileges.SelectGranteeByName(_context.Name, GranteeType.Role);
            Membership.SelectGranteeByName(_context.Name, GranteeType.Role);
        }
    }

    // ─── Shared apply / dialog helpers used by the panes ───────────────────

    /// <summary>Runs a single DDL/DCL statement (autonomous auto-commit). Returns
    /// false and sets <see cref="ErrorMessage"/> on failure; clears it on success.</summary>
    internal async Task<bool> ApplyDdlAsync(string sql)
    {
        try
        {
            await _ddl.ExecuteAsync(sql).ConfigureAwait(true);
            ErrorMessage = null;
            return true;
        }
        catch (DdlExecutionException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    internal Task<bool> ConfirmAsync(ConfirmRequest request)
        => ConfirmationRequested?.Invoke(request) ?? Task.FromResult(true);

    internal Task<UserEditResult?> RequestUserEditAsync(UserInfo? existing)
        => UserEditRequested?.Invoke(existing) ?? Task.FromResult<UserEditResult?>(null);

    internal Task<string?> RequestNewRoleAsync()
        => NewRoleRequested?.Invoke() ?? Task.FromResult<string?>(null);
}

/// <summary>Result of the User Edit dialog: the target user state plus the
/// (optional) new password. Password is empty when the user left it blank on an
/// edit (keep the existing one).</summary>
public sealed record UserEditResult(UserInfo User, string Password, bool IsNew);

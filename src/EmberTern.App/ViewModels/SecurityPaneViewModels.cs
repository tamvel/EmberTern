using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EmberTern.Core.Metadata;
using EmberTern.Core.Security;

namespace EmberTern.App.ViewModels;

/// <summary>A grantee option for the Membership / Privileges pickers (user or role,
/// with a display icon).</summary>
public sealed class GranteeOptionViewModel
{
    public GranteeOptionViewModel(GranteeRef granteeRef) => Ref = granteeRef;

    public GranteeRef Ref { get; }
    public string Name => Ref.Name;
    public GranteeType Type => Ref.Type;
    public string TypeLabel => Type == GranteeType.User ? UiStrings.SecurityGranteeUser : UiStrings.SecurityGranteeRole;
    private MetadataObjectKind Kind => Type == GranteeType.User ? MetadataObjectKind.User : MetadataObjectKind.Role;
    public string IconGeometryKey => MetadataNodeViewModel.GeometryKeyFor(Kind);
    public string IconResourceKey => MetadataNodeViewModel.ResourceKeyFor(Kind);
}

// ─── Users pane ────────────────────────────────────────────────────────────

public partial class SecurityUsersPaneViewModel : ViewModelBase
{
    private readonly SecurityManagerTabViewModel _owner;
    public SecurityUsersPaneViewModel(SecurityManagerTabViewModel owner) => _owner = owner;

    public ObservableCollection<UserInfo> Items { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteUserCommand))]
    private UserInfo? _selectedUser;

    public async Task LoadAsync(CancellationToken ct)
    {
        var users = await _owner.Security.ListUsersAsync(ct).ConfigureAwait(true);
        var keepName = SelectedUser?.UserName;
        Items.Clear();
        foreach (var u in users) Items.Add(u);
        SelectedUser = Items.FirstOrDefault(u => string.Equals(u.UserName, keepName, StringComparison.OrdinalIgnoreCase));
    }

    public void SelectByName(string name)
        => SelectedUser = Items.FirstOrDefault(u => string.Equals(u.UserName, name, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private async Task AddUserAsync()
    {
        var result = await _owner.RequestUserEditAsync(null).ConfigureAwait(true);
        if (result is null) return;
        var sql = SecurityDdlGenerator.BuildCreateUser(result.User, result.Password);
        if (!string.IsNullOrWhiteSpace(result.User.Description))
            sql += ";\n" + SecurityDdlGenerator.BuildCommentUser(result.User.UserName, result.User.Description);
        if (await _owner.ApplyDdlAsync(sql).ConfigureAwait(true))
            await _owner.RefreshAsync().ConfigureAwait(true);
    }

    private bool HasSelectedUser => SelectedUser is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedUser))]
    private async Task EditUserAsync()
    {
        if (SelectedUser is not { } existing) return;
        var result = await _owner.RequestUserEditAsync(existing).ConfigureAwait(true);
        if (result is null) return;
        var sql = SecurityDdlGenerator.BuildAlterUser(result.User, result.Password)
            + ";\n" + SecurityDdlGenerator.BuildCommentUser(result.User.UserName, result.User.Description);
        if (await _owner.ApplyDdlAsync(sql).ConfigureAwait(true))
            await _owner.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedUser))]
    private async Task DeleteUserAsync()
    {
        if (SelectedUser is not { } user) return;
        var ok = await _owner.ConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.SecurityDeleteUserTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.SecurityDeleteUserMessage, user.UserName),
            ConfirmLabel = UiStrings.SecurityDeleteConfirm,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!ok) return;
        if (await _owner.ApplyDdlAsync(SecurityDdlGenerator.BuildDropUser(user.UserName)).ConfigureAwait(true))
            await _owner.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await _owner.RefreshAsync().ConfigureAwait(true);
}

// ─── Roles pane ────────────────────────────────────────────────────────────

public partial class SecurityRolesPaneViewModel : ViewModelBase
{
    private readonly SecurityManagerTabViewModel _owner;

    public SecurityRolesPaneViewModel(SecurityManagerTabViewModel owner)
    {
        _owner = owner;
        // ⚠ Asercją strażnika jest POWIADOMIENIE, nie wartość — wiązanie odpytuje właściwość wyłącznie
        // po `PropertyChanged`, więc bez tego stan pusty zostałby na ekranie po dodaniu pierwszej roli.
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmptyState));
    }

    public ObservableCollection<RoleInfo> Items { get; } = new();

    /// <summary>
    /// Stan pusty listy ról (M5 / M‑3, B2): baza bez ról niesystemowych — ⭐ stan ZWYCZAJNY, nie awaryjny,
    /// bo <c>RDB$ROLES</c> po odfiltrowaniu systemowych jest pusta na świeżo utworzonej bazie.
    /// </summary>
    /// <remarks>
    /// ⚠ Ta lista NIE MA FILTRA (zmierzone: `FilterText` istnieje wyłącznie w panelu uprawnień), więc pusto
    /// znaczy tu dokładnie jedno — ról nie ma. ⛔ Nie wolno tu użyć treści mówiącej o filtrze; byłoby to
    /// wskazanie elementu, którego na tym ekranie nie ma.
    /// </remarks>
    public bool ShowEmptyState => Items.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DropRoleCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveDescriptionCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelectedRole))]
    private RoleInfo? _selectedRole;

    [ObservableProperty]
    private string _editableDescription = string.Empty;

    public bool HasSelectedRole => SelectedRole is not null;

    partial void OnSelectedRoleChanged(RoleInfo? value)
        => EditableDescription = value?.Description ?? string.Empty;

    public async Task LoadAsync(CancellationToken ct)
    {
        var roles = await _owner.Security.ListRolesAsync(ct).ConfigureAwait(true);
        var keepName = SelectedRole?.Name;
        Items.Clear();
        foreach (var r in roles) Items.Add(r);
        SelectedRole = Items.FirstOrDefault(r => string.Equals(r.Name, keepName, StringComparison.OrdinalIgnoreCase));
    }

    public void SelectByName(string name)
        => SelectedRole = Items.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    [RelayCommand]
    private async Task AddRoleAsync()
    {
        var name = await _owner.RequestNewRoleAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(name)) return;
        if (await _owner.ApplyDdlAsync(SecurityDdlGenerator.BuildCreateRole(name)).ConfigureAwait(true))
            await _owner.RefreshAsync().ConfigureAwait(true);
    }

    private bool HasSelection => SelectedRole is not null;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DropRoleAsync()
    {
        if (SelectedRole is not { } role) return;
        var ok = await _owner.ConfirmAsync(new ConfirmRequest
        {
            Title = UiStrings.SecurityDropRoleTitle,
            Message = string.Format(CultureInfo.CurrentCulture, UiStrings.SecurityDropRoleMessage, role.Name),
            ConfirmLabel = UiStrings.SecurityDeleteConfirm,
            IsDestructive = true,
        }).ConfigureAwait(true);
        if (!ok) return;
        if (await _owner.ApplyDdlAsync(SecurityDdlGenerator.BuildDropRole(role.Name)).ConfigureAwait(true))
            await _owner.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task SaveDescriptionAsync()
    {
        if (SelectedRole is not { } role) return;
        var sql = SecurityDdlGenerator.BuildCommentRole(role.Name, EditableDescription);
        if (await _owner.ApplyDdlAsync(sql).ConfigureAwait(true))
            await _owner.RefreshAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await _owner.RefreshAsync().ConfigureAwait(true);
}

// ─── Membership pane ───────────────────────────────────────────────────────

/// <summary>Which way the membership relation is viewed (feature A):
/// <see cref="MemberOf"/> = a grantee and the roles it belongs to;
/// <see cref="Members"/> = a role and the users/roles that are members of it.</summary>
public enum MembershipDirection { MemberOf, Members }

/// <summary>An entry in the direction selector.</summary>
public sealed record MembershipDirectionOption(string Label, MembershipDirection Direction);

public partial class SecurityMembershipPaneViewModel : ViewModelBase
{
    private readonly SecurityManagerTabViewModel _owner;
    private IReadOnlyList<MembershipInfo> _membership = Array.Empty<MembershipInfo>();

    public SecurityMembershipPaneViewModel(SecurityManagerTabViewModel owner)
    {
        _owner = owner;
        Directions = new ObservableCollection<MembershipDirectionOption>
        {
            new(UiStrings.SecurityDirectionMemberOf, MembershipDirection.MemberOf),
            new(UiStrings.SecurityDirectionMembers, MembershipDirection.Members),
        };
        _selectedDirection = Directions[0];
        // ⚠ Nie polegamy na kolejności generowanych powiadomień wobec `RebuildRows()`: wierszami rządzi
        // kolekcja, więc to ona ogłasza zmianę stanu pustego.
        Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(ShowEmptyState));
    }

    public ObservableCollection<MembershipDirectionOption> Directions { get; }
    /// <summary>Picker source — all grantees in "Member of", roles only in "Members".
    /// Same instance for the tab lifetime (Clear+Add).</summary>
    public ObservableCollection<GranteeOptionViewModel> PickerItems { get; } = new();
    public ObservableCollection<MembershipRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Direction))]
    [NotifyPropertyChangedFor(nameof(PickerLabel))]
    [NotifyPropertyChangedFor(nameof(RowHeader))]
    [NotifyPropertyChangedFor(nameof(EmptyText))]
    private MembershipDirectionOption _selectedDirection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private GranteeOptionViewModel? _selectedPicker;

    public MembershipDirection Direction => SelectedDirection?.Direction ?? MembershipDirection.MemberOf;
    public string PickerLabel => Direction == MembershipDirection.Members
        ? UiStrings.SecurityRolePickerLabel : UiStrings.SecurityGranteeLabel;
    public string RowHeader => Direction == MembershipDirection.Members
        ? UiStrings.SecurityColMemberName : UiStrings.SecurityColRoleName;

    /// <summary>
    /// Stan pusty przynależności (M5 / M‑3, B3): wybrany grantee nie ma wierszy.
    /// </summary>
    /// <remarks>
    /// ⭐⭐ WYMAGA WYBRANEGO ELEMENTU (<c>SelectedPicker is not null</c>) i to nie jest ostrożność, tylko
    /// rozgraniczenie stanów. Picker AUTOWYBIERA pierwszą pozycję, więc „nic nie wybrano" zachodzi jedynie
    /// wtedy, gdy jest pusty — a w kierunku „Members" picker zawiera SAME ROLE, więc pusty picker znaczy
    /// „baza nie ma ról". To jest ten sam fakt, który komunikuje już lista ról (B2), i decyzją użytkownika
    /// NIE dostaje tu drugiego komunikatu.
    /// </remarks>
    public bool ShowEmptyState => SelectedPicker is not null && Rows.Count == 0;

    /// <summary>
    /// ⭐ Treść zależy od KIERUNKU, bo to dwa różne pytania — i produkt już to wie: <see cref="RowHeader"/>
    /// przełącza się „Role name" ↔ „Member name" dokładnie z tego powodu. Jeden komunikat na oba kierunki
    /// byłby nieprawdziwy w jednym z nich.
    /// </summary>
    public string EmptyText => Direction == MembershipDirection.Members
        ? UiStrings.SecurityMembershipEmptyMembers
        : UiStrings.SecurityMembershipEmptyMemberOf;

    partial void OnSelectedDirectionChanged(MembershipDirectionOption value)
    {
        RebuildPicker();
        RebuildRows();
    }
    partial void OnSelectedPickerChanged(GranteeOptionViewModel? value) => RebuildRows();

    public async Task LoadAsync(CancellationToken ct)
    {
        _membership = await _owner.Security.ListMembershipAsync(ct).ConfigureAwait(true);
        RebuildPicker();
        RebuildRows();
    }

    // Orchestrator hook for a role context: pick that role in the picker (it's present
    // in either direction).
    public void SelectGranteeByName(string name, GranteeType type)
        => SelectedPicker = PickerItems.FirstOrDefault(g =>
            string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase) && g.Type == type) ?? SelectedPicker;

    // Test seam: inject membership data and rebuild (the owner's Grantees / Roles must
    // already be populated). Mirrors what LoadAsync does minus the DB read.
    internal void SetMembershipForTest(IReadOnlyList<MembershipInfo> membership)
    {
        _membership = membership;
        RebuildPicker();
        RebuildRows();
    }

    private void RebuildPicker()
    {
        var keepName = SelectedPicker?.Name;
        var keepType = SelectedPicker?.Type;
        PickerItems.Clear();
        // "Member of": pick any grantee (user/role). "Members": pick a role only.
        foreach (var g in _owner.Grantees)
            if (Direction == MembershipDirection.MemberOf || g.Type == GranteeType.Role)
                PickerItems.Add(g);
        SelectedPicker = PickerItems.FirstOrDefault(g => keepName is not null
            && string.Equals(g.Name, keepName, StringComparison.OrdinalIgnoreCase) && g.Type == keepType)
            ?? PickerItems.FirstOrDefault();
    }

    private void RebuildRows()
    {
        Rows.Clear();
        if (SelectedPicker is not { } picker) return;
        if (Direction == MembershipDirection.MemberOf)
        {
            // Rows = all roles; state = is the picked grantee a member of each role?
            foreach (var role in _owner.Roles.Items)
            {
                if (picker.Type == GranteeType.Role && string.Equals(role.Name, picker.Name, StringComparison.OrdinalIgnoreCase))
                    continue; // a role can't be a member of itself
                Rows.Add(new MembershipRowViewModel(this, new GranteeRef(role.Name, GranteeType.Role),
                    StateOf(picker.Name, picker.Type, role.Name)));
            }
        }
        else
        {
            // Rows = all grantees; state = is each grantee a member of the picked role?
            foreach (var opt in _owner.Grantees)
            {
                if (opt.Type == GranteeType.Role && string.Equals(opt.Name, picker.Name, StringComparison.OrdinalIgnoreCase))
                    continue;
                Rows.Add(new MembershipRowViewModel(this, opt.Ref,
                    StateOf(opt.Name, opt.Type, picker.Name)));
            }
        }
    }

    private int StateOf(string member, GranteeType memberType, string role)
    {
        var m = _membership.FirstOrDefault(x =>
            string.Equals(x.Member, member, StringComparison.OrdinalIgnoreCase) && x.MemberType == memberType
            && string.Equals(x.RoleName, role, StringComparison.OrdinalIgnoreCase));
        return m is null ? 0 : (m.WithAdminOption ? 2 : 1);
    }

    // Resolves the (role, member) edge a row toggles, given the current direction.
    internal bool TryResolveEdge(MembershipRowViewModel row, out string roleName, out GranteeRef member)
    {
        roleName = string.Empty;
        member = row.RowRef;
        if (SelectedPicker is not { } picker) return false;
        if (Direction == MembershipDirection.MemberOf)
        {
            roleName = row.RowRef.Name;   // the row is the role
            member = picker.Ref;          // the picked grantee
        }
        else
        {
            roleName = picker.Name;        // the picked role
            member = row.RowRef;           // the row is the grantee
        }
        return true;
    }

    // Cycle (feature B): 0→1 GRANT, 1→2 GRANT WITH ADMIN OPTION, 2→0 REVOKE. Optimistic
    // — the row already set the new state; revert on failure.
    internal async Task ApplyAsync(MembershipRowViewModel row, int newState, int oldState)
    {
        if (!TryResolveEdge(row, out var roleName, out var member)) return;
        var sql = newState switch
        {
            1 => SecurityDdlGenerator.BuildGrantRole(roleName, member, false),
            2 => SecurityDdlGenerator.BuildGrantRole(roleName, member, true),
            _ => SecurityDdlGenerator.BuildRevokeRole(roleName, member),
        };
        if (!await _owner.ApplyDdlAsync(sql).ConfigureAwait(true))
            row.SetStateSilent(oldState);
        else
            UpdateLocalMembership(roleName, member, newState);
    }

    // Keep the local membership cache in sync so flipping direction reflects the change
    // without a full reload.
    private void UpdateLocalMembership(string role, GranteeRef member, int state)
    {
        var list = _membership.Where(m => !(
            string.Equals(m.Member, member.Name, StringComparison.OrdinalIgnoreCase) && m.MemberType == member.Type
            && string.Equals(m.RoleName, role, StringComparison.OrdinalIgnoreCase))).ToList();
        if (state > 0)
            list.Add(new MembershipInfo(member.Name, member.Type, role, state == 2));
        _membership = list;
    }
}

/// <summary>One row in the Membership grid — a role (in "Member of") or a grantee
/// (in "Members"). A single tri-state cell: 0 not a member, 1 member, 2 member WITH
/// ADMIN OPTION; cycled on click.</summary>
public partial class MembershipRowViewModel : ViewModelBase
{
    private readonly SecurityMembershipPaneViewModel _pane;

    public MembershipRowViewModel(SecurityMembershipPaneViewModel pane, GranteeRef rowRef, int state)
    {
        _pane = pane;
        RowRef = rowRef;
        State = state;
        var kind = rowRef.Type == GranteeType.User ? MetadataObjectKind.User : MetadataObjectKind.Role;
        IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(kind);
        IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(kind);
    }

    public GranteeRef RowRef { get; }
    public string Name => RowRef.Name;
    public string TypeLabel => RowRef.Type == GranteeType.User ? UiStrings.SecurityGranteeUser : UiStrings.SecurityGranteeRole;
    public string IconGeometryKey { get; }
    public string IconResourceKey { get; }

    [ObservableProperty] private int _state; // 0 none, 1 member, 2 member + admin option

    [RelayCommand]
    private void Cycle()
    {
        var old = State;
        var next = (old + 1) % 3;
        State = next; // optimistic; ApplyAsync reverts on failure
        _ = _pane.ApplyAsync(this, next, old);
    }

    internal void SetStateSilent(int value) => State = value;
}

// ─── Privileges pane ───────────────────────────────────────────────────────

/// <summary>A grantable object category (Tables / Views / Procedures / …) with the
/// metadata kind to list it and the privilege letters that apply.</summary>
public sealed record PrivilegeCategory(string Label, MetadataObjectKind ListKind, PrivilegeObjectKind GrantKind, string Privileges);

/// <summary>The three bulk operations offered at every scope (row / column / all
/// visible): plain GRANT, GRANT … WITH GRANT OPTION, and REVOKE.</summary>
internal enum BulkAction { Grant, GrantWithOption, Revoke }

public partial class SecurityPrivilegesPaneViewModel : ViewModelBase
{
    private readonly SecurityManagerTabViewModel _owner;
    private IReadOnlyList<PrivilegeInfo> _privileges = Array.Empty<PrivilegeInfo>();
    private readonly List<PrivilegeRowViewModel> _allRows = new();

    public SecurityPrivilegesPaneViewModel(SecurityManagerTabViewModel owner)
    {
        _owner = owner;
        Grantees = owner.Grantees;
        Categories = new ObservableCollection<PrivilegeCategory>
        {
            new(UiStrings.MetadataGroupTables, MetadataObjectKind.Table, PrivilegeObjectKind.Relation, "SIUDR"),
            new(UiStrings.MetadataGroupViews, MetadataObjectKind.View, PrivilegeObjectKind.Relation, "SIUDR"),
            new(UiStrings.MetadataGroupProcedures, MetadataObjectKind.Procedure, PrivilegeObjectKind.Procedure, "X"),
            new(UiStrings.MetadataGroupFunctions, MetadataObjectKind.Function, PrivilegeObjectKind.Function, "X"),
            new(UiStrings.MetadataGroupPackages, MetadataObjectKind.Package, PrivilegeObjectKind.Package, "X"),
            new(UiStrings.MetadataGroupGenerators, MetadataObjectKind.Generator, PrivilegeObjectKind.Sequence, "G"),
            new(UiStrings.MetadataGroupExceptions, MetadataObjectKind.Exception, PrivilegeObjectKind.Exception, "G"),
        };
        SelectedCategory = Categories[0];
    }

    public ObservableCollection<GranteeOptionViewModel> Grantees { get; }
    public ObservableCollection<PrivilegeCategory> Categories { get; }
    public ObservableCollection<PrivilegeRowViewModel> Rows { get; } = new();
    public ObservableCollection<ColumnPrivilegeRowViewModel> Columns { get; } = new();

    [ObservableProperty]
    private GranteeOptionViewModel? _selectedGrantee;

    [ObservableProperty]
    private PrivilegeCategory? _selectedCategory;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasColumnTable))]
    [NotifyPropertyChangedFor(nameof(ShowColumnHint))]
    private PrivilegeRowViewModel? _selectedRow;

    /// <summary>The column-level panel is shown for the whole Tables category (so it's
    /// discoverable even before a row is picked), then shows either the selected table's
    /// columns or a "pick a table" hint.</summary>
    public bool ShowColumnPanel => SelectedCategory?.ListKind == MetadataObjectKind.Table;
    public bool HasColumnTable => ShowColumnPanel && SelectedRow is not null;
    public bool ShowColumnHint => ShowColumnPanel && SelectedRow is null;
    /// <summary>Header for the column panel: "Columns — TABLE" or just "Columns".</summary>
    public string ColumnPanelHeader => SelectedRow is { } r
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.SecurityColumnsForFormat, r.ObjectName)
        : UiStrings.SecurityColumnsHeader;

    partial void OnSelectedGranteeChanged(GranteeOptionViewModel? value) => _ = ReloadAsync();
    partial void OnSelectedCategoryChanged(PrivilegeCategory? value)
    {
        OnPropertyChanged(nameof(ShowColumnPanel));
        OnPropertyChanged(nameof(HasColumnTable));
        OnPropertyChanged(nameof(ShowColumnHint));
        _ = ReloadAsync();
    }
    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnSelectedRowChanged(PrivilegeRowViewModel? value)
    {
        OnPropertyChanged(nameof(ColumnPanelHeader));
        RebuildColumns();
    }

    public async Task LoadAsync(CancellationToken ct)
    {
        OnGranteesChanged();
        await ReloadAsync().ConfigureAwait(true);
    }

    /// <summary>Re-syncs the selected grantee after the shared Grantees list is rebuilt.</summary>
    public void OnGranteesChanged()
    {
        var keep = SelectedGrantee;
        SelectedGrantee = Grantees.FirstOrDefault(g => keep is not null
            && string.Equals(g.Name, keep.Name, StringComparison.OrdinalIgnoreCase) && g.Type == keep.Type)
            ?? Grantees.FirstOrDefault();
    }

    public void SelectGranteeByName(string name, GranteeType type)
        => SelectedGrantee = Grantees.FirstOrDefault(g =>
            string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase) && g.Type == type) ?? SelectedGrantee;

    private async Task ReloadAsync()
    {
        _allRows.Clear();
        Rows.Clear();
        Columns.Clear();
        SelectedRow = null;
        if (SelectedGrantee is not { } grantee || SelectedCategory is not { } category) return;

        _privileges = await _owner.Security.ListPrivilegesAsync(grantee.Ref).ConfigureAwait(true);
        var objects = await _owner.MetadataReader.ListAsync(category.ListKind).ConfigureAwait(true);

        // Per object: privilege char → cell state (1 granted, 2 granted WITH GRANT OPTION).
        var granted = _privileges
            .Where(p => p.ColumnName is null && IsRelevant(p, category))
            .GroupBy(p => p.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<char, int>)g
                    .GroupBy(p => p.Privilege)
                    .ToDictionary(pg => pg.Key, pg => pg.Max(x => x.GrantOption >= 1 ? 2 : 1)),
                StringComparer.OrdinalIgnoreCase);

        foreach (var obj in objects)
        {
            granted.TryGetValue(obj.Name, out var states);
            _allRows.Add(new PrivilegeRowViewModel(this, obj.Name, category,
                states ?? new Dictionary<char, int>()));
        }
        ApplyFilter();
    }

    private static bool IsRelevant(PrivilegeInfo p, PrivilegeCategory category)
        => p.ObjectKind == category.GrantKind && category.Privileges.IndexOf(p.Privilege) >= 0;

    private void ApplyFilter()
    {
        var f = FilterText?.Trim() ?? string.Empty;
        Rows.Clear();
        foreach (var row in _allRows)
            if (f.Length == 0 || row.ObjectName.Contains(f, StringComparison.OrdinalIgnoreCase))
                Rows.Add(row);
    }

    // Single cell cycle: 0→1 (GRANT), 1→2 (GRANT WITH GRANT OPTION), 2→0 (REVOKE).
    // Optimistic — the row already set the new state; revert on failure.
    internal async Task ApplyCycleAsync(PrivilegeRowViewModel row, char privilege, int newState, int oldState)
    {
        if (SelectedGrantee is not { } g || SelectedCategory is not { } category) return;
        var sql = newState switch
        {
            1 => SecurityDdlGenerator.BuildGrantPrivilege(category.GrantKind, row.ObjectName, privilege, g.Ref, null, false),
            2 => SecurityDdlGenerator.BuildGrantPrivilege(category.GrantKind, row.ObjectName, privilege, g.Ref, null, true),
            _ => SecurityDdlGenerator.BuildRevokePrivilege(category.GrantKind, row.ObjectName, privilege, g.Ref, null),
        };
        if (!await _owner.ApplyDdlAsync(sql).ConfigureAwait(true))
            row.SetState(privilege, oldState);
    }

    // ── Bulk operations: same three actions (Grant / GrantWithOption / Revoke) at
    // three scopes (row / column / all visible). Every scope acts on the currently
    // VISIBLE (filtered) rows and skips statements that would be a no-op (the cell is
    // already in the target state), so a re-run is cheap. No-ops without a grantee.

    // Builds the GRANT/REVOKE for one privilege of one object — or null when the
    // current state already satisfies the action. Predicates mirror the single-cell
    // tri-state: Grant only when none; GrantWithOption when below "with option"
    // (upgrades plain → with option); Revoke when anything is granted.
    private string? StatementFor(PrivilegeCategory category, string objectName, char privilege, GranteeOptionViewModel g, int state, BulkAction action) => action switch
    {
        BulkAction.Grant when state == 0 =>
            SecurityDdlGenerator.BuildGrantPrivilege(category.GrantKind, objectName, privilege, g.Ref, null, false),
        BulkAction.GrantWithOption when state < 2 =>
            SecurityDdlGenerator.BuildGrantPrivilege(category.GrantKind, objectName, privilege, g.Ref, null, true),
        BulkAction.Revoke when state > 0 =>
            SecurityDdlGenerator.BuildRevokePrivilege(category.GrantKind, objectName, privilege, g.Ref, null),
        _ => null,
    };

    // Row scope — one object, every applicable privilege. Invoked by the row VM's
    // commands (hover trio + right-click menu).
    internal async Task ApplyAllAsync(PrivilegeRowViewModel row, BulkAction action)
    {
        if (SelectedGrantee is not { } g || SelectedCategory is not { } category) return;
        var statements = new List<string>();
        foreach (var p in category.Privileges)
            if (StatementFor(category, row.ObjectName, p, g, row.GetState(p), action) is { } s)
                statements.Add(s);
        await RunBatchAndReloadAsync(statements).ConfigureAwait(true);
    }

    // All-visible scope — every visible object, every applicable privilege.
    [RelayCommand] private Task BulkGrant() => BulkApplyAsync(BulkAction.Grant);
    [RelayCommand] private Task BulkGrantWithOption() => BulkApplyAsync(BulkAction.GrantWithOption);
    [RelayCommand] private Task BulkRevoke() => BulkApplyAsync(BulkAction.Revoke);

    private async Task BulkApplyAsync(BulkAction action)
    {
        if (SelectedGrantee is not { } g || SelectedCategory is not { } category) return;
        // Confirm only the broadest destructive op (revoke everything from everyone visible).
        if (action == BulkAction.Revoke && Rows.Count > 0 && !await ConfirmGlobalRevokeAsync(g).ConfigureAwait(true)) return;
        var statements = new List<string>();
        foreach (var row in Rows) // visible (filtered) set
            foreach (var p in category.Privileges)
                if (StatementFor(category, row.ObjectName, p, g, row.GetState(p), action) is { } s)
                    statements.Add(s);
        await RunBatchAndReloadAsync(statements).ConfigureAwait(true);
    }

    private Task<bool> ConfirmGlobalRevokeAsync(GranteeOptionViewModel g) => _owner.ConfirmAsync(new ConfirmRequest
    {
        Title = UiStrings.SecurityRevokeAllConfirmTitle,
        Message = string.Format(System.Globalization.CultureInfo.CurrentCulture, UiStrings.SecurityRevokeAllConfirmFormat, g.Name, Rows.Count),
        ConfirmLabel = UiStrings.SecurityRevokeAllConfirmYes,
        IsDestructive = true,
    });

    // Column scope — one privilege, every visible object.
    [RelayCommand] private Task GrantColumn(string? privilege) => ColumnBulkAsync(privilege, BulkAction.Grant);
    [RelayCommand] private Task GrantColumnWithOption(string? privilege) => ColumnBulkAsync(privilege, BulkAction.GrantWithOption);
    [RelayCommand] private Task RevokeColumn(string? privilege) => ColumnBulkAsync(privilege, BulkAction.Revoke);

    private async Task ColumnBulkAsync(string? privilege, BulkAction action)
    {
        if (string.IsNullOrEmpty(privilege)) return;
        var c = privilege[0];
        if (SelectedGrantee is not { } g || SelectedCategory is not { } category || !category.Privileges.Contains(c)) return;
        var statements = new List<string>();
        foreach (var row in Rows) // visible (filtered) set
            if (StatementFor(category, row.ObjectName, c, g, row.GetState(c), action) is { } s)
                statements.Add(s);
        await RunBatchAndReloadAsync(statements).ConfigureAwait(true);
    }

    // One autonomous transaction for the whole batch; reload the privilege grid
    // (not the whole tab) to reflect the new state.
    private async Task RunBatchAndReloadAsync(List<string> statements)
    {
        if (statements.Count == 0) return;
        if (await _owner.ApplyDdlAsync(string.Join(";\n", statements)).ConfigureAwait(true))
            await ReloadAsync().ConfigureAwait(true);
    }

    private void RebuildColumns()
    {
        Columns.Clear();
        if (!ShowColumnPanel || SelectedRow is not { } row) return;
        _ = LoadColumnsAsync(row.ObjectName);
    }

    private async Task LoadColumnsAsync(string tableName)
    {
        var cols = await _owner.MetadataReader.ListColumnsAsync(tableName).ConfigureAwait(true);
        var updatable = _privileges
            .Where(p => string.Equals(p.ObjectName, tableName, StringComparison.OrdinalIgnoreCase) && p.ColumnName is not null && p.Privilege == 'U')
            .Select(p => p.ColumnName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencable = _privileges
            .Where(p => string.Equals(p.ObjectName, tableName, StringComparison.OrdinalIgnoreCase) && p.ColumnName is not null && p.Privilege == 'R')
            .Select(p => p.ColumnName!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Columns.Clear();
        foreach (var c in cols)
            Columns.Add(new ColumnPrivilegeRowViewModel(this, tableName, c.Name,
                updatable.Contains(c.Name), referencable.Contains(c.Name)));
    }

    internal async Task ApplyColumnPrivilegeAsync(ColumnPrivilegeRowViewModel row, char privilege, bool grant)
    {
        if (SelectedGrantee is not { } g) return;
        var sql = grant
            ? SecurityDdlGenerator.BuildGrantPrivilege(PrivilegeObjectKind.Relation, row.TableName, privilege, g.Ref, row.ColumnName, false)
            : SecurityDdlGenerator.BuildRevokePrivilege(PrivilegeObjectKind.Relation, row.TableName, privilege, g.Ref, row.ColumnName);
        if (!await _owner.ApplyDdlAsync(sql).ConfigureAwait(true))
            row.RevertPrivilege(privilege, !grant);
    }
}

/// <summary>One object row in the Privileges matrix. Each privilege is a TRI-STATE
/// cell — 0 none, 1 granted, 2 granted WITH GRANT OPTION — cycled on click. State is
/// set optimistically and reverted on DDL failure. Can* flags drive cell visibility
/// per object category.</summary>
public partial class PrivilegeRowViewModel : ViewModelBase
{
    private readonly SecurityPrivilegesPaneViewModel _pane;

    public PrivilegeRowViewModel(SecurityPrivilegesPaneViewModel pane, string objectName, PrivilegeCategory category, IReadOnlyDictionary<char, int> states)
    {
        _pane = pane;
        ObjectName = objectName;
        Category = category;
        IconGeometryKey = MetadataNodeViewModel.GeometryKeyFor(category.ListKind);
        IconResourceKey = MetadataNodeViewModel.ResourceKeyFor(category.ListKind);
        SelectState = states.TryGetValue('S', out var s) ? s : 0;
        InsertState = states.TryGetValue('I', out var i) ? i : 0;
        UpdateState = states.TryGetValue('U', out var u) ? u : 0;
        DeleteState = states.TryGetValue('D', out var d) ? d : 0;
        ReferencesState = states.TryGetValue('R', out var r) ? r : 0;
        ExecuteState = states.TryGetValue('X', out var x) ? x : 0;
        UsageState = states.TryGetValue('G', out var gg) ? gg : 0;
    }

    public string ObjectName { get; }
    public PrivilegeCategory Category { get; }
    public string IconGeometryKey { get; }
    public string IconResourceKey { get; }

    public bool CanSelect => Category.Privileges.Contains('S');
    public bool CanInsert => Category.Privileges.Contains('I');
    public bool CanUpdate => Category.Privileges.Contains('U');
    public bool CanDelete => Category.Privileges.Contains('D');
    public bool CanReferences => Category.Privileges.Contains('R');
    public bool CanExecute => Category.Privileges.Contains('X');
    public bool CanUsage => Category.Privileges.Contains('G');

    [ObservableProperty] private int _selectState;
    [ObservableProperty] private int _insertState;
    [ObservableProperty] private int _updateState;
    [ObservableProperty] private int _deleteState;
    [ObservableProperty] private int _referencesState;
    [ObservableProperty] private int _executeState;
    [ObservableProperty] private int _usageState;

    [RelayCommand]
    private void Cycle(string? privilege)
    {
        if (string.IsNullOrEmpty(privilege)) return;
        var c = privilege[0];
        if (!Category.Privileges.Contains(c)) return;
        var old = GetState(c);
        var next = (old + 1) % 3;
        SetState(c, next); // optimistic; ApplyCycleAsync reverts on failure
        _ = _pane.ApplyCycleAsync(this, c, next, old);
    }

    [RelayCommand]
    private void GrantAll() => _ = _pane.ApplyAllAsync(this, BulkAction.Grant);

    [RelayCommand]
    private void GrantAllWithOption() => _ = _pane.ApplyAllAsync(this, BulkAction.GrantWithOption);

    [RelayCommand]
    private void RevokeAll() => _ = _pane.ApplyAllAsync(this, BulkAction.Revoke);

    internal int GetState(char privilege) => privilege switch
    {
        'S' => SelectState, 'I' => InsertState, 'U' => UpdateState, 'D' => DeleteState,
        'R' => ReferencesState, 'X' => ExecuteState, 'G' => UsageState, _ => 0,
    };

    internal void SetState(char privilege, int value)
    {
        switch (privilege)
        {
            case 'S': SelectState = value; break;
            case 'I': InsertState = value; break;
            case 'U': UpdateState = value; break;
            case 'D': DeleteState = value; break;
            case 'R': ReferencesState = value; break;
            case 'X': ExecuteState = value; break;
            case 'G': UsageState = value; break;
        }
    }
}

/// <summary>One column row in the table column-privileges sub-panel (Update /
/// References per column).</summary>
public partial class ColumnPrivilegeRowViewModel : ViewModelBase
{
    private readonly SecurityPrivilegesPaneViewModel _pane;
    private bool _suppress;

    public ColumnPrivilegeRowViewModel(SecurityPrivilegesPaneViewModel pane, string tableName, string columnName, bool update, bool references)
    {
        _pane = pane;
        TableName = tableName;
        ColumnName = columnName;
        _suppress = true;
        Update = update;
        References = references;
        _suppress = false;
    }

    public string TableName { get; }
    public string ColumnName { get; }

    [ObservableProperty] private bool _update;
    [ObservableProperty] private bool _references;

    partial void OnUpdateChanged(bool value) { if (!_suppress) _ = _pane.ApplyColumnPrivilegeAsync(this, 'U', value); }
    partial void OnReferencesChanged(bool value) { if (!_suppress) _ = _pane.ApplyColumnPrivilegeAsync(this, 'R', value); }

    internal void RevertPrivilege(char privilege, bool value)
    {
        _suppress = true;
        if (privilege == 'U') Update = value; else if (privilege == 'R') References = value;
        _suppress = false;
    }
}

using System;
using System.Collections.Generic;
using EmberTern.App.ViewModels;
using EmberTern.Core.Metadata;
using EmberTern.Core.Security;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

public class SecurityManagerTests
{
    // ─── SecurityDdlGenerator: users ───────────────────────────────────────

    [Fact]
    public void BuildCreateUser_EmitsPasswordNamesActiveAdmin()
    {
        var user = new UserInfo
        {
            UserName = "DEVUSER",
            FirstName = "Jan",
            LastName = "Kowalski",
            Active = true,
            Admin = true,
        };
        var sql = SecurityDdlGenerator.BuildCreateUser(user, "secret");
        Assert.StartsWith("CREATE USER DEVUSER", sql);
        Assert.Contains("PASSWORD 'secret'", sql);
        Assert.Contains("FIRSTNAME 'Jan'", sql);
        Assert.Contains("LASTNAME 'Kowalski'", sql);
        Assert.Contains(" ACTIVE", sql);
        Assert.Contains("GRANT ADMIN ROLE", sql);
    }

    [Fact]
    public void BuildCreateUser_InactiveNoAdmin()
    {
        var sql = SecurityDdlGenerator.BuildCreateUser(new UserInfo { UserName = "U", Active = false, Admin = false }, "p");
        Assert.Contains(" INACTIVE", sql);
        Assert.DoesNotContain("GRANT ADMIN ROLE", sql);
    }

    [Fact]
    public void BuildCreateUser_EscapesPasswordQuotes()
    {
        var sql = SecurityDdlGenerator.BuildCreateUser(new UserInfo { UserName = "U" }, "a'b");
        Assert.Contains("PASSWORD 'a''b'", sql);
    }

    [Fact]
    public void BuildCreateUser_EmptyPassword_Throws()
        => Assert.Throws<ArgumentException>(() => SecurityDdlGenerator.BuildCreateUser(new UserInfo { UserName = "U" }, ""));

    [Fact]
    public void BuildCreateUser_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => SecurityDdlGenerator.BuildCreateUser(new UserInfo { UserName = "" }, "p"));

    [Fact]
    public void BuildAlterUser_OmitsPasswordWhenBlank()
    {
        var sql = SecurityDdlGenerator.BuildAlterUser(new UserInfo { UserName = "U", Active = true, Admin = false }, null);
        Assert.StartsWith("ALTER USER U", sql);
        Assert.DoesNotContain("PASSWORD", sql);
        Assert.Contains(" ACTIVE", sql);
        Assert.Contains("REVOKE ADMIN ROLE", sql);
    }

    [Fact]
    public void BuildAlterUser_IncludesPasswordWhenSet()
    {
        var sql = SecurityDdlGenerator.BuildAlterUser(new UserInfo { UserName = "U", Admin = true }, "newpw");
        Assert.Contains("PASSWORD 'newpw'", sql);
        Assert.Contains("GRANT ADMIN ROLE", sql);
    }

    [Fact]
    public void BuildDropUser_Quotes() => Assert.Equal("DROP USER U", SecurityDdlGenerator.BuildDropUser("U"));

    [Fact]
    public void BuildCommentUser_IsNullWhenBlank()
        => Assert.Equal("COMMENT ON USER U IS NULL", SecurityDdlGenerator.BuildCommentUser("U", "  "));

    [Fact]
    public void BuildCommentUser_EscapesAndQuotes()
        => Assert.Equal("COMMENT ON USER U IS 'a''b'", SecurityDdlGenerator.BuildCommentUser("U", "a'b"));

    // ─── SecurityDdlGenerator: roles + membership ──────────────────────────

    [Fact]
    public void BuildCreateRole_AndDrop()
    {
        Assert.Equal("CREATE ROLE R", SecurityDdlGenerator.BuildCreateRole("R"));
        Assert.Equal("DROP ROLE R", SecurityDdlGenerator.BuildDropRole("R"));
    }

    [Fact]
    public void BuildCommentRole_IsNullAndText()
    {
        Assert.Equal("COMMENT ON ROLE R IS NULL", SecurityDdlGenerator.BuildCommentRole("R", null));
        Assert.Equal("COMMENT ON ROLE R IS 'desc'", SecurityDdlGenerator.BuildCommentRole("R", "desc"));
    }

    [Fact]
    public void BuildGrantRole_ToUser_WithAdmin()
    {
        var sql = SecurityDdlGenerator.BuildGrantRole("R", new GranteeRef("U", GranteeType.User), true);
        Assert.Equal("GRANT R TO USER U WITH ADMIN OPTION", sql);
    }

    [Fact]
    public void BuildGrantRole_ToRole_BareName_NoAdmin()
        => Assert.Equal("GRANT R TO R2", SecurityDdlGenerator.BuildGrantRole("R", new GranteeRef("R2", GranteeType.Role), false));

    [Fact]
    public void BuildRevokeRole_FromUser()
        => Assert.Equal("REVOKE R FROM USER U", SecurityDdlGenerator.BuildRevokeRole("R", new GranteeRef("U", GranteeType.User)));

    // ─── SecurityDdlGenerator: object + column privileges ──────────────────

    [Fact]
    public void BuildGrant_Relation_NoKeyword_WithGrantOption()
    {
        var sql = SecurityDdlGenerator.BuildGrantPrivilege(
            PrivilegeObjectKind.Relation, "CUSTOMERS", 'S', new GranteeRef("LAB_READER", GranteeType.Role), null, true);
        Assert.Equal("GRANT SELECT ON CUSTOMERS TO LAB_READER WITH GRANT OPTION", sql);
    }

    [Fact]
    public void BuildGrant_ColumnLevel_Update()
    {
        var sql = SecurityDdlGenerator.BuildGrantPrivilege(
            PrivilegeObjectKind.Relation, "CUSTOMERS", 'U', new GranteeRef("ED", GranteeType.Role), "EMAIL", false);
        Assert.Equal("GRANT UPDATE(EMAIL) ON CUSTOMERS TO ED", sql);
    }

    [Theory]
    [InlineData(PrivilegeObjectKind.Procedure, 'X', "GRANT EXECUTE ON PROCEDURE P TO R")]
    [InlineData(PrivilegeObjectKind.Function, 'X', "GRANT EXECUTE ON FUNCTION P TO R")]
    [InlineData(PrivilegeObjectKind.Package, 'X', "GRANT EXECUTE ON PACKAGE P TO R")]
    [InlineData(PrivilegeObjectKind.Sequence, 'G', "GRANT USAGE ON SEQUENCE P TO R")]
    [InlineData(PrivilegeObjectKind.Exception, 'G', "GRANT USAGE ON EXCEPTION P TO R")]
    public void BuildGrant_ObjectKeywords(PrivilegeObjectKind kind, char priv, string expected)
    {
        var sql = SecurityDdlGenerator.BuildGrantPrivilege(kind, "P", priv, new GranteeRef("R", GranteeType.Role), null, false);
        Assert.Equal(expected, sql);
    }

    [Fact]
    public void BuildRevoke_Relation()
    {
        var sql = SecurityDdlGenerator.BuildRevokePrivilege(
            PrivilegeObjectKind.Relation, "ORDERS", 'D', new GranteeRef("U", GranteeType.User), null);
        Assert.Equal("REVOKE DELETE ON ORDERS FROM USER U", sql);
    }

    [Fact]
    public void PrivilegeKeyword_Mapping()
    {
        Assert.Equal("SELECT", SecurityDdlGenerator.PrivilegeKeyword('S'));
        Assert.Equal("REFERENCES", SecurityDdlGenerator.PrivilegeKeyword('R'));
        Assert.Throws<ArgumentException>(() => SecurityDdlGenerator.PrivilegeKeyword('Z'));
    }

    // ─── SecurityCatalog decoders ──────────────────────────────────────────

    [Theory]
    [InlineData(8, GranteeType.User)]
    [InlineData(13, GranteeType.Role)]
    [InlineData(99, GranteeType.User)]
    public void DecodeGranteeType(int code, GranteeType expected)
        => Assert.Equal(expected, SecurityCatalog.DecodeGranteeType(code));

    [Theory]
    [InlineData(0, PrivilegeObjectKind.Relation)]
    [InlineData(5, PrivilegeObjectKind.Procedure)]
    [InlineData(7, PrivilegeObjectKind.Exception)]
    [InlineData(13, PrivilegeObjectKind.Role)]
    [InlineData(14, PrivilegeObjectKind.Sequence)]
    [InlineData(15, PrivilegeObjectKind.Function)]
    [InlineData(18, PrivilegeObjectKind.Package)]
    [InlineData(99, PrivilegeObjectKind.Other)]
    public void DecodeObjectKind(int code, PrivilegeObjectKind expected)
        => Assert.Equal(expected, SecurityCatalog.DecodeObjectKind(code));

    [Fact]
    public void PrivilegeLabel_Mapping()
    {
        Assert.Equal("SELECT", SecurityCatalog.PrivilegeLabel('S'));
        Assert.Equal("MEMBER", SecurityCatalog.PrivilegeLabel('M'));
    }

    // ─── FirebirdSecurityReader SQL shape pins ─────────────────────────────

    [Fact]
    public void UsersSql_QueriesSecUsers()
    {
        Assert.Contains("SEC$USERS", FirebirdSecurityReader.UsersSql);
        Assert.Contains("SEC$ACTIVE", FirebirdSecurityReader.UsersSql);
        Assert.Contains("SEC$ADMIN", FirebirdSecurityReader.UsersSql);
    }

    [Fact]
    public void RolesSql_QueriesRdbRoles()
        => Assert.Contains("RDB$ROLES", FirebirdSecurityReader.RolesSql);

    [Fact]
    public void ObjectPrivilegesSql_FiltersMembershipAndGrantee()
    {
        Assert.Contains("RDB$USER_PRIVILEGES", FirebirdSecurityReader.ObjectPrivilegesSql);
        Assert.Contains("RDB$PRIVILEGE <> 'M'", FirebirdSecurityReader.ObjectPrivilegesSql);
        Assert.Contains("@grantee", FirebirdSecurityReader.ObjectPrivilegesSql);
    }

    [Fact]
    public void MembershipSql_FiltersMembership()
        => Assert.Contains("RDB$PRIVILEGE = 'M'", FirebirdSecurityReader.MembershipSql);

    // ─── Routing predicate ─────────────────────────────────────────────────

    [Theory]
    [InlineData(MetadataObjectKind.User, true)]
    [InlineData(MetadataObjectKind.Role, true)]
    [InlineData(MetadataObjectKind.Table, false)]
    [InlineData(MetadataObjectKind.Exception, false)]
    public void OpensAsSecurityManager(MetadataObjectKind kind, bool expected)
        => Assert.Equal(expected, MainWindowViewModel.OpensAsSecurityManager(kind));

    // ─── Orchestrator: initial sub-tab from context ────────────────────────

    [Theory]
    [InlineData(MetadataObjectKind.Role, SecurityManagerTabViewModel.PrivilegesTabIndex)]
    [InlineData(MetadataObjectKind.User, SecurityManagerTabViewModel.UsersTabIndex)]
    public void ContextSelectsInitialSubTab(MetadataObjectKind kind, int expectedTab)
    {
        var mgr = BuildManager(new MetadataObject("X", kind));
        Assert.Equal(expectedTab, mgr.ActiveSubTabIndex);
    }

    [Fact]
    public void NullContext_DefaultsToUsersTab()
    {
        var mgr = BuildManager(null);
        Assert.Equal(SecurityManagerTabViewModel.UsersTabIndex, mgr.ActiveSubTabIndex);
    }

    // ─── Privilege row VM ──────────────────────────────────────────────────

    [Fact]
    public void PrivilegeRow_CanFlags_AndTriState()
    {
        var mgr = BuildManager(null);
        var cat = new PrivilegeCategory("Tables", MetadataObjectKind.Table, PrivilegeObjectKind.Relation, "SIUDR");
        // State 1 = granted, 2 = granted WITH GRANT OPTION.
        var row = new PrivilegeRowViewModel(mgr.Privileges, "T", cat,
            new Dictionary<char, int> { ['S'] = 1, ['U'] = 2 });
        Assert.True(row.CanSelect);
        Assert.True(row.CanReferences);
        Assert.False(row.CanExecute);
        Assert.False(row.CanUsage);
        Assert.Equal(1, row.SelectState);
        Assert.Equal(2, row.UpdateState);
        Assert.Equal(0, row.InsertState);
        Assert.Equal(1, row.GetState('S'));
        Assert.Equal(0, row.GetState('I'));
    }

    [Fact]
    public void PrivilegeRow_SetState_RoundTrips()
    {
        var mgr = BuildManager(null);
        var cat = new PrivilegeCategory("Procs", MetadataObjectKind.Procedure, PrivilegeObjectKind.Procedure, "X");
        var row = new PrivilegeRowViewModel(mgr.Privileges, "P", cat, new Dictionary<char, int> { ['X'] = 2 });
        Assert.True(row.CanExecute);
        Assert.Equal(2, row.ExecuteState);
        row.SetState('X', 0);
        Assert.Equal(0, row.ExecuteState);
        Assert.Equal(0, row.GetState('X'));
    }

    [Fact]
    public void PrivilegeRow_Cycle_AdvancesNoneGrantOptionRevoke()
    {
        var mgr = BuildManager(null);
        var cat = mgr.Privileges.Categories.First(c => c.ListKind == MetadataObjectKind.Table);
        var row = new PrivilegeRowViewModel(mgr.Privileges, "T", cat, new Dictionary<char, int>());
        Assert.Equal(0, row.SelectState);
        row.CycleCommand.Execute("S");
        Assert.Equal(1, row.SelectState); // none → granted
        row.CycleCommand.Execute("S");
        Assert.Equal(2, row.SelectState); // granted → with grant option
        row.CycleCommand.Execute("S");
        Assert.Equal(0, row.SelectState); // → none
    }

    // ─── Bulk picker + column panel state ──────────────────────────────────

    [Fact]
    public void BulkPrivileges_AllPlusApplicable_PerCategory()
    {
        var mgr = BuildManager(null);
        var p = mgr.Privileges;
        // Tables: All + S/I/U/D/R = 6 options; first is "All".
        Assert.Equal(6, p.BulkPrivileges.Count);
        Assert.Equal('*', p.BulkPrivileges[0].Code);
        Assert.Equal('*', p.SelectedBulkPrivilege!.Code);

        p.SelectedCategory = p.Categories.First(c => c.ListKind == MetadataObjectKind.Procedure);
        Assert.Equal(2, p.BulkPrivileges.Count); // All + Execute
        Assert.Contains(p.BulkPrivileges, o => o.Code == 'X');
    }

    [Fact]
    public void ColumnHeaderCommands_Exist_AndNoOpWithoutGrantee()
    {
        var mgr = BuildManager(null);
        var p = mgr.Privileges;
        // Per-column header grant/revoke commands (feature A). No grantee/connection →
        // safe no-op (no exception).
        p.GrantColumnCommand.Execute("S");
        p.RevokeColumnCommand.Execute("S");
        Assert.Null(mgr.ErrorMessage);
    }

    [Fact]
    public void ColumnPanel_VisibleOnlyForTables()
    {
        var mgr = BuildManager(null);
        var p = mgr.Privileges;
        Assert.True(p.ShowColumnPanel);       // Tables (default)
        Assert.True(p.ShowColumnHint);        // no row picked yet
        Assert.False(p.HasColumnTable);

        p.SelectedCategory = p.Categories.First(c => c.ListKind == MetadataObjectKind.View);
        Assert.False(p.ShowColumnPanel);
    }

    // ─── Membership pane (A: direction, B: tri-state) ──────────────────────

    [Fact]
    public void Membership_DirectionSwitch_ChangesPickerAndLabels()
    {
        var mgr = BuildManager(null);
        var m = mgr.Membership;
        Assert.Equal(MembershipDirection.MemberOf, m.Direction);
        Assert.Equal("Grantee", m.PickerLabel);

        m.SelectedDirection = m.Directions.First(d => d.Direction == MembershipDirection.Members);
        Assert.Equal(MembershipDirection.Members, m.Direction);
        Assert.Equal("Role", m.PickerLabel);
        Assert.Equal("User / Role", m.RowHeader);
    }

    [Fact]
    public void Membership_BothDirections_TriState()
    {
        var mgr = BuildManager(null);
        mgr.Roles.Items.Add(new RoleInfo { Name = "R1" });
        mgr.Roles.Items.Add(new RoleInfo { Name = "R2" });
        mgr.Grantees.Add(new GranteeOptionViewModel(new GranteeRef("ALICE", GranteeType.User)));
        mgr.Grantees.Add(new GranteeOptionViewModel(new GranteeRef("R1", GranteeType.Role)));
        mgr.Grantees.Add(new GranteeOptionViewModel(new GranteeRef("R2", GranteeType.Role)));
        // ALICE is a member of R1 WITH ADMIN OPTION.
        mgr.Membership.SetMembershipForTest(new[] { new MembershipInfo("ALICE", GranteeType.User, "R1", true) });

        // "Member of" + ALICE → rows are roles; R1=admin(2), R2=none(0).
        mgr.Membership.SelectedPicker = mgr.Membership.PickerItems.First(p => p.Name == "ALICE");
        Assert.Equal(2, mgr.Membership.Rows.First(r => r.Name == "R1").State);
        Assert.Equal(0, mgr.Membership.Rows.First(r => r.Name == "R2").State);
        Assert.DoesNotContain(mgr.Membership.Rows, r => r.Name == "ALICE"); // grantee not a row here

        // "Members" + R1 → rows are grantees; ALICE=admin(2), R2=none(0); R1 excluded (self).
        mgr.Membership.SelectedDirection = mgr.Membership.Directions.First(d => d.Direction == MembershipDirection.Members);
        mgr.Membership.SelectedPicker = mgr.Membership.PickerItems.First(p => p.Name == "R1");
        Assert.Equal(2, mgr.Membership.Rows.First(r => r.Name == "ALICE").State);
        Assert.Equal(0, mgr.Membership.Rows.First(r => r.Name == "R2").State);
        Assert.DoesNotContain(mgr.Membership.Rows, r => r.Name == "R1");
    }

    [Fact]
    public void MembershipRow_Cycle_AdvancesNoneMemberAdmin()
    {
        var mgr = BuildManager(null);
        // No picker selected → ApplyAsync resolves no edge → no DDL, no revert.
        var row = new MembershipRowViewModel(mgr.Membership, new GranteeRef("R1", GranteeType.Role), 0);
        row.CycleCommand.Execute(null);
        Assert.Equal(1, row.State); // none → member
        row.CycleCommand.Execute(null);
        Assert.Equal(2, row.State); // member → member + admin option
        row.CycleCommand.Execute(null);
        Assert.Equal(0, row.State); // → none
    }

    // ─── Grantee option VM ─────────────────────────────────────────────────

    [Fact]
    public void GranteeOption_UserDisplay()
    {
        var opt = new GranteeOptionViewModel(new GranteeRef("BOB", GranteeType.User));
        Assert.Equal("BOB", opt.Name);
        Assert.Equal(GranteeType.User, opt.Type);
        Assert.Equal("Icon.User", opt.IconGeometryKey);
    }

    // ─── Dialog VMs ────────────────────────────────────────────────────────

    [Fact]
    public void UserDialog_New_RequiresNameAndMatchingPassword()
    {
        var vm = new UserEditDialogViewModel(null);
        Assert.True(vm.IsNew);
        Assert.True(vm.CanEditName);
        Assert.False(vm.AcceptCommand.CanExecute(null));
        vm.UserName = "bob";
        Assert.Equal("BOB", vm.UserName); // uppercase coercion
        vm.Password = "p1";
        Assert.False(vm.AcceptCommand.CanExecute(null)); // confirm mismatch
        vm.ConfirmPassword = "p1";
        Assert.True(vm.AcceptCommand.CanExecute(null));
        vm.AcceptCommand.Execute(null);
        Assert.NotNull(vm.Result);
        Assert.Equal("BOB", vm.Result!.User.UserName);
        Assert.True(vm.Result.IsNew);
        Assert.Equal("p1", vm.Result.Password);
    }

    [Fact]
    public void UserDialog_Edit_SeedsFields_BlankPasswordOk()
    {
        var vm = new UserEditDialogViewModel(new UserInfo
        {
            UserName = "ALICE", FirstName = "Alice", Active = true, Admin = true, Description = "d",
        });
        Assert.False(vm.IsNew);
        Assert.False(vm.CanEditName);
        Assert.Equal("Alice", vm.FirstName);
        Assert.True(vm.Admin);
        Assert.True(vm.AcceptCommand.CanExecute(null)); // name present, blank password allowed
    }

    [Fact]
    public void RoleDialog_UppercaseAndResult()
    {
        var vm = new NewRoleDialogViewModel();
        Assert.False(vm.AcceptCommand.CanExecute(null));
        vm.RoleName = "rdr";
        Assert.Equal("RDR", vm.RoleName);
        Assert.True(vm.AcceptCommand.CanExecute(null));
        vm.AcceptCommand.Execute(null);
        Assert.Equal("RDR", vm.Result);
    }

    // Builds a Security Manager against a non-connected service — exercises the
    // ctor / pure VM logic without a live database.
    private static SecurityManagerTabViewModel BuildManager(MetadataObject? context)
    {
        var svc = new FirebirdConnectionService();
        var sec = new FirebirdSecurityReader(svc);
        var meta = new FirebirdMetadataReader(svc);
        var ddl = new FirebirdDdlExecutor(svc);
        return new SecurityManagerTabViewModel(sec, meta, ddl, context);
    }
}

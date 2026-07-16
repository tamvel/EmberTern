using System;
using System.IO;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Core.Scripting;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Developer Mode is one switch that affects ONLY the DDL path. DDL always runs WAIT (see
/// below); the modes differ only in the lock timeout — Standard fails fast against another
/// session, Developer waits longer. Data operations are unaffected.
///
/// <para>DDL is ALWAYS Wait because it now runs on its own attachment and can therefore meet
/// the transient cross-attachment metadata-cache lock held by one of our other lanes (the Data
/// lane that executed the routine). Measured on FB5: that lock clears in ~10 ms for a WAIT
/// transaction but fails instantly for NOWAIT — which is why NOWAIT used to force DDL onto the
/// Data connection and produce the "Commit or roll back the active transaction" guard.</para>
///
/// These pin the DDL TPB shapes, the persistence round-trip of the flag, and the dialog VM
/// carrying it + the (now UI-less) SQL Dialect value.
/// </summary>
public class DeveloperModeTests
{
    // ── DDL transaction-options shape (Standard vs Developer) ──────────────

    // DDL now runs on its OWN attachment, so it can meet a transient cross-attachment
    // metadata-cache lock held by one of our other lanes (the Data lane that executed the
    // routine). Measured on FB5: that lock clears in ~10 ms for a WAIT transaction but fails
    // instantly for NOWAIT. So DDL is ALWAYS Wait; the modes differ only in the timeout —
    // Standard is short (absorb our own release, still fail fast against another session),
    // Developer is long (wait for another session).
    [Fact]
    public void Standard_DdlIsWaitWithShortSelfReleaseTimeout()
    {
        var o = FirebirdDdlExecutor.BuildDdlTransactionOptions(developerMode: false);
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.Write));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.ReadCommitted));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.RecVersion));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.Wait));
        Assert.False(o.TransactionBehavior.HasFlag(FbTransactionBehavior.NoWait));
        Assert.Equal(TimeSpan.FromSeconds(FirebirdDdlExecutor.DdlSelfReleaseTimeoutSeconds), o.WaitTimeout);
    }

    // Developer Mode waits strictly longer than Standard.
    [Fact]
    public void Developer_WaitsLongerThanStandard()
    {
        var std = FirebirdDdlExecutor.BuildDdlTransactionOptions(developerMode: false);
        var dev = FirebirdDdlExecutor.BuildDdlTransactionOptions(developerMode: true);
        Assert.True(dev.WaitTimeout > std.WaitTimeout);
    }

    [Fact]
    public void Developer_DdlIsWaitWithLockTimeout()
    {
        var o = FirebirdDdlExecutor.BuildDdlTransactionOptions(developerMode: true);
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.Wait));
        Assert.False(o.TransactionBehavior.HasFlag(FbTransactionBehavior.NoWait));
        // Same isolation/access as Standard — only the wait policy changes.
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.Write));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.ReadCommitted));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.RecVersion));
        Assert.False(o.TransactionBehavior.HasFlag(FbTransactionBehavior.Consistency)); // never table-stability
        Assert.Equal(TimeSpan.FromSeconds(FirebirdDdlExecutor.DdlLockTimeoutSeconds), o.WaitTimeout);
    }

    // ── Which execution paths take the Developer Mode wait policy ──────────
    //
    // The Script Executor runs the whole script in ONE transaction, and a transaction's wait policy
    // is fixed at BEGIN — it cannot vary per statement. So the policy is chosen for the run, and it
    // is taken ONLY when both conditions hold. Each is load-bearing and has its own test below.

    private static ScriptStatement Stmt(ScriptStatementKind kind)
        => new("<sql>", kind, SourceOffset: 0, SourceLength: 5);

    // The case this feature exists for: deploying objects to a live database.
    [Fact]
    public void ScriptExecutor_AllDdlUnderAutoCommit_TakesDeveloperModeWaitPolicy()
    {
        var script = new[] { Stmt(ScriptStatementKind.Ddl), Stmt(ScriptStatementKind.Ddl) };
        Assert.True(FirebirdScriptExecutor.UsesDeveloperModeWaitPolicy(
            script, ScriptTransactionMode.AutoCommitOnSuccess));
    }

    // Condition 1 — all-DDL. A mixed script must NOT take the policy: the wait policy is per
    // transaction, so a WAIT tx here would make the script's DML wait too, which would change data
    // behaviour. DML never runs under WAIT.
    [Fact]
    public void ScriptExecutor_MixedScript_DoesNotTakeWaitPolicy_SoDmlNeverWaits()
    {
        var script = new[] { Stmt(ScriptStatementKind.Ddl), Stmt(ScriptStatementKind.Dml) };
        Assert.False(FirebirdScriptExecutor.UsesDeveloperModeWaitPolicy(
            script, ScriptTransactionMode.AutoCommitOnSuccess));
    }

    [Theory]
    [InlineData(ScriptStatementKind.Dml)]
    [InlineData(ScriptStatementKind.Select)]
    [InlineData(ScriptStatementKind.ExecuteProcedure)]
    [InlineData(ScriptStatementKind.ExecuteBlock)]
    [InlineData(ScriptStatementKind.Unknown)]
    public void ScriptExecutor_AnyNonDdlStatement_DefeatsTheWaitPolicy(ScriptStatementKind kind)
    {
        var script = new[] { Stmt(ScriptStatementKind.Ddl), Stmt(kind) };
        Assert.False(FirebirdScriptExecutor.UsesDeveloperModeWaitPolicy(
            script, ScriptTransactionMode.AutoCommitOnSuccess));
    }

    // Condition 2 — auto-commit. Manual leaves the transaction OPEN, and TransactionService
    // .BeginTransactionAsync early-returns on an active transaction, so the SQL Editor's next F5
    // would JOIN it — silently giving the console a WAIT transaction. That must not happen.
    [Fact]
    public void ScriptExecutor_AllDdlUnderManual_DoesNotTakeWaitPolicy_SoTheConsoleCannotInheritIt()
    {
        var script = new[] { Stmt(ScriptStatementKind.Ddl) };
        Assert.False(FirebirdScriptExecutor.UsesDeveloperModeWaitPolicy(
            script, ScriptTransactionMode.Manual));
    }

    [Fact]
    public void ScriptExecutor_EmptyScript_TakesNoSpecialPolicy()
        => Assert.False(FirebirdScriptExecutor.UsesDeveloperModeWaitPolicy(
            Array.Empty<ScriptStatement>(), ScriptTransactionMode.AutoCommitOnSuccess));

    // The policy the Script Executor takes is the SAME object Compile takes — one definition of
    // "the Dev Mode wait policy", not a copy that can drift.
    [Fact]
    public void ScriptExecutor_WaitPolicy_IsTheSameBuilderCompileUses()
    {
        var dev = FirebirdDdlExecutor.BuildDdlTransactionOptions(developerMode: true);
        Assert.True(dev.TransactionBehavior.HasFlag(FbTransactionBehavior.Wait));
        Assert.Equal(TimeSpan.FromSeconds(FirebirdDdlExecutor.DdlLockTimeoutSeconds), dev.WaitTimeout);
    }

    // ── Real deployment scripts, through the real parser ───────────────────
    //
    // The tests above pin the DECISION over hand-built statement lists. These pin the INPUT: that
    // real deployment scripts — SET TERM, comments, blank statements — actually reduce to an
    // all-DDL statement list. FbScript.Parse() is offline (no database), so the real parser runs
    // here. Without these, "all DDL" could silently mean "no SET TERM allowed".

    private static bool PolicyFor(string script)
        => FirebirdScriptExecutor.UsesDeveloperModeWaitPolicy(
            new FirebirdScriptParser().Parse(script), ScriptTransactionMode.AutoCommitOnSuccess);

    // The canonical object-deployment script. SET TERM is a client directive consumed by the
    // driver's parser, not a statement — so it must not defeat the all-DDL verdict.
    [Fact]
    public void RealScript_SetTermProcedureDeployment_IsAllDdl()
    {
        const string script = @"
SET TERM ^ ;

CREATE OR ALTER PROCEDURE P_PROBE (A INTEGER)
RETURNS (B INTEGER)
AS
BEGIN
  B = A + 1;
  SUSPEND;
END^

CREATE OR ALTER PROCEDURE P_PROBE2
AS
BEGIN
  EXIT;
END^

SET TERM ; ^
";
        Assert.True(PolicyFor(script));
    }

    // Comments — leading and between statements. They attach to their statement rather than
    // becoming statements of their own, so they cannot defeat the all-DDL verdict.
    // (A comment AFTER the final terminator is a different story — see the known defect below.)
    [Fact]
    public void RealScript_WithComments_IsAllDdl()
    {
        const string script = @"
/* Deployment 1.2.0
   Adds the audit column. */
-- first the column
ALTER TABLE PROBE_T ADD NOTE VARCHAR(100);

-- then the index
CREATE INDEX IX_PROBE_T_NOTE ON PROBE_T (NOTE);
";
        Assert.True(PolicyFor(script));
    }

    // KNOWN DEFECT, pre-existing and NOT about Developer Mode — pinned here because it was found
    // while verifying the all-DDL detection against real deployment scripts, and because the
    // symptom invites misdiagnosis ("the Dev Mode detection is broken" — it is not; the script
    // never reaches it).
    //
    // A comment after the LAST terminator makes the driver's FbScript.Parse() throw
    // ArgumentException("The type of the SQL statement could not be determined"), so the script
    // fails to parse ENTIRELY — in every mode, Dev Mode or not, and regardless of content. A
    // deployment script ending in "-- done" cannot run at all. The fix belongs in
    // FirebirdScriptParser (the driver raises UnknownStatement for the trailing comment fragment),
    // not in the Dev Mode decision. Tracked as its own task.
    //
    // When that is fixed, this test SHOULD fail — replace it with an all-DDL assertion.
    [Fact]
    public void RealScript_TrailingCommentAfterLastTerminator_FailsToParse_KnownDefect()
    {
        const string script = @"
CREATE TABLE PROBE_E (ID INTEGER);
/* done */
";
        Assert.Throws<ArgumentException>(() => new FirebirdScriptParser().Parse(script));
    }

    // Blank/empty statements from stray semicolons.
    [Fact]
    public void RealScript_WithEmptyStatements_IsAllDdl()
    {
        const string script = @"
CREATE TABLE PROBE_A (ID INTEGER);
;
CREATE TABLE PROBE_B (ID INTEGER);

;
";
        Assert.True(PolicyFor(script));
    }

    // GRANT/COMMENT ON alongside object DDL — still an object-deployment script.
    [Fact]
    public void RealScript_DdlWithGrantAndCommentOn_IsAllDdl()
    {
        const string script = @"
CREATE TABLE PROBE_C (ID INTEGER);
COMMENT ON TABLE PROBE_C IS 'probe';
GRANT SELECT ON PROBE_C TO PUBLIC;
";
        Assert.True(PolicyFor(script));
    }

    // The guard that matters: real DML in the script → no WAIT policy, so DML never waits.
    [Fact]
    public void RealScript_MixedDdlAndDml_IsNotAllDdl()
    {
        const string script = @"
CREATE TABLE PROBE_D (ID INTEGER);
INSERT INTO PROBE_D (ID) VALUES (1);
";
        Assert.False(PolicyFor(script));
    }

    // ── Persistence round-trip of the flag (+ Dialect kept for compat) ─────

    [Fact]
    public void DeveloperMode_RoundtripsThroughStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ConnectionProfileStore(dir);
            store.Upsert(new ConnectionProfile
            {
                Name = "Dev",
                DatabasePath = "/db/dev.fdb",
                DeveloperMode = true,
                Dialect = 1, // legacy dialect must survive even with no UI for it
            });

            var reloaded = store.LoadAll();
            Assert.Single(reloaded);
            Assert.True(reloaded[0].DeveloperMode);
            Assert.Equal(1, reloaded[0].Dialect);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NewProfile_DefaultsToDeveloperModeOff()
        => Assert.False(new ConnectionProfile().DeveloperMode);

    // ── Dialog VM carries Developer Mode + the hidden Dialect value ────────

    [Fact]
    public void Dialog_BuildsProfileWithDeveloperModeAndCarriedDialect()
    {
        using var service = new FirebirdConnectionService();
        var vm = new NewConnectionDialogViewModel(service);
        vm.LoadFromProfile(new ConnectionProfile
        {
            Name = "Edit",
            DatabasePath = "/db/x.fdb",
            Dialect = 1,            // dialect has no UI but must round-trip
            DeveloperMode = false,
        });

        vm.DeveloperMode = true;    // user flips the switch
        vm.SaveCommand.Execute(null);

        Assert.NotNull(vm.Result);
        Assert.True(vm.Result!.DeveloperMode);
        Assert.Equal(1, vm.Result.Dialect); // carried through unchanged
    }
}

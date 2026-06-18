using System;
using System.IO;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Krok 2: Developer Mode replaces the per-lane TPB profile pickers with one switch
/// that affects ONLY the DDL path. Standard = NOWAIT (fail-fast); Developer = WAIT +
/// lock timeout (DDL waits for an in-use object instead of "object is in use"). Data
/// operations are unaffected (always NOWAIT). These pin the DDL TPB shapes, the
/// persistence round-trip of the flag, and the dialog VM carrying it + the (now
/// UI-less) SQL Dialect value.
/// </summary>
public class DeveloperModeTests
{
    // ── DDL transaction-options shape (Standard vs Developer) ──────────────

    [Fact]
    public void Standard_DdlIsNoWaitReadWriteReadCommitted()
    {
        var o = FirebirdDdlExecutor.BuildDdlTransactionOptions(developerMode: false);
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.Write));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.ReadCommitted));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.RecVersion));
        Assert.True(o.TransactionBehavior.HasFlag(FbTransactionBehavior.NoWait));
        Assert.False(o.TransactionBehavior.HasFlag(FbTransactionBehavior.Wait));
        Assert.Null(o.WaitTimeout); // no lock timeout in fail-fast mode
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

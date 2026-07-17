using System;
using EmberTern.Firebird;
using FirebirdSql.Data.FirebirdClient;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, milestone D2 seam (a): the debug session connection's PURE pieces — the
/// explicit debug TPB (spec §4.2) and the savepoint statement text (§4.5) — pinned without a live server
/// (mirrors <see cref="TransactionTpbTests"/>). The live round-trip (open attachment → begin tx → set /
/// rollback-to / release a savepoint → commit/rollback) needs a real Firebird and is verified against the
/// Lab (§15.3 probe [5] already confirmed SAVEPOINT/ROLLBACK TO work through the driver); reported as
/// "awaits user confirmation" per the QA rule.
/// </summary>
public class DebugSessionConnectionTests
{
    private static FbTransactionBehavior Behavior(DebugIsolation isolation)
        => DebugSessionConnection.BuildDebugTransactionOptions(isolation).TransactionBehavior;

    // ── TPB: READ COMMITTED = write + read_committed + rec_version + nowait (§4.2 default) ─────────

    [Fact]
    public void ReadCommitted_IsWriteReadCommittedRecVersionNoWait()
    {
        var b = Behavior(DebugIsolation.ReadCommitted);
        Assert.True(b.HasFlag(FbTransactionBehavior.Write));
        Assert.True(b.HasFlag(FbTransactionBehavior.ReadCommitted));
        Assert.True(b.HasFlag(FbTransactionBehavior.RecVersion));
        Assert.True(b.HasFlag(FbTransactionBehavior.NoWait));
    }

    [Fact]
    public void ReadCommitted_IsNeverWait()
    {
        // NOWAIT is the whole point (§4.2) — a lock met on the user's Data tx must be a step-level error at
        // a known line, never a silent hang.
        var b = Behavior(DebugIsolation.ReadCommitted);
        Assert.False(b.HasFlag(FbTransactionBehavior.Wait));
        Assert.False(b.HasFlag(FbTransactionBehavior.Concurrency));
    }

    // ── TPB: SNAPSHOT = write + concurrency + nowait ──────────────────────────────────────────────

    [Fact]
    public void Snapshot_IsWriteConcurrencyNoWait()
    {
        var b = Behavior(DebugIsolation.Snapshot);
        Assert.True(b.HasFlag(FbTransactionBehavior.Write));
        Assert.True(b.HasFlag(FbTransactionBehavior.Concurrency));
        Assert.True(b.HasFlag(FbTransactionBehavior.NoWait));
    }

    [Fact]
    public void Snapshot_IsNeverReadCommittedAndNeverWait()
    {
        var b = Behavior(DebugIsolation.Snapshot);
        Assert.False(b.HasFlag(FbTransactionBehavior.ReadCommitted));
        Assert.False(b.HasFlag(FbTransactionBehavior.Wait));
    }

    // ── Savepoint statement text (§4.5) ───────────────────────────────────────────────────────────

    [Fact]
    public void SavepointStatement_BuildsTheThreeForms()
    {
        Assert.Equal("SAVEPOINT ET_DBG_FRAME_0",
            DebugSessionConnection.SavepointStatement(DebugSessionConnection.SavepointOp.Set, "ET_DBG_FRAME_0"));
        Assert.Equal("RELEASE SAVEPOINT ET_DBG_FRAME_1",
            DebugSessionConnection.SavepointStatement(DebugSessionConnection.SavepointOp.Release, "ET_DBG_FRAME_1"));
        Assert.Equal("ROLLBACK TO SAVEPOINT ET_DBG_FRAME_2",
            DebugSessionConnection.SavepointStatement(DebugSessionConnection.SavepointOp.RollbackTo, "ET_DBG_FRAME_2"));
    }

    [Theory]
    [InlineData("ET_DBG_FRAME_0", true)]
    [InlineData("_x", true)]
    [InlineData("A1", true)]
    [InlineData("", false)]
    [InlineData("1BAD", false)]
    [InlineData("has space", false)]
    [InlineData("drop;--", false)]
    public void IsValidSavepointName_AcceptsBareIdentifiersOnly(string name, bool expected)
        => Assert.Equal(expected, DebugSessionConnection.IsValidSavepointName(name));

    [Fact]
    public void SavepointStatement_RejectsAnInvalidName()
        => Assert.Throws<ArgumentException>(
            () => DebugSessionConnection.SavepointStatement(DebugSessionConnection.SavepointOp.Set, "1; DROP"));
}

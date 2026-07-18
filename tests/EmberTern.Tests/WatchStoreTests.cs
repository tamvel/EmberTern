using System;
using System.IO;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

/// <summary>Stage X — Firebird Debugger, D5 seam (b): per-routine Watch persistence over the shared
/// settings.dat (mirrors <c>ParameterHistoryStoreTests</c>).</summary>
public class WatchStoreTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Get_ReturnsEmpty_WhenNothingSaved()
    {
        var dir = NewTempDir();
        try { Assert.Empty(new WatchStore(dir).Get("c1", "SP")); }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Save_ThenGet_RoundTripsAcrossInstances_InOrder()
    {
        var dir = NewTempDir();
        try
        {
            new WatchStore(dir).Save("c1", "SP", new[] { "a + b", "count(*)" });

            var loaded = new WatchStore(dir).Get("c1", "SP");
            Assert.Equal(new[] { "a + b", "count(*)" }, loaded);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Save_Replaces_PreviousList()
    {
        var dir = NewTempDir();
        try
        {
            var store = new WatchStore(dir);
            store.Save("c1", "SP", new[] { "a", "b" });
            store.Save("c1", "SP", new[] { "c" });
            Assert.Equal(new[] { "c" }, store.Get("c1", "SP"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Save_EmptyList_RemovesEntry()
    {
        var dir = NewTempDir();
        try
        {
            var store = new WatchStore(dir);
            store.Save("c1", "SP", new[] { "a" });
            store.Save("c1", "SP", Array.Empty<string>());
            Assert.Empty(store.Get("c1", "SP"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Save_IsPerRoutine()
    {
        var dir = NewTempDir();
        try
        {
            var store = new WatchStore(dir);
            store.Save("c1", "SP_A", new[] { "a" });
            store.Save("c1", "SP_B", new[] { "b" });
            Assert.Equal(new[] { "a" }, store.Get("c1", "SP_A"));
            Assert.Equal(new[] { "b" }, store.Get("c1", "SP_B"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void BlankKey_DisablesPersistence()
    {
        var dir = NewTempDir();
        try
        {
            var store = new WatchStore(dir);
            store.Save(null, "SP", new[] { "a" });    // no connection → no-op
            store.Save("c1", null, new[] { "a" });    // no routine → no-op
            Assert.Empty(store.Get(null, "SP"));
            Assert.Empty(store.Get("c1", null));
        }
        finally { Cleanup(dir); }
    }
}

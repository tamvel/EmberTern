using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EmberTern.Core.Connections;
using EmberTern.Core.Settings;
using EmberTern.Core.Workspace;
using Xunit;

namespace EmberTern.Tests;

public class ParameterHistoryStoreTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));

    private static List<ParameterValue> Set(params (string Name, string? Text)[] values)
        => values.Select(v => new ParameterValue { Name = v.Name, IsNull = v.Text is null, Text = v.Text }).ToList();

    private static List<ParameterValue> TypedSet(params (string Name, string? Text, string? TypeText)[] values)
        => values
            .Select(v => new ParameterValue { Name = v.Name, IsNull = v.Text is null, Text = v.Text, TypeText = v.TypeText })
            .ToList();

    [Fact]
    public void Record_CarriesTheDeclaredType()
    {
        // Dropping the type on the way in would store a value whose compatibility can never again be proven.
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            store.Record("c1", "Procedure", "SP", TypedSet(("A", "10", "INTEGER")));

            var stored = new ParameterHistoryStore(dir).Get("c1", "Procedure", "SP");
            Assert.Equal("INTEGER", stored[0].Values[0].TypeText);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Record_TreatsTheSameTextUnderADifferentTypeAsANewSet()
    {
        // Otherwise the repeat-detection would refresh the old entry's timestamp and keep its stale type, so the
        // value the user just used could never be proven restorable.
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            store.Record("c1", "Procedure", "SP", TypedSet(("A", "10", "INTEGER")));
            store.Record("c1", "Procedure", "SP", TypedSet(("A", "10", "VARCHAR(10)")));

            var stored = new ParameterHistoryStore(dir).Get("c1", "Procedure", "SP");
            Assert.Equal(2, stored.Count);
            Assert.Equal("VARCHAR(10)", stored[0].Values[0].TypeText); // newest first
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Get_ReturnsEmpty_WhenNothingSaved()
    {
        var dir = NewTempDir();
        try
        {
            Assert.Empty(new ParameterHistoryStore(dir).Get("c1", "Procedure", "SP"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Get_ReturnsEmpty_WhenKeyBlank()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            store.Record("c1", "Procedure", "SP", Set(("A", "1")));
            Assert.Empty(store.Get(null, "Procedure", "SP"));
            Assert.Empty(store.Get("c1", "Procedure", null));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Record_ThenGet_RoundTripsAcrossInstances()
    {
        var dir = NewTempDir();
        try
        {
            new ParameterHistoryStore(dir).Record("c1", "Procedure", "SP", Set(("DATAOD", "2024-01-01"), ("DATADO", "2024-12-31")));

            var loaded = new ParameterHistoryStore(dir).Get("c1", "Procedure", "SP");
            Assert.Single(loaded);
            Assert.Equal(2, loaded[0].Values.Count);
            Assert.Equal("DATAOD", loaded[0].Values[0].Name);
            Assert.Equal("2024-01-01", loaded[0].Values[0].Text);
            Assert.Equal("2024-12-31", loaded[0].Values[1].Text);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Record_NewestFirst()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            store.Record("c1", "Procedure", "SP", Set(("A", "1")));
            store.Record("c1", "Procedure", "SP", Set(("A", "2")));

            var loaded = store.Get("c1", "Procedure", "SP");
            Assert.Equal(2, loaded.Count);
            Assert.Equal("2", loaded[0].Values[0].Text);   // newest first
            Assert.Equal("1", loaded[1].Values[0].Text);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Record_DedupsIdenticalNewest()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            store.Record("c1", "Procedure", "SP", Set(("A", "1")));
            store.Record("c1", "Procedure", "SP", Set(("A", "1")));  // identical → no new entry

            Assert.Single(store.Get("c1", "Procedure", "SP"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Record_NullValueRoundTripsAndDiffersFromEmpty()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            store.Record("c1", "Procedure", "SP", new List<ParameterValue> { new() { Name = "A", IsNull = true, Text = null } });
            store.Record("c1", "Procedure", "SP", Set(("A", "")));  // empty text ≠ NULL → new entry

            var loaded = store.Get("c1", "Procedure", "SP");
            Assert.Equal(2, loaded.Count);
            Assert.False(loaded[0].Values[0].IsNull);
            Assert.True(loaded[1].Values[0].IsNull);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Record_CapsAtMaxSets()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            for (int i = 0; i < ParameterHistoryStore.MaxSets + 5; i++)
            {
                store.Record("c1", "Procedure", "SP", Set(("A", i.ToString())));
            }

            var loaded = store.Get("c1", "Procedure", "SP");
            Assert.Equal(ParameterHistoryStore.MaxSets, loaded.Count);
            // Newest kept, oldest dropped.
            Assert.Equal((ParameterHistoryStore.MaxSets + 4).ToString(), loaded[0].Values[0].Text);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Record_IsolatesByConnectionKindAndName()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            store.Record("c1", "Procedure", "SP", Set(("A", "conn1")));
            store.Record("c2", "Procedure", "SP", Set(("A", "conn2")));
            store.Record("c1", "Function", "SP", Set(("A", "func")));   // same name, different kind
            store.Record("c1", "Procedure", "OTHER", Set(("A", "other")));

            Assert.Equal("conn1", store.Get("c1", "Procedure", "SP")[0].Values[0].Text);
            Assert.Equal("conn2", store.Get("c2", "Procedure", "SP")[0].Values[0].Text);
            Assert.Equal("func", store.Get("c1", "Function", "SP")[0].Values[0].Text);
            Assert.Equal("other", store.Get("c1", "Procedure", "OTHER")[0].Values[0].Text);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Record_NameMatchIsCaseInsensitive()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ParameterHistoryStore(dir);
            store.Record("c1", "Procedure", "MyProc", Set(("A", "1")));
            store.Record("c1", "Procedure", "MYPROC", Set(("A", "1")));  // same entry → dedups

            // Both casings resolve to the ONE shared entry with a single (deduped) run.
            Assert.Single(store.Get("c1", "Procedure", "myproc"));
            Assert.Equal("1", store.Get("c1", "Procedure", "MyProc")[0].Values[0].Text);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Record_PreservesOtherSections()
    {
        var dir = NewTempDir();
        try
        {
            var connections = new ConnectionProfileStore(dir);
            connections.Upsert(new ConnectionProfile { Name = "Prod", Host = "db1" });
            new WorkspaceStore(dir).Save(new WorkspaceState { QueryPanelVisible = false });

            new ParameterHistoryStore(dir).Record("c1", "Procedure", "SP", Set(("A", "1")));

            Assert.Single(connections.LoadAll());
            Assert.Equal("Prod", connections.LoadAll()[0].Name);
            Assert.False(new WorkspaceStore(dir).Load()!.QueryPanelVisible);
            Assert.Single(new ParameterHistoryStore(dir).Get("c1", "Procedure", "SP"));
        }
        finally { Cleanup(dir); }
    }

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}

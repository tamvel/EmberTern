using System;
using System.IO;
using EmberTern.Core.Import;
using EmberTern.Core.Settings;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I1: the implicit "last used" configuration over the shared settings.dat (mirrors
/// <c>WatchStoreTests</c> / <c>ParameterHistoryStoreTests</c>).
/// <para>
/// This store is what makes profiles a foundation rather than a promise (design §4.8.4): the mechanism ships in
/// the MVP carrying the last-used configuration, and named profiles in etap I11 are more entries in the same
/// list. So these tests are not about a future feature — they pin the thing the surface will restore from on its
/// second run.
/// </para>
/// </summary>
public class ImportProfileStoreTests
{
    private static string NewTempDir()
        => Path.Combine(Path.GetTempPath(), "EmberTern-tests-" + Guid.NewGuid().ToString("N"));

    private static void Cleanup(string dir)
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    private static ImportConfiguration Sample(string table = "ORDERS", char delimiter = ';') => new()
    {
        Source = SourceDescriptor.File(ImportSourceKind.Csv, @"C:\dane\orders.csv"),
        Delimited = new DelimitedOptions { Delimiter = delimiter, FirstDataRow = 2 },
        Target = TargetDescriptor.Existing(table),
        Mapping = new[]
        {
            new ColumnMapping
            {
                TargetColumnName = "ORDER_ID",
                SourceFieldName = "Order id",
                SourceFieldIndex = 0,
                Origin = MappingOrigin.Restored,
            },
        },
        Transaction = ImportTransactionMode.Batched,
        CommitEveryRows = 5_000,
    };

    [Fact]
    public void GetLastUsed_ReturnsNull_WhenNothingSaved()
    {
        var dir = NewTempDir();
        try { Assert.Null(new ImportProfileStore(dir).GetLastUsed("c1")); }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SaveLastUsed_ThenGet_RoundTripsAcrossInstances()
    {
        var dir = NewTempDir();
        try
        {
            new ImportProfileStore(dir).SaveLastUsed("c1", Sample());

            var loaded = new ImportProfileStore(dir).GetLastUsed("c1");

            Assert.NotNull(loaded);
            Assert.Equal("ORDERS", loaded!.Target.TableName);
            Assert.Equal(';', loaded.Delimited!.Delimiter);
            Assert.Equal(ImportTransactionMode.Batched, loaded.Transaction);
            Assert.Equal(5_000, loaded.CommitEveryRows);
            // The mapping identity R16 depends on must survive persistence, not just the positions.
            Assert.Equal("Order id", loaded.Mapping[0].SourceFieldName);
            Assert.Equal(MappingOrigin.Restored, loaded.Mapping[0].Origin);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SaveLastUsed_Replaces_ThePreviousEntry_RatherThanAccumulating()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveLastUsed("c1", Sample("FIRST"));
            store.SaveLastUsed("c1", Sample("SECOND"));

            Assert.Equal("SECOND", store.GetLastUsed("c1")!.Target.TableName);

            // One implicit entry per connection — not a growing history.
            var settings = new ApplicationSettingsStore(dir).Load();
            Assert.NotNull(settings);
            Assert.Single(settings!.UserSettings.ImportProfiles);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Entries_AreScopedPerConnection()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveLastUsed("c1", Sample("FOR_C1"));
            store.SaveLastUsed("c2", Sample("FOR_C2"));

            Assert.Equal("FOR_C1", store.GetLastUsed("c1")!.Target.TableName);
            Assert.Equal("FOR_C2", store.GetLastUsed("c2")!.Target.TableName);
            Assert.Null(store.GetLastUsed("c3"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void ClearLastUsed_ForgetsTheEntry()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveLastUsed("c1", Sample());
            store.ClearLastUsed("c1");

            Assert.Null(store.GetLastUsed("c1"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void ClearLastUsed_IsANoOp_WhenNothingIsStored()
    {
        var dir = NewTempDir();
        try
        {
            new ImportProfileStore(dir).ClearLastUsed("c1");
            Assert.Null(new ImportProfileStore(dir).GetLastUsed("c1"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void BlankConnectionId_DisablesPersistence_RatherThanSharingOneGlobalSlot()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveLastUsed(null, Sample());
            store.SaveLastUsed(string.Empty, Sample());

            Assert.Null(store.GetLastUsed(null));
            Assert.Null(store.GetLastUsed(string.Empty));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// §0.7 — a configuration from a newer build is refused whole, never applied in part. Reading the fields this
    /// build happens to recognise would be exactly the silent, partial restore the paramount rule forbids.
    /// </summary>
    [Fact]
    public void ConfigurationFromAFutureVersion_IsRefused_NotPartiallyApplied()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveLastUsed("c1", Sample() with { Version = ImportConfiguration.CurrentVersion + 1 });

            Assert.Null(store.GetLastUsed("c1"));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// The section facade must not clobber the neighbours in the shared file — the property every other store in
    /// this codebase is written to preserve.
    /// </summary>
    [Fact]
    public void Writing_DoesNotClobber_OtherSectionsOfTheSharedFile()
    {
        var dir = NewTempDir();
        try
        {
            new WatchStore(dir).Save("c1", "SP_X", new[] { "a + b" });
            new ParameterHistoryStore(dir).Record("c1", "Procedure", "SP_X", Array.Empty<ParameterValue>());

            new ImportProfileStore(dir).SaveLastUsed("c1", Sample());

            Assert.Equal(new[] { "a + b" }, new WatchStore(dir).Get("c1", "SP_X"));
            Assert.NotEmpty(new ParameterHistoryStore(dir).Get("c1", "Procedure", "SP_X"));
            Assert.NotNull(new ImportProfileStore(dir).GetLastUsed("c1"));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// The settings.dat schema version must stay where it is: bumping it for an additive section would trip the
    /// store's own downgrade protection and make an older build refuse the WHOLE file (design §4.8.3).
    /// </summary>
    [Fact]
    public void AddingTheSection_DoesNotBumpTheSettingsSchemaVersion()
    {
        var dir = NewTempDir();
        try
        {
            new ImportProfileStore(dir).SaveLastUsed("c1", Sample());

            var settings = new ApplicationSettingsStore(dir).Load();

            Assert.NotNull(settings);
            Assert.Equal(ApplicationSettingsStore.CurrentSchemaVersion, settings!.SchemaVersion);
            Assert.Equal(2, ApplicationSettingsStore.CurrentSchemaVersion);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void ImplicitProfile_IsRecognisableAsSuch()
    {
        var dir = NewTempDir();
        try
        {
            new ImportProfileStore(dir).SaveLastUsed("c1", Sample());

            var settings = new ApplicationSettingsStore(dir).Load();
            var profile = Assert.Single(settings!.UserSettings.ImportProfiles);

            Assert.True(profile.IsImplicit);
            Assert.Equal(string.Empty, profile.Name);
            Assert.Equal("c1", profile.ConnectionId);
            Assert.NotEqual(default, profile.LastUsedUtc);
        }
        finally { Cleanup(dir); }
    }
}

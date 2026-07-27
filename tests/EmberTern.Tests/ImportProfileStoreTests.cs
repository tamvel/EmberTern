using System;
using System.IO;
using System.Linq;
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

    // ── Named profiles (etap I11) ───────────────────────────────────────────────────────────────────────
    //
    // ⭐ Read these against the implicit-profile tests above: the fixtures are the same, the file is the same
    // and the list is the same. That is the etap's whole claim — a named profile is a row that has a name.

    [Fact]
    public void SaveNamed_ThenList_RoundTripsAcrossInstances()
    {
        var dir = NewTempDir();
        try
        {
            new ImportProfileStore(dir).SaveNamed("c1", "Nightly orders", Sample("ORDERS", '|'));

            var listed = Assert.Single(new ImportProfileStore(dir).ListNamed("c1"));

            Assert.Equal("Nightly orders", listed.Name);
            Assert.Equal("ORDERS", listed.Configuration.Target.TableName);
            Assert.Equal('|', listed.Configuration.Delimited!.Delimiter);
            // The identity R16 depends on has to survive here exactly as it does for the implicit entry.
            Assert.Equal("Order id", listed.Configuration.Mapping[0].SourceFieldName);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>The implicit "last used" entry and a named profile share one list and must not disturb each
    /// other — the named list never shows the nameless row, and saving one does not overwrite the other.</summary>
    [Fact]
    public void NamedAndImplicit_CoexistWithoutSeeingEachOther()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveLastUsed("c1", Sample("IMPLICIT"));
            store.SaveNamed("c1", "Named", Sample("NAMED"));

            Assert.Equal("IMPLICIT", store.GetLastUsed("c1")!.Target.TableName);
            var named = Assert.Single(store.ListNamed("c1"));
            Assert.Equal("NAMED", named.Configuration.Target.TableName);

            var settings = new ApplicationSettingsStore(dir).Load();
            Assert.Equal(2, settings!.UserSettings.ImportProfiles.Count);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SaveNamed_ReplacesTheSameName_RatherThanAddingASecondRow()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveNamed("c1", "Orders", Sample("FIRST"));
            store.SaveNamed("c1", "Orders", Sample("SECOND"));

            var only = Assert.Single(store.ListNamed("c1"));
            Assert.Equal("SECOND", only.Configuration.Target.TableName);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void NamedProfiles_AreScopedToTheirConnection_AndConnectionlessOnesAreOfferedEverywhere()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveNamed("c1", "Only on c1", Sample());
            store.SaveNamed(null, "Shared", Sample());

            // ⭐ The scope rule the selector states on screen: this connection's, plus the portable ones. A
            // profile names a TABLE, so one made against another database is a promise this one may not keep.
            Assert.Equal(
                new[] { "Only on c1", "Shared" },
                store.ListNamed("c1").Select(p => p.Name).ToArray());

            Assert.Equal(new[] { "Shared" }, store.ListNamed("c2").Select(p => p.Name).ToArray());
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void ListNamed_IsOrderedByName()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveNamed("c1", "Zebra", Sample());
            store.SaveNamed("c1", "alpha", Sample());
            store.SaveNamed("c1", "Mango", Sample());

            Assert.Equal(
                new[] { "alpha", "Mango", "Zebra" },
                store.ListNamed("c1").Select(p => p.Name).ToArray());
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Rename_KeepsTheIdentity_SoNothingIsOrphaned()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            var saved = store.SaveNamed("c1", "Old name", Sample("ORDERS"));

            Assert.True(store.Rename(saved.Id, "New name"));

            var reloaded = Assert.Single(new ImportProfileStore(dir).ListNamed("c1"));
            Assert.Equal("New name", reloaded.Name);
            Assert.Equal(saved.Id, reloaded.Id);
            Assert.Equal("ORDERS", reloaded.Configuration.Target.TableName);
        }
        finally { Cleanup(dir); }
    }

    /// <summary>A refused rename is REPORTED, never resolved by picking some other name — the surface tells the
    /// user, and the profile keeps the name it had.</summary>
    [Fact]
    public void Rename_RefusesADuplicate_AndChangesNothing()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveNamed("c1", "Taken", Sample());
            var mine = store.SaveNamed("c1", "Mine", Sample());

            Assert.False(store.Rename(mine.Id, "taken"));

            Assert.Equal(
                new[] { "Mine", "Taken" },
                new ImportProfileStore(dir).ListNamed("c1").Select(p => p.Name).ToArray());
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Rename_ToItsOwnNameInADifferentCase_IsAllowed()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            var mine = store.SaveNamed("c1", "orders", Sample());

            Assert.True(store.Rename(mine.Id, "ORDERS"));
            Assert.Equal("ORDERS", Assert.Single(store.ListNamed("c1")).Name);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Delete_RemovesOnlyThatProfile()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            var doomed = store.SaveNamed("c1", "Doomed", Sample());
            store.SaveNamed("c1", "Keeper", Sample());
            store.SaveLastUsed("c1", Sample("IMPLICIT"));

            Assert.True(store.Delete(doomed.Id));

            Assert.Equal("Keeper", Assert.Single(store.ListNamed("c1")).Name);
            // Deleting a named profile must not disturb the implicit entry sharing the list.
            Assert.Equal("IMPLICIT", store.GetLastUsed("c1")!.Target.TableName);
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void Delete_CannotReachTheImplicitEntry()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveLastUsed("c1", Sample());

            var implicitId = new ApplicationSettingsStore(dir).Load()!.UserSettings.ImportProfiles[0].Id;

            Assert.False(store.Delete(implicitId));
            Assert.NotNull(store.GetLastUsed("c1"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void NameExists_IsCaseInsensitive_AndIgnoresTheProfileBeingRenamed()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            var mine = store.SaveNamed("c1", "Orders", Sample());

            Assert.True(store.NameExists("c1", "orders"));
            Assert.False(store.NameExists("c1", "orders", exceptId: mine.Id));
            Assert.False(store.NameExists("c2", "orders"));
        }
        finally { Cleanup(dir); }
    }

    [Fact]
    public void SaveNamed_TrimsTheName_AndRefusesABlankOne()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);

            Assert.Equal("Orders", store.SaveNamed("c1", "  Orders  ", Sample()).Name);
            Assert.Throws<ArgumentException>(() => store.SaveNamed("c1", "   ", Sample()));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>
    /// ⭐ A profile from a newer build is LISTED and marked unreadable, not hidden and not half-applied.
    /// <para>
    /// Both halves matter. Half-applying is §0.7 outright; hiding is subtler and just as bad — the user would
    /// see a profile they saved simply cease to exist, which reads as data loss.
    /// </para>
    /// </summary>
    [Fact]
    public void AProfileFromTheFuture_IsListedButNotReadable()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            store.SaveNamed("c1", "From tomorrow", Sample() with { Version = ImportConfiguration.CurrentVersion + 1 });
            store.SaveNamed("c1", "From today", Sample());

            var listed = new ImportProfileStore(dir).ListNamed("c1");

            Assert.Equal(2, listed.Count);
            Assert.False(ImportProfileStore.IsReadable(listed.Single(p => p.Name == "From tomorrow")));
            Assert.True(ImportProfileStore.IsReadable(listed.Single(p => p.Name == "From today")));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>The same predicate governs the implicit restore, which is why they cannot disagree about what
    /// "too new" means.</summary>
    [Fact]
    public void GetLastUsed_RefusesAConfigurationFromTheFuture()
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

    [Fact]
    public void GetById_FindsANamedProfile_AndNeverTheImplicitOne()
    {
        var dir = NewTempDir();
        try
        {
            var store = new ImportProfileStore(dir);
            var named = store.SaveNamed("c1", "Named", Sample());
            store.SaveLastUsed("c1", Sample());

            var implicitId = new ApplicationSettingsStore(dir).Load()!
                .UserSettings.ImportProfiles.Single(p => p.IsImplicit).Id;

            Assert.Equal("Named", store.GetById(named.Id)!.Name);
            Assert.Null(store.GetById(implicitId));
            Assert.Null(store.GetById("nope"));
        }
        finally { Cleanup(dir); }
    }

    /// <summary>Named profiles are additive to the same section, so the container version stays where it is —
    /// bumping it would trip the downgrade protection and an older build would refuse the WHOLE file.</summary>
    [Fact]
    public void NamedProfiles_DoNotBumpTheSettingsSchemaVersion()
    {
        var dir = NewTempDir();
        try
        {
            new ImportProfileStore(dir).SaveNamed("c1", "Orders", Sample());

            var settings = new ApplicationSettingsStore(dir).Load();

            Assert.Equal(ApplicationSettingsStore.CurrentSchemaVersion, settings!.SchemaVersion);
            Assert.Equal(2, ApplicationSettingsStore.CurrentSchemaVersion);
        }
        finally { Cleanup(dir); }
    }
}

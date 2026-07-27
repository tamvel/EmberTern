using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using EmberTern.Core.Import;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I1. ⭐ <b>These tests ARE the guarantee promised in the design's §4.8.6.</b>
/// <para>
/// <c>ImportConfiguration</c> is the single representation of every user decision — the surface's state, the
/// pipeline's input and a saved profile's payload at once. That identity is what lets named profiles arrive in
/// etap I11 as pure UI with no model rebuild. The risk is obvious and mundane: someone adds a setting, wires it
/// into a view model, and forgets the one record. Then profiles quietly stop round-tripping and the "no later
/// rebuild" promise is dead.
/// </para>
/// <para>
/// <see cref="EveryProperty_ParticipatesInTheRoundTrip"/> makes that failure impossible to miss: the fixture must
/// set EVERY writable property to a non-default value, and adding a property without extending the fixture
/// fails the suite. It is deliberately reflection-driven rather than a hand-written list of assertions, because a
/// hand-written list is exactly the thing that rots.
/// </para>
/// </summary>
public class ImportConfigurationRoundTripTests
{
    // Mirrors the App's persistence options (ApplicationSettingsStore) so this test proves the same
    // serialization the store will actually perform, not a friendlier one.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// The only properties exempt from "must differ from the default", each with its reason. Both exemptions are
    /// self-invalidating — see <see cref="ImportMode_StillHasOneMember_OrTheFixtureMustCoverIt"/>.
    /// <list type="bullet">
    /// <item><c>Version</c> — the schema stamp. A fixture claiming to be a version this build does not know
    /// would be nonsense: the store is REQUIRED to refuse such a configuration whole rather than read it
    /// half-way (§0.7), which is pinned by its own test in <c>ImportProfileStoreTests</c>.</item>
    /// <item><c>Mode</c> — <c>ImportMode</c> has exactly one member in v1 (<c>Insert</c>), so there is no
    /// non-default value to set. This exemption is conditional on that fact and disappears the moment the enum
    /// grows.</item>
    /// </list>
    /// </summary>
    private static HashSet<string> NonDefaultExemptions()
    {
        var exemptions = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(ImportConfiguration.Version),
        };

        // Conditional, not permanent: when UPSERT/UPDATE/MERGE arrive (design §9.5) the fixture can and must
        // vary this, and the companion test below fails until someone does.
        if (Enum.GetValues<ImportMode>().Length == 1)
        {
            exemptions.Add(nameof(ImportConfiguration.Mode));
        }

        return exemptions;
    }

    /// <summary>
    /// Guards the <c>Mode</c> exemption above. If <c>ImportMode</c> ever gains a member, this test fails and
    /// says what to do — so the exemption cannot quietly outlive the reason for it.
    /// </summary>
    [Fact]
    public void ImportMode_StillHasOneMember_OrTheFixtureMustCoverIt()
    {
        Assert.True(Enum.GetValues<ImportMode>().Length == 1,
            "ImportMode has gained a member, so 'Mode' is no longer un-varyable: set a non-default value for it " +
            "in Fully() — the conditional exemption in NonDefaultExemptions() has already stopped applying, so " +
            "EveryProperty_ParticipatesInTheRoundTrip is now the test that enforces it.");
    }

    /// <summary>
    /// A configuration with every property set to something other than its default.
    /// <para>
    /// Both <c>Delimited</c> and <c>Spreadsheet</c> are populated even though a real configuration carries
    /// exactly one of them: this fixture exists for COVERAGE of the persisted shape, not to be a valid import.
    /// Validity is <c>MatchesSourceKind</c>'s job and is asserted separately.
    /// </para>
    /// </summary>
    private static ImportConfiguration Fully() => new()
    {
        Version = ImportConfiguration.CurrentVersion,
        Source = SourceDescriptor.File(ImportSourceKind.Xlsx, @"C:\dane\fantomy.xlsx"),
        Delimited = new DelimitedOptions
        {
            Delimiter = '\t',
            AutoDetectDelimiter = false,
            Quote = '\'',
            EncodingName = "UTF8",
            AutoDetectEncoding = false,
            LineEnding = LineEndingMode.Crlf,
            HasHeader = false,
            FirstDataRow = 7,
            LastRow = 4242,
            TrimWhitespace = true,
            NullToken = "\\N",
        },
        Spreadsheet = new SpreadsheetOptions
        {
            SheetIndex = 3,
            SheetName = "Arkusz4",
            HasHeader = false,
            FirstDataRow = 9,
            LastRow = 999,
            DatesAsDates = false,
        },
        Culture = new ImportCultureOptions
        {
            DecimalSeparator = '.',
            ThousandsSeparator = ' ',
            DateOrder = DateFieldOrder.Ymd,
            DateSeparator = '-',
            TimeSeparator = '.',
            TrueTokens = new[] { "Y" },
            FalseTokens = new[] { "N" },
        },
        Target = TargetDescriptor.New("XXX_GG_TMP_IMPORT_FANTOM", new[]
        {
            new ImportColumnDefinition
            {
                Name = "KOD_FANTOMU",
                BasicType = "NUMERIC",
                Size = 15,
                Scale = 2,
                BlobSubType = 1,
                NotNull = true,
            },
        }),
        Mapping = new[]
        {
            new ColumnMapping
            {
                TargetColumnName = "KOD_FANTOMU",
                SourceFieldName = "Kod fantomu",
                SourceFieldIndex = 2,
                IsSkipped = true,
                Origin = MappingOrigin.Assumed,
            },
        },
        Mode = ImportMode.Insert,
        Transaction = ImportTransactionMode.Batched,
        CommitEveryRows = 25_000,
        BatchSize = 750,
        ErrorPolicy = ImportErrorPolicy.SkipInvalidRows,
        Behavior = new ImportBehaviorOptions
        {
            EmptyTargetBeforeImport = true,
            TrimTooLongValues = true,
            TreatEmptyAsNull = false,
            ExcelErrorCellsAsNull = true,
            DropTableOnFailure = true,
        },
    };

    // ── The guarantee ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ Every writable property of <c>ImportConfiguration</c> (recursively, through its nested option records)
    /// must be set to a non-default value by <see cref="Fully"/> AND survive a serialization round trip.
    /// <para>
    /// <b>If this test fails after you added a setting, the fix is to set it in <see cref="Fully"/></b> — not to
    /// exempt it. That is the whole point: it forces a new decision through the one representation, so a profile
    /// saved in etap I11 carries it without anyone having to remember.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryProperty_ParticipatesInTheRoundTrip()
    {
        var fixture = Fully();
        var defaults = new ImportConfiguration();

        var unset = new List<string>();
        CollectDefaultValued(fixture, defaults, prefix: nameof(ImportConfiguration), unset, NonDefaultExemptions());

        Assert.True(unset.Count == 0,
            "These configuration properties still hold their default value in the test fixture, so nothing " +
            "proves they survive being saved and reloaded. Set them in Fully():" +
            Environment.NewLine + string.Join(Environment.NewLine, unset.Select(p => "  - " + p)));

        // And the same fixture must come back byte-for-byte equal in value through the real serializer.
        var reloaded = JsonSerializer.Deserialize<ImportConfiguration>(
            JsonSerializer.Serialize(fixture, JsonOptions), JsonOptions);

        Assert.NotNull(reloaded);
        AssertStructurallyEqual(fixture, reloaded!, nameof(ImportConfiguration));
    }

    [Fact]
    public void Serialization_RoundTrips_TheFullyPopulatedConfiguration()
    {
        var fixture = Fully();

        var reloaded = JsonSerializer.Deserialize<ImportConfiguration>(
            JsonSerializer.Serialize(fixture, JsonOptions), JsonOptions);

        Assert.NotNull(reloaded);
        // Spot-check the members most likely to break silently: a char, a nullable char, an enum, a nested
        // collection and the mapping identity that R16 depends on.
        Assert.Equal('\t', reloaded!.Delimited!.Delimiter);
        Assert.Equal(' ', reloaded.Culture.ThousandsSeparator);
        Assert.Equal(DateFieldOrder.Ymd, reloaded.Culture.DateOrder);
        Assert.Equal(ImportTransactionMode.Batched, reloaded.Transaction);
        Assert.Equal("Kod fantomu", reloaded.Mapping[0].SourceFieldName);
        Assert.Equal(MappingOrigin.Assumed, reloaded.Mapping[0].Origin);
        Assert.Equal("NUMERIC", reloaded.Target.NewTableColumns[0].BasicType);
    }

    [Fact]
    public void Serialization_RoundTrips_TheEmptyConfiguration()
    {
        var reloaded = JsonSerializer.Deserialize<ImportConfiguration>(
            JsonSerializer.Serialize(ImportConfiguration.Empty, JsonOptions), JsonOptions);

        Assert.NotNull(reloaded);
        AssertStructurallyEqual(ImportConfiguration.Empty, reloaded!, nameof(ImportConfiguration));
    }

    [Fact]
    public void Enums_PersistByName_SoReorderingThemCannotChangeMeaning()
    {
        var json = JsonSerializer.Serialize(Fully(), JsonOptions);

        Assert.Contains("\"Batched\"", json, StringComparison.Ordinal);
        Assert.Contains("\"SkipInvalidRows\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Assumed\"", json, StringComparison.Ordinal);
        // A numeric enum would silently change meaning the day someone inserts a member.
        Assert.DoesNotContain("\"Transaction\": 2", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingProperty_TakesItsDefault_SoAnOlderFileStillLoads()
    {
        // An older settings.dat simply lacks the newer members — the additive rule from §4.8.3.
        const string sparse = """
            { "Version": 1, "Target": { "Kind": "ExistingTable", "TableName": "T" } }
            """;

        var loaded = JsonSerializer.Deserialize<ImportConfiguration>(sparse, JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal("T", loaded!.Target.TableName);
        Assert.Equal(ImportTransactionMode.Manual, loaded.Transaction);
        Assert.Equal(ImportErrorPolicy.StopOnFirstError, loaded.ErrorPolicy);
        Assert.Equal(ImportConfiguration.DefaultBatchSize, loaded.BatchSize);
        Assert.NotNull(loaded.Delimited);
        Assert.NotNull(loaded.Culture);
        Assert.NotNull(loaded.Behavior);
    }

    // ── Defaults that were measured, not guessed ─────────────────────────────────────────────────────

    [Fact]
    public void Defaults_MatchTheRatifiedDecisions()
    {
        var config = new ImportConfiguration();

        Assert.Equal(ImportTransactionMode.Manual, config.Transaction);          // D3
        Assert.Equal(ImportErrorPolicy.StopOnFirstError, config.ErrorPolicy);    // D4
        Assert.Equal(ImportMode.Insert, config.Mode);                            // v1 scope
        Assert.Equal(500, config.BatchSize);                                     // I0 §2.2 optimum
        Assert.Equal(10_000, config.CommitEveryRows);                            // I0 §2.1 — commit is cheap
        Assert.False(config.Behavior.TrimTooLongValues);                         // §0.2 — opt-in only
        Assert.False(config.Behavior.ExcelErrorCellsAsNull);                     // R20 — an error is an error
        Assert.False(config.Behavior.EmptyTargetBeforeImport);                   // never destructive by default
    }

    [Fact]
    public void MatchesSourceKind_RequiresTheOptionsBlockThatBelongsToTheSource()
    {
        var delimited = new ImportConfiguration
        {
            Source = SourceDescriptor.File(ImportSourceKind.Csv, "a.csv"),
            Delimited = new DelimitedOptions(),
            Spreadsheet = null,
        };
        var spreadsheet = new ImportConfiguration
        {
            Source = SourceDescriptor.File(ImportSourceKind.Xlsx, "a.xlsx"),
            Delimited = null,
            Spreadsheet = new SpreadsheetOptions(),
        };
        var mismatched = new ImportConfiguration
        {
            Source = SourceDescriptor.File(ImportSourceKind.Xlsx, "a.xlsx"),
            Delimited = new DelimitedOptions(),
            Spreadsheet = null,
        };

        Assert.True(delimited.MatchesSourceKind);
        Assert.True(spreadsheet.MatchesSourceKind);
        Assert.False(mismatched.MatchesSourceKind);
    }

    [Fact]
    public void ClipboardSource_CarriesNoPath()
    {
        var clipboard = SourceDescriptor.Clipboard();

        Assert.Null(clipboard.Path);
        Assert.False(clipboard.IsFile);
    }

    [Fact]
    public void MappedColumns_ExcludesSkippedAndUnmapped()
    {
        var config = new ImportConfiguration
        {
            Mapping = new[]
            {
                new ColumnMapping { TargetColumnName = "A", SourceFieldIndex = 0, Origin = MappingOrigin.Restored },
                ColumnMapping.Unmapped("B"),
                ColumnMapping.Skipped("C"),
                new ColumnMapping { TargetColumnName = "D", SourceFieldIndex = 3, IsSkipped = true },
            },
        };

        Assert.Equal(new[] { "A" }, config.MappedColumns().Select(m => m.TargetColumnName));
    }

    // ── Reflection machinery ─────────────────────────────────────────────────────────────────────────

    private static bool IsImportRecord(Type type)
        => type.Namespace == "EmberTern.Core.Import"
           && !type.IsEnum
           && type.IsClass;

    private static IEnumerable<PropertyInfo> WritableProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
               .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0);

    // Walks the fixture against an all-defaults instance and records every leaf still equal to its default.
    private static void CollectDefaultValued(
        object actual, object? defaults, string prefix, List<string> unset, HashSet<string> exemptions)
    {
        foreach (var property in WritableProperties(actual.GetType()))
        {
            var path = prefix + "." + property.Name;
            var value = property.GetValue(actual);
            var defaultValue = defaults is null ? null : property.GetValue(defaults);

            if (value is not null && IsImportRecord(property.PropertyType) && value is not string
                && value is not IEnumerable)
            {
                // A nested options record: recurse, and compare against ITS defaults (a fresh instance when the
                // default is null, so "the whole block was null by default" does not excuse leaving it unset).
                var nestedDefaults = defaultValue ?? CreateDefault(property.PropertyType);
                CollectDefaultValued(value, nestedDefaults, path, unset, exemptions);
                continue;
            }

            if (exemptions.Contains(property.Name)) continue;

            if (value is IEnumerable and not string)
            {
                if (!((IEnumerable)value).Cast<object?>().Any()) unset.Add(path + " (empty collection)");
                continue;
            }

            if (Equals(value, defaultValue)) unset.Add(path);
        }
    }

    private static object? CreateDefault(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (MissingMethodException)
        {
            return null;
        }
    }

    // Structural equality that understands collections — record equality would compare IReadOnlyList members by
    // REFERENCE and pass a round trip that dropped every element.
    private static void AssertStructurallyEqual(object expected, object actual, string path)
    {
        Assert.Equal(expected.GetType(), actual.GetType());

        foreach (var property in WritableProperties(expected.GetType()))
        {
            var propertyPath = path + "." + property.Name;
            var a = property.GetValue(expected);
            var b = property.GetValue(actual);

            if (a is null || b is null)
            {
                Assert.True(a is null && b is null, $"{propertyPath}: one side is null, the other is not.");
                continue;
            }

            if (a is string)
            {
                Assert.Equal(a, b);
                continue;
            }

            if (a is IEnumerable left && b is IEnumerable right)
            {
                var leftItems = left.Cast<object?>().ToList();
                var rightItems = right.Cast<object?>().ToList();
                Assert.Equal(leftItems.Count, rightItems.Count);
                for (int i = 0; i < leftItems.Count; i++)
                {
                    var itemPath = propertyPath + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    if (leftItems[i] is null || rightItems[i] is null)
                    {
                        Assert.True(leftItems[i] is null && rightItems[i] is null, itemPath);
                    }
                    else if (IsImportRecord(leftItems[i]!.GetType()))
                    {
                        AssertStructurallyEqual(leftItems[i]!, rightItems[i]!, itemPath);
                    }
                    else
                    {
                        Assert.Equal(leftItems[i], rightItems[i]);
                    }
                }
                continue;
            }

            if (IsImportRecord(property.PropertyType))
            {
                AssertStructurallyEqual(a, b, propertyPath);
                continue;
            }

            Assert.Equal(a, b);
        }
    }
}

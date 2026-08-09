using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EmberTern.App;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Connections;
using EmberTern.App.Settings;
using EmberTern.Core.Formatting;
using EmberTern.Core.Metadata;
using EmberTern.Core.Settings;
using EmberTern.Firebird;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Database Properties (post-M5 UX package, point 6) — the rules that decide whether this feature is safe,
/// all of them assertable with no server.
/// </summary>
public class DatabasePropertiesTests
{
    private static DatabaseProperties Sample(
        int sweep = 20000, bool forced = true, bool reserve = true, int? linger = null) => new()
    {
        DatabasePath = @"C:\db\LAB.FDB",
        Owner = "SYSDBA",
        EngineVersion = "5.0.3",
        OdsMajor = 13,
        OdsMinor = 1,
        Dialect = 3,
        Charset = "WIN1250",
        CreatedAt = new DateTime(2026, 8, 9, 19, 56, 57),
        PageSize = 4096,
        Pages = 280,
        PageBuffers = 51200,
        LingerSeconds = linger,
        SweepInterval = sweep,
        ForcedWrites = forced,
        ReserveSpace = reserve,
    };

    private static DatabasePropertiesViewModel VmOver(
        DatabaseProperties properties,
        Func<DatabaseConfigurationChange, DatabaseConfigurationResult>? apply = null,
        bool canWrite = true,
        List<DatabaseConfigurationChange>? sent = null)
        => new(
            "LAB",
            canWrite,
            _ => Task.FromResult(properties),
            (change, _) =>
            {
                sent?.Add(change);
                return Task.FromResult(apply?.Invoke(change) ?? new DatabaseConfigurationResult([]));
            });

    // ─── The central promise: only what was edited travels ───────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ The one rule the whole feature's safety rests on: <b>opening the window and pressing Apply must
    /// send nothing.</b> This writes to a shared production database through an API with no rollback, so a
    /// full-snapshot Apply would push values the user never looked at.
    /// </summary>
    [Fact]
    public async Task OpeningTheWindowAndApplying_SendsNothing()
    {
        var sent = new List<DatabaseConfigurationChange>();
        var vm = VmOver(Sample(), sent: sent);
        await vm.LoadAsync();

        Assert.False(vm.PendingChange.HasChanges);
        Assert.False(vm.ApplyCommand.CanExecute(null));

        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.Empty(sent);
    }

    [Fact]
    public async Task OnlyTheEditedValueTravels()
    {
        var sent = new List<DatabaseConfigurationChange>();
        var vm = VmOver(Sample(sweep: 20000, forced: true, reserve: true), sent: sent);
        await vm.LoadAsync();

        vm.ForcedWrites = false;

        var change = vm.PendingChange;
        Assert.True(change.HasChanges);
        Assert.Null(change.SweepInterval);
        Assert.Null(change.ReserveSpace);
        Assert.Equal(false, change.ForcedWrites);
        Assert.True(vm.ApplyCommand.CanExecute(null));
    }

    /// <summary>Typing a value and typing it back is not an edit — the comparison is against the READ value.</summary>
    [Fact]
    public async Task AValueEditedBackToItsOriginal_IsNotSent()
    {
        var vm = VmOver(Sample(sweep: 20000));
        await vm.LoadAsync();

        vm.SweepIntervalText = "12345";
        Assert.True(vm.PendingChange.HasChanges);

        vm.SweepIntervalText = "20000";
        Assert.False(vm.PendingChange.HasChanges);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-5")]
    public async Task AnUnparseableSweepInterval_SendsNothing(string text)
    {
        var vm = VmOver(Sample());
        await vm.LoadAsync();

        vm.SweepIntervalText = text;

        Assert.False(vm.PendingChange.HasChanges);
        Assert.False(vm.ApplyCommand.CanExecute(null));
    }

    /// <summary>A profile with no stored password cannot attempt an Apply — the one refusal that is knowable
    /// up front (measured: the driver refuses before reaching the server).</summary>
    [Fact]
    public async Task WithoutAStoredPassword_ApplyIsUnavailable_AndSaysWhy()
    {
        var vm = VmOver(Sample(), canWrite: false);
        await vm.LoadAsync();

        vm.ForcedWrites = false;

        Assert.True(vm.PendingChange.HasChanges);
        Assert.False(vm.ApplyCommand.CanExecute(null));
        Assert.True(vm.ShowWriteBlockedReason);
        Assert.False(string.IsNullOrWhiteSpace(vm.WriteBlockedReason));
    }

    // ─── Partial success ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ Apply is not atomic — each setting is its own Services call — so a partial success is reachable and
    /// the message must NAME what did land. "It failed" would leave the user unable to tell which changes are
    /// now live in the database.
    /// </summary>
    [Fact]
    public void APartialApply_NamesWhatSucceeded_AndWarnsRatherThanErrors()
    {
        var result = new DatabaseConfigurationResult(
        [
            new DatabaseSettingOutcome(DatabaseSetting.SweepInterval),
            new DatabaseSettingOutcome(DatabaseSetting.ForcedWrites, "boom", "28000", [335544788]),
        ]);

        var (text, severity) = DatabasePropertiesViewModel.Describe(result);

        Assert.Equal(MessageSeverity.Warning, severity);
        Assert.Contains(UiStrings.DatabasePropertiesSweepInterval, text, StringComparison.Ordinal);
        Assert.Contains("boom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFullySuccessfulApply_ReadsAsSuccess()
    {
        var result = new DatabaseConfigurationResult([new DatabaseSettingOutcome(DatabaseSetting.ReserveSpace)]);
        var (_, severity) = DatabasePropertiesViewModel.Describe(result);
        Assert.Equal(MessageSeverity.Success, severity);
    }

    /// <summary>The server's own words are always carried — the lead never replaces them.</summary>
    [Fact]
    public void AFailureAlwaysCarriesTheServersRawMessage()
    {
        var result = new DatabaseConfigurationResult(
            [new DatabaseSettingOutcome(DatabaseSetting.SweepInterval, "raw server words", "28000", [335544788])]);

        var (text, severity) = DatabasePropertiesViewModel.Describe(result);

        Assert.Equal(MessageSeverity.Error, severity);
        Assert.Contains("raw server words", text, StringComparison.Ordinal);
        Assert.Contains(UiStrings.DatabasePropertiesMissingPrivilege, text, StringComparison.Ordinal);
    }

    /// <summary>After an Apply the window shows what the DATABASE now holds — re-read, not what was asked for.</summary>
    [Fact]
    public async Task AfterApply_TheWindowShowsTheReReadState()
    {
        var current = Sample(forced: true);
        var vm = new DatabasePropertiesViewModel(
            "LAB", true,
            _ => Task.FromResult(current),
            (_, _) =>
            {
                // The server accepted nothing; the database still holds the old value.
                return Task.FromResult(new DatabaseConfigurationResult(
                    [new DatabaseSettingOutcome(DatabaseSetting.ForcedWrites, "refused")]));
            });

        await vm.LoadAsync();
        vm.ForcedWrites = false;
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.ForcedWrites);
        Assert.False(vm.PendingChange.HasChanges);
    }

    // ─── Classification is by CODE, never by text ────────────────────────────────────────────────────

    [Fact]
    public void ThePrivilegeRefusal_IsRecognisedByItsCodes()
    {
        Assert.Equal(
            DatabaseApplyFailure.MissingPrivilege,
            DatabaseConfigurationDiagnosis.Classify(
                new DatabaseSettingOutcome(DatabaseSetting.SweepInterval, "x", "28000", [335544788, 335545112])));

        Assert.Equal(
            DatabaseApplyFailure.DatabaseInUse,
            DatabaseConfigurationDiagnosis.Classify(
                new DatabaseSettingOutcome(DatabaseSetting.SweepInterval, "x", "40001", [335544510, 335544453])));
    }

    /// <summary>
    /// ⭐⭐ <b>The measured wrong-password message must NOT be translated.</b> The probe measured that a bad
    /// password surfaces as <i>"Not supported plugin 'Legacy_Auth'"</i> — the most tempting thing in the whole
    /// feature to explain away, and the one the user explicitly forbade, because it arrives with <b>no
    /// SQLSTATE and no GDS codes</b> and could therefore only be recognised from its text. A heuristic
    /// "Legacy_Auth ⇒ wrong password" is right until a server genuinely has a plugin problem.
    /// </summary>
    [Fact]
    public void TheLegacyAuthMessage_IsNeverClassified()
    {
        var outcome = new DatabaseSettingOutcome(
            DatabaseSetting.SweepInterval, "Not supported plugin 'Legacy_Auth'.");

        Assert.Equal(DatabaseApplyFailure.Unknown, DatabaseConfigurationDiagnosis.Classify(outcome));

        var (text, _) = DatabasePropertiesViewModel.Describe(new DatabaseConfigurationResult([outcome]));
        Assert.Contains("Legacy_Auth", text, StringComparison.Ordinal);
        Assert.DoesNotContain(UiStrings.DatabasePropertiesMissingPrivilege, text, StringComparison.Ordinal);
        Assert.DoesNotContain(UiStrings.DatabasePropertiesInUse, text, StringComparison.Ordinal);
    }

    // ─── Presentation of the two measured traps ──────────────────────────────────────────────────────

    /// <summary>⚠ NULL linger is "not set" and 0 is a configured zero — measured NULL on a fresh database.</summary>
    [Fact]
    public async Task LingerDistinguishesNotSetFromZero()
    {
        var notSet = VmOver(Sample(linger: null));
        await notSet.LoadAsync();
        Assert.Equal(UiStrings.DatabasePropertiesLingerNotSet, notSet.Linger);

        var zero = VmOver(Sample(linger: 0));
        await zero.LoadAsync();
        Assert.NotEqual(UiStrings.DatabasePropertiesLingerNotSet, zero.Linger);
    }

    [Fact]
    public void SizeIsPagesTimesPageSize()
        => Assert.Equal(280L * 4096, Sample().SizeBytes);

    // ─── Scope guards — the ratified V1 boundary, in the source ──────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The reader must stay compatible with EmberTern's declared FB3+ support</b>, and nothing else
    /// enforces that: every excluded column below exists on FB5 and would work perfectly on the developer's
    /// machine while failing with a plain "column unknown" on a customer's FB3 (gotcha #146's shape).
    ///
    /// <para>⚠ Written as a NEGATIVE assertion on purpose. A positive allow-list would be a transcription of
    /// the FB3 catalog (#333) and would need updating for a reason unrelated to the thing it protects; this
    /// asks only "did someone reach for a column we measured as later-than-FB3".</para>
    /// </summary>
    [Theory]
    [InlineData("MON$GUID")]
    [InlineData("MON$FILE_ID")]
    [InlineData("MON$SEC_DATABASE")]
    [InlineData("MON$CRYPT_STATE")]
    [InlineData("MON$REPLICA_MODE")]
    [InlineData("MON$NEXT_ATTACHMENT")]
    [InlineData("MON$NEXT_STATEMENT")]
    [InlineData("RDB$SQL_SECURITY")]
    public void TheReaderNamesNoColumnOutsideTheRatifiedScope(string column)
        => Assert.DoesNotContain(
            column,
            FirebirdDatabasePropertiesReader.PropertiesSql,
            StringComparison.Ordinal);

    /// <summary>
    /// ⭐⭐ <b>The three settings ratified OUT of V1 must stay out — and this is a guard against scope creep,
    /// not against a bug.</b> All three are technically available: the probe measured <c>SetSqlDialectAsync</c>
    /// succeeding ONLINE and <c>SetPageBuffersAsync</c> succeeding outright, so a future reader of the driver
    /// would find nothing stopping them and would reasonably assume the omission was an oversight.
    ///
    /// <para>It is not. Dialect changes the SQL EmberTern itself runs; page buffers reads the RUNNING cache
    /// rather than the stored value, so a seeded field would pin the inherited server default on Apply; and
    /// access mode needs exclusive access this window can never have.</para>
    /// </summary>
    [Theory]
    [InlineData("SetSqlDialectAsync")]
    [InlineData("SetPageBuffersAsync")]
    [InlineData("SetAccessModeAsync")]
    public void TheWriterNeverCallsASetterRatifiedOutOfV1(string setter)
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.Firebird", "FirebirdDatabaseConfigurationWriter.cs"));

        // The class doc names them as EXCLUDED, so only a call site counts — "SetX(" rather than the word.
        Assert.DoesNotContain(setter + "(", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Services connection string carries the database.
    /// <para>⚠ Measured: a database-scoped configuration call refuses a no-database service string with
    /// <i>"Action should be executed against a specific database"</i> — which is why
    /// <c>FirebirdTraceService.BuildServiceConnectionString</c> (deliberately no-database) cannot be reused.</para>
    /// </summary>
    [Fact]
    public void TheServicesConnectionStringCarriesTheDatabase()
    {
        var profile = new ConnectionProfile
        {
            Host = "srv", Port = 3051, DatabasePath = @"C:\db\LAB.FDB",
            Username = "SYSDBA", Password = "pwd",
        };

        var cs = FirebirdDatabaseConfigurationWriter.BuildServiceConnectionString(profile);

        Assert.Contains(@"C:\db\LAB.FDB", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("srv", cs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void APasswordlessProfile_CannotAttemptAnApply()
    {
        Assert.False(FirebirdDatabaseConfigurationWriter.CanAttempt(
            new ConnectionProfile { DatabasePath = "x", Username = "SYSDBA", Password = "" }));
        Assert.True(FirebirdDatabaseConfigurationWriter.CanAttempt(
            new ConnectionProfile { DatabasePath = "x", Username = "SYSDBA", Password = "p" }));
    }

    /// <summary>
    /// The menu entry is gated by DISABLING, and the gate is the connection.
    /// <para>⚠ Its neighbours in the same menu HIDE instead — that difference is the ratified rule (hide when
    /// the item makes no sense at all, disable when the operation is momentarily unavailable), so a guard is
    /// what stops someone "harmonising" it later.</para>
    /// </summary>
    [Fact]
    public void ThePropertiesCommandFollowsTheConnection()
    {
        var node = new ConnectionNodeViewModel(new ConnectionProfile { Name = "LAB" });

        node.IsConnected = false;
        Assert.False(node.DatabasePropertiesCommand.CanExecute(null));

        node.IsConnected = true;
        Assert.True(node.DatabasePropertiesCommand.CanExecute(null));
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}

/// <summary>
/// Trzy poprawki UX z zamknięcia punktu 6. Każda dotyka reguły, którą projekt ma zapisaną, więc każda ma
/// strażnika pilnującego, dlaczego jest wyjątkiem, a nie dryfem.
/// </summary>
public class PostPointSixUxFixTests
{
    // ─── 1. Komunikat uwierzytelniania SRP ───────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ Komunikat NAPROWADZA, nie ORZEKA — i to jest cała różnica wobec podpowiedzi, którą kiedyś dodano
    /// i usunięto. Tamta twierdziła, co jest przyczyną, i myliła się, bo ten sam błąd niosą też złe hasło
    /// i nieistniejący użytkownik. Ten test pilnuje, że nowy tekst NIE stwierdza żadnej z tych rzeczy.
    /// </summary>
    [Fact]
    public void TheSrpMessage_GuidesWithoutAssertingACause()
    {
        var msg = FirebirdConnectionService.SrpAuthenticationMessage;

        // Naprowadza na wszystkie cztery rzeczy, o które prosił użytkownik.
        Assert.Contains("SRP", msg, StringComparison.Ordinal);
        Assert.Contains("username and password", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USING PLUGIN Srp", msg, StringComparison.OrdinalIgnoreCase);

        // ⛔ I nie orzeka. Każde z tych zdań byłoby zgadywaniem przyczyny.
        Assert.DoesNotContain("password is wrong", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("incorrect password", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("is not an SRP", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Legacy_Auth", msg, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Podmieniany jest WYŁĄCZNIE ten jeden przypadek; każdy inny błąd nadal idzie surowy.</summary>
    [Fact]
    public void OnlyTheLegacyAuthRefusalIsRewritten()
    {
        var profile = new ConnectionProfile { Host = "localhost", Port = 3050 };

        var rewritten = FirebirdConnectionService.MapErrorMessage(
            new InvalidOperationException("Not supported plugin 'Legacy_Auth'."), profile);
        Assert.Contains("SRP", rewritten, StringComparison.Ordinal);
        Assert.DoesNotContain("Legacy_Auth", rewritten, StringComparison.Ordinal);

        // Dowolny inny błąd — słowa serwera bez zmian, zgodnie ze stojącą regułą.
        var raw = FirebirdConnectionService.MapErrorMessage(
            new InvalidOperationException("Unable to complete network request to host \"nosuchhost\"."), profile);
        Assert.Contains("nosuchhost", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("SRP", raw, StringComparison.Ordinal);

        // Endpoint zostaje w obu.
        Assert.Contains("localhost:3050", rewritten, StringComparison.Ordinal);
        Assert.Contains("localhost:3050", raw, StringComparison.Ordinal);
    }

    // ─── 3. Niełamliwe liczby w prozie ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Separator wewnątrz liczby staje się niełamliwy, a odstępy MIĘDZY słowami zostają zwykłe — inaczej
    /// cały opis przestałby się zawijać, co było wprost wykluczone.
    /// </summary>
    [Theory]
    [InlineData("Between 1 and 1 000 000.", "1\u00A0000\u00A0000")]
    [InlineData("Between 1 and 1 000.", "1\u00A0000")]
    [InlineData("The hard 1 000 000-row memory limit is separate.", "1\u00A0000\u00A0000")]
    public void AGroupedNumberTravelsAsOneUnit(string input, string expectedNumber)
    {
        var output = ProseNumbers.KeepNumbersWhole(input);

        Assert.Contains(expectedNumber, output, StringComparison.Ordinal);

        // ⚠ Odstępy MIĘDZY SŁOWAMI muszą zostać zwykłe — inaczej cały opis przestałby się zawijać, co było
        //   wprost wykluczone. Asercja jest o zwykłej spacji, a nie o konkretnym słowie: pierwotnie wymagała
        //   tu słowa „and", którego trzeci przypadek w ogóle nie zawiera, i test padał na poprawnym kodzie.
        Assert.Contains(" ", output, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyDigitToDigitSpacesAreTouched()
    {
        // Odstęp cyfra→litera i litera→cyfra to granice słów, nie separatory liczby.
        Assert.Equal("1 and 2", ProseNumbers.KeepNumbersWhole("1 and 2"));
        Assert.Equal("row 5", ProseNumbers.KeepNumbersWhole("row 5"));
        Assert.Equal(string.Empty, ProseNumbers.KeepNumbersWhole(null));
    }

    /// <summary>
    /// ⭐⭐ Reguła jest o KSZTAŁCIE, nie o konkretnym zdaniu — więc pilnuje jej przebieg po CAŁYM katalogu.
    /// Nowy opis z liczbą jest objęty bez dopisywania czegokolwiek do testu, a to było wprost wymaganie:
    /// „rozwiązanie nie ma być hardcoded pod jeden tekst".
    /// </summary>
    [Fact]
    public void EverySettingsDescriptionKeepsItsNumbersWhole()
    {
        var offenders = SettingsCatalog.Settings
            .Select(s => new { s.Id, Shown = ProseNumbers.KeepNumbersWhole(s.Description) })
            .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.Shown, @"\d \d"))
            .Select(x => x.Id)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "opis nadal może się złamać w środku liczby: " + string.Join(", ", offenders));
    }

    /// <summary>⚠ Wyszukiwanie czyta tekst SUROWY — inaczej wpisanie „1 000 000" ze zwykłymi spacjami
    /// przestałoby trafiać w wiersz, który tę liczbę widocznie zawiera.</summary>
    [Fact]
    public void SearchStillMatchesANumberTypedWithOrdinarySpaces()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EmberTern-prose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var vm = new SettingsCenterViewModel(
                new PreferencesService(new PreferencesStore(dir)),
                new EmberTern.App.Settings.SettingsPortability(
                    new EmberTern.Core.Settings.ApplicationSettingsStore(dir, null),
                    new PreferencesService(new PreferencesStore(dir)), "9.9.9-test"));

            vm.SearchText = "1 000 000";
            Assert.True(vm.HasMatches, "wyszukiwanie liczby ze zwykłymi spacjami przestało działać");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}

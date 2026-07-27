using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EmberTern.App.Controls;
using EmberTern.App.ViewModels;
using EmberTern.Core.Import;
using EmberTern.Core.Metadata;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Data Import — etap I11: named profiles.
/// <para>
/// ⭐⭐ <b>This etap is the design's own proof, so read these tests as evidence rather than as coverage.</b> §4.8
/// claimed that named profiles are a CONSEQUENCE of making <c>ImportConfiguration</c> the single representation
/// of every decision, and §6 named the disproof: "if named profiles require changing even one model or
/// rebuilding a UI section, §4.8 was violated along the way". Nothing here constructs a profile-specific
/// configuration, maps between two shapes, or reads a decision by any route the surface does not already use —
/// because none of that exists to be tested.
/// </para>
/// <para>
/// The two load-bearing tests are <see cref="ANamedProfile_RebuildsTheWholeImport_OnAFreshSurface"/> (the DoD)
/// and the pair that prove a profile which no longer fits the world is REPORTED rather than applied —
/// <see cref="AProfileWhoseFileIsGone_ShowsUpInReadiness_NotAsAnException"/> and
/// <see cref="AProfileWhoseTableIsGone_ShowsUpInReadiness_NotAsAnException"/>. That is §4.8.5, and it works only
/// because loading a profile is <c>ApplyConfiguration</c> and nothing else: the same call, the same chain and
/// the same <c>ImportMappingPlanner</c> an ordinary edit goes through.
/// </para>
/// </summary>
public class DataImportProfileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "et-import-profiles-" + Guid.NewGuid().ToString("N"));

    public DataImportProfileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string Connection = "c1";

    private static readonly ImportTarget Orders = new(
        "ORDERS",
        new[]
        {
            new ColumnSpec("KOD", "VARCHAR(10)"),
            new ColumnSpec("NAZWA", "VARCHAR(100)"),
        },
        Array.Empty<string>());

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>The settings file lives in the test's own directory, so every surface built here shares one
    /// real store — which is what makes "close the tab and open a new one" testable.</summary>
    private ImportProfileStore Store() => new(_dir);

    /// <summary>
    /// A surface wired to the real store, with whatever tables the case needs.
    /// <para>
    /// The confirmation and text-prompt seams are supplied as plain functions: the VM owns the question and the
    /// view owns the dialog (rule #1), so a test answers the question directly and never needs a window.
    /// </para>
    /// </summary>
    private DataImportTabViewModel Vm(
        Func<TextPromptRequest, Task<string?>>? answerName = null,
        Func<ConfirmRequest, Task<bool>>? answerConfirm = null,
        params ImportTarget[] tables)
    {
        var store = Store();
        var all = tables.Length == 0 ? new[] { Orders } : tables;

        var environment = new DataImportEnvironment(() => true, () => "LAB")
        {
            ListTablesAsync = _ => Task.FromResult<IReadOnlyList<string>>(all.Select(t => t.TableName).ToList()),
            ReadTargetAsync = (name, _) => Task.FromResult(
                all.FirstOrDefault(t => string.Equals(t.TableName, name, StringComparison.OrdinalIgnoreCase))),

            ListProfiles = () => store.ListNamed(Connection),
            SaveProfile = (name, configuration) => store.SaveNamed(Connection, name, configuration),
            RenameProfile = (id, name) => store.Rename(id, name),
            DeleteProfile = id => store.Delete(id),
        };

        var vm = new DataImportTabViewModel(environment) { PreviewDebounce = TimeSpan.Zero };

        if (answerName is not null) vm.TextRequested += answerName;
        if (answerConfirm is not null) vm.ConfirmRequested += answerConfirm;
        return vm;
    }

    private static async Task SettleAsync(DataImportTabViewModel vm)
    {
        for (var i = 0; i < 10; i++)
        {
            var pending = vm.PendingRecalculation;
            if (pending is null) return;
            await pending.ConfigureAwait(false);
            if (ReferenceEquals(pending, vm.PendingRecalculation)) return;
        }
    }

    private static Func<TextPromptRequest, Task<string?>> Answers(string? name)
        => _ => Task.FromResult(name);

    private static Func<ConfirmRequest, Task<bool>> Answers(bool yes)
        => _ => Task.FromResult(yes);

    /// <summary>The SAVED profiles — the list without the standing „(no profile)” row that always leads it.</summary>
    private static IReadOnlyList<ImportProfileRowViewModel> Saved(DataImportTabViewModel vm)
        => vm.Profiles.Where(p => !p.IsNone).ToList();

    /// <summary>Drives a surface to a complete, runnable import: a real file, a real target, a real mapping.</summary>
    private async Task<DataImportTabViewModel> ConfiguredVmAsync(
        string? path = null,
        Func<TextPromptRequest, Task<string?>>? answerName = null,
        Func<ConfirmRequest, Task<bool>>? answerConfirm = null,
        params ImportTarget[] tables)
    {
        var vm = Vm(answerName, answerConfirm, tables);
        await SettleAsync(vm);

        vm.Source.UseFile = true;
        vm.Source.FilePath = path ?? WriteFile("orders.csv", "KOD;NAZWA\nA1;Widget\nA2;Gadget\n");
        await SettleAsync(vm);

        vm.Target.SelectedTable = "ORDERS";
        await SettleAsync(vm);

        return vm;
    }

    // ── Saving ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ What a profile stores is <c>BuildConfiguration()</c> — the very record <c>Importuj</c> would hand to
    /// the pipeline. A profile therefore cannot describe an import different from the one on the screen, because
    /// there is nothing else for it to describe.
    /// </summary>
    [Fact]
    public async Task SavingAProfile_StoresExactlyWhatTheSurfaceWouldImport()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Nightly orders"));
        var expected = vm.BuildConfiguration();

        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        var stored = Assert.Single(Store().ListNamed(Connection));
        Assert.Equal("Nightly orders", stored.Name);
        Assert.Equal(expected.Source, stored.Configuration.Source);
        Assert.Equal(expected.Delimited, stored.Configuration.Delimited);
        // Field by field, not record equality: ImportCultureOptions holds the boolean token LISTS, and a record
        // compares those by reference — an array and the List<string> that comes back out of JSON are never
        // "equal" however identical their contents. A property of the comparison, not of the profile.
        Assert.Equal(expected.Culture.DecimalSeparator, stored.Configuration.Culture.DecimalSeparator);
        Assert.Equal(expected.Culture.DateOrder, stored.Configuration.Culture.DateOrder);
        Assert.Equal(expected.Culture.TrueTokens, stored.Configuration.Culture.TrueTokens);
        Assert.Equal(expected.Culture.FalseTokens, stored.Configuration.Culture.FalseTokens);
        Assert.Equal(expected.Target.TableName, stored.Configuration.Target.TableName);
        Assert.Equal(expected.Mapping.Count, stored.Configuration.Mapping.Count);
    }

    [Fact]
    public async Task SavingAProfile_SelectsIt_SoTheBarShowsWhatWasJustSaved()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Nightly orders"));

        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        Assert.Equal("Nightly orders", vm.SelectedProfile!.Name);
        Assert.Equal(
            new[] { "(no profile)", "Nightly orders" },
            vm.Profiles.Select(p => p.Display).ToArray());
    }

    [Fact]
    public async Task CancellingTheNamePrompt_SavesNothing()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers((string?)null));

        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        Assert.Empty(Store().ListNamed(Connection));
        Assert.Empty(Saved(vm));
    }

    /// <summary>Overwriting a saved profile destroys the decisions that were in it, so it is asked about — the
    /// same rule "empty the table" follows (§0).</summary>
    [Fact]
    public async Task SavingOverAnExistingName_AsksFirst_AndKeepsTheOldOneWhenDeclined()
    {
        Store().SaveNamed(Connection, "Orders", new ImportConfiguration
        {
            Target = TargetDescriptor.Existing("SOMETHING_ELSE"),
        });

        var asked = new List<string>();
        var vm = await ConfiguredVmAsync(
            answerName: Answers("Orders"),
            answerConfirm: r => { asked.Add(r.Title); return Task.FromResult(false); });

        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        Assert.Single(asked);
        Assert.Equal("SOMETHING_ELSE", Assert.Single(Store().ListNamed(Connection)).Configuration.Target.TableName);
    }

    [Fact]
    public async Task SavingOverAnExistingName_ReplacesIt_WhenConfirmed()
    {
        Store().SaveNamed(Connection, "Orders", new ImportConfiguration
        {
            Target = TargetDescriptor.Existing("SOMETHING_ELSE"),
        });

        var vm = await ConfiguredVmAsync(answerName: Answers("Orders"), answerConfirm: Answers(true));

        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        var only = Assert.Single(Store().ListNamed(Connection));
        Assert.Equal("ORDERS", only.Configuration.Target.TableName);
    }

    // ── Loading — the DoD ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The Definition of Done.</b> Configure an import, save it under a name, throw the surface away, open
    /// a new one, pick the profile — and the whole import is back: source, format, target and mapping, ready to
    /// run.
    /// <para>
    /// The second surface is a genuinely separate <c>DataImportTabViewModel</c> over the same settings file, which
    /// is what "close the tab and open it again" means here.
    /// </para>
    /// </summary>
    [Fact]
    public async Task ANamedProfile_RebuildsTheWholeImport_OnAFreshSurface()
    {
        var path = WriteFile("orders.csv", "KOD;NAZWA\nA1;Widget\n");

        var first = await ConfiguredVmAsync(path, answerName: Answers("Nightly orders"));
        first.Source.NullToken = "NULL";
        first.TransactionMode = ImportTransactionMode.Batched;
        await SettleAsync(first);

        var expected = first.BuildConfiguration();
        await first.SaveProfileAsCommand.ExecuteAsync(null);

        // A new surface — it opens knowing nothing but what the store holds.
        var second = Vm();
        await SettleAsync(second);
        Assert.True(second.SelectedProfile!.IsNone);

        second.SelectedProfile = Assert.Single(Saved(second));
        await SettleAsync(second);

        Assert.Equal(path, second.Source.FilePath);
        Assert.Equal("ORDERS", second.Target.SelectedTable);
        Assert.Equal("NULL", second.Source.NullToken);
        Assert.Equal(ImportTransactionMode.Batched, second.TransactionMode);

        // The mapping was re-planned from the source and the catalog, and landed on the same pairing.
        Assert.Equal(
            expected.Mapping.Where(m => m.IsMapped).Select(m => (m.TargetColumnName, m.SourceFieldName)),
            second.BuildConfiguration().Mapping.Where(m => m.IsMapped).Select(m => (m.TargetColumnName, m.SourceFieldName)));

        // And it can actually run — the point of the whole exercise.
        Assert.True(second.Readiness.CanRun);
    }

    /// <summary>Selecting a profile IS loading it — a picker that needed a second click would make the
    /// selection mean nothing on its own.</summary>
    [Fact]
    public async Task SelectingAProfile_LoadsIt_WithoutASecondCommand()
    {
        var first = await ConfiguredVmAsync(answerName: Answers("Orders"));
        await first.SaveProfileAsCommand.ExecuteAsync(null);

        var second = Vm();
        await SettleAsync(second);
        Assert.Equal(string.Empty, second.Source.FilePath);

        second.SelectedProfile = Saved(second)[0];
        await SettleAsync(second);

        Assert.NotEqual(string.Empty, second.Source.FilePath);
    }

    // ── §4.8.5 — a profile that no longer fits the world ────────────────────────────────────────────────

    /// <summary>
    /// ⭐ The file the profile names has been deleted. That is reported as <c>IMP0011</c> in the readiness strip —
    /// the same finding an ordinary edit would produce — and emphatically NOT as an exception or a silent load
    /// of a source that is not there.
    /// <para>
    /// Nothing in the profile code checks for this. It falls out of loading being <c>ApplyConfiguration</c>: the
    /// chain re-reads the source, the source is gone, and readiness says so.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AProfileWhoseFileIsGone_ShowsUpInReadiness_NotAsAnException()
    {
        var path = WriteFile("temporary.csv", "KOD;NAZWA\nA1;Widget\n");

        var first = await ConfiguredVmAsync(path, answerName: Answers("Orders"));
        await first.SaveProfileAsCommand.ExecuteAsync(null);

        File.Delete(path);

        var second = Vm();
        await SettleAsync(second);
        second.SelectedProfile = Saved(second)[0];
        await SettleAsync(second);

        Assert.Contains(second.Readiness.Items, i => i.Item.Code == ImportDiagnosticCode.SourceMissing);
        Assert.False(second.Readiness.CanRun);
    }

    /// <summary>
    /// ⭐ The table the profile names is not in this database any more. <c>IMP0016</c>, blocking, through the very
    /// same mechanism — the target is re-read from the catalog on load, exactly as it is after any edit.
    /// </summary>
    [Fact]
    public async Task AProfileWhoseTableIsGone_ShowsUpInReadiness_NotAsAnException()
    {
        var first = await ConfiguredVmAsync(answerName: Answers("Orders"));
        await first.SaveProfileAsCommand.ExecuteAsync(null);

        // The same store, a database that no longer has ORDERS.
        var second = Vm(tables: new ImportTarget("INVOICES", new[] { new ColumnSpec("ID", "INTEGER") }, Array.Empty<string>()));
        await SettleAsync(second);
        second.SelectedProfile = Saved(second)[0];
        await SettleAsync(second);

        Assert.Contains(second.Readiness.Items, i => i.Item.Code == ImportDiagnosticCode.TargetNotFound);
        Assert.False(second.Readiness.CanRun);
    }

    /// <summary>
    /// ⭐ The source still exists but its fields have changed. The mapping is re-planned by
    /// <c>ImportMappingPlanner</c> — the ONE owner of "is this pairing still correct" — so a column whose field
    /// disappeared comes back unmapped instead of silently pointing at whatever now sits in that position.
    /// <para>
    /// A purely positional restore is the defect §4.8.5 point 1 exists to prevent, and this is what proves the
    /// profile path is not one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task AProfileLoadedOverAChangedSource_IsReplanned_NeverRestoredByPosition()
    {
        var path = WriteFile("orders.csv", "KOD;NAZWA\nA1;Widget\n");

        var first = await ConfiguredVmAsync(path, answerName: Answers("Orders"));
        Assert.Equal(2, first.BuildConfiguration().Mapping.Count(m => m.IsMapped));
        await first.SaveProfileAsCommand.ExecuteAsync(null);

        // KOD moved to position 1 and NAZWA is gone. Position alone would now feed OPIS into KOD.
        //
        // ⚠ Two spare fields, not one, and that is deliberate: with exactly one unmatched column and exactly
        // one unused field the planner's sole-remaining-pair rule fires by design (IMP0008, marked "assumed"),
        // which is correct behaviour and would hide what this test is about. A second spare field keeps the
        // question squarely on name matching.
        File.WriteAllText(path, "OPIS;KOD;UWAGI\nWidget;A1;-\n");

        var second = Vm();
        await SettleAsync(second);
        second.SelectedProfile = Saved(second)[0];
        await SettleAsync(second);

        var mapping = second.BuildConfiguration().Mapping;
        Assert.Equal("KOD", mapping.Single(m => m.TargetColumnName == "KOD").SourceFieldName);
        Assert.False(mapping.Single(m => m.TargetColumnName == "NAZWA").IsMapped);
    }

    // ── §0.7 — never in part ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ A profile written by a newer build is listed, refused with a reason, and applied in NO part — because
    /// applying only the fields this build understands would change decisions the user never took.
    /// </summary>
    [Fact]
    public async Task AProfileFromTheFuture_IsRefusedWithAReason_AndNothingIsApplied()
    {
        Store().SaveNamed(Connection, "Tomorrow", new ImportConfiguration
        {
            Version = ImportConfiguration.CurrentVersion + 1,
            Source = SourceDescriptor.File(ImportSourceKind.Csv, @"C:\somewhere\else.csv"),
            Target = TargetDescriptor.Existing("ORDERS"),
        });

        var vm = Vm();
        await SettleAsync(vm);

        var row = Assert.Single(Saved(vm));
        Assert.False(row.IsReadable);
        Assert.Contains("newer", row.Display, StringComparison.OrdinalIgnoreCase);

        vm.SelectedProfile = row;
        await SettleAsync(vm);

        Assert.Equal(string.Empty, vm.Source.FilePath);
        Assert.Equal(MessageSeverity.Warning, vm.StatusSeverity);
        Assert.Contains("newer version", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ── Rename and delete ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rename_ChangesTheNameInTheBarAndInTheStore()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Old name"));
        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        vm.TextRequested += Answers("New name");
        await vm.RenameProfileCommand.ExecuteAsync(null);

        Assert.Equal("New name", vm.SelectedProfile!.Name);
        Assert.Equal("New name", Assert.Single(Store().ListNamed(Connection)).Name);
    }

    /// <summary>A refused rename is reported; the store never resolves a clash by choosing a different name.</summary>
    [Fact]
    public async Task Rename_ToATakenName_IsReported_AndChangesNothing()
    {
        Store().SaveNamed(Connection, "Taken", ImportConfiguration.Empty);

        var vm = await ConfiguredVmAsync(answerName: Answers("Mine"));
        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        vm.TextRequested += Answers("Taken");
        await vm.RenameProfileCommand.ExecuteAsync(null);

        Assert.Equal(MessageSeverity.Warning, vm.StatusSeverity);
        Assert.Equal("Mine", Store().GetById(vm.SelectedProfile!.Id)!.Name);
    }

    [Fact]
    public async Task Delete_AsksFirst_AndKeepsTheProfileWhenDeclined()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Orders"), answerConfirm: Answers(false));
        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        await vm.DeleteProfileCommand.ExecuteAsync(null);

        Assert.Single(Store().ListNamed(Connection));
        Assert.NotNull(vm.SelectedProfile);
    }

    /// <summary>
    /// Deleting the saved copy removes the saved copy — and nothing else. The decisions in front of the user are
    /// their work in progress; throwing them away because a stored copy was deleted would be rule #11 exactly.
    /// </summary>
    [Fact]
    public async Task Delete_RemovesTheProfile_AndLeavesTheSurfaceAsItWas()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Orders"), answerConfirm: Answers(true));
        await vm.SaveProfileAsCommand.ExecuteAsync(null);
        var path = vm.Source.FilePath;

        await vm.DeleteProfileCommand.ExecuteAsync(null);

        Assert.Empty(Store().ListNamed(Connection));
        Assert.Empty(Saved(vm));
        Assert.True(vm.SelectedProfile!.IsNone);
        Assert.Equal(path, vm.Source.FilePath);
        Assert.Equal("ORDERS", vm.Target.SelectedTable);
    }

    /// <summary>Rename and delete act on a selection, so they stay disabled until there is one — the commands
    /// say so themselves rather than a binding in the view.</summary>
    [Fact]
    public async Task RenameAndDelete_AreDisabled_UntilAProfileIsSelected()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Orders"));

        Assert.False(vm.RenameProfileCommand.CanExecute(null));
        Assert.False(vm.DeleteProfileCommand.CanExecute(null));

        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        Assert.True(vm.RenameProfileCommand.CanExecute(null));
        Assert.True(vm.DeleteProfileCommand.CanExecute(null));
    }

    // ── Working without a profile, and starting over ────────────────────────────────────────────────────
    //
    // ⭐ Two needs the first cut missed, both reported from the live surface: once a profile was picked there was
    // no way back to "no profile", and no way to clear the surface at all. They are deliberately DIFFERENT
    // actions — see the pair of tests below. Collapsing them would mean the selector silently discarded work.

    [Fact]
    public async Task TheList_AlwaysOffersWorkingWithoutAProfile()
    {
        var vm = Vm();
        await SettleAsync(vm);

        Assert.True(vm.Profiles[0].IsNone);
        Assert.Equal("(no profile)", vm.Profiles[0].Display);
        Assert.True(vm.SelectedProfile!.IsNone);

        // And it stays first once real profiles exist.
        Store().SaveNamed(Connection, "Orders", ImportConfiguration.Empty);
        var second = Vm();
        await SettleAsync(second);
        Assert.True(second.Profiles[0].IsNone);
    }

    /// <summary>
    /// ⭐ Picking „(no profile)" DETACHES and keeps every decision. It is not a reset, and it must not be one:
    /// throwing away the user's configuration because they stopped associating it with a saved profile is rule
    /// #11 exactly. That is also why the row is not called "default configuration".
    /// </summary>
    [Fact]
    public async Task SelectingNoProfile_Detaches_ButKeepsTheDecisions()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Orders"));
        await vm.SaveProfileAsCommand.ExecuteAsync(null);
        var path = vm.Source.FilePath;

        vm.SelectedProfile = ImportProfileRowViewModel.None;
        await SettleAsync(vm);

        Assert.True(vm.SelectedProfile!.IsNone);
        Assert.Equal(path, vm.Source.FilePath);
        Assert.Equal("ORDERS", vm.Target.SelectedTable);
        // The saved profile is untouched — detaching is about this surface, not about the store.
        Assert.Single(Store().ListNamed(Connection));
        // And it says so, because "does this also clear my work" is the one question the row raises.
        Assert.Contains("unchanged", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectingNoProfile_LeavesRenameAndDeleteWithNothingToActOn()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Orders"));
        await vm.SaveProfileAsCommand.ExecuteAsync(null);
        Assert.True(vm.DeleteProfileCommand.CanExecute(null));

        vm.SelectedProfile = ImportProfileRowViewModel.None;

        Assert.False(vm.RenameProfileCommand.CanExecute(null));
        Assert.False(vm.DeleteProfileCommand.CanExecute(null));
    }

    /// <summary>⭐ Reset is the other half: it clears every decision AND detaches, which is what „start a new
    /// configuration" has to mean.</summary>
    [Fact]
    public async Task Reset_ClearsEveryDecision_AndDetaches()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Orders"), answerConfirm: Answers(true));
        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        await vm.ResetConfigurationCommand.ExecuteAsync(null);
        await SettleAsync(vm);

        Assert.Equal(string.Empty, vm.Source.FilePath);
        Assert.True(string.IsNullOrEmpty(vm.Target.SelectedTable));
        Assert.True(vm.SelectedProfile!.IsNone);
        Assert.False(vm.RestoredLastConfiguration);

        // The saved profile survives — Reset clears the surface, not the store.
        Assert.Single(Store().ListNamed(Connection));
    }

    /// <summary>Reset discards work, so it asks — the same rule every destructive step in this module follows
    /// (§0).</summary>
    [Fact]
    public async Task Reset_AsksFirst_AndChangesNothingWhenDeclined()
    {
        var vm = await ConfiguredVmAsync(answerConfirm: Answers(false));
        var path = vm.Source.FilePath;

        await vm.ResetConfigurationCommand.ExecuteAsync(null);
        await SettleAsync(vm);

        Assert.Equal(path, vm.Source.FilePath);
        Assert.Equal("ORDERS", vm.Target.SelectedTable);
    }

    /// <summary>
    /// ⚠ …but only when there is something to lose. The question asked is about EMPTINESS, not modification —
    /// "has this changed since it loaded" would need a comparison the record cannot give (its list members
    /// compare by reference), so it would answer "changed" always. Asking to clear an already-empty surface
    /// would be a dialog that teaches the user to dismiss dialogs.
    /// </summary>
    [Fact]
    public async Task Reset_DoesNotAsk_WhenThereIsNothingToLose()
    {
        var asked = 0;
        var vm = Vm(answerConfirm: _ => { asked++; return Task.FromResult(true); });
        await SettleAsync(vm);

        await vm.ResetConfigurationCommand.ExecuteAsync(null);

        Assert.Equal(0, asked);
    }

    /// <summary>The „Clear" beside the restore note and the Reset button are the same act, so they are the same
    /// code — two ways to empty a surface would eventually empty different amounts of it.</summary>
    [Fact]
    public async Task ForgettingTheRestoredConfiguration_AlsoDetachesTheProfile()
    {
        var vm = await ConfiguredVmAsync(answerName: Answers("Orders"));
        await vm.SaveProfileAsCommand.ExecuteAsync(null);

        vm.ForgetLastConfigurationCommand.Execute(null);
        await SettleAsync(vm);

        Assert.Equal(string.Empty, vm.Source.FilePath);
        Assert.True(vm.SelectedProfile!.IsNone);
    }

    // ── The surface says what the list contains ─────────────────────────────────────────────────────────

    /// <summary>The scope restriction is stated on screen, because a list that quietly omits things is
    /// indistinguishable from one that has lost them (§4.8.3).</summary>
    [Fact]
    public async Task TheSelector_StatesWhichProfilesItIsShowing()
    {
        var vm = Vm();
        await SettleAsync(vm);

        Assert.Contains("LAB", vm.ProfileScopeNote, StringComparison.Ordinal);
        Assert.Contains("another connection is not offered", vm.ProfileScopeNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ASurfaceWithNoProfileStore_SimplyHasNone()
    {
        // The shape a disconnected surface gets: no delegates at all. It must not throw, and it must not
        // pretend to offer profiles.
        var vm = new DataImportTabViewModel(new DataImportEnvironment(() => false, () => "—"))
        {
            PreviewDebounce = TimeSpan.Zero,
        };
        await SettleAsync(vm);

        // The standing „(no profile)" row is still there — working without one is always available, even when
        // there is nowhere to save one.
        Assert.True(Assert.Single(vm.Profiles).IsNone);
        Assert.False(vm.RenameProfileCommand.CanExecute(null));
    }
}

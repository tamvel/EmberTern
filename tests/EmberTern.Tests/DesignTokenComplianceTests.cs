using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The Design Token guard (Product Polish §11). Typography, corner radii and font families belong to the
/// token catalog in <c>Themes/Tokens.axaml</c> and <c>Themes/Typography.axaml</c> — not to individual views.
/// <para>
/// <b>Why this test exists at all.</b> The project has a hard precedent for what happens without a guard: a
/// keyboard shortcut typed by hand into a tooltip survived the command being re-bound from <c>Alt+F</c> to
/// <c>Ctrl+K</c> for an entire sprint, with a green build and green tests (gotcha #284). A value copied by hand
/// goes stale silently. A design system without a guard grows back into 589 local <c>FontSize</c> declarations,
/// which is exactly the state the M0 audit measured.
/// </para>
/// <para>
/// <b>⭐ Why counts and not a plain file list.</b> §11 first described a list of exempted files. The measured
/// starting state showed why that is not enough: 609 <c>FontSize</c> declarations spread over 49 files, with a
/// single file holding 86. A file-level exemption would clear <c>DataImportTabView.axaml</c> wholesale — it
/// could add an 87th with no signal at all, which is precisely the silence this test exists to break. So an
/// exemption is a <i>pair</i>: file → how many declarations it had when the baseline was taken.
/// </para>
/// <para>
/// <b>⭐ The ratchet detects DRIFT — it does not veto DECISIONS</b> (user, on accepting the design, 2026-08-01).
/// A red test does not mean "you did something wrong"; it means "state which of the two things you are doing".
/// If a number changes deliberately and the change is written down in the stage's documentation, updating the
/// baseline here is a <i>correct part of the process</i>, not a way around the guard. What the guard prevents is
/// the third case: a number that moved because nobody noticed.
/// </para>
/// <para>
/// <b>What is deliberately NOT guarded.</b> <c>Margin</c> and <c>Padding</c> are too contextual — placement
/// inside a layout is the host's responsibility, not the chrome's — so a test over them would be either full of
/// holes or a constant nuisance. There the tool is the review in M2c and M5 (§11).
/// </para>
/// <para>
/// <b>Scope.</b> <c>Views/</c> and <c>Controls/</c> only. <c>Themes/</c> is excluded on purpose: that is where
/// the system lives, and a style setter declaring <c>FontSize</c> there is the catalog doing its job.
/// </para>
/// </summary>
public class DesignTokenComplianceTests
{
    // A declaration is an assignment (`FontSize="12"` in XAML, `FontSize = 12` in an object initializer, or
    // `label.FontSize = 12`) or a style setter (`<Setter Property="FontSize" …>`). `(?!=)` keeps a C# equality
    // comparison out, and matching on `=` rather than the bare word keeps `new FontFamily("…")` from counting
    // twice for one declaration.
    private static Regex DeclarationOf(string property) =>
        new($@"\b{property}\s*=(?!=)|Property\s*=\s*""{property}""", RegexOptions.Compiled);

    /// <summary>
    /// State measured on 2026-08-01, at the start of M2a — before any migration. A long list here is the
    /// <b>correct</b> state at this point: M2a builds the system, M2b switches it on, and <b>M2c</b> is the
    /// stage whose exit condition is this list reduced to a justified remainder. Until then the numbers are a
    /// ceiling, not an endorsement.
    /// </summary>
    private static readonly Dictionary<string, int> FontSizeBaseline = new(StringComparer.Ordinal)
    {
        ["Views/DataImportTabView.axaml"] = 86,
        ["Views/DebuggerTabView.axaml"] = 85,
        ["Views/PerformancePanelView.axaml"] = 42,
        ["Views/FunctionDetailTabView.axaml"] = 41,
        ["Views/ProcedureDetailTabView.axaml"] = 40,
        ["Views/TableDetailTabView.axaml"] = 27,
        ["Views/MainWindow.axaml"] = 26,
        ["Views/SessionManagerTabView.axaml"] = 26,
        ["Views/TriggerDetailTabView.axaml"] = 22,
        ["Views/ViewDetailTabView.axaml"] = 20,
        ["Views/PackageDetailTabView.axaml"] = 17,
        ["Views/SecurityManagerTabView.axaml"] = 17,
        ["Views/TraceMonitorTabView.axaml"] = 17,
        ["Views/DomainDetailTabView.axaml"] = 16,
        ["Views/GeneratorDetailTabView.axaml"] = 15,
        ["Views/ExceptionDetailTabView.axaml"] = 13,
        ["Views/IndexDetailTabView.axaml"] = 11,
        ["Views/ExecuteProcedureDialog.axaml"] = 9,
        ["Views/AddFieldDialog.axaml"] = 8,
        ["Views/ForeignKeyDialog.axaml"] = 8,
        ["Views/DebuggerTabView.axaml.cs"] = 6,
        ["Views/AboutWindow.axaml"] = 5,
        ["Views/IndexDialog.axaml"] = 5,
        ["Views/ConstraintFieldDialog.axaml"] = 4,
        ["Views/NewConnectionDialog.axaml"] = 4,
        ["Views/NewTableTabView.axaml"] = 4,
        ["Controls/TableColumnPicker.cs"] = 3,
        ["Views/CheckConstraintDialog.axaml"] = 3,
        ["Views/DiagnosticsPanelView.axaml"] = 3,
        ["Controls/BreadcrumbBar.axaml"] = 2,
        ["Controls/MessageBanner.axaml"] = 2,
        ["Views/BatchResultsDialog.axaml"] = 2,
        ["Views/GlobalSearchTabView.axaml"] = 2,
        ["Views/ScriptExecutorTabView.axaml"] = 2,
        ["Views/TableDetailTabView.axaml.cs"] = 2,
        ["Views/BlobEditorWindow.axaml"] = 1,
        ["Views/ChoiceDialog.axaml"] = 1,
        ["Views/ConfirmDialog.axaml"] = 1,
        ["Views/ExportDialog.axaml"] = 1,
        ["Views/GlobalSearchDialog.axaml"] = 1,
        ["Views/KeyboardShortcutsWindow.axaml"] = 1,
        ["Views/NewFolderDialog.axaml"] = 1,
        ["Views/RecompileDependentsDialog.axaml"] = 1,
        ["Views/SettingsExportDialog.axaml"] = 1,
        ["Views/SettingsImportDialog.axaml"] = 1,
        ["Views/SettingsWindow.axaml"] = 1,
        ["Views/SubprogramKindDialog.axaml"] = 1,
        ["Views/ThirdPartyNoticesWindow.axaml"] = 1,
        ["Views/UserEditDialog.axaml"] = 1,
    };

    /// <summary>
    /// Seven divergent monospace strings across the app, six of them in these files (D2 collapses them into the
    /// one <c>Font.Code</c> token). Unlike the other two guards this one has <b>no legitimate residue</b>: a
    /// view has no reason to name a font family, so M2c should drive this list to empty.
    /// </summary>
    private static readonly Dictionary<string, int> FontFamilyBaseline = new(StringComparer.Ordinal)
    {
        ["Views/DebuggerTabView.axaml"] = 17,
        ["Views/AddFieldDialog.axaml"] = 5,
        ["Views/FunctionDetailTabView.axaml"] = 5,
        ["Views/ProcedureDetailTabView.axaml"] = 5,
        ["Views/TraceMonitorTabView.axaml"] = 5,
        ["Views/MainWindow.axaml"] = 4,
        ["Views/PackageDetailTabView.axaml"] = 3,
        ["Views/PerformancePanelView.axaml"] = 3,
        ["Views/ScriptExecutorTabView.axaml"] = 3,
        ["Views/SessionManagerTabView.axaml"] = 3,
        ["Views/TriggerDetailTabView.axaml"] = 3,
        ["Views/ViewDetailTabView.axaml"] = 3,
        ["Views/CheckConstraintDialog.axaml"] = 2,
        ["Views/DataImportTabView.axaml"] = 2,
        ["Views/DebuggerTabView.axaml.cs"] = 2,
        ["Views/DiagnosticsPanelView.axaml"] = 2,
        ["Views/ForeignKeyDialog.axaml"] = 2,
        ["Views/IndexDialog.axaml"] = 2,
        ["Views/BlobEditorWindow.axaml"] = 1,
        ["Views/ConstraintFieldDialog.axaml"] = 1,
        ["Views/DomainDetailTabView.axaml"] = 1,
        ["Views/ExceptionDetailTabView.axaml"] = 1,
        ["Views/GeneratorDetailTabView.axaml"] = 1,
        ["Views/GlobalSearchTabView.axaml"] = 1,
        ["Views/IndexDetailTabView.axaml"] = 1,
        ["Views/NewTableTabView.axaml"] = 1,
        ["Views/TableDetailTabView.axaml"] = 1,
        ["Views/ThirdPartyNoticesWindow.axaml"] = 1,
    };

    /// <summary>
    /// Five values with no rule (audit M‑6). The measurement behind §4.2.2: every 4 / 4.5 / 5 / 6 is a chip, every
    /// 3 is a surface — two roles, <c>Radius.Chip</c> and <c>Radius.Surface</c>, not five numbers to average.
    /// </summary>
    private static readonly Dictionary<string, int> CornerRadiusBaseline = new(StringComparer.Ordinal)
    {
        ["Views/SessionManagerTabView.axaml"] = 9,
        ["Views/TraceMonitorTabView.axaml"] = 6,
        ["Views/DataImportTabView.axaml"] = 4,
        ["Views/PerformancePanelView.axaml"] = 4,
        ["Views/ForeignKeyDialog.axaml"] = 3,
        ["Views/ConstraintFieldDialog.axaml"] = 2,
        ["Views/IndexDialog.axaml"] = 2,
        ["Views/SecurityManagerTabView.axaml"] = 2,
        ["Views/AggregationBarView.axaml"] = 1,
        ["Views/CheckConstraintDialog.axaml"] = 1,
        ["Views/FunctionDetailTabView.axaml"] = 1,
        ["Views/MainWindow.axaml"] = 1,
        ["Views/ProcedureDetailTabView.axaml"] = 1,
    };

    private static Dictionary<string, int> BaselineFor(string property) => property switch
    {
        "FontSize" => FontSizeBaseline,
        "FontFamily" => FontFamilyBaseline,
        "CornerRadius" => CornerRadiusBaseline,
        _ => throw new ArgumentOutOfRangeException(nameof(property), property, "No baseline is declared for this property."),
    };

    public static TheoryData<string> GuardedProperties => new() { "FontSize", "FontFamily", "CornerRadius" };

    [Theory]
    [MemberData(nameof(GuardedProperties))]
    public void NoFileDeclaresMoreThanItsBaseline(string property)
    {
        var actual = Measure(property);
        var baseline = BaselineFor(property);

        var over = actual
            .Where(kv => kv.Value > baseline.GetValueOrDefault(kv.Key))
            .OrderByDescending(kv => kv.Value - baseline.GetValueOrDefault(kv.Key))
            .Select(kv => $"{kv.Key}: {baseline.GetValueOrDefault(kv.Key)} → {kv.Value}")
            .ToList();

        Assert.True(over.Count == 0,
            $"New local `{property}` declarations appeared in a view:\n  " + string.Join("\n  ", over) +
            $"\n\nThe catalog is in Themes/Tokens.axaml and Themes/Typography.axaml. Two ways out, and the\n" +
            "point of this test is that you say which one it is:\n" +
            $"  • The value belongs to a role ⇒ read it from the token instead of writing the number here.\n" +
            $"  • The change is a deliberate design decision ⇒ raise the baseline above AND record the reason\n" +
            "    in docs/design/product-polish.md. That is a correct part of the process (§11.1), not a\n" +
            "    workaround — what this guard exists to catch is the number that moved unnoticed.");
    }

    [Theory]
    [MemberData(nameof(GuardedProperties))]
    public void TheBaselineHasNoStaleEntries(string property)
    {
        // A ceiling nobody lowers stops being a ceiling: a file migrated down from 26 to 4 would silently keep
        // permission for 22 more. Lowering the number as the work lands is what makes M2c's exit condition a
        // number rather than an opinion.
        var actual = Measure(property);
        var baseline = BaselineFor(property);

        var stale = baseline
            .Where(kv => actual.GetValueOrDefault(kv.Key) < kv.Value)
            .OrderBy(kv => kv.Key)
            .Select(kv => actual.ContainsKey(kv.Key)
                ? $"{kv.Key}: baseline {kv.Value}, actually {actual[kv.Key]} — lower it"
                : $"{kv.Key}: baseline {kv.Value}, now clean or gone — remove the entry")
            .ToList();

        Assert.True(stale.Count == 0,
            $"The `{property}` baseline is higher than reality — this is progress that was not written down:\n  " +
            string.Join("\n  ", stale) +
            $"\n\nCurrent total: {actual.Values.Sum()} across {actual.Count} file(s); baseline says " +
            $"{baseline.Values.Sum()} across {baseline.Count}. Update the numbers above so the next reader sees\n" +
            "how much is genuinely left.");
    }

    [Fact]
    public void TheTokenDictionaries_AreRegisteredInTheApplication()
    {
        // The catalog is only a catalog if the application actually merges it. A dictionary that exists in the
        // repository but is not registered resolves no key at runtime, and the failure surfaces as "the token
        // does not work" somewhere in M2b — far from its cause.
        var app = File.ReadAllText(Path.Combine(AppRoot(), "App.axaml"));

        foreach (var dictionary in new[] { "Themes/Tokens.axaml", "Themes/Typography.axaml" })
        {
            Assert.True(app.Contains($"avares://EmberTern/{dictionary}", StringComparison.Ordinal),
                $"{dictionary} is not merged in App.axaml — every token it declares is unreachable at runtime.");
            Assert.True(File.Exists(Path.Combine(AppRoot(), dictionary.Replace('/', Path.DirectorySeparatorChar))),
                $"App.axaml merges {dictionary}, but the file does not exist.");
        }
    }

    [Fact]
    public void NoResourceKey_IsDeclaredInMoreThanOneThemeFile()
    {
        // Every Themes/*.axaml dictionary is merged into ONE resource scope, so a key declared in two of them
        // resolves to whichever loaded last — silently, with no warning and no failing build. A spacing token
        // shadowed by something in Colors.axaml is the kind of defect that surfaces months later as "this one
        // screen is 3 px off", far from its cause.
        //
        // ⚠ Scope is the whole Themes/ folder, not just the two token files. The catalog is the newcomer here:
        // it added 76 keys to a folder that already had 247, and M2b will add more. Checking only the new files
        // against each other verifies the half that is least likely to be wrong.
        var duplicates = KeysByFile()
            .SelectMany(entry => entry.Value.Select(key => (entry.Key, Key: key)))
            .GroupBy(x => x.Key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} — declared in {string.Join(", ", g.Select(x => x.Item1).OrderBy(f => f, StringComparer.Ordinal))}")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "A resource key is declared in more than one theme dictionary; the later declaration silently wins:\n  " +
            string.Join("\n  ", duplicates) +
            "\n\nRename one of them. A key is a name in a single global namespace — two owners means the winner " +
            "depends on merge order in App.axaml, which nobody reads as an ownership decision.");
    }

    [Fact]
    public void NoThemeFile_DeclaresTheSameKeyTwiceInOneScope()
    {
        // ⚠⚠ THE SUBTLETY THAT MAKES THIS TEST CORRECT: a file with <ThemeDictionaries> declares each key TWICE
        // ON PURPOSE — once for Dark and once for Light. That is UI rule #3 ("every colour comes from both
        // dictionaries"); a key present in only one variant is the actual defect there. Colors.axaml is 283
        // declarations over 146 distinct keys for exactly that reason.
        //
        // So the duplicate rule only applies to VARIANT-FREE dictionaries, where all keys share one scope and a
        // repeat is unambiguously a mistake. Writing this test without the distinction would have reported 137
        // "collisions" in a file that is correct, and the natural next move — relaxing it until it went green —
        // would have removed the check that matters.
        var offenders = new List<string>();

        foreach (var file in ThemeFiles())
        {
            var text = File.ReadAllText(file);
            if (text.Contains("ThemeDictionaries", StringComparison.Ordinal)) continue;

            var repeated = Regex.Matches(text, @"x:Key=""([^""]+)""")
                .Select(m => m.Groups[1].Value)
                .GroupBy(k => k, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .Select(g => $"{Path.GetFileName(file)}: {g.Key} ×{g.Count()}");

            offenders.AddRange(repeated);
        }

        Assert.True(offenders.Count == 0,
            "A theme dictionary declares the same key twice in one scope — the second declaration wins and the " +
            "first is dead:\n  " + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> ThemeFiles() =>
        Directory.EnumerateFiles(Path.Combine(AppRoot(), "Themes"), "*.axaml").OrderBy(f => f, StringComparer.Ordinal);

    /// <summary>
    /// File name → the DISTINCT keys it declares. Distinct per file on purpose: within a variant dictionary the
    /// same key legitimately appears once per theme, and that is not what the cross-file check is looking for.
    /// </summary>
    private static Dictionary<string, HashSet<string>> KeysByFile() =>
        ThemeFiles().ToDictionary(
            // Method group would infer string? (GetFileName is NotNullIfNotNull-annotated), which a dictionary
            // key may not be — the input is always a real path here.
            f => Path.GetFileName(f)!,
            f => Regex.Matches(File.ReadAllText(f), @"x:Key=""([^""]+)""")
                      .Select(m => m.Groups[1].Value)
                      .ToHashSet(StringComparer.Ordinal),
            StringComparer.Ordinal);

    /// <summary>
    /// Counts declarations per file, keyed by a repository-relative path with forward slashes. Keyed by path
    /// rather than by file name because a name can repeat between <c>Views/</c> and <c>Controls/</c>, and an
    /// exemption granted to the wrong file is worse than no exemption.
    /// </summary>
    private static Dictionary<string, int> Measure(string property)
    {
        var declaration = DeclarationOf(property);
        var appRoot = AppRoot();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var folder in new[] { "Views", "Controls" })
        {
            var root = Path.Combine(appRoot, folder);
            Assert.True(Directory.Exists(root), $"Could not locate {folder} at {root}");

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) &&
                    !file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hits = declaration.Matches(File.ReadAllText(file)).Count;
                if (hits > 0)
                {
                    counts[Path.GetRelativePath(appRoot, file).Replace('\\', '/')] = hits;
                }
            }
        }

        return counts;
    }

    private static string AppRoot() => Path.Combine(RepositoryRoot(), "src", "EmberTern.App");

    // Walks up from the test binary to the directory holding EmberTern.slnx. The test reads SOURCE, so it needs
    // the repository rather than the output folder.
    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

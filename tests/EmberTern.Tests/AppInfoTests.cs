using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmberTern.App;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The product's identity has ONE source — the <c>PropertyGroup</c> in <c>Directory.Build.props</c> — and the
/// About window reads it back off the built assembly. These tests are what make that a guarantee rather than
/// an intention.
///
/// <para>⭐ Note what is <b>not</b> written down here: the version number. Asserting <c>"1.2.0"</c> would make
/// this file a second source of truth, so every test below reads the expected value out of the props file
/// instead. Bumping the version therefore needs no test change — which is exactly the property being
/// protected.</para>
/// </summary>
public sealed class AppInfoTests
{
    [Fact]
    public void VersionComesFromTheBuild()
    {
        var declared = BuildProperty("Version");

        Assert.Equal(declared, AppInfo.Version);

        // The whole reason IncludeSourceRevisionInInformationalVersion is off AND AppInfo truncates at '+':
        // .NET 8+ appends the source-revision hash, and an About box showing "1.2.0+9a3f2c1…" is a defect.
        Assert.DoesNotContain("+", AppInfo.Version, StringComparison.Ordinal);
    }

    // ⛔ The user's standing requirement: no hard-coded version anywhere in the code. A hand-typed copy would
    // not merely duplicate the build — it would go stale silently on the next release, with a green build,
    // which is the failure mode gotcha #284 records for shortcuts.
    [Fact]
    public void NoVersionNumberIsHardCodedInTheApp()
    {
        var version = AppInfo.Version;
        Assert.NotEmpty(version);

        var offenders = AppSourceFiles()
            .Where(f => File.ReadAllText(f).Contains(version, StringComparison.Ordinal))
            .Select(Relative)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "the product version must live only in Directory.Build.props, but its text also appears in: "
            + string.Join(", ", offenders));
    }

    // ⭐ The test above is not enough, and the gap is worth understanding rather than patching over: it
    // searches for the CURRENT version, so a literal left over from an EARLIER one sails straight past. That
    // is not hypothetical — the status bar carried Text="EmberTern 0.1.0" in MainWindow.axaml, stale for who
    // knows how long, and the user found it by seeing the status bar and the About window disagree on screen.
    // A guard keyed to today's value can only catch a copy someone makes today.
    //
    // So this one looks for the SHAPE of a version rather than a value, in the two places a version can
    // actually reach a user:
    //   · a XAML Text= / Content= attribute  — verified against the exact removed literal, which it matches
    //     (a bare  "\d+\.\d+\.\d+"  pattern did NOT: the string was "EmberTern 0.1.0", so the quote is not
    //      adjacent to the digits. That regex would have felt like a guard while catching nothing.)
    //   · a C# string literal on a non-comment line
    //
    // Two deliberate exclusions, each with a reason. Spec references (§9.8.1, §4.8.3) are excluded by the
    // lookbehind — they are pervasive in this codebase's prose and are not versions. Comment lines are
    // excluded because prose legitimately names the literal that was REMOVED, which is a historical fact that
    // cannot go stale; the *current* version is still banned from comments too, by the test above. Between the
    // two: today's number appears nowhere at all, and a version shape appears nowhere it could be displayed.
    // Scoped to EmberTern.App — Core legitimately quotes dotted numbers (SqlLiteralWriter's "15.03.2024" date
    // example), and Core shows nothing to a user.
    [Fact]
    public void NoVersionShapedLiteralCanReachTheScreen()
    {
        var inXaml = new Regex(@"(?:Text|Content)=""[^""]*\d+\.\d+\.\d+[^""]*""");
        var inCode = new Regex(@"""[^""\n]*(?<![§.\d])\d+\.\d+\.\d+(?![\d.])[^""\n]*""");

        var offenders = new List<string>();

        foreach (var file in AppSourceFiles())
        {
            bool xaml = file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase);

            foreach (var (line, number) in File.ReadAllLines(file).Select((l, i) => (l, i + 1)))
            {
                var trimmed = line.TrimStart();
                if (!xaml && (trimmed.StartsWith("//", StringComparison.Ordinal)
                              || trimmed.StartsWith("*", StringComparison.Ordinal)
                              || trimmed.StartsWith("/*", StringComparison.Ordinal)))
                {
                    continue;
                }

                var hit = (xaml ? inXaml : inCode).Match(line);
                if (hit.Success) offenders.Add($"{Relative(file)}:{number} → {hit.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "a version-shaped literal on a surface that can be displayed is a second source of truth waiting "
            + "to go stale; read it from AppInfo instead. Found: " + string.Join(", ", offenders));
    }

    [Fact]
    public void ReleaseDateComesFromTheBuild()
    {
        var declared = BuildProperty("ReleaseDate");

        Assert.NotNull(AppInfo.ReleaseDate);
        Assert.Equal(declared, AppInfo.ReleaseDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static IEnumerable<string> AppSourceFiles()
        => Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "src", "EmberTern.App"), "*.*",
                SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase));

    private static string Relative(string file) => Path.GetRelativePath(RepositoryRoot(), file);

    [Fact]
    public void ProductAuthorAndCopyrightComeFromTheBuild()
    {
        Assert.Equal(BuildProperty("Product"), AppInfo.Product);
        Assert.Equal(BuildProperty("Company"), AppInfo.Author);
        Assert.Equal(BuildProperty("Copyright"), AppInfo.Copyright);

        // Both strings carry non-ASCII characters (© and ń) through an XML props file, an MSBuild property and
        // an assembly attribute. Comparing against the file proves the whole chain round-trips — the encoding
        // question is answered by measurement rather than by assuming UTF-8 held.
        Assert.Contains("©", AppInfo.Copyright, StringComparison.Ordinal);
        Assert.Contains("ń", AppInfo.Author, StringComparison.Ordinal);
    }

    private static string BuildProperty(string name)
    {
        var props = File.ReadAllText(Path.Combine(RepositoryRoot(), "Directory.Build.props"));
        var match = Regex.Match(props, $@"<{Regex.Escape(name)}>([^<]*)</{Regex.Escape(name)}>");
        Assert.True(match.Success, $"<{name}> is not declared in Directory.Build.props");
        return match.Groups[1].Value;
    }

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

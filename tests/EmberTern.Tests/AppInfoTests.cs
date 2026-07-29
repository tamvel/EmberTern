using System;
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

        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase)
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains(version, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepositoryRoot(), f))
            .ToArray();

        Assert.True(offenders.Length == 0,
            "the product version must live only in Directory.Build.props, but its text also appears in: "
            + string.Join(", ", offenders));
    }

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

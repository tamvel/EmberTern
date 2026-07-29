using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmberTern.App.Views;
using Xunit;
using Xunit.Abstractions;

namespace EmberTern.Tests;

/// <summary>
/// The third-party notices are a licence obligation, not documentation: MIT requires its copyright and
/// permission notice "in all copies", and <c>FirebirdSql.Data.FirebirdClient</c> is IDPL 1.0, whose §3.6 wants a
/// source-availability notice accompanying an executable distribution.
///
/// <para>⭐ The load-bearing test here is <see cref="EveryShippedDependencyIsNamedInTheNotices"/>. Adding a
/// NuGet package is easy and remembering to add a notice is not, so the notices file is checked against the
/// project files rather than against anyone's memory — the same reflex as pinning a gesture to the catalog.</para>
/// </summary>
public sealed class ThirdPartyNoticesTests
{
    private readonly ITestOutputHelper _out;

    public ThirdPartyNoticesTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void TheBuildCarriesTheNoticesAsAnEmbeddedResource()
    {
        // Reads the resource the WINDOW reads, so a packaging change that drops it fails here rather than
        // showing an apology to a user. A file on disk beside the exe can go missing; a resource cannot.
        var text = ThirdPartyNoticesWindow.ReadNotices();

        Assert.NotEqual(EmberTern.App.UiStrings.ThirdPartyNoticesUnavailable, text);
        Assert.Contains("EmberTern — Third-party notices", text, StringComparison.Ordinal);

        // The two obligations that made this file mandatory, each present in full rather than by reference.
        Assert.Contains("The MIT License (MIT)", text, StringComparison.Ordinal);
        Assert.Contains("Initial Developer's Public License Version 1.0", text, StringComparison.Ordinal);
        Assert.Contains("3.6. Distribution of Executable Versions", text, StringComparison.Ordinal);

        // The icon set: ISC for Lucide, plus the MIT notice for the portions it inherits from Feather. Both,
        // because Lucide's own LICENSE carries both — a detail that reciting "Lucide is ISC" would have lost.
        Assert.Contains("Lucide Icons and Contributors", text, StringComparison.Ordinal);
        Assert.Contains("Cole Bemis", text, StringComparison.Ordinal);

        _out.WriteLine($"notices: {text.Length} chars, {text.Split('\n').Length} lines");
    }

    // ⭐ A new PackageReference in a shipping project must arrive with its notice. Nothing else in the build
    // would object, and the omission is invisible until someone audits the product.
    [Fact]
    public void EveryShippedDependencyIsNamedInTheNotices()
    {
        var notices = ThirdPartyNoticesWindow.ReadNotices();

        // Debug-only, absent from Release output, and its package declares no licence at all — so it is not
        // distributed and the notices deliberately do not state a licence for it. Section 3 says exactly that.
        var notDistributed = new[] { "AvaloniaUI.DiagnosticsSupport" };
        Assert.All(notDistributed, id => Assert.Contains(id, notices, StringComparison.Ordinal));

        var missing = new List<string>();

        foreach (var project in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot(), "src"), "*.csproj", SearchOption.AllDirectories))
        {
            foreach (Match reference in Regex.Matches(
                         File.ReadAllText(project), @"<PackageReference\s+Include=""([^""]+)"""))
            {
                var id = reference.Groups[1].Value;
                if (notDistributed.Contains(id)) continue;

                // A package is "named" if its own id appears, or the family it belongs to does — the notices
                // list Avalonia's satellite assemblies under one entry rather than repeating the copyright
                // fifteen times, which is how a human reads a notices file.
                var family = id.Split('.')[0];
                if (!notices.Contains(id, StringComparison.Ordinal)
                    && !notices.Contains(family, StringComparison.Ordinal))
                {
                    missing.Add($"{id} (in {Path.GetFileName(project)})");
                }
            }
        }

        Assert.True(missing.Count == 0,
            "these shipping dependencies are not named in THIRD-PARTY-NOTICES.txt: " + string.Join(", ", missing));
    }

    // The file is one source with two destinations: embedded for the app, and beside the exe for whoever
    // audits the product without launching it. If they ever disagree, the copy a reviewer reads is not the
    // copy the application shows.
    [Fact]
    public void TheCopyBesideTheExecutableMatchesTheEmbeddedOne()
    {
        var onDisk = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        Assert.True(File.Exists(onDisk), $"the notices file is not beside the build output at {onDisk}");

        static string Normalise(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

        Assert.Equal(Normalise(ThirdPartyNoticesWindow.ReadNotices()), Normalise(File.ReadAllText(onDisk)));
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

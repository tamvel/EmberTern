using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>L4b — <c>LicensedConnections</c> is the ONLY way this application opens a database attachment.</b>
///
/// <para><b>Why a source guard and not four checks.</b> The licence question has to be asked on every path
/// that opens a new attachment — Connect, Test connection, a debug session, an import session (design §7,
/// ratified 2026-08-15 including the deliberate absence of an exception for Test connection). A check
/// written at each call site is a check the fifth call site forgets, silently, with a green build. The
/// check cannot live in a type system — <c>FirebirdConnectionService</c>'s API is public and perfectly
/// callable — so it lives here, in exactly the shape <c>CharsetGuardSeamTests</c> already uses for the
/// charset seam.</para>
///
/// <para>⚠⚠ <b>Comments are stripped before scanning, and that is load-bearing rather than tidy.</b> The
/// same trap fired three times in one L4a session (design §37.2): a guard that matches source TEXT fires on
/// the prose documenting its own rule. ⭐ The fix is always to strip comments, ⛔ never to reword the
/// documentation — a guard that fires on its own rule is one that gets suppressed, and a suppressed guard
/// reads as coverage while providing none. This file's own subject sentence names all four members.</para>
///
/// <para>⚠ <b>The bound is stated honestly.</b> Three of the four members have names nothing else in the
/// application uses, so they are matched outright. <c>ConnectAsync</c> is not unique —
/// <c>MainWindowViewModel.ConnectAsync</c> is a view-model method that legitimately forwards the user's
/// gesture — so it is matched on the RECEIVER instead: any identifier ending in <c>service</c>, which is
/// this codebase's name for the connection service everywhere it appears. ⛔ A future receiver named
/// something else would slip past; that is a known limit written down rather than an overclaim, and the
/// three unique members are what actually close the domain.</para>
/// </summary>
public class LicensingConnectionSeamTests
{
    /// <summary>The one file allowed to open an attachment — the seam itself.</summary>
    private const string SeamFile = "LicensedConnections.cs";

    /// <summary>
    /// The four members of <c>FirebirdConnectionService</c> that open a NEW attachment, with the pattern that
    /// recognises a call to each.
    /// </summary>
    private static readonly (string Member, string Pattern)[] Openers =
    {
        // ⚠ Matched on the receiver, because the member name is not unique. See the class remarks.
        ("ConnectAsync", @"(?i)\b[_a-z0-9]*service\s*\.\s*ConnectAsync\s*\("),
        ("TestConnectionAsync", @"\.\s*TestConnectionAsync\s*\("),
        ("CreateDebugSessionAsync", @"\.\s*CreateDebugSessionAsync\s*\("),
        ("CreateImportSessionAsync", @"\.\s*CreateImportSessionAsync\s*\("),
    };

    [Fact]
    public void NoAttachmentIsOpened_OutsideTheLicensingSeam()
    {
        var offenders = new List<string>();

        foreach (var (path, text) in AppSources())
        {
            if (Path.GetFileName(path) == SeamFile) continue;

            foreach (var (member, pattern) in Openers)
            {
                foreach (Match m in Regex.Matches(text, pattern))
                {
                    offenders.Add($"{Path.GetFileName(path)}:{LineOf(text, m.Index)}  {member}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These open a database attachment without asking the licence, so an expired or unusable licence "
            + $"would not stop them. Route them through {SeamFile} (OpenAsync / TestAsync / "
            + "OpenDebugSessionAsync / OpenImportSessionAsync) instead:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The other half, and the one that would otherwise rot silently: the seam has to still CALL all four.
    ///
    /// <para>⚠ Without this, deleting a passthrough from the seam would leave the guard above green while the
    /// capability disappeared — a guard proving only that nobody else does something nobody does.</para>
    /// </summary>
    [Fact]
    public void TheSeamItself_StillOpensAllFour()
    {
        var seam = File.ReadAllText(SeamPath());
        var missing = Openers
            .Where(o => !Regex.IsMatch(seam, o.Pattern))
            .Select(o => o.Member)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{SeamFile} no longer opens: " + string.Join(", ", missing)
            + ". Either the capability was lost, or it moved somewhere the guard above cannot see.");
    }

    /// <summary>
    /// ⭐ The seam refuses BEFORE the driver is touched, and it refuses by throwing.
    ///
    /// <para>⚠ Stated as a source property because the behaviour itself is only observable in a
    /// <c>Release</c> build (the gate is a compile-time <c>const</c>): every opener calls <c>Guard()</c>
    /// first. A passthrough that skipped it would compile, and in <c>Debug</c> would test identically.</para>
    /// </summary>
    [Fact]
    public void EverySeamMethod_GuardsBeforeItOpens()
    {
        var seam = StripComments(File.ReadAllText(SeamPath()));

        var bodies = Regex.Matches(
            seam,
            @"internal\s+(?:Task|Task<[^>]+>)\s+(?<name>\w+)\s*\([^)]*\)\s*\{(?<body>[^}]*)\}",
            RegexOptions.Singleline);

        var unguarded = bodies
            .Where(m => !m.Groups["body"].Value.Contains("Guard();", StringComparison.Ordinal))
            .Select(m => m.Groups["name"].Value)
            .ToList();

        Assert.Equal(4, bodies.Count);
        Assert.True(unguarded.Count == 0,
            "These seam methods open an attachment without calling Guard() first: "
            + string.Join(", ", unguarded));
    }

    private static string SeamPath()
        => Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Licensing", SeamFile);

    private static IEnumerable<(string Path, string Text)> AppSources()
    {
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepositoryRoot(), "src", "EmberTern.App"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            yield return (file, StripComments(File.ReadAllText(file)));
        }
    }

    /// <summary>
    /// ⚠⚠ Comments go first. A file that DOCUMENTS the rule — and several of them must, because the rule is
    /// the reason those files are shaped as they are — would otherwise be reported as breaking it.
    /// </summary>
    private static string StripComments(string source) =>
        Regex.Replace(
            Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
            @"//[^\r\n]*", string.Empty);

    private static int LineOf(string text, int index) =>
        text.Take(index).Count(c => c == '\n') + 1;

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Could not locate the repository root from the test binary.");
        return dir!.FullName;
    }
}

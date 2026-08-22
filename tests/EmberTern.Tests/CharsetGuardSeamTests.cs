using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>T6 — the seam is the ONLY way this product creates a Firebird command.</b>
///
/// <para><b>Why a source guard and not a design note.</b> The charset guard is only as good as its coverage:
/// one <c>connection.CreateCommand()</c> written next year, in a file nobody associates with charsets, and
/// that path silently loses data again — with a green build, green tests and no symptom until a user's source
/// code has already been rewritten. The check cannot live in a type system (the driver's API is public and
/// perfectly callable), so it lives here.</para>
///
/// <para>⚠ This is the same shape as the guards this codebase already relies on
/// (<c>FluentBridge_ContainsNoLocalValues</c>, the <c>FbServerType.Embedded</c> guard, the
/// no-version-number-in-code guards): <b>a rule that would otherwise decay silently, pinned by the build</b>.
/// It was verified RED by reintroducing a raw creation before being accepted green.</para>
///
/// <para>⛔ If a new call site genuinely cannot use the seam, that is a design conversation, not a reason to
/// widen this regex. The seam already covers text, named parameters, positional parameters and batches.</para>
/// </summary>
public class CharsetGuardSeamTests
{
    /// <summary>The one file allowed to touch the driver's raw command API — the seam itself.</summary>
    private const string SeamFile = "FirebirdCommandGuard.cs";

    [Fact]
    public void NoRawCommandCreation_BypassesTheCharsetSeam()
    {
        var offenders = new List<string>();

        foreach (var (path, text) in ProductSources())
        {
            if (Path.GetFileName(path) == SeamFile) continue;

            foreach (var pattern in new[]
                     {
                         // Raw command creation: the text would never be checked.
                         @"\.CreateCommand\s*\(",
                         @"\bnew\s+FbCommand\s*\(",
                         @"\bnew\s+FbBatchCommand\s*\(",
                     })
            {
                foreach (Match m in Regex.Matches(text, pattern))
                {
                    offenders.Add($"{Path.GetFileName(path)}:{LineOf(text, m.Index)}  {Trim(text, m.Index)}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These bypass the charset seam, so whatever text they carry can be silently rewritten by the "
            + $"connection charset before it reaches Firebird. Use {SeamFile}'s CreateGuardedCommand / "
            + "CreateGuardedBatchCommand instead:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The other half: a command may exist without the seam having seen its TEXT, if someone assigns
    /// <c>CommandText</c> directly after creating it.
    /// </summary>
    [Fact]
    public void NoDirectCommandTextAssignment_BypassesTheCharsetSeam()
    {
        var offenders = new List<string>();

        foreach (var (path, text) in ProductSources())
        {
            if (Path.GetFileName(path) == SeamFile) continue;

            foreach (Match m in Regex.Matches(text, @"\.CommandText\s*="))
            {
                offenders.Add($"{Path.GetFileName(path)}:{LineOf(text, m.Index)}  {Trim(text, m.Index)}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Assigning CommandText directly skips the charset check. Pass the SQL to CreateGuardedCommand "
            + "instead:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// And the parameter half: a bound string is encoded with the connection charset exactly as statement text
    /// is, and was measured losing data identically.
    /// </summary>
    [Fact]
    public void NoRawParameterBinding_BypassesTheCharsetSeam()
    {
        var offenders = new List<string>();

        foreach (var (path, text) in ProductSources())
        {
            if (Path.GetFileName(path) == SeamFile) continue;

            foreach (var pattern in new[] { @"\.Parameters\s*\.\s*AddWithValue\s*\(", @"\.Parameters\s*\.\s*Add\s*\(" })
            {
                foreach (Match m in Regex.Matches(text, pattern))
                {
                    // The batch path binds through FbBatchParameterCollection, which has no command to reach
                    // the connection through; it calls FirebirdCommandGuard.VerifyBatchValue explicitly on the
                    // line above. Recognised by that call being present in the same file.
                    if (text.Contains("FirebirdCommandGuard.VerifyBatchValue", StringComparison.Ordinal)) continue;

                    offenders.Add($"{Path.GetFileName(path)}:{LineOf(text, m.Index)}  {Trim(text, m.Index)}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "These bind a parameter without the charset check. Use AddGuardedParameter:\n  "
            + string.Join("\n  ", offenders));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Projects outside this guard's domain: they cannot reach the Firebird driver, so a
    /// <c>CreateCommand()</c> in them is a command on some other provider entirely.
    ///
    /// <para>⚠⚠ <b>An exclusion list is normally the wrong answer — and this one is only acceptable
    /// because <see cref="TheExcludedProjectsGenuinelyCannotReachTheFirebirdDriver"/> proves its
    /// precondition on every run.</b> The guard's domain was always "code that can create a Firebird
    /// command"; before the License Manager existed, "everything under src/" happened to be the same set,
    /// and the guard was written in those terms. It is not the same set any more: the License Manager
    /// talks to SQLite, whose provider does not transliterate anything. Narrowing by DOMAIN, with the
    /// domain re-checked mechanically, is the difference between a boundary and a loophole.</para>
    /// </summary>
    private static readonly string[] OutsideTheFirebirdDomain =
    [
        "EmberTern.Licensing",
        "EmberTern.Licensing.Issuing",
        "EmberTern.LicenseManager",
    ];

    [Fact]
    public void TheExcludedProjectsGenuinelyCannotReachTheFirebirdDriver()
    {
        // ⭐ The test that turns the exclusion above from a promise into a fact. If any of those projects
        //    ever gains a path to FirebirdSql, this fails — and the exclusion must be removed before the
        //    project can build, rather than quietly covering a real charset bypass.
        var root = RepoRoot();
        var offenders = new List<string>();

        foreach (var project in OutsideTheFirebirdDomain)
        {
            foreach (var reachable in ReachableProjects(Path.Combine(root, "src", project, project + ".csproj")))
            {
                var text = File.ReadAllText(reachable);
                if (text.Contains("FirebirdSql", StringComparison.Ordinal) ||
                    text.Contains("EmberTern.Firebird", StringComparison.Ordinal))
                {
                    offenders.Add($"{project} reaches Firebird through {Path.GetFileName(reachable)}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "These projects are excluded from the charset seam guard but CAN now reach the Firebird "
            + "driver. Remove them from OutsideTheFirebirdDomain — the exclusion is no longer true:\n  "
            + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> ReachableProjects(string projectPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(projectPath));

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current) || !File.Exists(current))
            {
                continue;
            }

            yield return current;

            var folder = Path.GetDirectoryName(current)!;
            foreach (Match match in Regex.Matches(
                         File.ReadAllText(current), @"ProjectReference\s+Include\s*=\s*""([^""]+)"""))
            {
                pending.Push(Path.GetFullPath(Path.Combine(
                    folder, match.Groups[1].Value.Replace('\\', Path.DirectorySeparatorChar))));
            }
        }
    }

    private static IEnumerable<(string Path, string Text)> ProductSources()
    {
        var root = RepoRoot();
        foreach (var path in Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            // Skip build output (bin/obj carry generated copies of the same sources).
            if (path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            // Outside the domain — see OutsideTheFirebirdDomain, and the test that keeps it honest.
            if (OutsideTheFirebirdDomain.Any(project => path.Contains(
                    $"{Path.DirectorySeparatorChar}{project}{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                continue;
            }

            yield return (path, File.ReadAllText(path));
        }
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static int LineOf(string text, int index) => text.Take(index).Count(c => c == '\n') + 1;

    private static string Trim(string text, int index)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var end = text.IndexOf('\n', index);
        if (end < 0) end = text.Length;
        return text[start..end].Trim();
    }
}

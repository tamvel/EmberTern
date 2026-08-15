using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>The four guards on the Debug/Release licensing gate (design §16.5, decision D15).</b>
///
/// <para>The rule they protect: <c>Debug</c> disables the BLOCK, not the LICENSING. Verification runs
/// identically in both configurations; only whether an unusable verdict prevents use differs. ⭐ The bypass
/// lives in the gate and never in the verifier — which is load-bearing, because this suite runs in
/// <c>Debug</c>, so a bypass inside <c>EmberTern.Licensing</c> would make the entire tamper corpus vacuous:
/// every licensing test would pass while proving nothing.</para>
///
/// <para>⚠⚠ <b>The fourth guard is the one that matters most and looks least important.</b> Without
/// <see cref="NoProjectFileSmugglesDebugIntoAnotherConfiguration"/>, the other three stay green while a
/// <c>Release</c> build ships with the gate off — because they all reason about the <c>DEBUG</c> symbol
/// rather than about who defines it.</para>
/// </summary>
public class LicensingGateTests
{
    private static readonly string Root = RepositoryRoot();

    private static string PolicySource => File.ReadAllText(
        Path.Combine(Root, "src", "EmberTern.App", "Licensing", "LicensingPolicy.cs"));

    [Fact]
    public void TheGateFollowsTheBuildConfiguration()
    {
        // ⭐ The runtime half, and the only one that can prove the RELEASE arm — which a Debug-only run never
        //   can. Running the suite in `-c Release` is therefore part of the acceptance, not hygiene.
#if DEBUG
        Assert.False(EmberTern.App.Licensing.LicensingPolicy.GateEnabled,
            "A Debug build must not block on licensing.");
#else
        Assert.True(EmberTern.App.Licensing.LicensingPolicy.GateEnabled,
            "A Release build MUST block on licensing. This is the whole feature.");
#endif
    }

    [Fact]
    public void ThePolicyFileHasExactlyOneConditionalAndItsReleaseArmIsTrue()
    {
        var source = PolicySource;

        Assert.Single(Regex.Matches(source, @"^\s*#if DEBUG\s*$", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(source, @"^\s*#else\s*$", RegexOptions.Multiline));
        Assert.Single(Regex.Matches(source, @"^\s*#endif\s*$", RegexOptions.Multiline));

        // ⭐ The `#else` arm — the one that ships — must read `true`. Asserted on the ARM rather than on the
        //   file, so inverting the two branches fails here instead of looking like a formatting change.
        var releaseArm = Regex.Match(
            source, @"#else(?<arm>.*?)#endif", RegexOptions.Singleline).Groups["arm"].Value;

        Assert.Contains("GateEnabled = true", releaseArm, StringComparison.Ordinal);
        Assert.DoesNotContain("GateEnabled = false", releaseArm, StringComparison.Ordinal);

        // ⭐ `const`, not `static readonly`: the compiler folds every `if (GateEnabled)` and eliminates the
        //   dead arm, so a Release binary carries no bypass code to patch back on.
        Assert.Equal(2, Regex.Matches(source, @"internal const bool GateEnabled").Count);
    }

    [Fact]
    public void TheGateHasExactlyOneInputAndItIsTheCompileTimeSymbol()
    {
        // ⛔ No setting, no environment variable, no command-line argument, no file may influence the gate.
        //    A second input would be a switch, and a switch in a Release build is the feature's undoing.
        //
        // ⚠⚠ THE FIRST VERSION OF THIS GUARD WAS A FALSE POSITIVE AND I ONLY SAW IT BY ACCIDENT. It scanned
        //    every file that MENTIONS `GateEnabled` for anything resembling a runtime input, and flagged
        //    `LicenseService.cs` — whose offence was reading `_settings.Load()` for the clock high-water,
        //    twenty lines from an unrelated use of the gate. ⭐ Two lessons, both worth more than the guard:
        //    a rule bounded by "appears in the same FILE" is not bounded by anything, and the test filter I
        //    used (`~License`) never matched `Licensing…`, so it had been failing unnoticed.
        //
        // ⭐ Stated positively instead: the POLICY FILE takes exactly one input — the compile-time symbol —
        //    and nowhere else may declare or assign the value. A consumer is free to read it beside whatever
        //    else it does, because reading it is the entire point.
        // ⚠⚠ COMMENTS ARE STRIPPED FIRST, and that is load-bearing rather than tidy. The first run of this
        //    guard failed on `LicensingPolicy.cs` itself — because its documentation SPELLS OUT the rule
        //    ("not a setting, not an environment variable, not a command-line argument"). ⭐ A guard that
        //    fires on the documentation of its own rule is a guard that gets suppressed, and a suppressed
        //    guard reads as coverage while providing none. The License Manager's theme tests learned the
        //    identical lesson in L3; this is the second time, so it is a pattern rather than an accident.
        var policy = StripComments(PolicySource);
        var runtimeInputs = new Regex(
            @"Environment\.|args\b|CommandLine|Preferences|Settings|Load\(|File\.|Registry",
            RegexOptions.IgnoreCase);

        Assert.False(runtimeInputs.IsMatch(policy),
            "LicensingPolicy.cs consults something other than the build configuration. The only input this "
            + "file may have is the DEBUG symbol.");

        var declarations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                string.Equals(Path.GetFileName(file), "LicensingPolicy.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var text = StripComments(File.ReadAllText(file));

            // ⛔ Nothing outside the policy file may DECLARE or ASSIGN it. Reading it is fine.
            if (Regex.IsMatch(text, @"GateEnabled\s*=") || text.Contains("bool GateEnabled", StringComparison.Ordinal))
            {
                declarations.Add(Path.GetRelativePath(Root, file));
            }
        }

        Assert.True(declarations.Count == 0,
            "The licensing gate is declared or assigned outside LicensingPolicy.cs: "
            + string.Join(", ", declarations));
    }

    [Fact]
    public void NoProjectFileSmugglesDebugIntoAnotherConfiguration()
    {
        // ⭐⭐ THE CHEAPEST AND LEAST OBVIOUS OF THE FOUR. The other three reason about the DEBUG symbol;
        //    this one is the only thing that checks WHO DEFINES IT. A `<DefineConstants>$(DefineConstants);
        //    DEBUG</DefineConstants>` in a Release property group turns the gate off in a shipped binary
        //    while every other guard here stays green.
        var offenders = new List<string>();

        var files = Directory
            .EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(Directory.EnumerateFiles(Root, "Directory.Build.props", SearchOption.AllDirectories))
            .ToList();

        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            foreach (Match group in Regex.Matches(
                         text, @"<PropertyGroup(?<attributes>[^>]*)>(?<body>.*?)</PropertyGroup>",
                         RegexOptions.Singleline))
            {
                var body = group.Groups["body"].Value;
                if (!Regex.IsMatch(body, @"<DefineConstants>[^<]*\bDEBUG\b"))
                {
                    continue;
                }

                // A DEBUG define is legitimate only inside a group conditioned on the Debug configuration.
                var condition = group.Groups["attributes"].Value;
                if (!condition.Contains("'Debug'", StringComparison.Ordinal) &&
                    !condition.Contains("Debug|", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetRelativePath(Root, file)}: {condition.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "DEBUG is defined outside a Debug-conditioned PropertyGroup, which would ship a Release build "
            + "with the licensing gate disabled: " + string.Join("; ", offenders));
    }

    /// <summary>Source with comments removed — see the note in the one-input guard for why this exists.</summary>
    private static string StripComments(string source) => Regex.Replace(
        Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
        @"//.*$", string.Empty, RegexOptions.Multiline);

    internal static string RepositoryRoot()
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EmberTern.App;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⛔⛔ <b>The private signing key must never reach a customer machine.</b> It is the one asset in this
/// system whose compromise is unrecoverable (§25.1), so the rule is enforced six different ways rather
/// than once.
///
/// <para>⭐ <b>The redundancy is the design, not belt-and-braces nervousness.</b> Each test below fails
/// for a different reason, and each catches a mistake the others cannot: a project reference added in
/// haste, a key pasted into a source file, a keystore copied into the output folder, a signing method
/// quietly exposed on the shared assembly. Any one of them alone would leave a route open.</para>
///
/// <para>⚠ <b>This file must never reference <c>EmberTern.Licensing.Issuing</c>.</b> That is not
/// squeamishness — <see cref="TheShippedOutputContainsNoIssuingAssembly"/> works by looking at what is
/// actually in the build output, and a reference from here would put the issuing assembly there and turn
/// the strongest of these tests into a tautology.</para>
/// </summary>
public sealed class PrivateKeyNeverShipsTests
{
    private const string IssuingAssembly = "EmberTern.Licensing.Issuing";

    [Fact]
    public void TheShippedOutputContainsNoIssuingAssembly()
    {
        // ⭐ The bluntest and most valuable of the six: whatever anyone intended, an assembly that is not
        //    in the output cannot ship. It is also transitive for free — the SDK copies the whole closure
        //    into this folder, so a reference anywhere in the graph would show up right here.
        var outputFolder = Path.GetDirectoryName(typeof(AppInfo).Assembly.Location)!;

        var offenders = Directory
            .EnumerateFiles(outputFolder, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name!.Contains("Issuing", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Signing-capable assemblies in the build output: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheShippedOutputContainsNoKeyMaterial()
    {
        var outputFolder = Path.GetDirectoryName(typeof(AppInfo).Assembly.Location)!;
        string[] patterns = ["*.etkeys", "*.pem", "*.key", "*.pfx", "*.p12"];

        var offenders = patterns
            .SelectMany(p => Directory.EnumerateFiles(outputFolder, p, SearchOption.AllDirectories))
            .Select(f => Path.GetRelativePath(outputFolder, f))
            .ToList();

        Assert.True(offenders.Count == 0, "Key-shaped files in the build output: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheEmberTernSolutionDoesNotContainTheIssuingProject()
    {
        // ⭐ A solution-level guard, which is a different thing from an assembly-level one: it fails at the
        //    moment someone adds the project "just to debug it", which is before any reference exists and
        //    therefore before every other test here would notice.
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot(), "EmberTern.slnx"));

        Assert.DoesNotContain(IssuingAssembly, solution, StringComparison.Ordinal);
    }

    [Fact]
    public void NoProjectInTheEmberTernSolutionReferencesIssuing()
    {
        var root = RepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "EmberTern.slnx"));

        var offenders = new List<string>();
        foreach (Match match in Regex.Matches(solution, @"Path=""([^""]+\.csproj)"""))
        {
            var projectPath = Path.Combine(root, match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(projectPath) &&
                File.ReadAllText(projectPath).Contains(IssuingAssembly, StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(root, projectPath));
            }
        }

        Assert.True(offenders.Count == 0, "Projects referencing the issuer: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NoShippedSourceUsesAPrivateKeyOrSigningApi()
    {
        // ⚠ Matched as ORDINAL, EXACT tokens — never a case-insensitive search for "private key". The
        //    shipped sources contain prose warning against pasting a private key, and a fuzzy matcher
        //    would flag the warning itself. A guard that cries wolf gets suppressed, and then it is gone.
        string[] forbidden =
        [
            "-----BEGIN PRIVATE KEY-----",
            "-----BEGIN EC PRIVATE KEY-----",
            "-----BEGIN RSA PRIVATE KEY-----",
            "ImportPkcs8PrivateKey",
            "ImportECPrivateKey",
            "ExportPkcs8PrivateKey",
            "ExportECPrivateKey",
            "SignData",
            "SignHash",
            ".etkeys",
        ];

        var root = RepositoryRoot();
        var offenders = new List<string>();

        foreach (var folder in new[] { "EmberTern.App", "EmberTern.Licensing" })
        {
            var directory = Path.Combine(root, "src", folder);
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var token in forbidden.Where(t => text.Contains(t, StringComparison.Ordinal)))
                {
                    offenders.Add($"{Path.GetRelativePath(root, file)}: {token}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Signing or private-key APIs in shipped source: " +
            string.Join("; ", offenders));
    }

    [Fact]
    public void ThePublicApiOfTheVerifierCannotSign()
    {
        // ⭐ Keyed on TYPES, not on names. A name check would flag SignatureAlgorithm and
        //    SignatureAlgorithmIds — which are exactly the harmless members the shared assembly must
        //    expose — and a guard tuned to ignore those would be tuned to ignore the real thing too.
        var offenders = new List<string>();

        foreach (var type in typeof(LicenseVerifier).Assembly.GetExportedTypes())
        {
            foreach (var method in type.GetMethods(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                         BindingFlags.DeclaredOnly))
            {
                var types = method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType);
                if (types.Any(IsAsymmetricKey))
                {
                    offenders.Add($"{type.Name}.{method.Name}");
                }
            }

            foreach (var property in type.GetProperties(
                         BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                         BindingFlags.DeclaredOnly).Where(p => IsAsymmetricKey(p.PropertyType)))
            {
                offenders.Add($"{type.Name}.{property.Name}");
            }
        }

        Assert.True(offenders.Count == 0,
            "The verifier's public API exposes key objects: " + string.Join(", ", offenders));
    }

    private static bool IsAsymmetricKey(Type type) =>
        typeof(AsymmetricAlgorithm).IsAssignableFrom(type) || type == typeof(ECParameters);

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

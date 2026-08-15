using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using EmberTern.Licensing;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐ <b>The machine-checkable form of the offline-first decision (D1).</b>
///
/// <para>The brief's central requirement is that EmberTern never contacts anything to run. A policy
/// saying "we do not call the network" is worth what the next person's memory is worth; this asserts the
/// stronger and permanent version — <b>there is no network call to make</b>, because the licensing
/// assembly does not reference a single networking type. It will still be true in 2031 when nobody
/// remembers this conversation.</para>
///
/// <para>⚠ When V2 adds online activation, that code goes into a DIFFERENT assembly. ⛔ Do not relax this
/// test to accommodate it — the whole point is that the thing which decides whether the product runs is
/// incapable of talking to anyone.</para>
/// </summary>
public sealed class LicensingMakesNoNetworkCallsTests
{
    private static readonly string AssemblyPath = typeof(LicenseVerifier).Assembly.Location;

    [Fact]
    public void TheLicensingAssemblyReferencesNoNetworkingType()
    {
        var offenders = ReferencedNamespaces()
            .Where(ns => ns.StartsWith("System.Net", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, "Networking namespaces referenced: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheLicensingAssemblyReferencesNoNetworkingAssembly()
    {
        var offenders = ReferencedAssemblies()
            .Where(name => name.StartsWith("System.Net", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, "Networking assemblies referenced: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheLicensingAssemblyDoesNoFileSystemIo()
    {
        // ⭐ Not tidiness — a separation of powers. Whether a file exists, where it lives and what the
        //    clock says are the HOST's knowledge; this assembly only decides what a given set of bytes
        //    means. That is what makes every state reachable in a test without a disk or a clock.
        var offenders = ReferencedNamespaces()
            .Where(ns => ns == "System.IO" || ns.StartsWith("System.IO.", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        Assert.True(offenders.Count == 0, "File-system namespaces referenced: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheLicensingAssemblyDependsOnNoOtherEmberTernAssembly()
    {
        // ⭐ It is shared with a SECOND application (the License Manager). A reference to Core or App
        //    here would drag EmberTern's world into a tool that is not EmberTern — and it is why the
        //    verdict travels as a closed enum rather than Core's MessageKey (§9.1).
        var offenders = ReferencedAssemblies()
            .Where(name => name.StartsWith("EmberTern", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, "EmberTern assemblies referenced: " + string.Join(", ", offenders));
    }

    [Fact]
    public void TheLicensingAssemblyHasNoThirdPartyDependency()
    {
        // ⭐ With TreatWarningsAsErrors escalating NU1902/NU1903, a third-party package on the
        //    verification path means a future CVE FAILS EMBERTERN'S BUILD — on the one code path that
        //    must be boring and stationary for a decade (§15.1). This is what decision D10 bought.
        var offenders = ReferencedAssemblies()
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) &&
                           !name.Equals("netstandard", StringComparison.Ordinal) &&
                           !name.Equals("mscorlib", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, "Non-framework assemblies referenced: " + string.Join(", ", offenders));
    }

    private static IEnumerable<string> ReferencedNamespaces()
    {
        using var stream = File.OpenRead(AssemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.TypeReferences)
        {
            var typeReference = reader.GetTypeReference(handle);
            if (!typeReference.Namespace.IsNil)
            {
                yield return reader.GetString(typeReference.Namespace);
            }
        }
    }

    private static IEnumerable<string> ReferencedAssemblies()
    {
        using var stream = File.OpenRead(AssemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.AssemblyReferences)
        {
            yield return reader.GetString(reader.GetAssemblyReference(handle).Name);
        }
    }
}

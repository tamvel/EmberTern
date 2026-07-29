using System;
using System.Reflection;

namespace EmberTern.App;

/// <summary>
/// The application's own identity, read from the built assembly — product name, version, author and
/// copyright. It exists so that <b>no release ever requires a code change</b>: every value here comes from the
/// one <c>PropertyGroup</c> in <c>Directory.Build.props</c>, so bumping <c>&lt;Version&gt;</c> is the whole of
/// shipping a new version as far as the UI is concerned.
///
/// <para>⛔ <b>Never hard-code a version number anywhere in the app</b> — not in a view, not in
/// <see cref="UiStrings"/>, and not as a fallback here. A second copy is not a duplicate so much as a value
/// that goes stale silently, which is the same failure mode a hand-typed keyboard gesture had (gotcha #284).
/// <c>AppInfoTests</c> enforces both halves: that these values agree with the props file, and that the current
/// version's text appears nowhere under <c>src/</c>.</para>
///
/// <para>⚠ <b>It reads THIS assembly, deliberately, not <see cref="Assembly.GetEntryAssembly"/>.</b> Under a
/// test host the entry assembly is the test runner, so an About window built on <c>GetEntryAssembly</c> would
/// quietly report vstest's version in every test — passing while measuring the wrong thing.</para>
/// </summary>
public static class AppInfo
{
    private static readonly Assembly Self = typeof(AppInfo).Assembly;

    /// <summary>
    /// The product version as a user reads it — <c>major.minor.patch</c>.
    ///
    /// <para>⚠ Truncated at <c>'+'</c>: since .NET 8 the SDK appends the source-revision hash to
    /// <see cref="AssemblyInformationalVersionAttribute"/>, so the raw attribute can read
    /// <c>&lt;version&gt;+9a3f2c1…</c>. <c>Directory.Build.props</c> also switches that off; this is the
    /// second, independent defence, and the one that survives someone removing the first.</para>
    ///
    /// <para>⚠ Note that no comment in this file spells the current version either — a stale comment teaches
    /// the next reader something false. <c>AppInfoTests.NoVersionNumberIsHardCodedInTheApp</c> caught exactly
    /// that when these very doc comments quoted the number as an example, which is a fair measure of how
    /// easily such a copy appears.</para>
    ///
    /// <para>Falls back to the assembly version's three parts, then to empty — never to a literal, because a
    /// literal fallback is exactly the stale copy this class exists to prevent.</para>
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>The product name — <c>&lt;Product&gt;</c>. Falls back to the app title, which is a name
    /// rather than a version, so it cannot go stale in the way a number would.</summary>
    public static string Product { get; } =
        Self.GetCustomAttribute<AssemblyProductAttribute>()?.Product is { Length: > 0 } p
            ? p
            : UiStrings.AppTitle;

    /// <summary>The author — <c>&lt;Company&gt;</c>, which is the slot Windows shows in a file's properties.</summary>
    public static string Author { get; } =
        Self.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;

    /// <summary>The copyright notice — <c>&lt;Copyright&gt;</c>.</summary>
    public static string Copyright { get; } =
        Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    private static string ResolveVersion()
    {
        var informational = Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (informational is { Length: > 0 })
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return Self.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : string.Empty;
    }
}

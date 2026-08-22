using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace EmberTern.LicenseManager;

/// <summary>
/// The License Manager's own identity, read back from the built assembly — product name, version,
/// author and copyright.
///
/// <para>⭐ <b>Every value comes from the one <c>PropertyGroup</c> in <c>Directory.Build.props</c></b>
/// (plus the <c>&lt;Product&gt;</c> this project overrides), so releasing a new version means editing that
/// file and nothing else. ⛔ <b>Never write a version number in code</b> — not in a view, not in a catalog,
/// not as a fallback. <c>ManagerInfoTests</c> enforces both halves: that these values agree with the props
/// file, and that the current version's text appears nowhere under this project.</para>
///
/// <para>⚠⚠ <b>It is a deliberate MIRROR of <c>EmberTern.App.AppInfo</c>, and the duplication is of the
/// MECHANISM only — never of a value.</b> Both classes read the attributes MSBuild composes from the same
/// <c>Directory.Build.props</c>, so they cannot disagree about the version or the release date; what is
/// copied is thirty lines of reflection. Three alternatives were measured and rejected:
/// <list type="bullet">
///   <item>referencing <c>EmberTern.App</c> — ⛔ this solution must never acquire the product assembly;</item>
///   <item><c>&lt;Compile Include="..\EmberTern.App\AppInfo.cs"&gt;</c> — it would put a type in namespace
///   <c>EmberTern.App</c> inside this assembly, which is a lie about the architecture, and it needs an edit
///   to a product file to compile at all (its fallback reads <c>UiStrings</c>);</item>
///   <item>a new shared project for forty lines — an abstraction with no second reason to exist.</item>
/// </list>
/// ⭐ The precedent is stated in this project already: <c>ThemeToggleIconConverter</c> is mirrored, with its
/// reason, because it lives in an assembly this solution must not reference.</para>
///
/// <para>⚠ <b>It reads THIS assembly, deliberately, not <see cref="Assembly.GetEntryAssembly"/>.</b> Under
/// a test host the entry assembly is the test runner, so an About window built on <c>GetEntryAssembly</c>
/// would quietly report vstest's version in every test — passing while measuring the wrong thing.</para>
/// </summary>
public static class ManagerInfo
{
    private static readonly Assembly Self = typeof(ManagerInfo).Assembly;

    /// <summary>
    /// The version as an operator reads it — <c>major.minor.patch</c>.
    ///
    /// <para>⚠ Truncated at <c>'+'</c>: since .NET 8 the SDK appends the source-revision hash to
    /// <see cref="AssemblyInformationalVersionAttribute"/>. <c>Directory.Build.props</c> also switches that
    /// off; this is the second, independent defence — the one that survives someone removing the first.</para>
    ///
    /// <para>⚠ No comment in this file spells the current version either: a stale comment teaches the next
    /// reader something false, and the guard sweeps comments too.</para>
    /// </summary>
    public static string Version { get; } = ResolveVersion();

    /// <summary>
    /// The product name — <c>&lt;Product&gt;</c>, which this project overrides to name itself rather than
    /// the product it administers.
    /// </summary>
    /// <remarks>
    /// ⭐ Falls back to the ASSEMBLY NAME, which is a technical identifier rather than a word — so the
    /// fallback needs no catalog entry and cannot go stale. ⛔ Not a literal: branding is exempt from
    /// localization (`terminology.md` §4.4), but a literal here would still be a second source of a name
    /// the build already declares.
    /// </remarks>
    public static string Product { get; } =
        Self.GetCustomAttribute<AssemblyProductAttribute>()?.Product is { Length: > 0 } product
            ? product
            : Self.GetName().Name ?? string.Empty;

    /// <summary>The author — <c>&lt;Company&gt;</c>, the slot Windows shows in a file's properties.</summary>
    public static string Author { get; } =
        Self.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company ?? string.Empty;

    /// <summary>The copyright notice — <c>&lt;Copyright&gt;</c>.</summary>
    public static string Copyright { get; } =
        Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    /// <summary>
    /// When this version was released, or <see langword="null"/> when the build did not declare it.
    ///
    /// <para>There is no standard assembly attribute for a release date, so it travels as
    /// <see cref="AssemblyMetadataAttribute"/> — which keeps it in the same single source as the version
    /// instead of becoming a date typed into a view. Stored ISO, formatted for display by the caller.</para>
    /// </summary>
    public static DateOnly? ReleaseDate { get; } = ResolveReleaseDate();

    private static string ResolveVersion()
    {
        var informational = Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (informational is { Length: > 0 })
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
        }

        return Self.GetName().Version is { } version
            ? string.Create(CultureInfo.InvariantCulture, $"{version.Major}.{version.Minor}.{version.Build}")
            : string.Empty;
    }

    private static DateOnly? ResolveReleaseDate()
    {
        var raw = Self.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "ReleaseDate", StringComparison.Ordinal))
            ?.Value;

        // ⚠ Parsed strictly under the invariant culture: the value is ISO by construction, and a date we
        //   cannot read is reported as ABSENT rather than guessed — the view then hides the line instead of
        //   showing a label with nothing after it.
        return DateOnly.TryParseExact(
            raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}

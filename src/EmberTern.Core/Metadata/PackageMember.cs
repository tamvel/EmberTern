namespace EmberTern.Core.Metadata;

/// <summary>The kind of a packaged routine — drives the Members tab grouping
/// (Functions / Procedures) and the navigation keyword (FUNCTION / PROCEDURE).</summary>
public enum PackageMemberKind
{
    Function,
    Procedure,
}

/// <summary>
/// One routine declared in a Firebird package. Sourced from the catalog
/// (<c>RDB$FUNCTIONS</c> / <c>RDB$PROCEDURES</c> rows carrying
/// <c>RDB$PACKAGE_NAME</c>), not by parsing the package source.
/// </summary>
public sealed record PackageMember(string Name, PackageMemberKind Kind);

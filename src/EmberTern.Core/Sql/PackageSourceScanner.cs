using EmberTern.Core.Metadata;

namespace EmberTern.Core.Sql;

/// <summary>
/// Locates a packaged routine's declaration/implementation within package source
/// text, for the Members tab's "jump to it" navigation. NOT a parser — the member
/// LIST comes from the catalog (<see cref="Metadata.PackageMember"/>); this only
/// finds the offset of a known member's <c>FUNCTION name</c> / <c>PROCEDURE name</c>
/// token, reusing the shared <see cref="SqlScanHelpers"/> scan primitives (skips
/// string literals, quoted identifiers, and comments; whole-token, case-insensitive).
/// </summary>
public static class PackageSourceScanner
{
    /// <summary>Returns the offset of the member's NAME token in
    /// <paramref name="text"/> (immediately after the <c>FUNCTION</c>/<c>PROCEDURE</c>
    /// keyword), or -1 when not found.</summary>
    public static int FindMemberOffset(string? text, PackageMemberKind kind, string? name)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name)) return -1;
        var keyword = kind == PackageMemberKind.Function ? "FUNCTION" : "PROCEDURE";

        int i = 0;
        var s = text!;
        while (i < s.Length)
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (i >= s.Length) break;
            if (SqlScanHelpers.TrySkipQuoted(s, ref i)) continue;
            if (!SqlScanHelpers.IsIdentifierChar(s[i])) { i++; continue; }

            int wordStart = i;
            var word = SqlScanHelpers.ReadWord(s, ref i);
            if (!string.Equals(word, keyword, System.StringComparison.OrdinalIgnoreCase)) continue;

            // The next identifier token is the routine name.
            int afterKeyword = i;
            SqlScanHelpers.SkipTrivia(s, ref i);
            int nameStart = i;
            var memberName = SqlScanHelpers.ReadIdentifier(s, ref i);
            if (memberName is not null
                && string.Equals(memberName, name, System.StringComparison.OrdinalIgnoreCase))
            {
                return nameStart;
            }
            // Not our member — resume scanning after the keyword (the name token was
            // already consumed, so continue from the current cursor).
            if (i <= afterKeyword) i = afterKeyword;
        }
        return -1;
    }
}

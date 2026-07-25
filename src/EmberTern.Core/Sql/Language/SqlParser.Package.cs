using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language;

// Stage X / D11 (packages) — the package BODY sub-parser. Firebird stores a package body as ONE source blob
// (RDB$PACKAGE_BODY_SOURCE): a `BEGIN <member routines> END` where each member is
//     [PROCEDURE|FUNCTION] name (params) [RETURNS …] AS <declarations> BEGIN … END
// i.e. structurally a D9 local sub-routine WITHOUT the leading DECLARE keyword (packages have no
// package-level variables — spec §8.2 — so the body is only routine declarations). Extracting an individual
// routine is therefore real parsing, not a lookup (implementation plan D11): this turns the blob into member
// SubroutineDeclaration nodes so the debugger can build a frame from a member's Body and read its signature
// with the SAME D9 machinery (ParseScopedBlockBody / PsqlDeclarationExtractor.ExtractSignature), rather than a
// hand-rolled scanner. Additive + pure (string → AST, no server, no metadata); never throws.
//
// Private-ness is NOT a parse fact (the body shows every member the same way); it is a metadata fact
// (RDB$PRIVATE_FLAG) the executor supplies (Seam B). This parser only structures the source.
public static partial class SqlParser
{
    /// <summary>Parses a package body source blob (<c>RDB$PACKAGE_BODY_SOURCE</c> — a
    /// <c>BEGIN … END</c> of member routine declarations) into its member sub-routines, in source order. Each
    /// member is a <see cref="SubroutineDeclaration"/> whose spans index into <paramref name="bodySource"/>
    /// (so a debugger frame can use the blob as its source). Returns an empty list for null/blank input;
    /// never throws.</summary>
    public static IReadOnlyList<SubroutineDeclaration> ParsePackageBodyMembers(string? bodySource)
    {
        if (string.IsNullOrWhiteSpace(bodySource)) return Array.Empty<SubroutineDeclaration>();

        // Significant tokens (trivia — whitespace/comments — is attached, not emitted; drop the EOF sentinel),
        // mirroring SqlParser.Parse. Spans stay relative to bodySource.
        var tokens = SqlLexer.Tokenize(bodySource!);
        var sig = new List<SqlToken>(tokens.Count);
        foreach (var t in tokens)
        {
            if (t.Kind != TokenKind.EndOfFile) sig.Add(t);
        }

        // Enter the body's outer BEGIN (the first one); the members follow it up to the matching outer END.
        // Defensive: if there is no BEGIN (mid-edit / unexpected shape), scan from the top.
        int i = 0;
        for (int k = 0; k < sig.Count; k++)
        {
            if (IsBodyWord(sig[k], "BEGIN")) { i = k + 1; break; }
        }

        var members = new List<SubroutineDeclaration>();
        while (i < sig.Count)
        {
            if (IsBodyWord(sig[i], "END")) break; // the body's closing END — done
            if (IsPackageMemberStart(sig, i))
            {
                int before = i;
                members.Add(ParseSubroutineDeclaration(sig, ref i)); // reuses the D9 sub-routine body parser
                if (i == before) i++; // never stall (defensive)
            }
            else
            {
                i++; // skip anything that is not a member declaration (lossless — we only collect members)
            }
        }
        return members;
    }

    // A package body member begins with a bare PROCEDURE/FUNCTION keyword (no DECLARE — that is the D9 local
    // form). ParseSubroutineDeclaration handles both leading forms.
    private static bool IsPackageMemberStart(IReadOnlyList<SqlToken> sig, int i)
        => IsBodyWord(sig[i], "PROCEDURE") || IsBodyWord(sig[i], "FUNCTION");

    /// <summary>Reconstructs a package member's standalone <c>CREATE PROCEDURE/FUNCTION</c> source from the
    /// package body blob (Stage X / D11): <c>"CREATE "</c> + the member's own <c>PROCEDURE/FUNCTION … AS … END</c>
    /// slice, the D8-reusable form so a member parses + frames exactly like a stored routine. Returns null when
    /// no runnable member of that kind + name exists. This is the <b>single owner</b> of the reconstruction — the
    /// executor's step-into (with pre-parsed <paramref name="members"/>) and the App's root-launch source provider
    /// (the blob overload below) both go through it, so there is no parallel implementation.</summary>
    public static string? ReconstructPackageMemberSource(
        string bodySource, IReadOnlyList<SubroutineDeclaration> members, string memberName, SubroutineKind kind)
    {
        if (bodySource is null || members is null) return null;
        foreach (var m in members)
        {
            if (m.Kind == kind && m.Body is not null
                && string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase))
            {
                return "CREATE " + bodySource.Substring(m.Start, m.Length);
            }
        }
        return null;
    }

    /// <summary>Convenience overload for callers that have only the raw body blob (the App / probe root-launch
    /// path): parses the members via <see cref="ParsePackageBodyMembers"/> then reconstructs. Returns null for
    /// null/blank input or a missing member.</summary>
    public static string? ReconstructPackageMemberSource(string? bodySource, string memberName, SubroutineKind kind)
        => string.IsNullOrWhiteSpace(bodySource)
            ? null
            : ReconstructPackageMemberSource(bodySource!, ParsePackageBodyMembers(bodySource), memberName, kind);
}

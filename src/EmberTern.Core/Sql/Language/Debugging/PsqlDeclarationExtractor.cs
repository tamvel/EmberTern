using System;
using System.Collections.Generic;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Debugging;

/// <summary>One frame-local variable declaration lifted from a routine body — the pieces the harness needs
/// to honour the §3.4 rules for a local. <see cref="Verbatim"/> is the declaration copied 1:1 from source
/// (R3 — domain / <c>NOT NULL</c> / <c>CHECK</c> / default preserved, so the statement's own assignments keep
/// domain semantics); <see cref="TypeSpec"/> is the declared type portion (the tokens after the name, before
/// any <c>NOT NULL</c>/<c>CHECK</c>/<c>DEFAULT</c>/<c>=</c>) — the input the base-type resolver maps to a base
/// type for the harness parameter / <c>RETURNS</c> column (R2). <see cref="Name"/> is the folded variable
/// name, matching the frame's variable store.</summary>
public sealed record LocalDeclaration(string Name, string Verbatim, string TypeSpec);

/// <summary>The declarations in scope for a routine frame (spec §3.4/§3.5): the frame's local variables
/// (verbatim, R3) and its in-scope local sub-routine declarations (verbatim, R5). <see cref="Locals"/> is the
/// body's <c>DECLARE VARIABLE</c> section; <see cref="SubRoutines"/> is its <c>DECLARE PROCEDURE/FUNCTION</c>
/// local sub-routines (Stage X / D9), carried 1:1 so a call in this frame binds to the local, not a like-named
/// global. Empty for a routine that declares no sub-routines.</summary>
public sealed record RoutineDeclarations(
    IReadOnlyList<LocalDeclaration> Locals,
    IReadOnlyList<string> SubRoutines);

/// <summary>
/// Extracts a routine frame's variable declarations from its parsed body (Stage X / D2 seam c). Pure Core:
/// a function of the <see cref="BlockStatement"/> body plus the routine <c>source</c> text (needed to slice
/// spans verbatim — the AST stores spans, never the string). It consumes the AST the parser already built
/// (Architecture rule #1 — never re-parse): the frame variables are the body's <c>DECLARE VARIABLE</c>
/// section (<see cref="BlockStatement.Declarations"/>). The Firebird executor (seam c) adds the base type
/// (R2, from metadata) and current values (from the frame) around these declarations.
/// <para>
/// <b>Two declaration kinds, both verbatim.</b> Local variables come from
/// <see cref="BlockStatement.Declarations"/> (the <c>DECLARE VARIABLE</c> section, R3); local sub-routines come
/// from <see cref="BlockStatement.LocalRoutines"/> (the <c>DECLARE PROCEDURE/FUNCTION</c> declarations, R5 —
/// Stage X / D9), each carried 1:1 into <see cref="RoutineDeclarations.SubRoutines"/> so a call in this frame
/// binds to the local, never a like-named global (a §F violation). The base-type derivation the harness needs
/// around each declaration is the Firebird layer's concern (R2, from metadata); this stays pure verbatim
/// extraction. A <c>DECLARE … CURSOR</c> is skipped (the Cursor Bridge's concern, D6) — a routine that steps a
/// <c>FOR SELECT</c> hits the executor's cursor boundary, not this extractor.
/// </para>
/// </summary>
public static class PsqlDeclarationExtractor
{
    /// <summary>Extracts the frame declarations of <paramref name="body"/> from <paramref name="source"/>
    /// (the routine's source text — the span backing).</summary>
    public static RoutineDeclarations Extract(BlockStatement body, string source)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(source);

        var locals = new List<LocalDeclaration>();
        foreach (var d in body.Declarations)
        {
            if (d is DeclareVariableStatement v && !string.IsNullOrEmpty(v.Name))
            {
                locals.Add(new LocalDeclaration(v.Name!, Slice(source, v), TypeSpecOf(v, source)));
            }
        }

        // R5: every in-scope local sub-routine, carried VERBATIM (header + body) — the parser groups these
        // into BlockStatement.LocalRoutines (Stage X / D9), out of Declarations. The harness re-declares each
        // one 1:1 so a statement in this frame that calls a local F()/P() resolves to the local, never a
        // like-named global (a §F violation). The base-type derivation + per-sub-routine frame layout the
        // callee frames need live in the Firebird layer (D9 seam b), not here — this is the pure verbatim
        // carry, mirroring how a local variable is carried verbatim.
        var subRoutines = new List<string>();
        foreach (var r in body.LocalRoutines)
        {
            subRoutines.Add(Slice(source, r));
        }
        return new RoutineDeclarations(locals, subRoutines);
    }

    /// <summary>The declared type portion of a <c>DECLARE [VARIABLE] name &lt;type&gt; …</c> — the source
    /// between the name and the first of <c>NOT</c>/<c>CHECK</c>/<c>DEFAULT</c>/<c>COLLATE</c>/<c>=</c> or the
    /// terminator, paren-aware so a parametrised type (<c>NUMERIC(15,2)</c>, <c>VARCHAR(80)</c>) is captured
    /// whole. A domain name (<c>D_AMOUNT</c>) comes back as a bare identifier; a <c>TYPE OF …</c> comes back
    /// with its keywords — the base-type resolver decides how to resolve each (R2). Returns "" when the
    /// declaration is too short to have a type (mid-edit).</summary>
    public static string TypeSpecOf(DeclareVariableStatement decl, string source)
    {
        ArgumentNullException.ThrowIfNull(decl);
        ArgumentNullException.ThrowIfNull(source);

        var toks = decl.Tokens;
        int i = 0;
        if (i < toks.Count && IsWord(toks[i], "DECLARE")) i++;
        if (i < toks.Count && IsWord(toks[i], "VARIABLE")) i++;
        if (i >= toks.Count) return string.Empty;
        i++; // consume the name token

        int typeStart = i;
        int typeEndExclusive = i;
        int depth = 0;
        for (; i < toks.Count; i++)
        {
            var t = toks[i];
            if (t.Kind == TokenKind.LParen) { depth++; typeEndExclusive = i + 1; continue; }
            if (t.Kind == TokenKind.RParen) { if (depth > 0) depth--; typeEndExclusive = i + 1; continue; }
            if (depth == 0)
            {
                if (t.Kind == TokenKind.Semicolon) break;
                if (IsWord(t, "NOT") || IsWord(t, "CHECK") || IsWord(t, "DEFAULT") || IsWord(t, "COLLATE")) break;
                if (t.Kind == TokenKind.Operator && t.Text == "=") break;
            }
            typeEndExclusive = i + 1;
        }
        if (typeEndExclusive <= typeStart) return string.Empty;

        int s = toks[typeStart].Start;
        int e = toks[typeEndExclusive - 1].End;
        if (s < 0 || e > source.Length || e <= s) return string.Empty;
        return source.Substring(s, e - s);
    }

    private static string Slice(string source, SqlNode node)
    {
        int start = Math.Clamp(node.Start, 0, source.Length);
        int len = Math.Clamp(node.Length, 0, source.Length - start);
        return source.Substring(start, len);
    }

    private static bool IsWord(SqlToken t, string keyword)
        => (t.Kind == TokenKind.Keyword || t.Kind == TokenKind.Identifier)
           && string.Equals(t.Text, keyword, StringComparison.OrdinalIgnoreCase);
}

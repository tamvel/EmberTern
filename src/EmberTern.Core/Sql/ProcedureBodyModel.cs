using System;
using System.Collections.Generic;
using System.Text;

namespace EmberTern.Core.Sql;

/// <summary>One local variable declared in a procedure body. <see cref="TypeText"/>
/// is the raw type spec verbatim (e.g. <c>VARCHAR(50)</c>, <c>NUMERIC(18,4)</c>, a
/// domain, or <c>TYPE OF COLUMN T.C</c>) — kept as free text so every Firebird type
/// form round-trips without modelling. Mutable so the editable grid round-trips it.</summary>
public sealed class ProcedureVariable
{
    public string Name { get; set; } = string.Empty;
    public string TypeText { get; set; } = string.Empty;
    public bool NotNull { get; set; }
    /// <summary>Initial value (the part after <c>=</c> / <c>DEFAULT</c>), or null.</summary>
    public string? Default { get; set; }
}

/// <summary>One local cursor. <see cref="Declaration"/> is the full
/// <c>DECLARE … CURSOR …;</c> statement text, edited verbatim in the cursor split
/// editor — preserves every Firebird cursor form (SCROLL, FOR, etc.) round-trip.</summary>
public sealed class ProcedureCursor
{
    public string Name { get; set; } = string.Empty;
    public string Declaration { get; set; } = string.Empty;
}

/// <summary>One local subprogram (FB3+). <see cref="Kind"/> is <c>PROCEDURE</c> or
/// <c>FUNCTION</c>; <see cref="Declaration"/> is the full declaration text (header +
/// body), edited verbatim in the subprogram split editor.</summary>
public sealed class ProcedureSubprogram
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "PROCEDURE";
    public string Declaration { get; set; } = string.Empty;
}

/// <summary>
/// Structured view of a procedure body (the text after <c>AS</c>): the DECLARE
/// section split into editable variables / cursors / subprograms, plus the
/// executable <c>BEGIN…END</c> block. This is the canonical model behind Easy mode —
/// edits to any element regenerate the DECLARE section deterministically
/// (<see cref="EmberTern.Core.Metadata.DdlGenerator.BuildProcedureBody"/>), so the
/// body editor is a projection of the model, never the source of truth.
/// </summary>
public sealed class ProcedureBodyModel
{
    public List<ProcedureVariable> Variables { get; } = new();
    public List<ProcedureCursor> Cursors { get; } = new();
    public List<ProcedureSubprogram> Subprograms { get; } = new();

    /// <summary>The executable block — from the top-level <c>BEGIN</c> through its
    /// matching <c>END</c>, verbatim. Empty only for a declaration-only fragment.</summary>
    public string ExecutableBody { get; set; } = string.Empty;
}

/// <summary>
/// Splits a procedure body into its <see cref="ProcedureBodyModel"/>: walks the
/// top-level DECLARE section (variables / cursors / subprograms) and keeps the
/// remaining <c>BEGIN…END</c> as the executable body. Pure + testable without a DB;
/// the inverse is <see cref="EmberTern.Core.Metadata.DdlGenerator.BuildProcedureBody"/>.
/// Round-trip safe: cursor/subprogram declarations are preserved verbatim and the
/// executable body is preserved verbatim; variables normalise to a canonical
/// <c>DECLARE VARIABLE</c> form (semantics preserved, idempotent on re-split).
/// </summary>
public static class ProcedureBodySplitter
{
    public static ProcedureBodyModel Split(string? body)
    {
        var model = new ProcedureBodyModel();
        if (string.IsNullOrWhiteSpace(body)) return model;
        var s = body!;
        int i = 0;
        int bodyStart = -1;

        while (i < s.Length)
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (i >= s.Length) break;
            if (SqlScanHelpers.TrySkipQuoted(s, ref i)) continue;
            if (!SqlScanHelpers.IsIdentifierChar(s[i])) { i++; continue; }

            int wordStart = i;
            var up = SqlScanHelpers.ReadWord(s, ref i).ToUpperInvariant();

            if (up == "BEGIN")
            {
                bodyStart = wordStart; // executable block starts here
                break;
            }
            if (up != "DECLARE")
            {
                bodyStart = wordStart; // left the declaration zone
                break;
            }

            SqlScanHelpers.SkipTrivia(s, ref i);
            int peek = i;
            var nextUp = SqlScanHelpers.ReadWord(s, ref peek).ToUpperInvariant();

            if (nextUp == "VARIABLE")
            {
                i = peek;
                SqlScanHelpers.SkipTrivia(s, ref i);
                var name = SqlScanHelpers.ReadIdentifier(s, ref i) ?? string.Empty;
                var rest = SqlScanHelpers.ReadUntilSemicolon(s, ref i);
                AddVariable(model, name, rest);
            }
            else if (nextUp == "PROCEDURE" || nextUp == "FUNCTION")
            {
                i = peek;
                SqlScanHelpers.SkipTrivia(s, ref i);
                var name = SqlScanHelpers.ReadIdentifier(s, ref i) ?? string.Empty;
                SkipSubprogramBody(s, ref i);
                model.Subprograms.Add(new ProcedureSubprogram
                {
                    Name = name,
                    Kind = nextUp == "FUNCTION" ? "FUNCTION" : "PROCEDURE",
                    Declaration = Slice(s, wordStart, i),
                });
            }
            else
            {
                // DECLARE <name> … ; — FB3 variable (no VARIABLE keyword) or a cursor.
                // 'i' still points at the name (only 'peek' advanced past it); read
                // the original-cased identifier from there.
                var name = SqlScanHelpers.ReadIdentifier(s, ref i) ?? string.Empty;
                var rest = SqlScanHelpers.ReadUntilSemicolon(s, ref i);
                if (SqlScanHelpers.ContainsWord(rest, "CURSOR"))
                {
                    model.Cursors.Add(new ProcedureCursor
                    {
                        Name = name,
                        Declaration = Slice(s, wordStart, i),
                    });
                }
                else
                {
                    AddVariable(model, name, rest);
                }
            }

            if (i <= wordStart) i = wordStart + 1; // never spin
        }

        model.ExecutableBody = bodyStart >= 0 ? s.Substring(bodyStart).Trim() : string.Empty;
        return model;
    }

    /// <summary>Extracts the cursor name from a <c>DECLARE [VARIABLE] name CURSOR …</c>
    /// declaration (the name after the optional <c>DECLARE</c>). Used to keep a
    /// cursor row's display name in sync as its declaration text is edited.</summary>
    public static string ParseCursorName(string? declaration)
    {
        var (_, name) = ParseLocalHeader(declaration, cursorMode: true);
        return name;
    }

    /// <summary>Extracts <c>(Kind, Name)</c> from a <c>DECLARE PROCEDURE|FUNCTION name …</c>
    /// declaration. Kind defaults to <c>PROCEDURE</c> when not recognised.</summary>
    public static (string Kind, string Name) ParseSubprogram(string? declaration)
    {
        var (kind, name) = ParseLocalHeader(declaration, cursorMode: false);
        return (string.IsNullOrEmpty(kind) ? "PROCEDURE" : kind, name);
    }

    /// <summary>True when a cursor declaration carries the <c>SCROLL</c> keyword
    /// (a scrollable cursor) — i.e. SCROLL appears before the CURSOR keyword.</summary>
    public static bool CursorIsScroll(string? declaration)
    {
        if (string.IsNullOrWhiteSpace(declaration)) return false;
        var s = declaration!;
        int i = 0;
        while (i < s.Length)
        {
            SqlScanHelpers.SkipTrivia(s, ref i);
            if (i >= s.Length) break;
            if (SqlScanHelpers.TrySkipQuoted(s, ref i)) continue;
            if (!SqlScanHelpers.IsIdentifierChar(s[i])) { i++; continue; }
            var w = SqlScanHelpers.ReadWord(s, ref i).ToUpperInvariant();
            if (w == "SCROLL") return true;
            if (w == "CURSOR") return false;
        }
        return false;
    }

    /// <summary>Rewrites a cursor declaration's header — the name and the optional
    /// <c>SCROLL</c> keyword — while preserving the <c>CURSOR FOR (…)</c> body. Used to
    /// keep the declaration in sync when the user edits the cursor's name / Scroll flag
    /// in the list. Returns null when the text isn't a recognisable cursor declaration.</summary>
    public static string? RewriteCursorHeader(string? declaration, string? newName, bool scroll)
    {
        if (string.IsNullOrWhiteSpace(declaration)) return null;
        var s = declaration!;
        int i = 0;
        SqlScanHelpers.SkipTrivia(s, ref i);
        if (!SqlScanHelpers.TryKeyword(s, ref i, "DECLARE")) return null;
        SqlScanHelpers.SkipTrivia(s, ref i);
        int p = i;
        if (SqlScanHelpers.TryKeyword(s, ref p, "VARIABLE")) { i = p; SqlScanHelpers.SkipTrivia(s, ref i); }
        var oldName = SqlScanHelpers.ReadIdentifier(s, ref i);
        if (string.IsNullOrEmpty(oldName)) return null;
        SqlScanHelpers.SkipTrivia(s, ref i);
        int sp = i;
        if (SqlScanHelpers.TryKeyword(s, ref sp, "SCROLL")) { i = sp; SqlScanHelpers.SkipTrivia(s, ref i); }
        else if (SqlScanHelpers.TryKeyword(s, ref sp, "NO"))
        {
            SqlScanHelpers.SkipTrivia(s, ref sp);
            if (SqlScanHelpers.TryKeyword(s, ref sp, "SCROLL")) { i = sp; SqlScanHelpers.SkipTrivia(s, ref i); }
        }
        int curStart = i;
        if (!SqlScanHelpers.TryKeyword(s, ref i, "CURSOR")) return null;
        var rest = s.Substring(curStart); // "CURSOR FOR (…)" onward, verbatim
        var nm = string.IsNullOrWhiteSpace(newName) ? oldName! : newName!.Trim();
        return "DECLARE " + nm + " " + (scroll ? "SCROLL " : string.Empty) + rest;
    }

    /// <summary>Rewrites a subprogram declaration's name while preserving its kind
    /// keyword, parameter list, and body. Returns null when the text isn't a
    /// recognisable <c>DECLARE PROCEDURE|FUNCTION</c> declaration.</summary>
    public static string? RewriteSubprogramName(string? declaration, string? newName)
    {
        if (string.IsNullOrWhiteSpace(declaration)) return null;
        var s = declaration!;
        int i = 0;
        SqlScanHelpers.SkipTrivia(s, ref i);
        if (!SqlScanHelpers.TryKeyword(s, ref i, "DECLARE")) return null;
        SqlScanHelpers.SkipTrivia(s, ref i);
        string kind;
        int p = i;
        if (SqlScanHelpers.TryKeyword(s, ref p, "FUNCTION")) { kind = "FUNCTION"; i = p; }
        else if (SqlScanHelpers.TryKeyword(s, ref p, "PROCEDURE")) { kind = "PROCEDURE"; i = p; }
        else return null;
        SqlScanHelpers.SkipTrivia(s, ref i);
        var oldName = SqlScanHelpers.ReadIdentifier(s, ref i);
        if (string.IsNullOrEmpty(oldName)) return null;
        var rest = s.Substring(i); // " (params) AS …" onward, verbatim
        var nm = string.IsNullOrWhiteSpace(newName) ? oldName! : newName!.Trim();
        return "DECLARE " + kind + " " + nm + rest;
    }

    private static (string Kind, string Name) ParseLocalHeader(string? declaration, bool cursorMode)
    {
        if (string.IsNullOrWhiteSpace(declaration)) return (string.Empty, string.Empty);
        var s = declaration!;
        int i = 0;
        SqlScanHelpers.SkipTrivia(s, ref i);
        SqlScanHelpers.TryKeyword(s, ref i, "DECLARE");
        SqlScanHelpers.SkipTrivia(s, ref i);

        if (cursorMode)
        {
            // Optional VARIABLE (rare for cursors) — skip, then the name.
            int peek = i;
            if (SqlScanHelpers.TryKeyword(s, ref peek, "VARIABLE")) { i = peek; SqlScanHelpers.SkipTrivia(s, ref i); }
            return (string.Empty, SqlScanHelpers.ReadIdentifier(s, ref i) ?? string.Empty);
        }

        string kind = string.Empty;
        int p2 = i;
        if (SqlScanHelpers.TryKeyword(s, ref p2, "FUNCTION")) { kind = "FUNCTION"; i = p2; }
        else if (SqlScanHelpers.TryKeyword(s, ref p2, "PROCEDURE")) { kind = "PROCEDURE"; i = p2; }
        SqlScanHelpers.SkipTrivia(s, ref i);
        return (kind, SqlScanHelpers.ReadIdentifier(s, ref i) ?? string.Empty);
    }

    private static void AddVariable(ProcedureBodyModel model, string name, string rest)
    {
        // Reuse the param-segment parser: "<name> <type> [NOT NULL] [= default]".
        var seg = (name + " " + rest).Trim();
        var p = ProcedureSignatureParser.ParseSegment(seg);
        if (p is null) return;
        model.Variables.Add(new ProcedureVariable
        {
            Name = p.Name,
            TypeText = p.TypeText,
            NotNull = p.NotNull,
            Default = p.DefaultValue,
        });
    }

    private static string Slice(string s, int start, int end)
        => start >= 0 && end <= s.Length && end > start ? s.Substring(start, end - start).Trim() : string.Empty;

    // Consumes a subprogram declaration through the close of its BEGIN…END body (and
    // an optional trailing ';') via the shared CASE-aware scanner — a CASE…END inside
    // the body no longer truncates the declaration on Source→Easy split.
    private static void SkipSubprogramBody(string s, ref int i)
        => SqlScanHelpers.SkipToEndOfBlock(s, ref i);
}

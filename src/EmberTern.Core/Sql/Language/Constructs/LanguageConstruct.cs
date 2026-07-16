using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Language.Constructs;

/// <summary>
/// One daily Firebird language construct that <b>Language Completion</b> finishes from a natural prefix
/// (design: <c>docs/design/editor-language-expansion.md</c>). There is no invented abbreviation — the
/// developer types the real leading characters of the construct and Tab completes it.
/// <list type="bullet">
///   <item><see cref="Spelling"/> — the construct's real, canonical (lowercase) spelling, matched
///   case-insensitively as a prefix (e.g. <c>group by</c>, <c>execute procedure</c>, <c>if</c>).</item>
///   <item><see cref="Expansion"/> — the exact minimal text inserted (Rule 0: nothing to delete). The
///   App applies the document's casing; the catalog stores it lowercase.</item>
///   <item><see cref="CaretOffset"/> — where the caret lands within <see cref="Expansion"/> after
///   insertion (e.g. inside the parens of <c>if () then</c>).</item>
/// </list>
/// Pure value; zero UI; no timing. This is declarative data — adding a construct is one catalog row,
/// never special-case code.
/// </summary>
/// <param name="Category">Which grammatical position the construct may begin in — the single fact the
/// arming gate needs (see <see cref="ConstructContext"/>). Declarative, per row.</param>
public sealed record LanguageConstruct(string Spelling, string Expansion, int CaretOffset, ConstructCategory Category);

/// <summary>
/// Where a construct may legally begin — the coarse, deterministic classification the arming gate uses
/// (design §5, kept simple: two buckets, decided by the previous significant token).
/// </summary>
public enum ConstructCategory
{
    /// <summary>Begins a statement / PSQL body statement (<c>if</c>, <c>select</c>, <c>insert into</c>,
    /// <c>declare variable</c>, …) — arms at a statement boundary.</summary>
    Statement,

    /// <summary>Continues a query with a clause (<c>where</c>, <c>group by</c>, <c>order by</c>,
    /// <c>union</c>, …) — arms after something that completes a table/expression.</summary>
    Clause,
}

/// <summary>
/// The result of resolving the editor text + caret against the catalog: the construct to expand and how
/// many characters immediately before the caret the developer already typed (the prefix the expansion
/// replaces). <see cref="PrefixLength"/> lets the App replace exactly what was typed and place the caret
/// at <see cref="LanguageConstruct.CaretOffset"/> relative to the insertion point.
/// </summary>
public sealed record ConstructMatch(LanguageConstruct Construct, int PrefixLength);

/// <summary>
/// The curated set of daily constructs — statements, clauses, control-flow, and the few multi-word
/// phrases developers type hundreds of times a day. Intentionally small: this is not meant to cover
/// every SQL statement, only to remove repetitive typing (design §2.5). <c>begin … end</c> is
/// deliberately absent — it is a structural delimiter pair owned by Typing Ergonomics, not a construct.
/// <para>Ambiguity is measured <b>against this set</b>: because it is curated, common prefixes resolve
/// uniquely (<c>decl</c> → <c>declare variable</c>), while genuinely ambiguous ones stay silent until
/// unique (<c>exec</c> waits for <c>execute p…</c> / <c>execute b…</c>).</para>
/// </summary>
public static class LanguageConstructCatalog
{
    // Authoring marker for the caret position inside a template; stripped when the catalog is built.
    // A non-character code point that never appears in SQL, so it can't collide with real text.
    private const char CaretMark = '￿';

    /// <summary>Every construct, canonical order. Read-only.</summary>
    public static IReadOnlyList<LanguageConstruct> All { get; } = Build();

    /// <summary>The largest word count among catalog spellings — the resolver looks back at most this
    /// many trailing words when matching a multi-word construct (e.g. <c>group by</c>).</summary>
    public static int MaxWords { get; } = ComputeMaxWords(All);

    // Caret marked with CaretMark where it belongs; no mark → caret at the end of the expansion.
    private static LanguageConstruct C(string spelling, ConstructCategory category, string template)
    {
        int mark = template.IndexOf(CaretMark);
        if (mark < 0) return new LanguageConstruct(spelling, template, template.Length, category);
        var expansion = template.Substring(0, mark) + template.Substring(mark + 1);
        return new LanguageConstruct(spelling, expansion, mark, category);
    }

    private static IReadOnlyList<LanguageConstruct> Build() => new[]
    {
        // Statements & control flow (arm at a statement boundary). Control-flow carets sit in the condition.
        C("if", ConstructCategory.Statement, "if (￿) then"),
        C("while", ConstructCategory.Statement, "while (￿) do"),
        C("for select", ConstructCategory.Statement, "for select "),
        C("select", ConstructCategory.Statement, "select "),
        C("insert into", ConstructCategory.Statement, "insert into "),
        C("update", ConstructCategory.Statement, "update "),
        C("delete from", ConstructCategory.Statement, "delete from "),
        C("execute procedure", ConstructCategory.Statement, "execute procedure "),
        C("execute block", ConstructCategory.Statement, "execute block "),
        C("declare variable", ConstructCategory.Statement, "declare variable "),
        C("when", ConstructCategory.Statement, "when ￿ do"),

        // Query clauses (arm after something that completes a table/expression). Caret at end.
        C("where", ConstructCategory.Clause, "where "),
        C("group by", ConstructCategory.Clause, "group by "),
        C("having", ConstructCategory.Clause, "having "),
        C("order by", ConstructCategory.Clause, "order by "),
        C("union", ConstructCategory.Clause, "union "),
    };

    private static int ComputeMaxWords(IReadOnlyList<LanguageConstruct> all)
    {
        int max = 1;
        foreach (var c in all)
        {
            int words = c.Spelling.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (words > max) max = words;
        }
        return max;
    }
}

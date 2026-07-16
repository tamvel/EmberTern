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
public sealed record LanguageConstruct(string Spelling, string Expansion, int CaretOffset);

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
    private static LanguageConstruct C(string spelling, string template)
    {
        int mark = template.IndexOf(CaretMark);
        if (mark < 0) return new LanguageConstruct(spelling, template, template.Length);
        var expansion = template.Substring(0, mark) + template.Substring(mark + 1);
        return new LanguageConstruct(spelling, expansion, mark);
    }

    private static IReadOnlyList<LanguageConstruct> Build() => new[]
    {
        // Control flow (caret inside the condition).
        C("if", "if (￿) then"),
        C("while", "while (￿) do"),
        C("for select", "for select "),

        // Statements.
        C("select", "select "),
        C("insert into", "insert into "),
        C("update", "update "),
        C("delete from", "delete from "),
        C("execute procedure", "execute procedure "),
        C("execute block", "execute block "),

        // Clauses (caret at end — the developer continues typing the clause body).
        C("where", "where "),
        C("group by", "group by "),
        C("having", "having "),
        C("order by", "order by "),
        C("union", "union "),

        // PSQL declarations / handlers.
        C("declare variable", "declare variable "),
        C("when", "when ￿ do"),
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

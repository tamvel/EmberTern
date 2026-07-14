using EmberTern.Core.Sql.Language;

namespace EmberTern.Core.Sql;

/// <summary>How the user writes SQL words in the document.</summary>
public enum SqlCaseStyle
{
    /// <summary>No usable signal — keep the catalog's own casing.</summary>
    Unknown,
    Lower,
    Upper,
}

/// <summary>
/// Infers the user's ACTUAL writing style from the document, so completion can pick the right
/// case even when the typed prefix carries no signal.
///
/// <para>Why this exists: <see cref="CaseMatcher"/> shapes the inserted text to match what the user
/// already typed — but right after a qualifier dot (<c>k.</c>) the typed prefix is EMPTY. With no
/// letters to copy, completion fell back to the catalog casing and inserted <c>ID_KONTRAHENT</c>
/// into an all-lowercase query. The dot was effectively treated as the start of a fresh word. The
/// fix is to stop deciding from the immediately-preceding character and decide from how the user
/// writes in this document.</para>
///
/// <para>Identifiers are the evidence that counts — the question being answered is "how does this
/// user write an identifier?" — so keywords are only consulted when the document contains no
/// identifiers yet. Mixed-case words (<c>Kontrahent</c>) are deliberately ignored: they vote for
/// neither style, and if neither style clearly leads we return <see cref="SqlCaseStyle.Unknown"/>
/// and the catalog casing stands. Tokenization is delegated to the shared
/// <see cref="SqlLexer"/>, so words inside string literals, quoted identifiers and comments are
/// never counted — no second scanner.</para>
/// </summary>
public static class SqlCaseStyleDetector
{
    public static SqlCaseStyle Detect(string? sql)
    {
        if (string.IsNullOrEmpty(sql)) return SqlCaseStyle.Unknown;

        int idLower = 0, idUpper = 0, kwLower = 0, kwUpper = 0;

        foreach (var token in SqlLexer.Tokenize(sql))
        {
            bool isIdentifier = token.Kind == TokenKind.Identifier;
            bool isKeyword = token.Kind == TokenKind.Keyword;
            if (!isIdentifier && !isKeyword) continue;

            switch (Classify(token.Text))
            {
                case SqlCaseStyle.Lower:
                    if (isIdentifier) idLower++; else kwLower++;
                    break;
                case SqlCaseStyle.Upper:
                    if (isIdentifier) idUpper++; else kwUpper++;
                    break;
                default:
                    break; // mixed case → no vote
            }
        }

        // Identifiers answer the question directly; keywords are the fallback signal.
        if (idLower > 0 || idUpper > 0) return Winner(idLower, idUpper);
        return Winner(kwLower, kwUpper);
    }

    private static SqlCaseStyle Winner(int lower, int upper)
    {
        if (lower > upper) return SqlCaseStyle.Lower;
        if (upper > lower) return SqlCaseStyle.Upper;
        return SqlCaseStyle.Unknown;   // tie (incl. 0/0) → don't guess
    }

    /// <summary>All-lower / all-upper over the word's LETTERS (digits and underscores are
    /// case-less and ignored). A word with no letters, or with both cases, votes for nothing.</summary>
    private static SqlCaseStyle Classify(string? word)
    {
        if (string.IsNullOrEmpty(word)) return SqlCaseStyle.Unknown;

        bool sawLetter = false, allLower = true, allUpper = true;
        foreach (var c in word)
        {
            if (!char.IsLetter(c)) continue;
            sawLetter = true;
            if (!char.IsLower(c)) allLower = false;
            if (!char.IsUpper(c)) allUpper = false;
            if (!allLower && !allUpper) return SqlCaseStyle.Unknown;
        }

        if (!sawLetter) return SqlCaseStyle.Unknown;
        if (allLower) return SqlCaseStyle.Lower;
        if (allUpper) return SqlCaseStyle.Upper;
        return SqlCaseStyle.Unknown;
    }
}

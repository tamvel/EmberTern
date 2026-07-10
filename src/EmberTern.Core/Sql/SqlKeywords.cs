using System.Collections.Generic;
using EmberTern.Core.Sql.Language;

namespace EmberTern.Core.Sql;

// Keyword list surfaced by the SQL editor's autocomplete. Uppercase so the
// completion display matches the IBExpert / SQL convention (the formatter
// lowercases on demand, but the completion list reads better in caps).
//
// Single-token only — multi-word phrases (CHARACTER SET, NEXT VALUE FOR) are
// completed one keyword at a time, the same way they're tokenized.
//
// Etap 1: this is now a thin ADAPTER over the single FirebirdSyntax keyword
// catalog. The completion vocabulary lives in one place (FirebirdSyntax); this
// type keeps the historical `All` API its one consumer (the completion
// controller) reads. It may retire when the context-ranked Completion Engine
// reads FirebirdSyntax directly (Etap 5).
public static class SqlKeywords
{
    public static IReadOnlyList<string> All => FirebirdSyntax.CompletionKeywords;
}

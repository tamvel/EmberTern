using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Ast;

namespace EmberTern.Core.Sql.Language;

/// <summary>
/// The result of <see cref="SqlParser.Parse(string)"/>: the parsed <see cref="Root"/> tree and any
/// <see cref="Diagnostics"/>. The parser is error-tolerant — this is always non-null and never
/// throws (§4.2 #1). In Etap 2 <see cref="Diagnostics"/> is empty for well-formed statement
/// segmentation (see <see cref="Diagnostic"/>).
/// </summary>
/// <param name="Root">The parsed script (never null).</param>
/// <param name="Diagnostics">Findings gathered during parsing (never null; may be empty).</param>
public sealed record ParseResult(SqlScript Root, IReadOnlyList<Diagnostic> Diagnostics);

using System;
using System.Collections.Generic;

namespace EmberTern.Core.Sql.Templates;

/// <summary>
/// A tab-stop inside a generated <see cref="SqlSnippet"/>. <see cref="Start"/> is a
/// character offset into <see cref="SqlSnippet.Text"/>; the App layer maps these onto
/// <c>AvaloniaEdit.Snippets</c> so the user can Tab between the <c>:param</c> tokens.
/// Placeholders may share a <see cref="Name"/> (e.g. a PK appearing in both VALUES and
/// MATCHING) — same-named stops are edited together by the editor, which is desirable.
/// </summary>
public sealed record SqlPlaceholder(string Name, int Start, int Length);

/// <summary>
/// The result of generating an SQL template — plain text plus the tab-stops within it.
/// Pure data, zero UI dependency; the drop sink inserts <see cref="Text"/> at the drop
/// offset and activates the <see cref="Placeholders"/>.
/// </summary>
public sealed record SqlSnippet(string Text, IReadOnlyList<SqlPlaceholder> Placeholders)
{
    public static SqlSnippet Plain(string text) => new(text, Array.Empty<SqlPlaceholder>());
}

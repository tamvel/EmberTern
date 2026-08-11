using System.Collections.Generic;
using EmberTern.Core.Localization;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.QuickInfo;

/// <summary>
/// One structured "label : value" fact in a <see cref="QuickInfo"/> — e.g.
/// <c>(QuickInfoMessages.Table, "KONTRAHENT")</c>, <c>(QuickInfoMessages.Nullability, "NOT NULL")</c>,
/// <c>(QuickInfoMessages.Key, "PRIMARY KEY")</c>. The App renders them however it likes (a compact
/// "· value" chip for the hover tooltip, a two-column list for a detail pane). Pure value — no Avalonia.
///
/// <para>⭐ <b>The two halves have different owners, which is why they have different types (D‑3).</b> The
/// <paramref name="Label"/> is EmberTern speaking, so it is a <see cref="MessageKey"/> the App resolves in
/// the reader's language. The <paramref name="Value"/> is a <c>string</c> because it is almost always
/// <i>Firebird</i> speaking — <c>NOT NULL</c>, <c>PRIMARY KEY</c>, <c>BEFORE INSERT</c>, a domain, a type,
/// a count — and that vocabulary must match the DDL the card describes.</para>
///
/// <para>⚠ A handful of values are EmberTern's own words rather than Firebird's (<c>Active</c>,
/// <c>Identity</c>, the object-kind names). They are knowingly left verbatim for now: naming a
/// <see cref="SymbolKind"/> is a vocabulary the App already owns and localizes, so the fix is to stop
/// producing the words here rather than to add a second copy of them to the catalog. ⛔ Do not "complete"
/// this by declaring kind keys in Core — that is the duplication the App stage removed elsewhere.</para>
/// </summary>
public sealed record QuickInfoFact(MessageKey Label, string Value);

/// <summary>The group a <see cref="QuickInfo"/> member belongs to — lets the App label/section
/// the member list ("Columns" for a table, "Parameters"/"Returns" for a routine).</summary>
public enum QuickInfoMemberGroup
{
    Column,
    Parameter,
    Returns,
}

/// <summary>One pre-formatted member line of a <see cref="QuickInfo"/> (a table/view column, or a
/// routine parameter/return). <see cref="Text"/> is display-ready, e.g. <c>"NAZWA VARCHAR(50)"</c>
/// or <c>"A INTEGER (IN)"</c>. Pure value — no Avalonia.</summary>
public sealed record QuickInfoMember(string Text, QuickInfoMemberGroup Group);

/// <summary>
/// The structured quick-documentation model for a resolved symbol — Etap 6 (design §5.12 / §8A / P9).
/// Produced by <see cref="QuickInfoEngine"/> from the <see cref="SemanticModel"/> + its metadata
/// snapshot; rendered by the App as the Ctrl-hover tooltip and (later) the completion detail pane.
/// It lets the user check an object's key facts <b>without opening its definition</b>.
/// <para>
/// Pure data — no Avalonia. Read-only, so §0 (never lose information) holds by construction: Quick
/// Info never modifies code. The content is rich-but-optional: an implementation fills what the
/// metadata snapshot carries and leaves the rest empty, so it grows as the snapshot grows without an
/// API change (LSP-ready, mirroring the rest of the semantic layer).
/// </para>
/// </summary>
public sealed class QuickInfo
{
    public QuickInfo(
        SymbolKind kind,
        string header,
        string? description = null,
        IReadOnlyList<QuickInfoFact>? facts = null,
        IReadOnlyList<QuickInfoMember>? members = null)
    {
        Kind = kind;
        Header = header ?? string.Empty;
        Description = description;
        Facts = facts ?? System.Array.Empty<QuickInfoFact>();
        Members = members ?? System.Array.Empty<QuickInfoMember>();
    }

    /// <summary>What the symbol denotes — drives the App's kind badge / colour (reuses the same
    /// per-kind palette as the metadata tree and semantic highlighting).</summary>
    public SymbolKind Kind { get; }

    /// <summary>The primary line — the identifier and its most important fact, e.g.
    /// <c>"NAZWA : VARCHAR(50)"</c> for a column, <c>"KONTRAHENT"</c> for a table, <c>"K → KONTRAHENT"</c>
    /// for an alias. Never empty for a real symbol.</summary>
    public string Header { get; }

    /// <summary>The object's comment/description, when known.</summary>
    public string? Description { get; }

    /// <summary>Ordered "label : value" facts (owner, domain, nullability, default, keys, generated,
    /// direction, …). May be empty.</summary>
    public IReadOnlyList<QuickInfoFact> Facts { get; }

    /// <summary>Member lines — a table/view's columns, or a routine's parameters/returns — when the
    /// metadata snapshot has them loaded. Empty when the object has no members or they are not cached
    /// (the App warms-then-rebuilds, like dot completion). Grouped via
    /// <see cref="QuickInfoMember.Group"/>.</summary>
    public IReadOnlyList<QuickInfoMember> Members { get; }
}

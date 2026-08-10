using EmberTern.Core.Localization;

namespace EmberTern.Core.Sql.Language.QuickInfo;

/// <summary>
/// The fact LABELS a <see cref="QuickInfoEngine"/> card can carry — decision <b>D‑3</b>'s second producer.
///
/// <para>⭐ <b>Labels only, and that boundary is the point of the type.</b> A fact is a
/// <c>label : value</c> pair in which the two halves have different owners: the label is EmberTern speaking
/// (<i>"Nullability"</i>, <i>"Fires"</i>) and belongs in the resource catalog, while the value is almost
/// always Firebird speaking — <c>NOT NULL</c>, <c>PRIMARY KEY</c>, <c>BEFORE INSERT OR UPDATE</c>, a domain
/// name, a type, a count. ⛔ Those values stay verbatim: they are the vocabulary the user reads in every
/// other Firebird tool, and translating them would make the card disagree with the DDL it describes.</para>
///
/// <para>⚠ <b><c>Table</c> labels two different relationships</b> — a column's owning table and a trigger's
/// target table — and they deliberately share ONE key. The label reads the same in English and names the same
/// idea ("which table"), so a translator gains nothing from two entries. ⚠ Recorded because the reverse move
/// is the dangerous one: splitting this later is trivial, whereas merging two keys after a language has
/// translated them differently silently discards a distinction. If a target language ever needs them apart,
/// split here — not in the engine.</para>
///
/// <para>⛔ <b>Not here, and not an oversight: the plural fact value <c>"1 column"</c> / <c>"N columns"</c></b>
/// under <c>Primary key</c>. Its LABEL is below; its VALUE is chosen by a count and therefore waits for the
/// shared plural mechanism, exactly as <c>SessionHealthVerdict.Headline</c> does.</para>
/// </summary>
public static class QuickInfoMessages
{
    // ── Column facts ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>The table a column belongs to, and the table a trigger fires on — see the type remarks.</summary>
    public static readonly MessageKey Table = new("QuickInfo.Fact.Table");

    public static readonly MessageKey Domain = new("QuickInfo.Fact.Domain");

    public static readonly MessageKey Nullability = new("QuickInfo.Fact.Nullability");

    /// <summary>Shared by a column, a variable and a parameter — one idea, "the value used when none is given".</summary>
    public static readonly MessageKey Default = new("QuickInfo.Fact.Default");

    /// <summary>Primary/foreign key membership.</summary>
    public static readonly MessageKey Key = new("QuickInfo.Fact.Key");

    /// <summary>Identity or computed.</summary>
    public static readonly MessageKey Generated = new("QuickInfo.Fact.Generated");

    // ── Schema-object facts ──────────────────────────────────────────────────────────────────────────

    public static readonly MessageKey Owner = new("QuickInfo.Fact.Owner");

    /// <summary>A function's return type.</summary>
    public static readonly MessageKey Returns = new("QuickInfo.Fact.Returns");

    public static readonly MessageKey Columns = new("QuickInfo.Fact.Columns");

    public static readonly MessageKey PrimaryKey = new("QuickInfo.Fact.PrimaryKey");

    public static readonly MessageKey ForeignKeys = new("QuickInfo.Fact.ForeignKeys");

    public static readonly MessageKey Parameters = new("QuickInfo.Fact.Parameters");

    /// <summary>What kind of thing the symbol is.</summary>
    public static readonly MessageKey Kind = new("QuickInfo.Fact.Kind");

    // ── Trigger facts ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Timing + events, e.g. <c>BEFORE INSERT OR UPDATE</c>.</summary>
    public static readonly MessageKey Fires = new("QuickInfo.Fact.Fires");

    public static readonly MessageKey Position = new("QuickInfo.Fact.Position");

    /// <summary>Active / inactive.</summary>
    public static readonly MessageKey State = new("QuickInfo.Fact.State");

    // ── Generator facts ──────────────────────────────────────────────────────────────────────────────

    public static readonly MessageKey Increment = new("QuickInfo.Fact.Increment");

    public static readonly MessageKey Start = new("QuickInfo.Fact.Start");
}

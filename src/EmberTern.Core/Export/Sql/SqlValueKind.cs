namespace EmberTern.Core.Export.Sql;

/// <summary>
/// The <b>declared Firebird type</b> of a result column, reduced to the distinctions that change how
/// its value becomes an SQL literal. Core-owned (rule #1: Core has no FirebirdSql dependency) — the
/// Firebird layer maps <c>FbDbType</c> onto this; every other consumer (the SQL exporters today, a
/// future JSON/XML exporter) reads only this.
/// <para>
/// <b>Why this exists at all</b> — the CLR type is not sufficient. DATE and TIMESTAMP are <em>both</em>
/// <see cref="System.DateTime"/> and are indistinguishable by <c>Type</c>; rendering a DATE through the
/// TIMESTAMP format yields a misleading <c>'2024-03-15 00:00:00'</c>, and rendering a TIMESTAMP through
/// the DATE format <em>loses the time</em>. The literal writer must therefore be driven by the declared
/// type, never by the value's runtime type (design §1.5).
/// </para>
/// <para>
/// <see cref="Unknown"/> is deliberately <c>0</c>, i.e. the default: a column whose kind was never
/// mapped refuses to render rather than guessing. Uncertainty ⇒ do nothing (architecture rule #11).
/// Firebird types with no member here (ARRAY, and FB4's DECFLOAT / INT128 / <c>WITH TIME ZONE</c>) map
/// to <see cref="Unknown"/> and are refused — a safe, loud outcome, and additive to fix if one is ever
/// wanted.
/// </para>
/// </summary>
public enum SqlValueKind
{
    /// <summary>No mapping — refuse to render (see the type-level remarks).</summary>
    Unknown = 0,

    /// <summary>SMALLINT / INTEGER / BIGINT — a bare digit literal.</summary>
    Integer,

    /// <summary>NUMERIC / DECIMAL — exact; a bare literal with a <c>.</c> decimal point.</summary>
    Decimal,

    /// <summary>FLOAT / DOUBLE PRECISION — approximate; rendered round-trip-exact.</summary>
    Float,

    /// <summary>CHAR / VARCHAR — a quoted literal.</summary>
    Text,

    /// <summary>DATE — quoted <c>yyyy-MM-dd</c>, no time part.</summary>
    Date,

    /// <summary>TIME — quoted <c>hh:mm:ss.ffff</c>; arrives as a <see cref="System.TimeSpan"/>.</summary>
    Time,

    /// <summary>TIMESTAMP — quoted <c>yyyy-MM-dd HH:mm:ss.ffff</c>, space-separated (never ISO <c>T</c>).</summary>
    Timestamp,

    /// <summary>BOOLEAN — bare <c>true</c> / <c>false</c>.</summary>
    Boolean,

    /// <summary>BLOB SUB_TYPE 0 (binary) — an <c>x'…'</c> hex literal; arrives as a <c>byte[]</c>.</summary>
    BinaryBlob,

    /// <summary>BLOB SUB_TYPE TEXT — arrives already decoded as a <c>string</c>; quoted like text.</summary>
    TextBlob,
}

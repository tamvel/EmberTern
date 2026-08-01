namespace EmberTern.Core.Sql;

/// <summary>
/// How <see cref="SqlFormatter"/> cases one class of word.
/// <para>
/// ⚠ <b>This enum is NOT persisted, and that is deliberate.</b> The stored vocabulary is
/// <c>PreferenceOptions.Casing</c>'s strings (<c>"Lower"</c> / <c>"Upper"</c>), for the reason recorded
/// there: an unknown enum name makes <c>JsonStringEnumConverter</c> throw, which in this codebase costs the
/// <i>whole</i> settings file. This type is the formatter's own currency, mapped from those keys at the App
/// boundary — so adding a value here stays an ordinary additive change.
/// </para>
/// </summary>
public enum FormatterCase
{
    /// <summary>Fold to lower case — the shipped default, so output is byte-identical to pre-setting builds.</summary>
    Lower,

    /// <summary>Fold to upper case.</summary>
    Upper,
}

/// <summary>
/// The formatter's style profile — the user's own layout decisions, which the class comment on
/// <see cref="SqlFormatter"/> deferred to "the future application configurator" from the day it was written.
///
/// <para><b>It travels as a parameter, never as ambient state</b> (<c>Format(sql, style)</c> with a default).
/// The formatter is a pure static function of (text, style): that is what lets the §0 differential and
/// idempotency suites run the same corpus under every style, and what stops a formatting result from
/// depending on when a setting happened to change relative to a background parse.</para>
///
/// <para><b>Scope is exactly two settings, by ratified decision</b> (design §6.4 / §9.1): no
/// <c>MaxLineWidth</c>, no indent size, no comma placement. The formatter's other style constants stay
/// constants until something concrete asks for them.</para>
///
/// <para>⚠ <b>Casing applies only to UNQUOTED words.</b> A quoted identifier is case-sensitive in Firebird,
/// so re-casing one changes which object is named; the formatter's existing quoted-identifier guard passes it
/// through verbatim and the setting is applied strictly inside that guard (§0 / architecture rule #11).</para>
///
/// <para>⚠ <b>Generated DDL is NOT governed by this</b> (ratified Q1). <c>DdlGenerator</c> composes new DDL
/// for the catalog and keeps uppercasing identifiers; this type governs the formatter, which reformats text
/// the user already has.</para>
/// </summary>
public sealed record FormatterStyle
{
    /// <summary>
    /// The shipped style. Both cases <see cref="FormatterCase.Lower"/>, so
    /// <c>Format(sql)</c> and <c>Format(sql, FormatterStyle.Default)</c> reproduce exactly the output every
    /// build before this setting existed produced — the property the etap's regression gate pins.
    /// </summary>
    public static FormatterStyle Default { get; } = new();

    /// <summary>How SQL/PSQL keywords are cased. A "keyword" is whatever the ONE catalog
    /// (<c>FirebirdSyntax</c>) says it is — the same verdict the lexer records as
    /// <c>TokenKind.Keyword</c> — which is why there is no second keyword list anywhere.</summary>
    public FormatterCase KeywordCase { get; init; } = FormatterCase.Lower;

    /// <summary>How unquoted identifiers are cased. Named parameters (<c>:name</c> / <c>@name</c>) follow
    /// this too: a parameter's text is a variable name, not vocabulary.</summary>
    public FormatterCase IdentifierCase { get; init; } = FormatterCase.Lower;
}

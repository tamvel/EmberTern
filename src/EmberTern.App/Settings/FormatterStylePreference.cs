using System;
using EmberTern.Core.Settings;
using EmberTern.Core.Sql;

namespace EmberTern.App.Settings;

/// <summary>
/// The ONE place a stored casing key (<c>"Lower"</c> / <c>"Upper"</c>) becomes the formatter's own
/// <see cref="FormatterCase"/>, and therefore the one place a <see cref="Preferences"/> value becomes a
/// <see cref="FormatterStyle"/>.
///
/// <para>The sibling of <see cref="ThemePreference"/>, and it exists for the same reason: Core owns the
/// persisted <i>key</i> (validated against <c>PreferenceOptions.Casing</c>), the consumer owns its own type,
/// and the translation between them happens once. Written twice, the two copies would be two answers to
/// "what does Upper mean" — and the failure mode is the quiet one, a setting that takes effect on one surface
/// and not another with a green build.</para>
///
/// <para>⚠ <b>No second list of casing names is introduced here.</b> The vocabulary is
/// <c>PreferenceOptions.Casing</c>'s and this class only reads it; <see cref="FormatterCase"/> is the
/// formatter's internal currency and is deliberately not persisted (see its own remarks).</para>
///
/// <para>⚠ <b>Nothing here reads the store.</b> It maps a <see cref="Preferences"/> value that a caller
/// already holds — which is what keeps <see cref="SqlFormatter"/> a pure function of (text, style) instead of
/// something that consults ambient state mid-format.</para>
/// </summary>
public static class FormatterStylePreference
{
    /// <summary>The formatter style the given preferences describe.</summary>
    public static FormatterStyle From(Preferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return new FormatterStyle
        {
            KeywordCase = CaseFor(preferences.FormatterKeywordCase),
            IdentifierCase = CaseFor(preferences.FormatterIdentifierCase),
        };
    }

    /// <summary>Stored key → case. Anything unrecognised is <see cref="FormatterCase.Lower"/>, matching
    /// <c>PreferenceOptions.Casing.Default</c> — though the store normalizes first, so this is a second net
    /// rather than the primary one (the same belt-and-braces shape as
    /// <see cref="ThemePreference.VariantFor"/>).</summary>
    public static FormatterCase CaseFor(string? key)
        => string.Equals(key, PreferenceOptions.CaseUpper, StringComparison.OrdinalIgnoreCase)
            ? FormatterCase.Upper
            : FormatterCase.Lower;
}

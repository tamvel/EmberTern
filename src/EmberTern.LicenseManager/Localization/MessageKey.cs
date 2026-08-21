using System;

namespace EmberTern.LicenseManager.Localization;

/// <summary>
/// The identity of a sentence in the catalog — a key, and deliberately NOT a <see cref="string"/>.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>The type exists to make the pre-L8.2 shape stop compiling.</b> <c>StatusMessage.Info</c> used
/// to take the finished sentence; it now takes a key. Had the key stayed a <c>string</c>, every one of the
/// 104 call sites would have gone on compiling with the sentence sitting where the key belongs — and
/// <see cref="Loc.Text"/> answers a missing entry with the key itself, so each of them would have RENDERED
/// THE SENTENCE PERFECTLY. The application would look untouched, every existing assertion would stay green,
/// and nothing would be localized. ⛔ Do not add an implicit conversion from <c>string</c>: it would hand
/// that failure mode straight back.</para>
///
/// <para>⚠ The same reasoning is why there is no <c>MessageKey(string)</c> shortcut anywhere outside a
/// catalog. A key is minted in ONE place — the catalog that owns the prefix — so a key on screen is
/// traceable to a member, and the guards can sweep every key that exists by reflecting over the catalogs
/// (<see cref="StringCatalogAttribute"/>).</para>
///
/// <para>⚠ <see cref="ToString"/> returns the key rather than the resolved text, and that is deliberate: a
/// key is an identifier, and a type whose <c>ToString</c> silently resolved words would be a second,
/// invisible resolution path beside <see cref="Loc"/>.</para>
/// </remarks>
public readonly record struct MessageKey
{
    /// <summary>Creates a key. ⚠ Called by the string catalogs, and by nothing else.</summary>
    /// <param name="value">The full key, prefix included.</param>
    public MessageKey(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        Value = value;
    }

    /// <summary>The catalog key this names, prefix included.</summary>
    public string Value { get; }

    /// <summary>The key itself. ⛔ Never the resolved words — see the type's remarks.</summary>
    public override string ToString() => Value;
}

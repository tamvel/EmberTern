using System;

namespace EmberTern.LicenseManager.Localization;

/// <summary>
/// Marks a class as one of the application's string catalogs, and declares the prefix its keys carry.
/// </summary>
/// <remarks>
/// <para>⭐⭐ <b>It exists so the guards can DISCOVER the catalogs instead of listing them</b> (L8 decision
/// D‑5: the catalog is split by theme over ONE mechanism). A hand-written list of catalog classes is a
/// second copy of a fact, and the copy goes stale the first time somebody adds an area — silently, which is
/// the dead-list trap this project has met before.</para>
///
/// <para>⭐ The prefix is what makes the split safe: two themes may each want a <c>WindowTitle</c>, and the
/// key is <c>Prefix + MemberName</c>, so they cannot collide. ⛔ Two catalogs must never declare the same
/// prefix — <c>NoTwoCatalogs_ShareAKeyPrefix</c> says so.</para>
///
/// <para>⚠ The prefix ends with its own separator (<c>"Settings."</c>), stated rather than appended by the
/// reader: a separator added in one place and assumed in another is how the two drift.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
internal sealed class StringCatalogAttribute(string keyPrefix) : Attribute
{
    /// <summary>The prefix every key in this catalog carries, separator included.</summary>
    public string KeyPrefix { get; } = keyPrefix;
}

using System.Collections.Concurrent;
using System.ComponentModel;

namespace EmberTern.App.Localization;

/// <summary>
/// One notifying object per resource key — what <c>{app:Loc Key}</c> binds to, and what makes a language
/// change repaint every bound control.
///
/// <para>⚠⚠ <b>THE SHAPE HERE WAS DECIDED BY MEASUREMENT, AND THE OBVIOUS DESIGN DOES NOT WORK.</b> The first
/// version was a single object with a string INDEXER (<c>this[key]</c>) — the standard localization pattern,
/// one object for the whole app, no per-key allocation. A headless test driving a real
/// <c>TextBlock</c> proved it dead: the initial value bound correctly, and after the language changed the
/// control still showed the old text. Neither notification name reaches an indexer binding in Avalonia
/// 12.1.1 — not the WPF convention <c>"Item[]"</c>, and not <c>string.Empty</c> ("everything changed").
/// ⛔ Do not "simplify" this back into an indexer; it renders correctly on first load, which is exactly what
/// makes the failure hard to see.</para>
///
/// <para>⭐ So a key is bound through an ORDINARY property (<see cref="LocalizedString.Value"/>) on a small
/// per-key object, notified by its exact name — the most reliable notification any XAML framework has. The
/// cost is one ~32-byte object per distinct key actually used in XAML, created on first use and living for
/// the process; against ~940 such keys that is noise, and it buys a mechanism whose liveness is measured
/// rather than assumed.</para>
/// </summary>
public static class LocalizationSource
{
    private static readonly ConcurrentDictionary<string, LocalizedString> Entries =
        new(System.StringComparer.Ordinal);

    /// <summary>The bindable entry for <paramref name="key"/>, created once and reused.</summary>
    public static LocalizedString For(string key) => Entries.GetOrAdd(key, static k => new LocalizedString(k));

    /// <summary>Tells every bound control to re-read. Called by <see cref="Loc"/> when the language changes;
    /// internal because the language is changed through the preference and nowhere else.</summary>
    internal static void InvalidateAll()
    {
        foreach (var entry in Entries.Values)
        {
            entry.Invalidate();
        }
    }
}

/// <summary>A single localized string, bindable and live. Obtained from <see cref="LocalizationSource.For"/>.</summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    private readonly string _key;

    /// <summary>
    /// The <see cref="UiStrings"/> property of the same name, when the key names a COMPOSED member rather
    /// than a catalog entry. Resolved once — whether a name is a member is a fact about the type, not about
    /// the language.
    /// </summary>
    private readonly System.Reflection.PropertyInfo? _member;

    internal LocalizedString(string key)
    {
        _key = key;
        var member = typeof(UiStrings).GetProperty(
            key, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        _member = member?.PropertyType == typeof(string) ? member : null;
    }

    /// <summary>
    /// The text in the current language, resolved at read time.
    ///
    /// <para>⭐⭐ <b>The catalog first, then the member — because a <c>UiStrings</c> member has TWO legal
    /// forms and only one of them is an entry.</b> Most are <c>Loc.Text(nameof(X))</c>, so the catalog answers.
    /// A handful are COMPOSED at read time — <c>CommandTip.For(…)</c> glues a localized label to a keyboard
    /// gesture, <c>SecurityRolesEmpty</c> formats one localized string into another — and
    /// <c>UiStringsShortcutSourceTests</c> positively FORBIDS storing those in the catalog, because a stored
    /// gesture is the stale-by-construction defect gotcha #284 describes.</para>
    ///
    /// <para>⚠⚠ So the two mechanisms were mutually exclusive and nothing said so: <c>{app:Loc X}</c> asked the
    /// catalog, the catalog had no entry, and <see cref="Loc.Text(string)"/> did what it promises — returned
    /// the key. Six views rendered <c>ToolbarExecuteHint</c> / <c>DebuggerContinueTooltip</c> as literal
    /// identifiers, in ENGLISH too; the Polish stage only made it easy to notice. <c>EveryLocalizedMember_…</c>
    /// walks resource keys → members and therefore cannot see a member with no entry, which is the same
    /// asymmetry as gotcha #367 one layer out. <c>EveryLocBindingKey_ResolvesToSomething</c> is the guard.</para>
    ///
    /// <para>⭐ Liveness is unchanged and free: a composed member re-reads its own parts, and
    /// <see cref="Invalidate"/> already tells the binding to ask again.</para>
    /// </summary>
    public string Value
    {
        get
        {
            var text = Loc.Find(_key);
            if (text is not null)
            {
                return text;
            }

            return _member is not null ? (string?)_member.GetValue(null) ?? _key : _key;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Invalidate() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}

using System.Collections.Concurrent;
using System.ComponentModel;

namespace EmberTern.LicenseManager.Localization;

/// <summary>
/// What <c>{lm:Loc Key}</c> binds to: one small notifying object per key.
/// </summary>
/// <remarks>
/// <para>⚠⚠ <b>NOT an indexer, and that is a measured finding rather than a style choice.</b> The obvious
/// design — one object with <c>this[key]</c>, notified once — is what EmberTern built first, and a headless
/// test on a real <c>TextBlock</c> proved it does not work: the initial value binds correctly and the
/// control keeps the OLD text after a language change. Neither WPF's <c>"Item[]"</c> convention nor
/// <c>string.Empty</c> ("everything changed") reaches a binding over an indexer in Avalonia 12.1.1.
/// ⛔ Do not "simplify" this back into an indexer — the broken version renders correctly on first load,
/// which is exactly what makes the failure hard to see.</para>
///
/// <para>⭐ Cost: one small object per distinct key actually used, created on demand.</para>
/// </remarks>
public static class LocalizationSource
{
    private static readonly ConcurrentDictionary<string, LocalizedString> Entries =
        new(System.StringComparer.Ordinal);

    /// <summary>The notifying holder for <paramref name="key"/>, created once and reused.</summary>
    public static LocalizedString For(string key) => Entries.GetOrAdd(key, static k => new LocalizedString(k));

    /// <summary>Tells every holder to re-read. ⭐ Called by <see cref="Loc.Apply"/> and by nothing else.</summary>
    internal static void InvalidateAll()
    {
        foreach (var entry in Entries.Values)
        {
            entry.Invalidate();
        }
    }
}

/// <summary>One key's current text, as a bindable property.</summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    private readonly string _key;

    internal LocalizedString(string key) => _key = key;

    /// <summary>
    /// The text, resolved at the moment of the read.
    /// </summary>
    /// <remarks>
    /// ⚠ Falls back to the KEY when the catalog has no entry — visible, and deliberately so: a key on
    /// screen is a defect somebody notices, where a blank label is one nobody does. ⭐ It should be
    /// unreachable in a shipped build, and <c>NoLocKeyInXaml_IsMissingFromTheCatalog</c> is what keeps it
    /// that way.
    /// </remarks>
    public string Value => Loc.Find(_key) ?? _key;

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Invalidate() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}

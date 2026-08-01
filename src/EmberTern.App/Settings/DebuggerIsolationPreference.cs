using System;
using EmberTern.Core.Settings;
using EmberTern.Firebird;

namespace EmberTern.App.Settings;

/// <summary>
/// The ONE place a stored debugger-isolation key becomes the Firebird layer's own
/// <see cref="DebugIsolation"/>.
///
/// <para>The third member of the <see cref="ThemePreference"/> / <see cref="FormatterStylePreference"/> family,
/// and it exists for the same ratified reason (§14.4a/2): Core owns the persisted <i>key</i>, validated against
/// <c>PreferenceOptions.DebuggerIsolation</c>; the consumer owns its own type; the translation happens once. Two
/// copies would be two answers to "what does Snapshot mean", and the failure would be the quiet kind — a
/// debugger that opened in the wrong isolation from one entry point and the right one from another.</para>
///
/// <para>⚠ <b>Nothing here reads the store</b>, and nothing here knows about the launch panel's selector index.
/// The index convention (0 = Read Committed, 1 = Snapshot) stays inside <c>DebuggerTabViewModel</c>, which is
/// the only class that has ever expressed it.</para>
/// </summary>
public static class DebuggerIsolationPreference
{
    /// <summary>The isolation the given preferences describe.</summary>
    public static DebugIsolation From(Preferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return IsolationFor(preferences.DebuggerIsolation);
    }

    /// <summary>Stored key → isolation. Anything unrecognised is
    /// <see cref="DebugIsolation.ReadCommitted"/>, matching
    /// <c>PreferenceOptions.DebuggerIsolation.Default</c> — a second net rather than the primary one, since the
    /// store normalizes on load (the same belt-and-braces shape as
    /// <see cref="ThemePreference.VariantFor"/>).</summary>
    public static DebugIsolation IsolationFor(string? key)
        => string.Equals(key, PreferenceOptions.DebuggerIsolationSnapshot, StringComparison.OrdinalIgnoreCase)
            ? DebugIsolation.Snapshot
            : DebugIsolation.ReadCommitted;
}

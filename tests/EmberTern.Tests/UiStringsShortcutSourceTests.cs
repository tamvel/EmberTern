using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Input;
using EmberTern.App;
using EmberTern.App.Commands;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Pins the sprint's central rule: <b>a keyboard gesture is written down in exactly one place</b>
/// (<see cref="CommandCatalog"/>) and every string that mentions one composes it through
/// <see cref="CommandTip"/>.
///
/// <para>⭐ Why this guard, and why it is shaped this way. Etap 3 moved Format SQL from <c>Alt+F</c> to
/// <c>Ctrl+K</c>; <c>ToolbarFormatSqlTooltip</c> went on reading <c>"Format SQL · Alt+F"</c> and nothing
/// failed — build green, tests green, and a tooltip teaching a shortcut that no longer existed. A hand-typed
/// gesture does not merely duplicate the catalog, it goes stale silently.</para>
///
/// <para>⚠ The check keys on <c>const</c> vs <c>static readonly</c>, not on the runtime text — and it has to.
/// A composed string legitimately CONTAINS <c>" · F7"</c> at run time, so its value proves nothing; what
/// distinguishes it is that a <c>const</c> is a literal by definition. So: no <c>const</c> in
/// <see cref="UiStrings"/> may contain gesture-shaped text, and the exceptions are listed with a reason.</para>
/// </summary>
public sealed class UiStringsShortcutSourceTests
{
    // Gesture-shaped text: "· F7", "· Ctrl+K", "(Shift+F9)", "(F5)". Loose on purpose — a guard that misses
    // a shape it should have caught is worse than one that occasionally needs a new allowlist entry.
    private static readonly Regex GestureShaped = new(
        @"(·\s*(F\d|Ctrl\+|Shift\+|Alt\+|Esc\b|Del\b))|(\((F\d+|Ctrl\+\w|Shift\+F\d)[^)]*\))",
        RegexOptions.Compiled);

    /// <summary>
    /// The only resource entries allowed to name a gesture, each because the gesture is deliberately NOT a
    /// catalog command. Adding an entry here is a decision to be defended, not a way past the test.
    ///
    /// <para>⭐ <b>Re-homed by the localization stage, and the new home is the right one.</b> This guard used
    /// to read <c>const</c> declarations; the words now live in <c>Localization/Strings.resx</c>, so it reads
    /// the ENGLISH RESOURCE VALUES — which is where a hand-typed gesture can actually be written today, and
    /// what a translator will be handed. Scanning the C# would now scan nothing but <c>Loc.Text(nameof(X))</c>
    /// calls: green, and blind.</para>
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new()
    {
        [nameof(UiStrings.ImportCancelTooltip)] =
            "Esc is a universal dismiss owned by every popup, dialog and filter box; the catalog deliberately "
            + "does not declare it (declaring it would invent collisions with all of them).",
        [nameof(UiStrings.ImportSourceUseClipboardTooltip)] =
            "Ctrl+V here means 're-read the clipboard SOURCE', i.e. paste semantics that must yield to a "
            + "focused text box, so it stayed a local handler and has no descriptor to read from.",
        [nameof(UiStrings.ImportRefreshTooltipClipboardNote)] =
            "The same local Ctrl+V as ImportSourceUseClipboardTooltip, named in prose rather than as a "
            + "gesture chip. ⭐ This entry is NEW to the allowlist and its arrival is the guard working: the "
            + "note used to be a fragment concatenated onto a `static readonly` member, and the old guard "
            + "read only `const` fields, so it could not see it. Re-homing the guard onto the English "
            + "resource values surfaced a standing exemption that had never been written down.",
        // (FieldEditEditTooltip was here — "Edit selected field · F2" — until the UX Consistency Pass found it
        //  had no consumer at all: the toolbar Edit button it described was never built. Removing the string
        //  and building the button retired the exemption, and F2 is now CommandId.CollectionEdit.)
    };

    /// <summary>Every English entry, read from the shipped resource set — never a hand-written list.</summary>
    private static IEnumerable<KeyValuePair<string, string>> EnglishEntries()
    {
        var resources = new System.Resources.ResourceManager(
            "EmberTern.App.Localization.Strings", typeof(UiStrings).Assembly);
        var set = resources.GetResourceSet(
            System.Globalization.CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: true)!;
        foreach (System.Collections.DictionaryEntry e in set)
        {
            if (e.Value is string v)
            {
                yield return new KeyValuePair<string, string>((string)e.Key, v);
            }
        }
    }

    [Fact]
    public void NoConstantTypesAGestureByHand()
    {
        var offenders = EnglishEntries()
            .Where(e => !Allowed.ContainsKey(e.Key))
            .Where(e => GestureShaped.IsMatch(e.Value))
            .Select(e => $"{e.Key} = \"{e.Value}\"")
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These English resource entries type a keyboard gesture by hand, so they will go stale the moment "
            + "it is re-bound — and a translator would be handed a key name to translate. Compose them with "
            + "CommandTip.For / .Gesture / .Sentence, or add an allowlist entry stating why the gesture is "
            + "not a catalog command:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // The allowlist must not rot either: an entry naming a member that no longer exists, or one that no
    // longer needs excusing, is a stale exemption that would hide a real regression later.
    [Fact]
    public void EveryAllowlistEntry_StillNamesAConstantThatStillNeedsIt()
    {
        var entries = EnglishEntries().ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

        foreach (var (name, reason) in Allowed)
        {
            Assert.True(entries.ContainsKey(name), $"Allowlisted '{name}' is no longer a resource entry.");
            Assert.True(GestureShaped.IsMatch(entries[name]),
                $"Allowlisted '{name}' no longer contains a gesture — drop the exemption.");
            Assert.False(string.IsNullOrWhiteSpace(reason), $"Allowlisted '{name}' has no reason.");
        }
    }

    // ── The composer ────────────────────────────────────────────────────────────────────────────────

    // The rendering must match what the hand-written strings said, or migrating them would have silently
    // restyled ~20 tooltips. Ctrl+. is the case that rules out KeyGesture.ToString(), which spells the raw
    // enum name and would show the user "Ctrl+OemPeriod".
    [Theory]
    [InlineData(Key.F5, KeyModifiers.None, "F5")]
    [InlineData(Key.F5, KeyModifiers.Shift, "Shift+F5")]
    [InlineData(Key.F5, KeyModifiers.Control | KeyModifiers.Shift, "Ctrl+Shift+F5")]
    [InlineData(Key.F11, KeyModifiers.Shift, "Shift+F11")]
    [InlineData(Key.K, KeyModifiers.Control, "Ctrl+K")]
    [InlineData(Key.OemPeriod, KeyModifiers.Control, "Ctrl+.")]
    [InlineData(Key.Escape, KeyModifiers.None, "Esc")]
    [InlineData(Key.Delete, KeyModifiers.None, "Del")]
    [InlineData(Key.Return, KeyModifiers.Control, "Ctrl+Enter")]
    public void Gesture_RendersTheWayWindowsWritesIt(Key key, KeyModifiers modifiers, string expected)
        => Assert.Equal(expected, CommandTip.Format(new KeyGesture(key, modifiers)));

    [Fact]
    public void For_AppendsTheGesture_AndLeavesTextAloneWhenThereIsNone()
    {
        Assert.Equal("Compile · F7", CommandTip.For(CommandId.Compile, "Compile"));
        Assert.Equal("F7", CommandTip.Gesture(CommandId.Compile));

        // A command with no declared gesture must not produce a dangling separator.
        Assert.Equal(string.Empty, CommandTip.Gesture((CommandId)(-1)));
        Assert.Equal("Something", CommandTip.For((CommandId)(-1), "Something"));
    }

    [Fact]
    public void Sentence_SubstitutesTheGestureMidSentence()
        => Assert.Equal(
            "Restart (Ctrl+Shift+F5) runs it",
            CommandTip.Sentence(CommandId.DebuggerRestart, "Restart ({0}) runs it"));

    // ── The strings the migration produced ──────────────────────────────────────────────────────────

    // Spot-checks that the composed values really carry the ratified gesture. ToolbarFormatSqlTooltip is
    // first because it is the one that was provably wrong before this etap.
    [Fact]
    public void MigratedTooltips_CarryTheCatalogsGesture()
    {
        // ⭐⭐ THIS PAIR OF LINES IS THE WHOLE POINT OF CommandTip, AND IT HAS NOW FIRED IN BOTH DIRECTIONS.
        // It was written because the tooltip read "Format SQL · Alt+F" after etap 3 re-bound the command to
        // Ctrl+K — a hand-typed gesture teaching a key that no longer existed. On 2026-08-03 the user ratified
        // Alt+F back, and this assertion failed again, in the opposite direction, without anyone touching a
        // string. That is exactly the property being bought: the tooltip cannot disagree with the catalog.
        Assert.Equal("Format SQL · Alt+F", UiStrings.ToolbarFormatSqlTooltip);
        // ⚠ And it shows the PRIMARY gesture only. Ctrl+K is a live alternate — it still works — but a tooltip
        // listing both would be teaching two keys for one action in the app's most crowded strip.
        Assert.DoesNotContain("Ctrl+K", UiStrings.ToolbarFormatSqlTooltip, StringComparison.Ordinal);

        Assert.EndsWith("· F7", UiStrings.ProcedureCompileTooltip, StringComparison.Ordinal);
        Assert.EndsWith("· F7", UiStrings.IndexCompileTooltip, StringComparison.Ordinal);
        Assert.Equal("Commit · F6", UiStrings.TransactionCommitTooltip);
        // ⚠ „Rollback” jednym słowem — `docs/design/terminology.md` §1 (M‑4, 2026-08-10).
        Assert.Equal("Rollback · Shift+F6", UiStrings.TransactionRollbackTooltip);
        Assert.Equal("Close active tab · Ctrl+W", UiStrings.ToolbarCloseTabTooltip);
        Assert.Equal("Continue · F5", UiStrings.DebuggerContinueTooltip);
        Assert.Equal("Step Out · Shift+F11", UiStrings.DebuggerStepOutTooltip);
        Assert.Equal("Show code actions · Ctrl+.", UiStrings.CodeActionsTooltip);
        Assert.Equal("Global Search · Ctrl+Shift+F", UiStrings.ToolbarGlobalSearchTooltip);

        // The shortcut chips carry the gesture alone.
        Assert.Equal("F5", UiStrings.ToolbarExecuteHint);
        Assert.Equal("F5", UiStrings.DebuggerLaunchShortcut);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using EmberTern.Core.Formatting;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// ⭐⭐ <b>The date boundary (P5, 2026-08-07): a date shown to a READER follows the reader's culture; a date
/// handed to a PARSER never does.</b> Two rules that point in opposite directions, which is exactly why they
/// need one guard rather than a convention.
/// <para>
/// ⚠ <b>What the audit measured, stated first because it contradicts the obvious guess.</b> The data grids
/// were never tied to the invariant culture. The SQL results grid renders through an Avalonia binding and the
/// Table Data grid through <c>object.ToString()</c>; both were measured live to resolve to
/// <see cref="CultureInfo.CurrentCulture"/>, honouring even this machine's Windows short-date override
/// (<c>pl-PL</c> overridden to <c>yyyy-MM-dd</c> — which is where the "rigid ISO" impression came from, and it
/// is the user's own setting, not ours). ⭐ So the report named a symptom the grids did not cause: what was
/// genuinely hard-coded were quieter surfaces — the About window's release date, spelled with an English
/// month name on every machine, and the parameter-history label's fixed <c>yyyy-MM-dd HH:mm</c>.
/// </para>
/// <para>
/// ⛔ The invariant half is not a leftover to clean up later: a Polish <c>07.08.2026</c> inside a
/// <c>Copy as INSERT</c> statement, a generated <c>.sql</c> file, an import value or a settings backup
/// filename is a broken artefact, not a formatting preference. <see cref="MachineReadablePaths"/> records
/// every such file WITH ITS REASON, so an author who adds one has to say which side of the line it is on.
/// </para>
/// </summary>
public class DatePresentationTests
{
    // ── The rules the display formatter must satisfy ──────────────────────────────────────────

    [Fact]
    public void DisplayFormatting_FollowsTheReadersCulture()
    {
        var value = new DateTime(2026, 8, 7, 14, 5, 9);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // Two cultures that disagree about EVERY part of a date, so a hard-coded pattern cannot pass both.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE", useUserOverride: false);
            var german = (DateTimeDisplay.Date(value), DateTimeDisplay.LongDate(value), DateTimeDisplay.DateAndTime(value));

            CultureInfo.CurrentCulture = new CultureInfo("en-US", useUserOverride: false);
            var american = (DateTimeDisplay.Date(value), DateTimeDisplay.LongDate(value), DateTimeDisplay.DateAndTime(value));

            Assert.NotEqual(german.Item1, american.Item1);
            Assert.NotEqual(german.Item2, american.Item2);
            Assert.NotEqual(german.Item3, american.Item3);

            Assert.Equal("07.08.2026", german.Item1);
            Assert.Equal("8/7/2026", american.Item1);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ⚠ The one deliberate exception, pinned so it reads as a decision rather than an oversight: a LOG column
    // stays 24-hour and fixed-width, because its stamps are compared down a column and a 12-hour culture
    // would make consecutive rows change length.
    [Fact]
    public void LogTime_IsFixedWidthInEveryCulture()
    {
        var value = new DateTime(2026, 8, 7, 14, 5, 9, 123);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "pl-PL" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name, useUserOverride: false);
                Assert.Equal("14:05:09", DateTimeDisplay.LogTime(value));
                Assert.Equal("14:05:09.123", DateTimeDisplay.LogTime(value, withMilliseconds: true));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ⭐⭐ A grid cell is rendered from the COLUMN's Firebird type, never from the value's CLR type — reported
    // 2026-08-08. DATE and TIMESTAMP both arrive as a DateTime, so a renderer that interrogates the value
    // prints an invented `00:00:00` on a date-only column (the defect) or hides a real one on a timestamp.
    // ⚠ The predecessor of this method guessed from `TimeOfDay == 0` and was deleted with it, so the tempting
    // heuristic is no longer available to reach for.
    [Fact]
    public void ACell_IsRenderedFromTheColumnsFirebirdType_NotTheValuesClrType()
    {
        var midnight = new DateTime(2026, 8, 7);
        var withTime = new DateTime(2026, 8, 7, 9, 30, 15);

        // The same CLR value, two column types, two correct answers.
        Assert.Equal(DateTimeDisplay.Date(midnight), DateTimeDisplay.CellForType(midnight, "DATE"));
        Assert.Equal(DateTimeDisplay.DateAndTimeWithSeconds(midnight),
            DateTimeDisplay.CellForType(midnight, "TIMESTAMP"));

        Assert.Equal(DateTimeDisplay.Date(withTime), DateTimeDisplay.CellForType(withTime, "DATE"));
        Assert.Equal(DateTimeDisplay.DateAndTimeWithSeconds(withTime),
            DateTimeDisplay.CellForType(withTime, "TIMESTAMP"));

        Assert.Equal(DateTimeDisplay.Time(new TimeSpan(9, 30, 15)),
            DateTimeDisplay.CellForType(new TimeSpan(9, 30, 15), "TIME"));
    }

    // ⚠ Seconds are part of the timestamp answer: the Table Data grid has always shown them, and dropping
    // them here would be a silent regression wearing a bug fix's clothes.
    [Fact]
    public void ATimestampCell_KeepsItsSeconds()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL", useUserOverride: false);
            Assert.Contains(":15", DateTimeDisplay.CellForType(new DateTime(2026, 8, 7, 9, 30, 15), "TIMESTAMP")!);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ⭐ Returning null for "not mine" is what keeps this out of every other column's business — and a zoned
    // value keeps its own rendering, because a DateTime cannot carry the zone it would be reformatted through.
    [Fact]
    public void ACell_LeavesEverythingElseAlone()
    {
        Assert.Null(DateTimeDisplay.CellForType(null, "DATE"));
        Assert.Null(DateTimeDisplay.CellForType("abc", "VARCHAR(3)"));
        Assert.Null(DateTimeDisplay.CellForType(42, "INTEGER"));
        Assert.Null(DateTimeDisplay.CellForType(new DateTime(2026, 8, 7), firebirdType: null));
        Assert.Null(DateTimeDisplay.CellForType(new DateTime(2026, 8, 7), "TIMESTAMP WITH TIME ZONE"));
    }

    // ⭐ The second documented departure (QA, 2026-08-07): the ENGINE's own form, for a reader who is looking
    // at Firebird's values in the debugger. Culture-independent by design — the panel and isql must print the
    // same string on every machine.
    [Fact]
    public void TheFirebirdForm_IsTheEnginesInEveryCulture()
    {
        var value = new DateTime(2026, 8, 7, 14, 5, 9);
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var name in new[] { "de-DE", "en-US", "pl-PL" })
            {
                CultureInfo.CurrentCulture = new CultureInfo(name, useUserOverride: false);
                Assert.Equal("2026-08-07", DateTimeDisplay.FirebirdDate(value));
                Assert.Equal("2026-08-07 14:05:09", DateTimeDisplay.FirebirdTimestamp(value));
                Assert.Equal("14:05:09", DateTimeDisplay.FirebirdTime(new TimeSpan(14, 5, 9)));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // ⚠ The fraction is kept when it exists, and this is not cosmetic: the same text seeds the debugger's
    // inline-edit box, so dropping it would silently truncate the value the user then commits.
    [Fact]
    public void TheFirebirdForm_KeepsASubSecondValue()
    {
        Assert.Equal("2026-08-07 14:05:09",
            DateTimeDisplay.FirebirdTimestamp(new DateTime(2026, 8, 7, 14, 5, 9)));
        Assert.Equal("2026-08-07 14:05:09.5",
            DateTimeDisplay.FirebirdTimestamp(new DateTime(2026, 8, 7, 14, 5, 9).AddMilliseconds(500)));
    }

    // ⭐ Returning null for a non-date is the seam that keeps this out of everyone else's business: the
    // debugger renders numbers under the invariant culture (the harness's literal convention), and that
    // decision must stay where it lives.
    [Fact]
    public void TheFirebirdForm_AnswersOnlyForDateKinds()
    {
        Assert.Null(DateTimeDisplay.FirebirdValue(null));
        Assert.Null(DateTimeDisplay.FirebirdValue(42));
        Assert.Null(DateTimeDisplay.FirebirdValue("2026-08-07"));

        var midnight = new DateTime(2026, 8, 7);
        Assert.Equal("2026-08-07", DateTimeDisplay.FirebirdValue(midnight, "DATE"));
        Assert.Equal("2026-08-07 00:00:00", DateTimeDisplay.FirebirdValue(midnight, "TIMESTAMP"));
        // No declared type (a Watch on an arbitrary expression): the value is the only evidence there is.
        Assert.Equal("2026-08-07", DateTimeDisplay.FirebirdValue(midnight));
    }

    // ── The boundary: who is still allowed to format a date invariantly ───────────────────────

    /// <summary>
    /// Every source file permitted to build a date/time string under the invariant culture, each with the
    /// contract that requires it. ⚠ A reason is mandatory: the value of this list is not the names in it but
    /// the fact that adding one forces the author to state which side of the boundary the code is on.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> MachineReadablePaths =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/EmberTern.Core/Export/Sql/SqlLiteralWriter.cs"] =
                "Copy as INSERT / UPDATE and .sql export emit SQL literals — a runnable statement, parsed by Firebird.",
            ["src/EmberTern.Core/Import/ImportValueConverter.cs"] =
                "Converts a source cell into the value written to the database.",
            ["src/EmberTern.Core/Settings/ApplicationSettingsStore.cs"] =
                "Timestamps a backup FILENAME — sortable, and it must not contain a culture's date separator.",
            ["src/EmberTern.Core/Formatting/DateTimeDisplay.cs"] =
                "Owns the boundary; its two documented departures from the reader's culture live here — LogTime "
                + "(fixed-width, for log COLUMNS) and the Firebird* family (the engine's own form, for the "
                + "debugger, where the value is compared against isql and the stepped source).",
            ["src/EmberTern.App/AppInfo.cs"] =
                "PARSES the ISO release date declared in Directory.Build.props — a build contract, not a display.",
            ["src/EmberTern.App/Program.cs"] =
                "Crash-log stamp in EmberTern-debug.log — read by us, and diffed across machines.",
            ["src/EmberTern.App/Diagnostics/TreeDiagnostics.cs"] =
                "Developer diagnostic log + its filename (EMBERTERN_TREE_DIAG).",
            ["src/EmberTern.App/ViewModels/ExecuteProcedureDialogViewModel.cs"] =
                "Resolve() renders a parameter VALUE for the SQL it is bound into; the visible history label "
                + "goes through DateTimeDisplay.",
        };

    // Matches a date/time custom format string being built: ToString("…yyyy…") and friends.
    private static readonly Regex DatePattern =
        new(@"ToString\(\s*@?""[^""]*(yyyy|MMMM|MMM| MM|dd|HH)[^""]*""", RegexOptions.Compiled);

    [Fact]
    public void NoUserFacingSurface_FormatsADateInvariantly()
    {
        var offenders = new List<string>();

        foreach (var file in SourceFiles())
        {
            var relative = Relative(file);
            if (MachineReadablePaths.ContainsKey(relative)) continue;

            var text = File.ReadAllText(file);
            foreach (Match m in DatePattern.Matches(text))
            {
                // Only the invariant form is a finding; a call with no provider, or with CurrentCulture,
                // already follows the reader.
                int close = text.IndexOf(')', m.Index);
                var call = close > m.Index ? text.Substring(m.Index, close - m.Index) : m.Value;
                if (call.Contains("InvariantCulture", StringComparison.Ordinal))
                {
                    offenders.Add($"{relative}: {call.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A date is being formatted invariantly outside the recorded machine-readable paths. If the value "
            + "is shown to a user, route it through EmberTern.Core.Formatting.DateTimeDisplay; if it is parsed "
            + "or written to a file, add the file to MachineReadablePaths WITH ITS REASON.\n  "
            + string.Join("\n  ", offenders));
    }

    // ⚠ The other direction, and the one that actually protects data: every recorded path must still BE
    // invariant. Without it the allowlist would silently become permission to drift the wrong way.
    [Fact]
    public void EveryRecordedMachineReadablePath_StillExists()
    {
        foreach (var (relative, reason) in MachineReadablePaths)
        {
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{relative} is listed without a reason.");
            Assert.True(File.Exists(Path.Combine(RepoRoot(), relative)),
                $"{relative} is recorded as a machine-readable date path but no longer exists — a stale entry "
                + "is an exemption nobody is checking.");
        }
    }

    private static IEnumerable<string> SourceFiles()
        => Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                            StringComparison.Ordinal));

    private static string Relative(string absolute)
        => Path.GetRelativePath(RepoRoot(), absolute).Replace('\\', '/');

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "EmberTern.slnx")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        return dir!;
    }
}

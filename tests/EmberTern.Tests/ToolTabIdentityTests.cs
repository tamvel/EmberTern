using System;
using System.IO;
using System.Linq;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// A TOOL TAB'S ICON CARRIES THE TOOL ROLE (S2), NEVER AN OBJECT-KIND COLOUR (S1).
///
/// <para>⭐ This is the colour language's R‑6 („wejście do narzędzia”) read at the tab strip. The rule was
/// ratified when the Security Manager's TOOLBAR button was migrated off <c>IconColor_Role</c> onto
/// <c>AccentBrush</c> at closure K‑final, with the reason written in place: when an element would carry both
/// the KIND and the EFFECT, the effect wins.</para>
///
/// <para>⚠⚠ THE DEFECT THIS PINS WAS A HALF-APPLIED DECISION, NOT A RENDERING BUG. The toolbar button was
/// migrated and the tab was not, so five tool tabs read <c>AccentBrush</c> while the sixth still read
/// <c>MetadataNodeViewModel.ResourceKeyFor(User|Role)</c> — <c>#90A4AE</c>, which at 14 px reads as white.
/// Nothing failed: the icon rendered perfectly, in a colour that meant something else. That is #340's shape
/// (the decision lives in one place, the register in another, and only the register ever gets closed), and it
/// is why the guard is worth its cost: a colour that is merely WRONG looks exactly like a colour that is right.</para>
///
/// <para>⭐ It guards the PREMISE, not the policy (#322): it asserts that every factory for a tab which is a
/// TOOL resolves its icon colour to the accent role, and it reads the production source rather than a
/// transcribed list of expected values (#333). A seventh tool tab that forgets the role fails here.</para>
///
/// <para>⚠ Geometry is deliberately NOT constrained. The Security Manager tab keeps a User/Role glyph chosen
/// from the context it was opened on — colour answers „what kind of thing is this tab”, the glyph answers
/// „what is it open on”. Two axes, two answers; conflating them is what produced the defect.</para>
/// </summary>
public class ToolTabIdentityTests
{
    /// <summary>The tool tabs: surfaces opened from the toolbar/menus that ARE a tool, as opposed to an
    /// object editor (whose icon correctly carries the object's kind colour).</summary>
    private static readonly string[] ToolTabFactories =
    [
        "CreateSecurityManager",
        "CreateTraceMonitor",
        "CreateSessionManager",
        "CreateGlobalSearch",
        "CreateScriptExecutor",
        "CreateDataImport",
    ];

    [Fact]
    public void EveryToolTab_CarriesTheToolRoleColour_NotAnObjectKindColour()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "EmberTern.App", "ViewModels", "WorkspaceTabViewModel.cs"));

        foreach (var factory in ToolTabFactories)
        {
            var body = FactoryBody(source, factory);
            var assignment = body
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("IconResourceKey", StringComparison.Ordinal));

            Assert.True(assignment is not null,
                $"{factory} sets no IconResourceKey — a tool tab must declare its icon colour.");

            Assert.True(
                assignment!.Contains("\"AccentBrush\"", StringComparison.Ordinal),
                $"""
                 {factory} does not carry the tool role colour.
                   found: {assignment}
                 A tool tab's icon colour is the ACCENT (colour-language R‑6 "wejście do narzędzia", S2).
                 An object-kind colour (IconColor_*, MetadataNodeViewModel.ResourceKeyFor) belongs to an
                 OBJECT EDITOR tab, not to a tool. This is the exact substitution that left the Security
                 Manager tab painting #90A4AE — a colour that renders perfectly and means the wrong thing.
                 """);
        }
    }

    /// <summary>The counterpart, so the rule above cannot be satisfied by painting EVERYTHING accent: an
    /// object editor's tab still carries its object's kind colour, which is what makes a table tab and a
    /// procedure tab distinguishable at a glance.</summary>
    [Fact]
    public void AnObjectEditorTab_StillCarriesItsObjectKindColour()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "EmberTern.App", "ViewModels", "WorkspaceTabViewModel.cs"));

        var body = FactoryBody(source, "CreateTableDetail");
        Assert.Contains("MetadataNodeViewModel.ResourceKeyFor", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IconResourceKey = \"AccentBrush\"", body, StringComparison.Ordinal);
    }

    /// <summary>The text of one factory method: from its signature to the next one. Deliberately crude — it
    /// only has to isolate an assignment, and a parser here would be a second thing to maintain.</summary>
    private static string FactoryBody(string source, string factory)
    {
        var start = source.IndexOf($"WorkspaceTabViewModel {factory}(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Factory {factory} not found — the guard's own premise moved.");

        var next = source.IndexOf("public static WorkspaceTabViewModel Create", start + 1, StringComparison.Ordinal);
        return next < 0 ? source[start..] : source[start..next];
    }

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

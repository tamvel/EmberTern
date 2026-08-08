using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stabilization sprint S-1a + S-3 (2026-08-05): <b>every editable <c>DataGrid</c> goes through
/// <c>EditableGridBehavior.Attach</c></b> — the one seam carrying the Enter gesture and the cell-editor
/// height role.
///
/// <para>⭐⭐ THIS GUARD IS THE ACTUAL FIX, not the behaviours it protects. Two separate user reports —
/// "Enter does not start editing" and "the TextBox in Table is still too low" — had ONE cause: the set of
/// editable definition grids was implicit. It was <em>whoever calls <c>FieldGridColumns.Build</c></em>, which
/// is where the <c>field-grid</c> class used to be applied, so Table Detail Fields, New Table Fields and View
/// Detail Columns — which declare their columns in XAML and only insert the shared picker — silently missed
/// it. The old comment on that class even described the scope as "a class on the grid, applied in one place";
/// that was true, and it was the defect, because the one place was not every place. Making the set explicit
/// and MACHINE-CHECKED is what stops the next grid from being missed.</para>
///
/// <para>⚠⚠ THE GUARD CANNOT KEY ON <c>IsReadOnly="False"</c>, and that is measured: Table Detail's fields
/// grid writes <c>IsReadOnly="{Binding IsFieldsReadOnly}"</c>, and <c>DataGrid.IsReadOnly</c> defaults to
/// false anyway — so a scan for that attribute misses the very grid that was reported. Same shape as gotcha
/// #285: a measurement by CARRIER cannot answer a question about the ROLE. It therefore works the other way
/// round — every named <c>DataGrid</c> in a metadata-editor view is either attached or listed below with a
/// reason.</para>
///
/// <para>⚠ Reads source files and constructs nothing, so it belongs to the MAIN test partition. The
/// behavioural half (Enter actually beginning an edit) lives in <see cref="EditableGridEnterTests"/>, which
/// needs a headless session.</para>
/// </summary>
public class EditableGridSeamTests
{
    /// <summary>
    /// The grids that are deliberately NOT attached, each with the reason. A read-only grid has nothing to
    /// begin editing and no in-cell editor to size, so attaching it would be inert — and an inert call reads
    /// to the next author as a real one (§15.7's lesson).
    /// </summary>
    /// <remarks>
    /// ⚠ Only grids whose markup does NOT already say <c>IsReadOnly="True"</c> need an entry — that literal
    /// is checked automatically below, because it is a claim the markup makes about itself and a second copy
    /// of it here would go stale silently. What this dictionary is for is the grid that LOOKS editable (no
    /// literal, or a binding) and deliberately is not attached.
    /// </remarks>
    private static readonly Dictionary<string, string> NotAttached = new(StringComparer.Ordinal)
    {
        ["ProcResultGrid"] = "Execute Procedure RESULTS — a materialised result set, not a definition; its "
            + "columns are built per result and it is never edited in place.",
        ["DataPreviewGrid|ViewDetailTabView"] = "View DATA — a view's rows are not editable here (the grid is "
            + "server-paged read-only output; Table Detail's namesake IS attached, as Data).",
    };

    [Fact]
    public void EveryEditableGrid_InAMetadataEditor_GoesThroughTheSeam()
    {
        var viewsDir = Path.Combine(RepositoryRoot(), "src", "EmberTern.App", "Views");
        var editors = new[]
        {
            "TableDetailTabView", "NewTableTabView", "ViewDetailTabView",
            "ProcedureDetailTabView", "FunctionDetailTabView", "TriggerDetailTabView",
        };

        var unattached = new List<string>();
        foreach (var editor in editors)
        {
            var axaml = File.ReadAllText(Path.Combine(viewsDir, editor + ".axaml"));
            var codeBehind = File.ReadAllText(Path.Combine(viewsDir, editor + ".axaml.cs"));

            foreach (Match m in Regex.Matches(axaml, @"<DataGrid\b(?<attrs>[^>]*?)x:Name=""(?<name>[A-Za-z0-9_]+)""(?<rest>[^>]*)"))
            {
                var name = m.Groups["name"].Value;
                if (NotAttached.ContainsKey(name) || NotAttached.ContainsKey($"{name}|{editor}")) continue;

                // ⭐ A grid whose markup states IsReadOnly="True" needs no entry above: it has nothing to
                // begin editing and no in-cell editor to size, and the markup already says so. Read from the
                // markup rather than copied into this file, so it cannot go stale — but ONLY the literal
                // counts: a BINDING (Table Detail's `{Binding IsFieldsReadOnly}`) means the grid is editable
                // some of the time, which is exactly the case that must be attached.
                var element = m.Groups["attrs"].Value + m.Groups["rest"].Value;
                if (element.Contains("IsReadOnly=\"True\"", StringComparison.Ordinal)) continue;

                // Attached either by name (FindControl in the ctor) or via a field that holds it.
                var attached = codeBehind.Contains("EditableGridBehavior.Attach", StringComparison.Ordinal)
                               && MentionsGrid(codeBehind, name);
                if (!attached) unattached.Add($"{editor}.{name}");
            }
        }

        Assert.True(
            unattached.Count == 0,
            "These editable grids do not go through EditableGridBehavior.Attach, so they get neither the Enter "
            + "gesture nor the cell-editor height role — the exact defect S-1a/S-3 fixed. Attach them, or add "
            + "them to NotAttached WITH A REASON: " + string.Join(", ", unattached));
    }

    // A grid reaches Attach through whatever identifier the code-behind bound it to. Three shapes exist in
    // these views — a field (`_x = this.FindControl<DataGrid>("Name")`), a pattern variable
    // (`if (this.FindControl<DataGrid>("Name") is { } x)`), and the x:Name used directly — so the check
    // resolves the identifier and then requires an Attach call on THAT identifier.
    //
    // ⚠⚠ It deliberately no longer accepts "some Attach call exists somewhere in this file", which is what an
    // earlier fallback did (it keyed on the two-argument call shape and matched any grid in the file). That
    // fallback would answer "yes" for a grid nobody attached, as long as a sibling was attached — a guard
    // whose green means less than it looks.
    private static bool MentionsGrid(string codeBehind, string name)
    {
        if (!codeBehind.Contains($"\"{name}\"", StringComparison.Ordinal)) return false;

        var find = @"this\.FindControl<DataGrid>\(""" + Regex.Escape(name) + @"""\)";
        var identifiers = new List<string> { name };

        if (Regex.Match(codeBehind, @"(?<field>_[A-Za-z0-9_]+)\s*=\s*" + find) is { Success: true } assigned)
            identifiers.Add(assigned.Groups["field"].Value);

        if (Regex.Match(codeBehind, find + @"\s+is\s*\{\s*\}\s*(?<local>[A-Za-z0-9_]+)") is { Success: true } pattern)
            identifiers.Add(pattern.Groups["local"].Value);

        return identifiers.Any(id =>
            Regex.IsMatch(codeBehind, @"EditableGridBehavior\.Attach\(\s*" + Regex.Escape(id) + @"\s*[,)]"));
    }

    /// <summary>
    /// ⛔ <c>FieldGridColumns</c> must not apply the height class again. Two owners of one class means the
    /// grids that go through only one of them are silently different — which is the defect this sprint spent
    /// two rounds locating.
    /// </summary>
    [Fact]
    public void FieldGridColumns_DoesNotApplyTheHeightClassItself()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Views", "FieldGridColumns.cs"));

        Assert.DoesNotContain("Classes.Add(", source);
        Assert.DoesNotContain("\"field-grid\"", source);
    }

    /// <summary>
    /// ⚠⚠ REPLACES <c>TableData_IsAttachedAsAData_Grid_NotAsADefinitionOne</c> (2026-08-07), which pinned the
    /// OPPOSITE and called this change "the tempting simplification". It was not a simplification, it was the
    /// reported defect: that test rested on "a 24 px minimum on a data grid's in-cell editor grows every row",
    /// a statement about a data grid in general that nobody checked against the one grid it governed. The next
    /// test measures the premise instead of asserting it.
    /// </summary>
    [Fact]
    public void TableData_GoesThroughTheSameSeam_AsTheDefinitionGrids()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Views", "TableDetailTabView.axaml.cs"));

        Assert.Matches(@"EditableGridBehavior\.Attach\(\s*_dataPreviewGrid\s*\)", source);
        Assert.Matches(@"EditableGridBehavior\.Attach\(\s*_fieldsGrid\s*\)", source);

        // ⛔ No second answer about height may come back through a parameter. The seam carries ONE rule now,
        // so a re-introduced "kind" is the shape of the defect returning.
        Assert.DoesNotContain("EditableGridKind", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ THE PREMISE THE SEAM RESTS ON, MEASURED AGAINST THE MARKUP RATHER THAN ASSERTED IN PROSE: an
    /// editable grid's row must be able to CARRY a <c>Size.Control</c> in-cell editor. That is what makes
    /// granting the height role free of layout shift — the row declares a fixed <c>Height</c>, so it cannot
    /// grow from its content, and what is left after the cell padding still exceeds the editor's minimum.
    ///
    /// <para>⚠ This is deliberately a guard over the three numbers that must stay in relation to each other
    /// (row height, cell padding, <c>Size.Control</c>), each read from where it actually lives — not a copy of
    /// today's values. Lower the row to 26 or raise <c>Size.Control</c> to 30 and this fails, which is exactly
    /// when the seam's assumption stops holding.</para>
    ///
    /// <para>⚠⚠ WIDENED IN M4 / C‑1, AND THE WIDENING IS THE POINT. The earlier version checked exactly one
    /// grid (Table Data) and read its height as a LITERAL — so the moment those four grids were unified onto
    /// the <c>Size.Row.GridEdit</c> role, a guard written to protect them stopped being able to read them at
    /// all. ⭐ Two lessons, both already paid for elsewhere in this project: a guard that keys on the CARRIER
    /// (a literal) cannot answer a question about the ROLE (#285), and a guard stated about one member of a
    /// class silently says nothing about its siblings (#322). It now resolves <c>{DynamicResource}</c> against
    /// the catalog and runs over EVERY editable definition grid.</para>
    /// </summary>
    [Theory]
    [InlineData("TableDetailTabView", @"DataGrid\.data-edit\s+DataGridRow")]
    [InlineData("TableDetailTabView", @"DataGrid#FieldsGrid\s+DataGridRow")]
    [InlineData("NewTableTabView", "DataGridRow")]
    [InlineData("ProcedureDetailTabView", "DataGridRow")]
    [InlineData("FunctionDetailTabView", "DataGridRow")]
    [InlineData("TriggerDetailTabView", "DataGridRow")]
    public void EveryEditableGridRow_DeclaresAHeightThatCanCarryTheCellEditor(string view, string selector)
    {
        var markup = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Views", view + ".axaml"));
        var tokens = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Themes", "Tokens.axaml"));

        // A FIXED Height, not a MinHeight — that distinction is the whole safety argument, so it is asserted
        // by the pattern rather than left to the number.
        var row = Regex.Match(
            markup,
            @"Selector=""" + selector + @"""\s*>\s*<Setter\s+Property=""Height""\s+Value=""(?<h>[^""]+)""");
        Assert.True(row.Success,
            $"{view} / {selector} no longer declares a fixed Height. EditableGridBehavior grants every "
            + "editable grid the Size.Control cell-editor role on the ground that its row cannot grow from "
            + "its content — re-measure before changing it.");

        var padding = Regex.Match(markup, @"Selector=""DataGridCell""\s*>\s*<Setter\s+Property=""Padding""\s+Value=""\d+\s+(?<v>\d+)""");
        Assert.True(padding.Success, "The view's DataGridCell padding is what the row height must pay for first.");

        var available = Resolve(row.Groups["h"].Value, tokens) - (2 * int.Parse(padding.Groups["v"].Value));
        var required = Resolve("{DynamicResource Size.Control}", tokens);

        Assert.True(available >= required,
            $"{view} / {selector} leaves {available} px for an editor whose role asks for {required} px, so "
            + "granting the height role there WOULD grow the row on entering edit mode — the layout shift "
            + "§13.3 forbids. Either raise the row or stop granting the role to this grid.");
    }

    /// <summary>
    /// A declared size is either a literal or a role. ⭐ Resolving the role here rather than accepting only a
    /// literal is what lets the guard survive the migration it exists to protect — and it fails loudly on a
    /// key the catalog does not define, which XAML itself would not (a missing <c>{DynamicResource}</c>
    /// silently leaves the property at its inherited value — trap 1).
    /// </summary>
    private static double Resolve(string value, string tokens)
    {
        var role = Regex.Match(value, @"^\{DynamicResource\s+(?<key>[^}]+)\}$");
        if (!role.Success)
        {
            return double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        var key = role.Groups["key"].Value.Trim();
        var declared = Regex.Match(tokens, @"x:Key=""" + Regex.Escape(key) + @"""\s*>\s*(?<v>[\d.]+)\s*<");
        Assert.True(declared.Success, $"`{key}` is read by a view but not declared in Tokens.axaml.");
        return double.Parse(declared.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EmberTern.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}

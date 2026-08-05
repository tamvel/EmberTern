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

    // A grid reaches Attach either directly by its x:Name (FindControl inline) or through the field the
    // constructor assigned it to. Both shapes exist in these views, so the check accepts either — what it
    // must not accept is a grid the code-behind never mentions at all.
    private static bool MentionsGrid(string codeBehind, string name)
    {
        if (!codeBehind.Contains($"\"{name}\"", StringComparison.Ordinal)) return false;

        // Find the field (if any) that this x:Name was assigned to: `_x = this.FindControl<DataGrid>("Name")`.
        var assign = Regex.Match(codeBehind, @"(?<field>_[A-Za-z0-9_]+)\s*=\s*this\.FindControl<DataGrid>\(""" + Regex.Escape(name) + @"""\)");
        var token = assign.Success ? assign.Groups["field"].Value : name;
        return Regex.IsMatch(codeBehind, @"EditableGridBehavior\.Attach\(\s*" + Regex.Escape(token) + @"\b")
               || Regex.IsMatch(codeBehind, @"EditableGridBehavior\.Attach\(\s*[A-Za-z0-9_]+\s*,")
                  && Regex.IsMatch(codeBehind, @"FindControl<DataGrid>\(""" + Regex.Escape(name) + @"""\)\s*is\s*\{");
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
    /// ⚠ The DATA grid must keep the Enter gesture WITHOUT the height role. Pinned because the tempting
    /// simplification — "attach everything the same way" — reintroduces a measured layout shift: a 24 px
    /// minimum on a data grid's in-cell editor grows every row the moment editing starts (M2b step 7).
    /// </summary>
    [Fact]
    public void TableData_IsAttachedAsAData_Grid_NotAsADefinitionOne()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "EmberTern.App", "Views", "TableDetailTabView.axaml.cs"));

        Assert.Matches(@"EditableGridBehavior\.Attach\(\s*_dataPreviewGrid\s*,\s*EditableGridKind\.Data\s*\)", source);
        Assert.Matches(@"EditableGridBehavior\.Attach\(\s*_fieldsGrid\s*,\s*EditableGridKind\.Definition\s*\)", source);
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

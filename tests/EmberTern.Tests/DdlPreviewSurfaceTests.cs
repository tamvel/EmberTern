using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// EVERY LIVE DDL PREVIEW IS THE APP'S ONE READ-ONLY SQL SURFACE — never a plain read-only
/// <c>TextBox</c>.
///
/// <para>⭐ Measured before the fix: twelve surfaces already went through
/// <c>SqlEditorBehavior.AttachReadOnlyHighlighting</c>, while <b>five</b> — New Table plus the
/// Check-constraint, PK/Unique, Foreign-key and Index dialogs — rendered generated SQL as uncoloured
/// monospace text. They were byte-identical copies of one shape, which is why fixing only the one that
/// was reported would have left four screens disagreeing with the rest of the application (R7).</para>
///
/// <para>⚠ This guards the SOURCE rather than a render, and that is the honest tool here: a screenshot
/// cannot tell "no highlighting attached" from "this statement happens to contain no keyword", whereas
/// the wiring either exists or it does not. The behavioural half — that attaching really produces a
/// theme-matched palette — is <c>ReadOnlyPreview_TakesTheThemeSyntaxDefinition</c> in
/// <c>DesignTokenApplicationTests</c>.</para>
///
/// <para>⚠⚠ A sixth preview added as a <c>TextBox</c> fails here. That is the point: the defect was not
/// that someone chose the wrong control once, it was that the right way to do it lived only in the other
/// twelve files and nothing said so.</para>
/// </summary>
public class DdlPreviewSurfaceTests
{
    /// <summary>view file → its code-behind. The five surfaces whose preview is a LIVE, form-driven DDL
    /// (an <see cref="EmberTern.App.ViewModels.IDdlPreviewSource"/>), as opposed to the object editors'
    /// reconstructed-from-the-database DDL tabs, which have their own (already correct) wiring.</summary>
    private static readonly Dictionary<string, string> LivePreviewSurfaces = new()
    {
        ["NewTableTabView"] = "NewTableTabView.axaml.cs",
        ["CheckConstraintDialog"] = "CheckConstraintDialog.axaml.cs",
        ["ConstraintFieldDialog"] = "ConstraintFieldDialog.axaml.cs",
        ["ForeignKeyDialog"] = "ForeignKeyDialog.axaml.cs",
        ["IndexDialog"] = "IndexDialog.axaml.cs",
    };

    [Fact]
    public void EveryLiveDdlPreview_UsesTheSharedReadOnlySqlSurface()
    {
        foreach (var (view, codeBehind) in LivePreviewSurfaces)
        {
            var xaml = File.ReadAllText(Path.Combine(ViewsDirectory(), view + ".axaml"));

            Assert.True(
                xaml.Contains("ae:TextEditor", StringComparison.Ordinal)
                && xaml.Contains("x:Name=\"DdlEditor\"", StringComparison.Ordinal),
                $"""
                 {view}.axaml does not host the shared read-only SQL surface.
                 A Live DDL preview is an AvaloniaEdit `ae:TextEditor` named "DdlEditor", wired by
                 SqlEditorBehavior.AttachDdlPreview — never a read-only TextBox, which would make it the
                 only generated SQL in the application without colour.
                 """);

            var cs = File.ReadAllText(Path.Combine(ViewsDirectory(), codeBehind));
            Assert.True(
                cs.Contains("AttachDdlPreview", StringComparison.Ordinal),
                $"""
                 {codeBehind} never calls SqlEditorBehavior.AttachDdlPreview.
                 ⚠ The XAML alone paints NOTHING: a bare TextEditor has no syntax definition and no text
                 (the DDL is pushed, not bound — a two-way TextEditor.Text binding is flaky). This is the
                 failure mode that looks like a working control showing an empty box.
                 """);
        }
    }

    /// <summary>The counterpart that stops the rule above from being satisfied by deleting the previews:
    /// each of the five view models still declares it offers a live DDL preview.</summary>
    [Fact]
    public void EveryLiveDdlPreviewViewModel_DeclaresTheSharedContract()
    {
        var viewModels = new[]
        {
            "NewTableTabViewModel", "CheckConstraintDialogViewModel", "ConstraintFieldDialogViewModel",
            "ForeignKeyDialogViewModel", "IndexDialogViewModel",
        };

        foreach (var vm in viewModels)
        {
            var source = File.ReadAllText(Path.Combine(
                RepoRoot(), "src", "EmberTern.App", "ViewModels", vm + ".cs"));

            Assert.True(source.Contains("IDdlPreviewSource", StringComparison.Ordinal),
                $"{vm} no longer declares IDdlPreviewSource — AttachDdlPreview resolves its text through "
                + "that interface, so dropping it makes the preview silently empty rather than failing to build.");
        }
    }

    private static string ViewsDirectory()
        => Path.Combine(RepoRoot(), "src", "EmberTern.App", "Views");

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

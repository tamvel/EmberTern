using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The document-mutation contract (audit A-04): every change to a user's document either has no drift window,
/// or goes through <c>TextEditApplier</c>.
/// <para>
/// <b>Why this test exists at all.</b> <c>TextEditApplier</c> used to document itself as "the one owner of every
/// change EmberTern makes to a user document … no second path that writes to a TextDocument", and that was not
/// true — thirteen files call <c>Document.Replace</c>/<c>Insert</c> directly. None of them is dangerous today,
/// and the audit that found it agreed: each is the synchronous response to the very keystroke or command that
/// produced it, computed and applied in the same turn, reversible by one Ctrl+Z. The real defect was that
/// nothing prevented the NEXT feature from adding a path that skips the drift check while the comment went on
/// claiming otherwise.
/// </para>
/// <para>
/// <b>What is actually being protected.</b> An <i>assisted</i> edit is computed from a <c>SemanticModel</c> at
/// one moment and applied at another; the user may have typed in between, so its offsets must be re-verified
/// against the document (<c>TextEdit.ExpectedOldText</c>). That re-verification lives in exactly one place. This
/// test makes the boundary a build-time fact rather than a convention: adding a direct mutation is still
/// allowed, but it now requires editing this list, which is where the question "does this edit have a drift
/// window?" gets asked.
/// </para>
/// <para>
/// <b>This is a reviewer's tripwire, not a ban.</b> A failure is not "you did something wrong" — it is "state
/// which of the two kinds of edit this is". If it has no drift window, add the file with a reason. If it does,
/// route it through <c>TextEditApplier</c>.
/// </para>
/// </summary>
public class DocumentMutationContractTests
{
    // Mutating calls on an AvaloniaEdit TextDocument. Matches the receiver by name — `doc`, `document`, or
    // anything ending in `Document` (`editor.Document`, `textArea.Document`, `ed.Document`, …) — which is how
    // every current call site spells it. A future caller who aliases the document to an unrelated name evades
    // this, so the pattern is a strong tripwire rather than a proof; that is the honest description of it.
    private static readonly Regex Mutation =
        new(@"\b(doc|[A-Za-z_.]*[Dd]ocument)\.(Replace|Insert|Remove)\(", RegexOptions.Compiled);

    /// <summary>
    /// Every file permitted to mutate a document directly, with the reason it needs no drift check. Keyed by
    /// file name; the value is documentation for whoever reads a failure.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal)
    {
        ["TextEditApplier.cs"] =
            "THE owner of assisted edits. This is the drift check, so it is the one file that legitimately writes " +
            "model-derived edits.",

        // ─── Explicit user commands. Synchronous, one undo unit, offsets from the same turn. ───
        ["EditorSearch.cs"] =
            "Format / Comment / Uncomment from the editor context menu. The user asked, now; the formatter also " +
            "carries its own §0 lexeme-preservation invariant, so it either reproduces every token or changes nothing.",
        ["MainWindow.axaml.cs"] =
            "The SQL Editor's Format / Comment commands — same shape as EditorSearch.",
        ["ProcedureDetailTabView.axaml.cs"] = "Object-editor Format / Comment command.",
        ["FunctionDetailTabView.axaml.cs"] = "Object-editor Format / Comment command.",
        ["TriggerDetailTabView.axaml.cs"] = "Object-editor Format / Comment command.",
        ["ViewDetailTabView.axaml.cs"] = "Object-editor Format / Comment command.",
        ["PackageDetailTabView.axaml.cs"] = "Object-editor Format / Comment command.",
        ["SqlSnippetDropTarget.cs"] =
            "Drag-and-drop snippet insertion at the drop point. An insertion, at a position the user chose with " +
            "the pointer in that same gesture — it overwrites nothing.",

        // ─── Typing mechanics. Computed and applied inside one key event. ───
        ["LanguageExpansionController.cs"] =
            "Tab expansion of a construct the user is mid-typing. The edit shown in the hint IS the edit applied " +
            "(one CurrentEdit()), and it is applied in the keystroke that requested it.",
        ["TypingErgonomicsController.cs"] =
            "begin…end and bracket/quote pairing, inside the key event that triggered it.",
        ["SqlIndentationStrategy.cs"] =
            "Auto-indent of the current line via AvaloniaEdit's own IIndentationStrategy seam.",
        ["SqlCompletionData.cs"] =
            "Completion insertion over the completion segment — AvaloniaEdit's own ICompletionData.Complete " +
            "contract, which hands us the segment to replace.",
    };

    [Fact]
    public void OnlyTheApprovedFiles_MutateADocumentDirectly()
    {
        var appRoot = Path.Combine(RepositoryRoot(), "src", "EmberTern.App");
        Assert.True(Directory.Exists(appRoot), $"Could not locate the App project at {appRoot}");

        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (Allowed.ContainsKey(name)) continue;

            var text = File.ReadAllText(file);
            foreach (Match m in Mutation.Matches(text))
            {
                var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetRelativePath(appRoot, file)}:{line} — {m.Value}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A new direct document mutation appeared outside TextEditApplier:\n  " +
            string.Join("\n  ", offenders) +
            "\n\nDecide which kind of edit it is (see this test's class comment):\n" +
            "  • Computed from the SemanticModel and applied later (a DRIFT WINDOW) ⇒ route it through\n" +
            "    TextEditApplier, so the ExpectedOldText check runs. This is not optional: without it the\n" +
            "    edit can land on text the user has since changed (Architecture rule #11).\n" +
            "  • Computed and applied inside the same keystroke or command, against offsets that cannot have\n" +
            "    moved ⇒ add the file to `Allowed` above WITH the reason, which is what makes the next\n" +
            "    reviewer's job possible.");
    }

    [Fact]
    public void TheAllowList_HasNoStaleEntries()
    {
        // A list that outlives its entries stops describing anything. If a file no longer mutates documents (or
        // no longer exists), its exemption should go with it — otherwise the exemption silently pre-approves a
        // future file that happens to reuse the name.
        var appRoot = Path.Combine(RepositoryRoot(), "src", "EmberTern.App");
        var actuallyMutating = Directory
            .EnumerateFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => Mutation.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToHashSet(StringComparer.Ordinal);

        var stale = Allowed.Keys.Where(k => !actuallyMutating.Contains(k)).ToList();

        Assert.True(stale.Count == 0,
            "These files are exempted but no longer mutate a document — remove them from `Allowed`:\n  " +
            string.Join("\n  ", stale));
    }

    [Fact]
    public void EveryExemption_StatesAReason()
    {
        // The reason is the entire value of the list. An exemption without one is a hole with a name on it.
        var unexplained = Allowed.Where(kv => kv.Value.Length < 30).Select(kv => kv.Key).ToList();
        Assert.True(unexplained.Count == 0,
            "These exemptions need a real reason: " + string.Join(", ", unexplained));
    }

    // Walks up from the test binary to the directory holding EmberTern.slnx. The test reads SOURCE, so it needs
    // the repository rather than the output folder.
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

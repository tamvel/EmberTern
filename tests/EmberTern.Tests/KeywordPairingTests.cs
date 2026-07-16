using EmberTern.Core.Sql;
using EmberTern.Core.Sql.Language.Ergonomics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Typing Ergonomics — the `begin … end` keyword pair (design §3.1). Pure and synchronous: a function
/// of (text, caret). Enter stays an ordinary indented newline; the closer appears on the line below the
/// caret, which lands exactly where a plain Enter would have put it.
/// </summary>
public class KeywordPairingTests
{
    private const string Nl = "\n";

    /// <summary>Applies the edit for Enter at the caret marked `|`, returning the resulting text with
    /// the caret marked again — so a case reads as what the developer sees.</summary>
    private static string? Enter(string textWithCaret)
    {
        int caret = textWithCaret.IndexOf('|');
        Assert.True(caret >= 0, "the case must mark the caret with '|'");
        var text = textWithCaret.Remove(caret, 1);

        var edit = KeywordPairing.OnNewLine(text, caret, Nl);
        if (edit is null) return null;

        var result = text.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.InsertText);
        return result.Insert(edit.Start + edit.CaretOffset, "|");
    }

    // ── The pair forms ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Begin_AtStatementStart_PairsWithIndentedBody()
        => Assert.Equal("begin\n  |\nend", Enter("begin|"));

    [Fact]
    public void Begin_AfterAs_InRoutineHeader_Pairs()
        => Assert.Equal("create procedure P\nas\nbegin\n  |\nend", Enter("create procedure P\nas\nbegin|"));

    [Fact]
    public void Begin_AfterThen_Pairs()
        => Assert.Equal("begin\n  if (x = 1) then\n  begin\n    |\n  end\nend",
            Enter("begin\n  if (x = 1) then\n  begin|\nend"));

    [Fact]
    public void NestedBegin_ClosesAtItsOwnIndent()
        => Assert.Equal("begin\n  begin\n    |\n  end\nend", Enter("begin\n  begin|\nend"));

    [Fact]
    public void TrailingWhitespaceBeforeEnter_IsReplaced()
        => Assert.Equal("begin\n  |\nend", Enter("begin   |"));

    [Fact]
    public void Casing_FollowsHowTheOpenerWasTyped()
    {
        Assert.Equal("BEGIN\n  |\nEND", Enter("BEGIN|"));
        Assert.Equal("begin\n  |\nend", Enter("begin|"));
    }

    [Fact]
    public void DocumentNewLine_IsHonoured()
    {
        const string text = "begin";
        var edit = KeywordPairing.OnNewLine(text, text.Length, "\r\n");
        Assert.NotNull(edit);
        var result = text.Remove(edit!.Start, edit.Length).Insert(edit.Start, edit.InsertText);
        Assert.Equal("begin\r\n  \r\nend", result);
        Assert.Equal("begin\r\n  ".Length, edit.Start + edit.CaretOffset);
    }

    // ── One formatting language ──────────────────────────────────────────────────────────────
    //
    // The block Typing Ergonomics generates must be EXACTLY what the formatter would emit, or the first
    // Alt+F rewrites a block the editor just created. Asserted by running the real formatter over the
    // generated text (with a statement typed into the body) rather than by restating its style here — so
    // if either side's indent convention ever moves, this fails instead of drifting silently.

    [Fact]
    public void GeneratedBlock_IsAlreadyFormatterStyle()
    {
        var typed = Enter("begin|");
        Assert.Equal("begin\n  |\nend", typed);
        var withBody = typed!.Replace("|", "x = 1;");
        Assert.Equal(withBody, SqlFormatter.Format(withBody));
    }

    [Fact]
    public void GeneratedNestedBlock_IsAlreadyFormatterStyle()
    {
        var typed = Enter("begin\n  begin|\nend");
        var withBody = typed!.Replace("|", "x = 1;");
        Assert.Equal(withBody, SqlFormatter.Format(withBody));
    }

    [Fact]
    public void GeneratedBlockUnderThen_IsAlreadyFormatterStyle()
    {
        var typed = Enter("begin\n  if (x = 1) then\n  begin|\nend");
        var withBody = typed!.Replace("|", "y = 2;");
        Assert.Equal(withBody, SqlFormatter.Format(withBody));
    }

    // The block's indent is structural, so it does NOT depend on where the opener was typed. This is the
    // case that forces it: auto-indent lands the caret at the `then`-body's statement indent (one level
    // deeper than the `if`), but the formatter puts a BLOCK under `then` at the `if`'s own level. Aligning
    // the closer to the opener's typed column would put the whole block one level too deep and Alt+F would
    // immediately move it.
    [Fact]
    public void BlockUnderThen_IsIndentedStructurally_NotWhereItWasTyped()
    {
        var typed = Enter("begin\n  if (x = 1) then\n    begin|\nend");
        Assert.Equal("begin\n  if (x = 1) then\n  begin\n    |\n  end\nend", typed);
        var withBody = typed!.Replace("|", "y = 2;");
        Assert.Equal(withBody, SqlFormatter.Format(withBody));
    }

    [Fact]
    public void OpenerNotFirstOnItsLine_IsLeftWhereTheDeveloperPutIt()
    {
        // `… as begin` — we append the block but never reflow the code the developer wrote around it.
        var typed = Enter("create procedure P as begin|");
        Assert.Equal("create procedure P as begin\n  |\nend", typed);
    }

    [Fact]
    public void GeneratedRoutineBody_IsAlreadyFormatterStyle()
    {
        var typed = Enter("create procedure P\nas\nbegin|");
        var withBody = typed!.Replace("|", "x = 1;");
        Assert.Equal(withBody, SqlFormatter.Format(withBody));
    }

    // ── The pair does NOT form ───────────────────────────────────────────────────────────────

    // Rule 0: `begin_date = current_date;` is an ordinary PSQL statement. Pairing on the word's final
    // letter would generate a block the developer immediately deletes — which is why the trigger is the
    // boundary keystroke, not the completion of the word.
    [Fact]
    public void IdentifierStartingWithBegin_NeverPairs()
        => Assert.Null(Enter("begin\n  begin_date = current_date;|\nend"));

    // Pressing Enter after the `begin` of an ALREADY-CLOSED block must not bolt on a second `end`.
    [Fact]
    public void AlreadyBalancedBlock_DoesNotAddASecondEnd()
        => Assert.Null(Enter("begin|\n  x = 1;\nend"));

    // The CASE-aware count (gotchas #117/#128/#129): the CASE's END has no BEGIN, so a bare
    // begin/end counter would read this genuinely-unbalanced body as balanced and refuse to pair.
    [Fact]
    public void CaseEnd_DoesNotMaskAMissingBlockEnd()
        => Assert.Equal(
            "begin\n  x = case when a then 1 else 2 end;\n  begin\n    |\n  end\nend",
            Enter("begin\n  x = case when a then 1 else 2 end;\n  begin|\nend"));

    // A `begin`/`end` inside a literal or comment is not a token at all — the count must ignore it.
    [Fact]
    public void BeginInsideStringLiteral_DoesNotCount()
        => Assert.Null(Enter("begin|\n  x = 'begin';\nend"));

    [Fact]
    public void CodeAfterTheCaretOnTheSameLine_NeverSplitsTheLine()
        => Assert.Null(Enter("begin| x = 1; end"));

    [Fact]
    public void NotAStatementPosition_DoesNotPair()
    {
        Assert.Null(Enter("select begin|"));      // after a non-boundary keyword
        Assert.Null(Enter("select * from T|"));   // not an opener at all
    }

    [Fact]
    public void QuotedIdentifier_IsNotTheKeyword()
        => Assert.Null(Enter("select \"begin\"|"));

    [Fact]
    public void EmptyOrOutOfRange_IsNull()
    {
        Assert.Null(KeywordPairing.OnNewLine("", 0, Nl));
        Assert.Null(KeywordPairing.OnNewLine("begin", 0, Nl));
        Assert.Null(KeywordPairing.OnNewLine("begin", 99, Nl));
    }
}

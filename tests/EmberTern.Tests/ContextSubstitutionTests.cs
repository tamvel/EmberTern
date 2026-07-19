using System;
using System.Linq;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, milestone D10 (Triggers), Seam A: the pure-Core context-substitution engine
/// (spec §8.1). It removes a trigger's <c>NEW</c>/<c>OLD</c> columns and <c>INSERTING</c>/<c>UPDATING</c>/
/// <c>DELETING</c> predicates from a source fragment so it can run inside the harness. Entirely reference-driven
/// — so these tests build the model the way the debugger does: the <b>strict whole-routine parse, with NO
/// metadata</b> (<c>SemanticModel.Build(SqlParser.Parse(sql).Root)</c>). That is the load-bearing case: with no
/// metadata a <c>NEW.col</c> member does not resolve to a column symbol, yet the binder still records the
/// <c>Column</c> reference (span + text), which is all the engine needs. Pure Core, no server, no UI.
/// </summary>
public class ContextSubstitutionTests
{
    private static (SemanticModel Model, DdlStatement Ddl) Build(string sql)
    {
        var model = SemanticModel.Build(SqlParser.Parse(sql).Root);
        var ddl = model.Syntax.Statements.OfType<DdlStatement>().First(d => d.Body is not null);
        return (model, ddl);
    }

    private static TextSpan Span(SqlNode node) => new(node.Start, node.Length);

    // A BEFORE UPDATE trigger (both NEW and OLD available) whose body assigns NEW from OLD.
    private const string BeforeUpdate = """
        create trigger tr for orders active before update position 0 as
        begin
          new.total = old.total;
        end
        """;

    [Fact]
    public void BuildColumns_FindsDistinctNewOldColumns_WithStableSynthetics()
    {
        var (model, ddl) = Build(BeforeUpdate);

        var columns = ContextSubstitution.BuildColumns(model, Span(ddl.Body!));

        Assert.Collection(columns,
            c => Assert.Equal(new ContextColumn(TriggerRecord.New, "TOTAL", "ET_CTX_0"), c),
            c => Assert.Equal(new ContextColumn(TriggerRecord.Old, "TOTAL", "ET_CTX_1"), c));
    }

    [Fact]
    public void BuildColumns_DeduplicatesRepeatedColumn()
    {
        const string sql = """
            create trigger tr for orders active after update position 0 as
            begin
              if (old.status is distinct from new.status) then
                new.status = old.status;
            end
            """;
        var (model, ddl) = Build(sql);

        var columns = ContextSubstitution.BuildColumns(model, Span(ddl.Body!));

        // OLD.STATUS and NEW.STATUS each appear twice in the body; each becomes exactly one context column.
        Assert.Equal(2, columns.Count);
        Assert.Contains(columns, c => c is { Record: TriggerRecord.Old, Column: "STATUS" });
        Assert.Contains(columns, c => c is { Record: TriggerRecord.New, Column: "STATUS" });
        Assert.Equal(columns.Select(c => c.Synthetic).Distinct().Count(), columns.Count); // distinct names
    }

    [Fact]
    public void Substitute_ReplacesNewOldWithSynthetics_AndReportsReadsWrites()
    {
        var (model, ddl) = Build(BeforeUpdate);
        var columns = ContextSubstitution.BuildColumns(model, Span(ddl.Body!));
        var context = new TriggerContext("ORDERS", TriggerEvent.Update, TriggerTiming.Before, columns);
        var stmt = ddl.Body!.Statements.First();

        var rewrite = ContextSubstitution.Substitute(model, BeforeUpdate, Span(stmt), context);

        Assert.DoesNotContain("new.", rewrite.Fragment, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("old.", rewrite.Fragment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ET_CTX_0", rewrite.Fragment); // NEW.TOTAL
        Assert.Contains("ET_CTX_1", rewrite.Fragment); // OLD.TOTAL
        // Both context columns are injected (reads); only NEW is written back (BEFORE trigger).
        Assert.Equal(new[] { "ET_CTX_0", "ET_CTX_1" }, rewrite.ContextReads.OrderBy(x => x));
        Assert.Equal(new[] { "ET_CTX_0" }, rewrite.ContextWrites);
    }

    [Fact]
    public void Substitute_AfterTrigger_NewIsReadOnly_NoWrites()
    {
        // Same reference shape, but an AFTER trigger — NEW is not writable (§8.1), so nothing is written back
        // even though NEW.TOTAL is referenced.
        var (model, ddl) = Build(BeforeUpdate);
        var columns = ContextSubstitution.BuildColumns(model, Span(ddl.Body!));
        var context = new TriggerContext("ORDERS", TriggerEvent.Update, TriggerTiming.After, columns);
        var stmt = ddl.Body!.Statements.First();

        var rewrite = ContextSubstitution.Substitute(model, BeforeUpdate, Span(stmt), context);

        Assert.Empty(rewrite.ContextWrites);
        Assert.NotEmpty(rewrite.ContextReads); // still injected for reading
    }

    [Fact]
    public void Substitute_ReplacesPredicates_PerSimulatedEvent()
    {
        const string sql = """
            create trigger tr for orders active before insert or update position 0 as
            begin
              if (inserting) then new.total = 0;
              if (updating) then new.total = 1;
            end
            """;
        var (model, ddl) = Build(sql);
        var columns = ContextSubstitution.BuildColumns(model, Span(ddl.Body!));
        var body = Span(ddl.Body!);

        var asInsert = ContextSubstitution.Substitute(
            model, sql, body, new TriggerContext("ORDERS", TriggerEvent.Insert, TriggerTiming.Before, columns));
        Assert.DoesNotContain("inserting", asInsert.Fragment, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("if (TRUE) then", asInsert.Fragment, StringComparison.OrdinalIgnoreCase);   // inserting
        Assert.Contains("if (FALSE) then", asInsert.Fragment, StringComparison.OrdinalIgnoreCase);  // updating

        var asUpdate = ContextSubstitution.Substitute(
            model, sql, body, new TriggerContext("ORDERS", TriggerEvent.Update, TriggerTiming.Before, columns));
        // Order flips: inserting → FALSE, updating → TRUE.
        int firstTrue = asUpdate.Fragment.IndexOf("TRUE", StringComparison.Ordinal);
        int firstFalse = asUpdate.Fragment.IndexOf("FALSE", StringComparison.Ordinal);
        Assert.True(firstFalse < firstTrue, "inserting should be FALSE (appears first), updating TRUE");
    }

    [Fact]
    public void Substitute_NeverTouchesStringLiterals_ReferenceDrivenOnly()
    {
        // A string literal that spells 'OLD.STATUS' has no RecordAlias/Column reference, so the engine — which
        // only ever rewrites resolved reference spans — must leave it byte-for-byte intact, while rewriting the
        // real OLD.STATUS beside it.
        const string sql = """
            create trigger tr for orders active before update position 0 as
            declare v varchar(40);
            begin
              v = old.status || ' was OLD.STATUS';
            end
            """;
        var (model, ddl) = Build(sql);
        var columns = ContextSubstitution.BuildColumns(model, Span(ddl.Body!));
        var context = new TriggerContext("ORDERS", TriggerEvent.Update, TriggerTiming.Before, columns);
        var stmt = ddl.Body!.Statements.First();

        var rewrite = ContextSubstitution.Substitute(model, sql, Span(stmt), context);

        Assert.Contains("' was OLD.STATUS'", rewrite.Fragment);           // the literal, untouched
        Assert.Contains("v = ET_CTX_0 || ' was OLD.STATUS';", rewrite.Fragment); // the real ref, rewritten
        Assert.Single(columns); // only the real OLD.STATUS became a context column
    }

    [Fact]
    public void Substitute_NoContext_ReturnsRegionVerbatim()
    {
        const string sql = """
            create trigger tr for orders active before insert position 0 as
            declare v integer;
            begin
              v = 1 + 2;
            end
            """;
        var (model, ddl) = Build(sql);
        var context = new TriggerContext(
            "ORDERS", TriggerEvent.Insert, TriggerTiming.Before, Array.Empty<ContextColumn>());
        var stmt = ddl.Body!.Statements.First();

        var rewrite = ContextSubstitution.Substitute(model, sql, Span(stmt), context);

        Assert.Equal(sql.Substring(stmt.Start, stmt.Length), rewrite.Fragment);
        Assert.Empty(rewrite.ContextReads);
        Assert.Empty(rewrite.ContextWrites);
    }

    [Theory]
    // event, timing, OLD available, NEW available, NEW writable — the spec §8.1 availability matrix.
    [InlineData(TriggerEvent.Insert, TriggerTiming.Before, false, true, true)]
    [InlineData(TriggerEvent.Insert, TriggerTiming.After, false, true, false)]
    [InlineData(TriggerEvent.Update, TriggerTiming.Before, true, true, true)]
    [InlineData(TriggerEvent.Update, TriggerTiming.After, true, true, false)]
    [InlineData(TriggerEvent.Delete, TriggerTiming.Before, true, false, false)]
    [InlineData(TriggerEvent.Delete, TriggerTiming.After, true, false, false)]
    public void TriggerContext_Availability_MatchesSpec(
        TriggerEvent evt, TriggerTiming timing, bool oldAvailable, bool newAvailable, bool newWritable)
    {
        var context = new TriggerContext("ORDERS", evt, timing, Array.Empty<ContextColumn>());

        Assert.Equal(oldAvailable, context.OldAvailable);
        Assert.Equal(newAvailable, context.NewAvailable);
        Assert.Equal(newWritable, context.NewWritable);
    }
}

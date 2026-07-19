using System.Linq;
using EmberTern.Core.Sql.Debugging;
using EmberTern.Core.Sql.Language;
using EmberTern.Core.Sql.Language.Ast;
using EmberTern.Core.Sql.Language.Semantics;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// Stage X — Firebird Debugger, D10 Seam C: the Core reader that extracts a relation trigger's header facts
/// (target table, timing, declared DML events) from its parsed <see cref="DdlStatement"/>, so the launch UI
/// never re-parses the header. Built the debugger's way (strict whole-routine parse). Pure Core.
/// </summary>
public class TriggerHeaderReaderTests
{
    private static DdlStatement Ddl(string sql)
        => SemanticModel.Build(SqlParser.Parse(sql).Root).Syntax.Statements.OfType<DdlStatement>().First();

    [Fact]
    public void Reads_ForForm_SingleAction()
    {
        var h = TriggerHeaderReader.Read(Ddl(
            "create trigger tr for orders active before insert position 0 as\nbegin\n  new.status = 'x';\nend"));

        Assert.NotNull(h);
        Assert.Equal("ORDERS", h!.TargetTable);
        Assert.Equal(TriggerTiming.Before, h.Timing);
        Assert.Equal(new[] { TriggerEvent.Insert }, h.Events);
    }

    [Fact]
    public void Reads_MultiAction_InsertOrUpdate()
    {
        var h = TriggerHeaderReader.Read(Ddl(
            "create trigger tr for orders active before insert or update position 1 as\nbegin\n  new.status = 'x';\nend"));

        Assert.NotNull(h);
        Assert.Equal(TriggerTiming.Before, h!.Timing);
        Assert.Equal(new[] { TriggerEvent.Insert, TriggerEvent.Update }, h.Events);
    }

    [Fact]
    public void Reads_AfterUpdate_DoesNotCountBodyDml()
    {
        // The body performs an INSERT — it must not be read as a trigger event (the header ends at AS).
        var h = TriggerHeaderReader.Read(Ddl(
            "create trigger tr for orders active after update position 0 as\n" +
            "begin\n  insert into audit_log (entity) values ('ORDERS');\nend"));

        Assert.NotNull(h);
        Assert.Equal(TriggerTiming.After, h!.Timing);
        Assert.Equal(new[] { TriggerEvent.Update }, h.Events);
    }

    [Fact]
    public void Reads_BeforeDelete()
    {
        var h = TriggerHeaderReader.Read(Ddl(
            "create trigger tr for orders active before delete position 0 as\nbegin\n  exception e_x;\nend"));

        Assert.NotNull(h);
        Assert.Equal(TriggerTiming.Before, h!.Timing);
        Assert.Equal(new[] { TriggerEvent.Delete }, h.Events);
    }

    [Fact]
    public void DatabaseLevelTrigger_IsOutOfScope_ReturnsNull()
    {
        // ON CONNECT has no target table and no DML event — out of scope (§8.1).
        var h = TriggerHeaderReader.Read(Ddl(
            "create trigger tr active on connect position 0 as\nbegin\n  exit;\nend"));

        Assert.Null(h);
    }

    [Fact]
    public void NonTrigger_ReturnsNull()
    {
        var h = TriggerHeaderReader.Read(Ddl("create procedure p as begin suspend; end"));
        Assert.Null(h);
    }
}

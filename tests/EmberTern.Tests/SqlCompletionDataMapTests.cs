using System;
using EmberTern.App.Completion;
using EmberTern.Core.Sql.Language.Completion;
using Xunit;

namespace EmberTern.Tests;

/// <summary>
/// The App↔Core bridge for M5: the pure mapping from the Core <see cref="CompletionItemKind"/>
/// (what the engine emits) to the editor's display <see cref="SqlCompletionKind"/>. Pure — no
/// Avalonia control is constructed — so it is a plain, non-flaky unit test (unlike the
/// window-driven completion glue, which is manual smoke per the DB-path convention).
/// </summary>
public class SqlCompletionDataMapTests
{
    [Theory]
    [InlineData(CompletionItemKind.Keyword, SqlCompletionKind.Keyword)]
    [InlineData(CompletionItemKind.Table, SqlCompletionKind.Table)]
    [InlineData(CompletionItemKind.View, SqlCompletionKind.View)]
    [InlineData(CompletionItemKind.SystemTable, SqlCompletionKind.Table)]
    [InlineData(CompletionItemKind.Procedure, SqlCompletionKind.Procedure)]
    [InlineData(CompletionItemKind.Function, SqlCompletionKind.Function)]
    [InlineData(CompletionItemKind.Trigger, SqlCompletionKind.Trigger)]
    [InlineData(CompletionItemKind.Domain, SqlCompletionKind.Domain)]
    [InlineData(CompletionItemKind.Exception, SqlCompletionKind.Exception)]
    [InlineData(CompletionItemKind.Sequence, SqlCompletionKind.Generator)]
    [InlineData(CompletionItemKind.Role, SqlCompletionKind.Role)]
    [InlineData(CompletionItemKind.Package, SqlCompletionKind.Package)]
    [InlineData(CompletionItemKind.Index, SqlCompletionKind.Index)]
    [InlineData(CompletionItemKind.Column, SqlCompletionKind.Column)]
    [InlineData(CompletionItemKind.TableAlias, SqlCompletionKind.Alias)]
    [InlineData(CompletionItemKind.Variable, SqlCompletionKind.Variable)]
    [InlineData(CompletionItemKind.Parameter, SqlCompletionKind.Parameter)]
    [InlineData(CompletionItemKind.Cte, SqlCompletionKind.Cte)]
    [InlineData(CompletionItemKind.Cursor, SqlCompletionKind.Cursor)]
    [InlineData(CompletionItemKind.RecordAlias, SqlCompletionKind.Record)]
    public void MapKind_MapsEachEngineKindToADisplayKind(CompletionItemKind engineKind, SqlCompletionKind expected)
        => Assert.Equal(expected, SqlCompletionData.MapKind(engineKind));

    [Fact]
    public void MapKind_CoversEveryEngineKind_WithoutThrowing()
    {
        // Every kind the engine can emit must map deterministically (no exception,
        // no accidental unmapped default surprising the display). Unknown → Keyword.
        foreach (CompletionItemKind kind in Enum.GetValues(typeof(CompletionItemKind)))
        {
            var mapped = SqlCompletionData.MapKind(kind);
            Assert.True(Enum.IsDefined(typeof(SqlCompletionKind), mapped));
        }
    }
}

using System;
using System.Collections.Generic;

namespace EmberTern.Core.Query;

public sealed record QueryColumn(string Name, Type ClrType);

public sealed class QueryResult
{
    public IReadOnlyList<QueryColumn> Columns { get; init; } = Array.Empty<QueryColumn>();
    public IReadOnlyList<object?[]> Rows { get; init; } = Array.Empty<object?[]>();
    public TimeSpan Elapsed { get; init; }
    public bool Truncated { get; init; }
    public int? RecordsAffected { get; init; }

    public bool HasResultSet => Columns.Count > 0;
}

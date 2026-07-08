using System;
using System.Collections.Generic;

namespace EmberTern.Core.Query;

public sealed record QueryColumn(string Name, Type ClrType);

public sealed class QueryResult
{
    public IReadOnlyList<QueryColumn> Columns { get; init; } = Array.Empty<QueryColumn>();
    public IReadOnlyList<object?[]> Rows { get; init; } = Array.Empty<object?[]>();
    public TimeSpan Elapsed { get; init; }

    /// <summary>Preview stopped at its row limit — there is more data. Drives the truncated-Preview
    /// notification bar + the <c>N+ (preview)</c> record indicator.</summary>
    public bool Truncated { get; init; }

    /// <summary>Full stopped at the hard <see cref="EmberTern.Core.Query.ExecutionDefaults.FullSafetyCeiling"/>
    /// backstop — this is a safety limit, not the end of the result.</summary>
    public bool CeilingHit { get; init; }

    public int? RecordsAffected { get; init; }

    public bool HasResultSet => Columns.Count > 0;
}

namespace EmberTern.Core.Performance;

/// <summary>The form of an execution plan as returned by the engine/driver.</summary>
public enum PlanDialect
{
    /// <summary>The classic <c>PLAN (X NATURAL)</c> / <c>PLAN JOIN (...)</c> form.</summary>
    Legacy,

    /// <summary>The indented "Select Expression -&gt; ... -&gt; Table Full Scan" tree.</summary>
    Explain,
}

/// <summary>Coarse classification of a plan node's access/merge method. Unknown is the
/// tolerant fallback so an unrecognized construct never breaks parsing.</summary>
public enum AccessMethod
{
    Unknown,
    SelectExpression,
    FullScan,
    AccessById,
    Bitmap,
    IndexScan,
    NestedLoopJoin,
    HashJoin,
    MergeJoin,
    Aggregate,
    Sort,
    Filter,
    RecordBuffer,
    ProcedureScan,
    Union,
}

/// <summary>Overall performance grade shown in the verdict bar. In Phase 1 this is a
/// coarse, time-based proxy (the only signal available without per-table reads).</summary>
public enum PerformanceGrade
{
    Unknown,
    Fast,
    Acceptable,
    NeedsAttention,
    Slow,
}

/// <summary>Which strategy produced the capture. Phase 1 only measures timings + plan.</summary>
public enum CaptureMethod
{
    /// <summary>Plan + self-measured timings only; no per-table reads.</summary>
    PlanOnly,

    /// <summary>Reserved for Phase 2 — MON$ own-attachment before/after delta.</summary>
    MonAttachmentDelta,

    /// <summary>Reserved for Phase 2 — MON$ per-statement stats.</summary>
    MonStatement,

    /// <summary>Reserved for Phase 3 — Services trace session.</summary>
    Trace,
}

/// <summary>What the analyzed statement is. Phase 1 only produces <see cref="Query"/>.</summary>
public enum StatementRole
{
    Query,
    ProcedureCursor,
    FunctionBodyCursor,
}

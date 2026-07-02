using System;
using System.Collections.Generic;
using EmberTern.Core.Metadata;

namespace EmberTern.Core.Sql.Templates;

/// <summary>
/// Where a snippet is being inserted. Derived from the drop-target editor: a plain SQL
/// editor is <see cref="PlainSql"/>; a procedure/trigger/function body editor is
/// <see cref="PsqlBody"/>. Templates consult it to decide applicability (PSQL scaffolds
/// only make sense in a body). MVP ships only PlainSql-appropriate templates and only
/// the plain SQL editor as a drop target; the flag is here so PSQL templates slot in
/// later without changing the engine.
/// </summary>
public enum SnippetInsertionContext
{
    PlainSql,
    PsqlBody,
}

/// <summary>
/// Generation options — parameter/quoting conventions and layout. Sourced from user
/// settings in a later phase; <see cref="Default"/> matches the app's conventions today.
/// </summary>
public sealed record SnippetOptions
{
    /// <summary>Prefix for named parameter tokens, e.g. <c>:</c> → <c>:CUSTOMER_ID</c>.</summary>
    public string ParamPrefix { get; init; } = ":";

    /// <summary>Prefix for declared PSQL variable names, e.g. <c>V_</c> → <c>V_CUSTOMER_ID</c>.</summary>
    public string VarPrefix { get; init; } = "V_";

    /// <summary>Exclude COMPUTED BY columns from INSERT/UPDATE column lists.</summary>
    public bool ExcludeComputed { get; init; } = true;

    /// <summary>Exclude auto-increment/identity columns from the INSERT column list.</summary>
    public bool ExcludeIdentityOnInsert { get; init; } = true;

    public string NewLine { get; init; } = "\n";
    public string Indent { get; init; } = "  ";

    public static SnippetOptions Default { get; } = new();
}

/// <summary>
/// The fully-materialized input to template generation. Pure data — no DB connection,
/// no readers. The App-side context builder loads metadata (columns / params / PK) via
/// the existing readers and caches, then generation is synchronous and testable.
/// </summary>
public sealed class SnippetContext
{
    public required MetadataObject Object { get; init; }

    public SnippetInsertionContext Insertion { get; init; } = SnippetInsertionContext.PlainSql;
    public SnippetOptions Options { get; init; } = SnippetOptions.Default;

    /// <summary>Table/view columns in catalog order (empty until loaded).</summary>
    public IReadOnlyList<FieldInfo> Columns { get; init; } = Array.Empty<FieldInfo>();

    /// <summary>
    /// Primary-key column names, sourced from the PRIMARY KEY constraint (never the
    /// per-field flag — see architecture gotcha #103). Empty when the table has no PK,
    /// in which case WHERE/MATCHING templates emit a placeholder condition.
    /// </summary>
    public IReadOnlyList<string> PrimaryKey { get; init; } = Array.Empty<string>();

    /// <summary>Procedure input parameters (empty for a parameterless proc).</summary>
    public IReadOnlyList<ProcedureParameterInfo> Inputs { get; init; } = Array.Empty<ProcedureParameterInfo>();

    /// <summary>Procedure output parameters (drives the SELECT-from column list).</summary>
    public IReadOnlyList<ProcedureParameterInfo> Outputs { get; init; } = Array.Empty<ProcedureParameterInfo>();

    /// <summary>Function signature (arguments + return); null unless the object is a function.</summary>
    public FunctionSignatureInfo? Function { get; init; }

    /// <summary>True when the procedure is selectable (has SUSPEND) — gates the SELECT-from template.</summary>
    public bool ProcedureIsSelectable { get; init; }
}

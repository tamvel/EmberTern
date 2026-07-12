using System.Collections.Generic;
using EmberTern.Core.Sql.Language.Semantics;

namespace EmberTern.Core.Sql.Language.Signatures;

/// <summary>What a <see cref="SignatureInfo"/> describes — shapes the App's label/heading, not the
/// parameter set.</summary>
public enum SignatureKind
{
    /// <summary>An <c>EXECUTE PROCEDURE</c> or selectable-procedure call.</summary>
    Procedure,

    /// <summary>A function call in an expression.</summary>
    Function,

    /// <summary>An <c>INSERT</c> column ↔ value mapping (column list, VALUES list, or the
    /// projection of an <c>INSERT … SELECT</c>).</summary>
    Insert,

    /// <summary>An <c>UPDATE … SET col = …</c> assignment list.</summary>
    Update,
}

/// <summary>
/// One parameter/target in a <see cref="SignatureInfo"/> — a routine parameter, or (for DML) a
/// target column. Rich-but-optional facts mirror <see cref="RoutineParameterMetadata"/> /
/// <see cref="ColumnMetadata"/>. Pure value — no Avalonia.
/// </summary>
public sealed record SignatureParameter(
    string Name,
    string Type,
    ParameterDirection Direction = ParameterDirection.Input,
    bool? Nullable = null,
    string? Default = null,
    string? Description = null);

/// <summary>
/// The signature-help model for a call/DML site — Etap 5 / M6 (design §8 / §5.10). Produced by
/// <see cref="SignatureHelpEngine"/> from the AST + <see cref="SemanticModel"/>; rendered by the App
/// popup (M7) with the active parameter highlighted. Pure — no Avalonia.
/// </summary>
public sealed class SignatureInfo
{
    public SignatureInfo(
        string label,
        IReadOnlyList<SignatureParameter> parameters,
        int activeParameter,
        SignatureKind kind)
    {
        Label = label ?? string.Empty;
        Parameters = parameters;
        ActiveParameter = activeParameter;
        Kind = kind;
    }

    /// <summary>The callee/target name — the procedure/function name, or the target table for a DML
    /// signature. The App renders it as the popup heading.</summary>
    public string Label { get; }

    /// <summary>The ordered parameters/targets. May be empty (e.g. a known routine with no inputs).</summary>
    public IReadOnlyList<SignatureParameter> Parameters { get; }

    /// <summary>The zero-based index of the active parameter — the comma-separated argument the
    /// caret is on. May exceed <see cref="Parameters"/> count when the caller supplied more
    /// arguments than exist (the App guards its highlight; count-mismatch <i>diagnostics</i> are
    /// Etap 7, not here). -1 when there is no active parameter.</summary>
    public int ActiveParameter { get; }

    /// <summary>What the signature describes.</summary>
    public SignatureKind Kind { get; }
}

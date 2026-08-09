using EmberTern.Core.Metadata;
using EmberTern.Core.Sql;

namespace EmberTern.App.ViewModels;

/// <summary>
/// One editable row in a procedure's Input/Output parameter grid (Easy mode). Inherits
/// the full field-definition editing surface (Type/Domain dropdowns, TYPE OF, Size,
/// Scale, Sub Type, Charset, Collate, Not Null, Default) from
/// <see cref="ProcedureFieldRowBase"/> — the same infrastructure as the table field
/// grids. Maps to/from the Core <see cref="ProcedureParameter"/>; the canonical
/// <see cref="ProcedureFieldRowBase.TypeText"/> preserves any Firebird type form on
/// round-trip. Output parameters never carry a default.
/// </summary>
public sealed class ProcedureParamRowViewModel : ProcedureFieldRowBase
{
    public ProcedureParamRowViewModel() : base(null) { }
    public ProcedureParamRowViewModel(IFieldRowOwner? owner) : base(owner) { }

    /// <summary>True for an output parameter — the grid hides the Default column and
    /// reassembly omits any default.</summary>
    public bool IsOutput { get; init; }

    public ProcedureParameter ToParameter() => new()
    {
        Name = (Name ?? string.Empty).Trim(),
        TypeText = (TypeText ?? string.Empty).Trim(),
        NotNull = NotNull,
        DefaultValue = IsOutput || string.IsNullOrWhiteSpace(DefaultValue) ? null : DefaultValue.Trim(),
    };

    public static ProcedureParamRowViewModel From(ProcedureParameter p, IFieldRowOwner? owner = null, bool isOutput = false)
    {
        var row = new ProcedureParamRowViewModel(owner)
        {
            IsOutput = isOutput,
            NotNull = p.NotNull,
            DefaultValue = p.DefaultValue ?? string.Empty,
        };
        row.Name = p.Name;
        row.LoadType(p.TypeText);
        return row;
    }

    public static ProcedureParamRowViewModel From(ProcedureParameterInfo p, IFieldRowOwner? owner = null, bool isOutput = false)
    {
        var row = new ProcedureParamRowViewModel(owner)
        {
            IsOutput = isOutput,
            NotNull = p.NotNull,
            DefaultValue = p.DefaultValue ?? string.Empty,
        };
        row.Name = p.Name;
        // ⭐⭐ A domain-typed parameter loads its DOMAIN, not its resolved base type (S-1b, 2026-08-05).
        // Without this the grid showed CHAR(8) for a `P_CODE D_CODE` parameter, and Compile — which
        // reassembles the whole CREATE OR ALTER from these rows — wrote CHAR(8) back, destroying the
        // domain link in the database for a user who only edited the body. Rule #11.
        //
        // ⭐ Handing the domain NAME to the existing LoadType is deliberate reuse, not a shortcut: its
        // "unknown base token ⇒ this is a domain" branch already does the two things this needs — keeps
        // TypeText as the domain (so ComposeType re-emits the domain) and resolves the domain's base type
        // into the Type/Size/Scale cells with `adoptNotNull: false`. That last flag is load-bearing: the
        // parameter's own NOT NULL comes from its declaration and must not be overwritten by the
        // domain's (§19.8 — that overwrite would change the user's stored code at open time).
        row.LoadType(string.IsNullOrWhiteSpace(p.Domain) ? p.Type : p.Domain);
        return row;
    }
}

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
    public ProcedureParamRowViewModel(ProcedureDetailTabViewModel? owner) : base(owner) { }

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

    public static ProcedureParamRowViewModel From(ProcedureParameter p, ProcedureDetailTabViewModel? owner = null, bool isOutput = false)
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

    public static ProcedureParamRowViewModel From(ProcedureParameterInfo p, ProcedureDetailTabViewModel? owner = null, bool isOutput = false)
    {
        var row = new ProcedureParamRowViewModel(owner)
        {
            IsOutput = isOutput,
            NotNull = p.NotNull,
            DefaultValue = p.DefaultValue ?? string.Empty,
        };
        row.Name = p.Name;
        row.LoadType(p.Type);
        return row;
    }
}

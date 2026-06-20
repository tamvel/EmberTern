using System.Collections.ObjectModel;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// What a <see cref="ProcedureFieldRowBase"/> row needs from its owning editor VM:
/// the live domain list (for the Domain combo) and the basic-type list (for the Type
/// combo). Implemented by both <see cref="ProcedureDetailTabViewModel"/> and
/// <see cref="TriggerDetailTabViewModel"/> so the editable field/variable rows are
/// shared across object editors with no second type system (two implementers — the
/// interface is justified per the project's "no interface without two impls" rule).
/// </summary>
public interface IFieldRowOwner
{
    ObservableCollection<DomainSpec> AvailableDomains { get; }
    IReadOnlyList<string> BasicTypes { get; }
}

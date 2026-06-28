using System.Collections.ObjectModel;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// A field-row VM that drives the merged "Domena/Kolumna" picker — exposes the data its
/// two tabs need (domain list + table list + lazy column loader). Implemented by all
/// editable field rows (Procedure/Trigger <see cref="ProcedureFieldRowBase"/>, Table
/// Detail <see cref="FieldRowViewModel"/>, New Table <c>NewTableFieldRowViewModel</c>)
/// so one merged-column builder serves every grid. The picker also binds
/// <c>SelectedTypeSource</c> (object) + <c>TypeSourceDisplay</c> (string) by name.
/// </summary>
public interface ITypeSourceRow
{
    ObservableCollection<DomainSpec> AvailableDomains { get; }
    ObservableCollection<string> AvailableTables { get; }
    IColumnsLoader? ColumnsLoader { get; }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using EmberTern.Core.Metadata;

namespace EmberTern.App.ViewModels;

/// <summary>
/// Lazily loads a table's columns for the Table-column tab of the merged
/// "Domena/Kolumna" picker — so it never eager-loads every column of every table.
/// Implemented by the field-row owners (they have the metadata reader).
/// </summary>
public interface IColumnsLoader
{
    Task<IReadOnlyList<ColumnSpec>> LoadColumnsAsync(string table);
}

using System.Collections.Generic;

namespace EmberTern.Core.Settings;

// Per-grid layout memory. Shipped with the full field set up front (even though no
// consumer wires it yet) so that the future "remember column widths / order / auto-fit"
// milestone can light up without another settings schema migration.
//
// GridId names which grid the profile belongs to — a stable string key the consuming
// view supplies (e.g. "TableDetail.Fields", "QueryResults"). One GridProfile per grid.
public sealed class GridProfile
{
    public string GridId { get; set; } = string.Empty;

    // Column display name -> pixel width. Absent columns fall back to their default width.
    public Dictionary<string, double> ColumnWidths { get; set; } = new();

    // Column display names in the user's preferred left-to-right order. Empty = default order.
    public List<string> ColumnOrder { get; set; } = new();

    // When true, the grid auto-fits columns to content instead of honouring ColumnWidths.
    public bool AutoFitColumns { get; set; }
}

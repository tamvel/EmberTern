using EmberTern.Core.Export;

namespace EmberTern.App.ViewModels;

/// <summary>
/// A VM→View request to open the shared Export dialog for a grid's data source, with a default
/// scope pre-selected (the banner's "Export all…" passes <see cref="ExportScope.AllRows"/>). The view
/// builds the dialog and returns the completed <see cref="ExportOutcome"/> (or null on cancel).
/// </summary>
public sealed record ExportDialogRequest(IExportDataSource Source, ExportScope DefaultScope);

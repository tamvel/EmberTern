using System;

namespace EmberTern.App.Controls;

/// <summary>
/// Implemented by a custom <see cref="SearchableComboBoxSection.Content"/> (e.g. the
/// two-pane Table-column picker) so it can commit a chosen item back to the host
/// <see cref="SearchableComboBox"/> — which sets <c>SelectedItem</c> and closes the
/// popup. The host assigns <see cref="CommitRequested"/> when it mounts the content.
/// </summary>
public interface ISearchableComboBoxContent
{
    Action<object?>? CommitRequested { get; set; }
}

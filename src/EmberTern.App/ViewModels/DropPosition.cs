namespace EmberTern.App.ViewModels;

/// <summary>
/// Where in the sibling order a drag-and-drop operation should land the source
/// relative to the target. <see cref="Into"/> only applies to dropping a
/// connection onto a folder (membership change); <see cref="Before"/> /
/// <see cref="After"/> reorder siblings within the same container.
/// </summary>
public enum DropPosition
{
    Before,
    After,
    Into,
}

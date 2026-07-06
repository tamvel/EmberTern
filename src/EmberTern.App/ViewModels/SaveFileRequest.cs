namespace EmberTern.App.ViewModels;

/// <summary>
/// A VM→View request to open a Save-file picker. The view returns the chosen absolute path
/// (or null on cancel); the VM owns the actual write. Kept a plain DTO so no Avalonia types
/// leak into the view-model layer.
/// </summary>
public sealed record SaveFileRequest(string Title, string SuggestedFileName, string FilterName, string Extension);

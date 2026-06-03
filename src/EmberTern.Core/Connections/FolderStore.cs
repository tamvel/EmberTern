using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace EmberTern.Core.Connections;

// In-memory snapshot of the folder layout. ConnectionFolderMap maps a
// ConnectionProfile.Id to its parent FolderEntry.Id. Connections whose id is
// absent (or whose value is empty) live at the root. ConnectionSortOrders
// carries the per-connection sort key so the tree's ordering is preserved
// after restart — connection profile JSON stays untouched (forward-compatible
// with profiles written by older builds).
public sealed class FolderState
{
    public List<FolderEntry> Folders { get; set; } = new();
    public Dictionary<string, string> ConnectionFolderMap { get; set; } = new();
    public Dictionary<string, int> ConnectionSortOrders { get; set; } = new();
}

public sealed class FolderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public FolderStore()
        : this(DefaultStoreDirectory())
    {
    }

    public FolderStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "folders.json");
    }

    public string FilePath => _filePath;

    public FolderState Load()
    {
        if (!File.Exists(_filePath))
        {
            return new FolderState();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new FolderState();
            }
            return JsonSerializer.Deserialize<FolderState>(json, JsonOptions) ?? new FolderState();
        }
        // Same forgiving stance as ConnectionProfileStore / WorkspaceStore: corrupt
        // or unreadable files reset to defaults rather than crashing startup.
        catch (JsonException)
        {
            return new FolderState();
        }
        catch (IOException)
        {
            return new FolderState();
        }
        catch (UnauthorizedAccessException)
        {
            return new FolderState();
        }
    }

    public void Save(FolderState state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static string DefaultStoreDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "EmberTern");
    }
}

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EmberTern.Core.Workspace;

public sealed class WorkspaceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;

    public WorkspaceStore()
        : this(DefaultStoreDirectory())
    {
    }

    public WorkspaceStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "workspace.json");
    }

    public string FilePath => _filePath;

    public WorkspaceState? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            return JsonSerializer.Deserialize<WorkspaceState>(json, JsonOptions);
        }
        // Corrupt JSON, partial writes, locked file, etc. — silently fall back to "no
        // saved state" rather than crashing app startup. Workspace state is convenience,
        // not data the user can't recreate.
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(WorkspaceState state)
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

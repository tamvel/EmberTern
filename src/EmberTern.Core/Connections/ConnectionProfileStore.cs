using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EmberTern.Core.Connections;

public sealed class ConnectionProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public ConnectionProfileStore()
        : this(DefaultStoreDirectory())
    {
    }

    public ConnectionProfileStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "connections.json");
    }

    public string FilePath => _filePath;

    public IReadOnlyList<ConnectionProfile> LoadAll()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<ConnectionProfile>();
        }

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<ConnectionProfile>();
        }

        var profiles = JsonSerializer.Deserialize<List<ConnectionProfile>>(json, JsonOptions);
        return profiles ?? new List<ConnectionProfile>();
    }

    public void SaveAll(IEnumerable<ConnectionProfile> profiles)
    {
        var list = profiles.ToList();
        var json = JsonSerializer.Serialize(list, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    public void Upsert(ConnectionProfile profile)
    {
        var profiles = LoadAll().ToList();
        var existing = profiles.FindIndex(p => p.Id == profile.Id);
        if (existing >= 0)
        {
            profiles[existing] = profile;
        }
        else
        {
            profiles.Add(profile);
        }
        SaveAll(profiles);
    }

    public void Delete(string id)
    {
        var profiles = LoadAll().Where(p => p.Id != id);
        SaveAll(profiles);
    }

    private static string DefaultStoreDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "EmberTern");
    }
}

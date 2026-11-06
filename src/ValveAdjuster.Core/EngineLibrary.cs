using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValveAdjuster.Core;

/// <summary>A named engine type: its layout and clearance/shim specs, without any measurements.</summary>
public sealed class EngineProfile
{
    public string Name { get; set; } = "";
    public EngineConfig Config { get; set; } = new();
}

public sealed class EngineLibraryFile
{
    public int FormatVersion { get; set; } = EngineLibrary.CurrentVersion;
    public List<EngineProfile> EngineTypes { get; set; } = new();
}

/// <summary>
/// Saved engine types, stored as JSON so they can be reused across sessions without re-entering the specs.
/// Names are unique and case-insensitive.
/// </summary>
public sealed class EngineLibrary
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly List<EngineProfile> _profiles = new();

    public EngineLibrary(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public bool IsEmpty => _profiles.Count == 0;

    public IReadOnlyList<EngineProfile> Profiles =>
        _profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>Per-user location, e.g. C:\Users\name\AppData\Roaming\ValveAdjuster\engine-types.json.</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ValveAdjuster",
        "engine-types.json");

    public static EngineLibrary Load(string path)
    {
        var library = new EngineLibrary(path);
        if (!File.Exists(path)) return library;

        var file = JsonSerializer.Deserialize<EngineLibraryFile>(File.ReadAllText(path), Options)
                   ?? throw new InvalidDataException("The engine types file is empty.");

        if (file.FormatVersion > CurrentVersion)
            throw new InvalidDataException("The engine types file was saved by a newer version of the application.");

        foreach (var profile in file.EngineTypes)
        {
            if (!string.IsNullOrWhiteSpace(profile.Name))
                library.Upsert(profile.Name, profile.Config);
        }

        return library;
    }

    public void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var file = new EngineLibraryFile { EngineTypes = Profiles.ToList() };
        File.WriteAllText(Path, JsonSerializer.Serialize(file, Options));
    }

    public EngineProfile? Find(string name) =>
        _profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Adds the engine type, or replaces an existing one with the same name. The config is copied.</summary>
    public void Upsert(string name, EngineConfig config)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("An engine type name is required.", nameof(name));

        Remove(trimmed);
        var copy = Copy(config);
        copy.Name = trimmed;
        _profiles.Add(new EngineProfile { Name = trimmed, Config = copy });
    }

    public bool Remove(string name) =>
        _profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

    public static EngineConfig Copy(EngineConfig config) =>
        JsonSerializer.Deserialize<EngineConfig>(JsonSerializer.Serialize(config))!;
}

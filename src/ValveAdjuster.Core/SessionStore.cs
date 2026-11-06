using System.Text.Json;
using System.Text.Json.Serialization;

namespace ValveAdjuster.Core;

public sealed class SessionFile
{
    public int FormatVersion { get; set; } = SessionStore.CurrentVersion;
    public EngineConfig Engine { get; set; } = new();
    public List<ValveRecord> Valves { get; set; } = new();
}

/// <summary>Saves and loads adjustment sessions as JSON.</summary>
public static class SessionStore
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(string path, EngineConfig engine, IEnumerable<ValveRecord> valves)
    {
        var file = new SessionFile { Engine = engine, Valves = valves.ToList() };
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
    }

    public static SessionFile Load(string path)
    {
        var json = File.ReadAllText(path);
        var file = JsonSerializer.Deserialize<SessionFile>(json, Options)
                   ?? throw new InvalidDataException("The file is empty or is not a valid session.");

        if (file.FormatVersion > CurrentVersion)
            throw new InvalidDataException("This session was saved by a newer version of the application.");

        return file;
    }
}

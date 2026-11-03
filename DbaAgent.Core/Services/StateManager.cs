using System.Text.Encodings.Web;
using System.Text.Json;
using DbaAgent.Core.Models;

namespace DbaAgent.Core.Services;

public sealed class StateManager
{
    readonly private string _outputDir;
    readonly private string _stateFilePath;

    readonly private static JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public StateManager(string outputDir = "output")
    {
        _outputDir = outputDir;
        if (!Directory.Exists(_outputDir))
        {
            Directory.CreateDirectory(_outputDir);
        }

        _stateFilePath = Path.Combine(_outputDir, "state.json");
    }

    public bool HasExistingSession() => File.Exists(_stateFilePath);

    public async Task<SessionState> LoadOrCreateAsync()
    {
        if (!File.Exists(_stateFilePath))
        {
            var newState = new SessionState();
            await SaveAsync(newState);
            return newState;
        }

        var json = await File.ReadAllTextAsync(_stateFilePath);
        var state = JsonSerializer.Deserialize<SessionState>(json, JsonOptions);
        return state ?? new SessionState();
    }

    public async Task SaveAsync(SessionState state)
    {
        state.LastUpdatedAt = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(_stateFilePath, json);
    }

    public async Task WriteArtifactAsync(string filename, string content)
    {
        var filePath = Path.Combine(_outputDir, filename);
        await File.WriteAllTextAsync(filePath, content);
    }
}
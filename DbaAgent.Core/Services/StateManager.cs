using DbaAgent.Core.Models;

namespace DbaAgent.Core.Services;

public sealed class StateManager
{
    readonly private string _baseDir;
    readonly private string _outputDir;
    readonly private string _stateFilePath;

    public string OutputDirectory => _outputDir;

    public StateManager(string baseDir = "output", string scenarioName = "default")
    {
        _baseDir = baseDir;
        _outputDir = Path.Combine(baseDir, scenarioName.Trim().ToLowerInvariant());
        _stateFilePath = Path.Combine(_outputDir, "state.json");
        Directory.CreateDirectory(_outputDir);
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
        return System.Text.Json.JsonSerializer.Deserialize<SessionState>(json) ?? new SessionState();
    }

    public async Task SaveAsync(SessionState state)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(state,
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

        await File.WriteAllTextAsync(_stateFilePath, json);
    }

    public async Task WriteArtifactAsync(string filename, string content)
    {
        var sanitizedContent = TextSanitizer.StripOuterCodeFence(content);
        sanitizedContent = TextSanitizer.SanitizeMermaid(sanitizedContent);

        var filePath = Path.Combine(_outputDir, filename);
        await File.WriteAllTextAsync(filePath, sanitizedContent);
    }
}
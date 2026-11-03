using System.Text.Encodings.Web;
using System.Text.Json;
using DbaAgent.Core.Models;

namespace DbaAgent.Core.Services;

public sealed class LlmCallLogger
{
    readonly private string _logFilePath;
    readonly private static JsonSerializerOptions SerializerOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public LlmCallLogger(string logDirectory = "logs")
    {
        if (!Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        _logFilePath = Path.Combine(logDirectory, "llm_calls.jsonl");
    }

    public async Task LogAsync(LlmCallLog logEntry)
    {
        var line = JsonSerializer.Serialize(logEntry, SerializerOptions);
        await File.AppendAllTextAsync(_logFilePath, line + Environment.NewLine);
    }
}
namespace DbaAgent.Core.Models;

public sealed record LlmCallLog(
    DateTime Timestamp,
    string Stage,
    int Attempt,
    string SystemPrompt,
    string UserPrompt,
    string Response,
    long DurationMs,
    bool Success,
    string? ErrorMessage
);
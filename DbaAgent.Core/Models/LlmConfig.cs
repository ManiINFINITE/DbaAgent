namespace DbaAgent.Core.Models;

public sealed record LlmConfig
{
    public string Provider { get; init; } = "GapGPT";
    public string BaseUrl { get; init; } = "https://api.gapgpt.app/v1";
    public string ApiKey { get; init; } = string.Empty;
    public string ModelName { get; init; } = "gpt-4o-mini";
    public float Temperature { get; init; } = 0.2f;
    public int MaxAttempts { get; init; } = 3;
}
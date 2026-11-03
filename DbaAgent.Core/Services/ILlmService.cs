namespace DbaAgent.Core.Services;

public interface ILlmService
{
    Task<string> GenerateAsync(string stageName, string systemPrompt, string userPrompt);
}
using System.ClientModel;
using System.Diagnostics;
using DbaAgent.Core.Models;
using OpenAI;
using OpenAI.Chat;

namespace DbaAgent.Core.Services;

public sealed class LlmService : ILlmService
{
    readonly private ChatClient _chatClient;
    readonly private LlmConfig _config;
    readonly private LlmCallLogger _logger;

    public LlmService(LlmConfig config, LlmCallLogger logger)
    {
        _config = config;
        _logger = logger;

        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(config.BaseUrl)
        };

        var client = new OpenAIClient(new ApiKeyCredential(config.ApiKey), clientOptions);
        _chatClient = client.GetChatClient(config.ModelName);
    }

    public async Task<string> GenerateAsync(string stageName, string systemPrompt, string userPrompt)
    {
        var attempts = _config.MaxAttempts;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var options = new ChatCompletionOptions
                {
                    Temperature = _config.Temperature
                };

                var messages = new ChatMessage[]
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(userPrompt)
                };

                var completion = await _chatClient.CompleteChatAsync(messages, options);
                stopwatch.Stop();

                var responseText = completion.Value.Content[0].Text;

                await _logger.LogAsync(new LlmCallLog(
                    Timestamp: DateTime.UtcNow,
                    Stage: stageName,
                    Attempt: attempt,
                    SystemPrompt: systemPrompt,
                    UserPrompt: userPrompt,
                    Response: responseText,
                    DurationMs: stopwatch.ElapsedMilliseconds,
                    Success: true,
                    ErrorMessage: null
                ));

                return responseText;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                await _logger.LogAsync(new LlmCallLog(
                    Timestamp: DateTime.UtcNow,
                    Stage: stageName,
                    Attempt: attempt,
                    SystemPrompt: systemPrompt,
                    UserPrompt: userPrompt,
                    Response: string.Empty,
                    DurationMs: stopwatch.ElapsedMilliseconds,
                    Success: false,
                    ErrorMessage: ex.Message
                ));

                if (attempt == attempts)
                {
                    throw new InvalidOperationException(
                        $"LLM call failed at stage '{stageName}' after {attempts} attempts. Error: {ex.Message}",
                        ex);
                }

                await Task.Delay(1000 * attempt);
            }
        }

        throw new InvalidOperationException($"Exhausted retry attempts for stage '{stageName}'.");
    }
}
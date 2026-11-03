using DbaAgent.Core.Models;
using DbaAgent.Core.Services;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

AnsiConsole.Write(new FigletText("DBA Agent").Color(Color.Cyan1));

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables()
    .Build();

var config = configuration.GetSection("LlmConfig").Get<LlmConfig>();

if (config is null || string.IsNullOrWhiteSpace(config.ApiKey))
{
    AnsiConsole.MarkupLine("[red]Error: API Key is missing in appsettings.json[/]");
    return;
}

AnsiConsole.MarkupLine($"[green]Provider:[/] {config.Provider}");
AnsiConsole.MarkupLine($"[green]Model:[/] {config.ModelName}");
AnsiConsole.MarkupLine($"[green]Endpoint:[/] {config.BaseUrl}");

var logger = new LlmCallLogger();
var llmService = new LlmService(config, logger);

try
{
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("Testing LLM connectivity...",
            async _ =>
            {
                var response = await llmService.GenerateAsync(
                    stageName: "SystemCheck",
                    systemPrompt: "You are a database administrator assistant.",
                    userPrompt: "Respond with exactly: 'DBA Agent pipeline operational.'"
                );

                AnsiConsole.MarkupLine($"[bold blue]LLM Response:[/] {response.Trim()}");
            });

    AnsiConsole.MarkupLine("[green]Test call logged successfully to logs/llm_calls.jsonl[/]");
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[bold red]Connection Failed:[/] {ex.Message}");
}
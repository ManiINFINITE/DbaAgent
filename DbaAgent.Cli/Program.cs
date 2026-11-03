using DbaAgent.Core.Models;
using DbaAgent.Core.Services;
using DbaAgent.Core.Stages;
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

var logger = new LlmCallLogger();
var llmService = new LlmService(config, logger);
var promptService = new PromptService("prompts");
var stateManager = new StateManager("output");

var state = await stateManager.LoadOrCreateAsync();

AnsiConsole.MarkupLine(
    $"[grey]Session ID:[/] [yellow]{state.SessionId}[/] | [grey]Current Stage:[/] [yellow]{state.CurrentStage}[/]");

if (stateManager.HasExistingSession() && !string.IsNullOrWhiteSpace(state.RawDescription))
{
    var resume = AnsiConsole.Confirm("An existing session was found. Do you want to resume it?");

    if (!resume)
    {
        state = new SessionState();
        await stateManager.SaveAsync(state);
        AnsiConsole.MarkupLine("[yellow]Started a fresh session.[/]");
    }
}

// Stage 1: System Intake
if (state.CurrentStage == WorkflowStage.Intake)
{
    AnsiConsole.Write(new Rule("[bold yellow]Stage 1: System Intake[/]").LeftJustified());
    AnsiConsole.MarkupLine("[italic grey]Describe the system you need a database for. Press Enter when finished:[/]\n");

    var userInput = AnsiConsole.Prompt(
        new TextPrompt<string>("[green]Describe system>[/]")
            .PromptStyle("white")
    );

    var intakeStage = new Stage1Intake(llmService, promptService, stateManager);

    string summary = string.Empty;

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("Analyzing requirements and extracting domain entities...",
            async _ => { summary = await intakeStage.ExecuteAsync(state, userInput); });

    AnsiConsole.WriteLine();

    AnsiConsole.Write(new Panel(new Markup(summary.Replace("[", "[[").Replace("]", "]]")))
    {
        Header = new PanelHeader("[bold cyan]Intake Domain Summary[/]"),
        Border = BoxBorder.Rounded
    });

    AnsiConsole.MarkupLine("\n[green]Stage 1 complete! State saved to output/state.json[/]");
}
else
{
    AnsiConsole.MarkupLine($"[blue]Resumed at stage:[/] [bold]{state.CurrentStage}[/]");

    if (!string.IsNullOrWhiteSpace(state.IntakeSummary))
    {
        AnsiConsole.Write(new Panel(new Markup(state.IntakeSummary.Replace("[", "[[").Replace("]", "]]")))
        {
            Header = new PanelHeader("[bold cyan]Loaded Intake Summary[/]"),
            Border = BoxBorder.Rounded
        });
    }
}
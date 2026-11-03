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

// -------------------------------------------------------------
// Stage 1: System Intake
// -------------------------------------------------------------
if (state.CurrentStage == WorkflowStage.Intake)
{
    AnsiConsole.Write(new Rule("[bold yellow]Stage 1: System Intake[/]").LeftJustified());

    var userInput = ReadMultiLineInput("[green]Describe the system you need a database for:[/]");

    var intakeStage = new Stage1Intake(llmService, promptService, stateManager);

    var summary = string.Empty;

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

// -------------------------------------------------------------
// Stage 2: Requirements Interview
// -------------------------------------------------------------
if (state.CurrentStage == WorkflowStage.Interview)
{
    AnsiConsole.Write(new Rule("[bold yellow]Stage 2: Requirements Interview[/]").LeftJustified());

    AnsiConsole.MarkupLine(
        "[grey]The agent will ask technical questions to clarify database constraints, volumes, and business rules.[/]\n");

    var interviewStage = new Stage2Interview(llmService, promptService, stateManager);
    var roundNumber = state.InterviewHistory.Count + 1;
    var interviewComplete = false;

    while (!interviewComplete)
    {
        AnsiConsole.MarkupLine($"\n[bold cyan]── Interview Round {roundNumber} ──[/]");

        var questions = string.Empty;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Formulating questions based on domain model and prior answers...",
                async _ => { questions = await interviewStage.GenerateQuestionsAsync(state); });

        AnsiConsole.Write(new Panel(new Markup(questions.Replace("[", "[[").Replace("]", "]]")))
        {
            Header = new PanelHeader($"[bold green]Questions (Round {roundNumber})[/]"),
            Border = BoxBorder.Rounded
        });

        var clientAnswer = ReadMultiLineInput("[green]Your Answers:[/]");

        state.InterviewHistory.Add(new InterviewExchange(questions, clientAnswer));
        await stateManager.SaveAsync(state);

        InterviewEvaluationResult evalResult = null!;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Evaluating requirements sufficiency...",
                async _ => { evalResult = await interviewStage.EvaluateSufficiencyAsync(state, roundNumber); });

        AnsiConsole.MarkupLine($"[grey]Evaluation:[/] [yellow]{evalResult.Reasoning}[/]");

        if (evalResult.IsComplete)
        {
            interviewComplete = true;

            AnsiConsole.MarkupLine(
                "[bold green]Agent has determined that sufficient information has been gathered.[/]");
        }
        else
        {
            roundNumber++;
        }
    }

    // Synthesize final requirements.md
    var requirementsMd = string.Empty;

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("Synthesizing formal requirements specification (requirements.md)...",
            async _ => { requirementsMd = await interviewStage.FinalizeRequirementsAsync(state); });

    AnsiConsole.MarkupLine("\n[bold green]Stage 2 Complete![/] Generated [cyan]output/requirements.md[/]");
}

// -------------------------------------------------------------
// Stage 3: DBMS Selection
// -------------------------------------------------------------
if (state.CurrentStage == WorkflowStage.DbmsSelection)
{
    AnsiConsole.Write(new Rule("[bold yellow]Stage 3: DBMS Selection[/]").LeftJustified());

    AnsiConsole.MarkupLine(
        "[grey]The agent is evaluating candidate relational DBMS engines (PostgreSQL, MySQL, SQLite, SQL Server)...[/]\n");

    var dbmsStage = new Stage3DbmsSelection(llmService, promptService, stateManager);
    var dbmsDoc = string.Empty;

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("Benchmarking requirements against RDBMS engines...",
            async _ => { dbmsDoc = await dbmsStage.ExecuteAsync(state); });

    AnsiConsole.MarkupLine($"[bold green]Selected DBMS:[/] [bold cyan]{state.SelectedDbms}[/]");

    AnsiConsole.Write(new Panel(new Markup(dbmsDoc.Replace("[", "[[").Replace("]", "]]")))
    {
        Header = new PanelHeader($"[bold cyan]DBMS Decision: {state.SelectedDbms}[/]"),
        Border = BoxBorder.Rounded
    });

    AnsiConsole.MarkupLine("\n[bold green]Stage 3 Complete![/] Written to [cyan]output/dbms-choice.md[/]");
}

static string ReadMultiLineInput(string headerMarkup)
{
    AnsiConsole.MarkupLine(headerMarkup);

    AnsiConsole.MarkupLine(
        "[italic grey](Paste or type your text. When finished, type [bold white]END[/] on a new line and press Enter)[/]\n");

    var lines = new List<string>();

    while (true)
    {
        var line = Console.ReadLine();

        if (line is null || line.Trim().Equals("END", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        lines.Add(line);
    }

    return string.Join(Environment.NewLine, lines).Trim();
}
using DbaAgent.Core.Models;
using DbaAgent.Core.Services;
using DbaAgent.Core.Stages;
using Microsoft.Extensions.Configuration;
using Spectre.Console;

if (Directory.Exists("output"))
{
    foreach (var file in Directory.GetFiles("output", "*.md"))
    {
        var text = await File.ReadAllTextAsync(file);
        await File.WriteAllTextAsync(file, TextSanitizer.StripOuterCodeFence(text));
    }
}

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

var dbValidationConfig = configuration.GetSection("DbValidationConfig").Get<DbValidationConfig>()
                         ?? new DbValidationConfig();

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

// -------------------------------------------------------------
// Stage 4: Design Documentation
// -------------------------------------------------------------
if (state.CurrentStage == WorkflowStage.DesignDocs)
{
    AnsiConsole.Write(new Rule("[bold yellow]Stage 4: Design Documentation[/]").LeftJustified());
    AnsiConsole.MarkupLine("[grey]The agent is generating complete design documentation and diagrams...[/]\n");

    var designStage = new Stage4DesignDocumentation(llmService, promptService, stateManager);

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("Initializing documentation pipeline...",
            async ctx => { await designStage.ExecuteAsync(state, statusText => { ctx.Status(statusText); }); });

    AnsiConsole.MarkupLine("[bold green]Stage 4 Complete![/] Generated 4 design artifacts in [cyan]output/[/]:");
    AnsiConsole.MarkupLine("  [green]✓[/] [cyan]output/conceptual-model.md[/] (with Mermaid ER diagram)");
    AnsiConsole.MarkupLine("  [green]✓[/] [cyan]output/logical-schema.md[/] (tables, columns, types, indexes)");
    AnsiConsole.MarkupLine("  [green]✓[/] [cyan]output/normalization.md[/] (FDs and normal-form proofs)");
    AnsiConsole.MarkupLine("  [green]✓[/] [cyan]output/assumptions.md[/] (boundary rules and ambiguities)");
}

// -------------------------------------------------------------
// Stage 5: Revision Loop
// -------------------------------------------------------------
if (state.CurrentStage == WorkflowStage.RevisionLoop)
{
    AnsiConsole.Write(new Rule("[bold yellow]Stage 5: Revision Loop[/]").LeftJustified());

    AnsiConsole.MarkupLine(
        "[grey]You can request changes in natural language (e.g. 'add courier tip support', 'make addresses M:N').[/]");

    AnsiConsole.MarkupLine(
        "[grey]The agent will propagate changes to all affected files and record them in changelog.md.[/]");

    AnsiConsole.MarkupLine(
        "[grey]Type [bold white]approve[/] when you are satisfied with the design to proceed to SQL generation.[/]\n");

    var revisionStage = new Stage5RevisionLoop(llmService, promptService, stateManager);

    while (!state.IsApproved)
    {
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What would you like to do?")
                .AddChoices([
                    "Request a design modification",
                    "Approve design and proceed to SQL Generation"
                ])
        );

        if (action.StartsWith("Approve", StringComparison.OrdinalIgnoreCase))
        {
            await revisionStage.FinalizeApprovalAsync(state);

            AnsiConsole.MarkupLine(
                "[bold green]Design explicitly approved![/] Advancing to Stage 6 (SQL Generation)...");

            break;
        }

        var modificationRequest = ReadMultiLineInput("[green]Describe the requested design modification:[/]");

        if (string.IsNullOrWhiteSpace(modificationRequest) ||
            modificationRequest.Trim().Equals("approve", StringComparison.OrdinalIgnoreCase))
        {
            await revisionStage.FinalizeApprovalAsync(state);

            AnsiConsole.MarkupLine(
                "[bold green]Design explicitly approved![/] Advancing to Stage 6 (SQL Generation)...");

            break;
        }

        RevisionExecutionResult result = null!;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Propagating revision across affected artifacts...",
                async ctx =>
                {
                    result = await revisionStage.ApplyChangeAsync(state,
                        modificationRequest,
                        statusMsg => { ctx.Status(statusMsg); });
                });

        AnsiConsole.MarkupLine($"\n[bold green]Revision Applied:[/] {result.Summary}");
        AnsiConsole.MarkupLine($"[grey]Rationale:[/] {result.Rationale}");

        AnsiConsole.MarkupLine("[bold cyan]Propagated to files:[/] " +
                               string.Join(", ", result.UpdatedFiles.Select(f => $"[yellow]{f}[/]")));

        AnsiConsole.MarkupLine("[grey]Entry appended to [cyan]output/changelog.md[/][/]\n");
    }
}

// -------------------------------------------------------------
// Stage 6: SQL Generation
// -------------------------------------------------------------
if (state.CurrentStage == WorkflowStage.SqlGeneration)
{
    AnsiConsole.Write(new Rule("[bold yellow]Stage 6: SQL Generation[/]").LeftJustified());
    AnsiConsole.MarkupLine($"[grey]Generating executable SQL artifacts for [bold cyan]{state.SelectedDbms}[/]...[/]\n");

    var sqlStage = new Stage6SqlGeneration(llmService, promptService, stateManager);

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("Generating SQL scripts...",
            async ctx => { await sqlStage.ExecuteAsync(state, statusText => { ctx.Status(statusText); }); });

    AnsiConsole.MarkupLine("\n[bold green]Stage 6 Complete![/] Generated SQL artifacts in [cyan]output/[/]:");
    AnsiConsole.MarkupLine("  [green]✓[/] [cyan]output/schema.sql[/] (DDL tables, constraints, partial indexes)");
    AnsiConsole.MarkupLine("  [green]✓[/] [cyan]output/seed.sql[/] (Realistic test dataset with foreign key ordering)");
}

// -------------------------------------------------------------
// Bonus (+15%): Real DBMS Validation Loop
// -------------------------------------------------------------
if (state.CurrentStage == WorkflowStage.Completed)
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold yellow]Bonus (+15%): Real DBMS Validation Loop[/]").LeftJustified());

    var runBonus = AnsiConsole.Confirm(
        "Would you like to run automated live validation against the real DBMS instance?",
        defaultValue: true);

    if (runBonus)
    {
        var validationEngine = new ValidationEngine(
            llmService,
            promptService,
            stateManager,
            logger,
            dbValidationConfig);

        try
        {
            var isFullyValidated = false;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Executing schema and seed scripts in real DBMS...",
                    async ctx =>
                    {
                        isFullyValidated =
                            await validationEngine.RunValidationLoopAsync(state, msg => { ctx.Status(msg); });
                    });

            if (isFullyValidated)
            {
                AnsiConsole.MarkupLine(
                    "[bold green]SUCCESS:[/] Both schema.sql and seed.sql executed cleanly with 0 errors in the live database!");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"[bold red]Validation failed after {dbValidationConfig.MaxRepairAttempts} repair attempts.[/] Check logs/llm_calls.jsonl for database error traces.");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[bold red]Database Connection Note:[/] {ex.Message}");

            AnsiConsole.MarkupLine(
                "[grey]To run PostgreSQL in Docker: [white]docker run --name pg-test -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=dba_agent_test -p 5432:5432 -d postgres:16-alpine[/][/]");
        }
    }
}

// -------------------------------------------------------------
// Pipeline Complete Summary
// -------------------------------------------------------------
if (state.CurrentStage == WorkflowStage.Completed)
{
    AnsiConsole.WriteLine();
    AnsiConsole.Write(new Rule("[bold green]Pipeline Execution Completed[/]").LeftJustified());

    var summaryTable = new Table().Border(TableBorder.Rounded);
    summaryTable.AddColumn("[bold]Artifact[/]");
    summaryTable.AddColumn("[bold]Description[/]");
    summaryTable.AddColumn("[bold]Status[/]");

    summaryTable.AddRow("requirements.md", "Synthesized requirements specification", "[green]Ready[/]");
    summaryTable.AddRow("dbms-choice.md", $"Comparison matrix & {state.SelectedDbms} justification", "[green]Ready[/]");
    summaryTable.AddRow("conceptual-model.md", "Conceptual entities & Mermaid ER diagram", "[green]Ready[/]");
    summaryTable.AddRow("logical-schema.md", "Tables, types, constraints, relational diagram", "[green]Ready[/]");
    summaryTable.AddRow("normalization.md", "Functional dependencies & 1NF-BCNF proofs", "[green]Ready[/]");
    summaryTable.AddRow("assumptions.md", "System boundaries & architectural assumptions", "[green]Ready[/]");
    summaryTable.AddRow("changelog.md", "Historical log of all propagated design revisions", "[green]Ready[/]");
    summaryTable.AddRow("schema.sql", $"Executable DDL script for {state.SelectedDbms}", "[green]Ready[/]");
    summaryTable.AddRow("seed.sql", "Executable realistic sample data insertion script", "[green]Ready[/]");

    AnsiConsole.Write(summaryTable);
    AnsiConsole.MarkupLine("\n[bold green]All deliverables generated successfully in output/ directory![/]");
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
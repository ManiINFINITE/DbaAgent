using System.Text.RegularExpressions;
using DbaAgent.Core.Models;
using DbaAgent.Core.Services;

namespace DbaAgent.Core.Stages;

public sealed partial class Stage3DbmsSelection(
    ILlmService llmService,
    IPromptService promptService,
    StateManager stateManager)
{
    [GeneratedRegex(@"\[SELECTED_DBMS:\s*(?<name>PostgreSQL|MySQL|SQLite|SQL Server)\]", RegexOptions.IgnoreCase)]
    private static partial Regex SelectedDbmsRegex();

    public async Task<string> ExecuteAsync(SessionState state)
    {
        const string systemPrompt = "You are a Principal Database Administrator selecting a relational DBMS. Provide rigorous architectural justifications and a comparison table.";

        var userPrompt = await promptService.RenderAsync("stage3_dbms_selection.scriban",
            new
            {
                requirements_doc = state.RequirementsDoc
            });

        var rawEvaluation = await llmService.GenerateAsync(
            stageName: "Stage3_DbmsSelection",
            systemPrompt: systemPrompt,
            userPrompt: userPrompt
        );

        // Extract machine-readable tag
        var match = SelectedDbmsRegex().Match(rawEvaluation);
        state.SelectedDbms = match.Success ? match.Groups["name"].Value : "PostgreSQL";

        // Strip the machine-readable tag from the persisted Markdown artifact
        state.DbmsChoiceDoc = SelectedDbmsRegex().Replace(rawEvaluation, string.Empty).Trim();
        state.CurrentStage = WorkflowStage.DesignDocs;

        // Persist artifacts
        await stateManager.WriteArtifactAsync("dbms-choice.md", state.DbmsChoiceDoc);
        await stateManager.SaveAsync(state);

        return state.DbmsChoiceDoc;
    }
}
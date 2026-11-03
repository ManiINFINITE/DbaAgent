using DbaAgent.Core.Models;
using DbaAgent.Core.Services;

namespace DbaAgent.Core.Stages;

public sealed class Stage1Intake(ILlmService llmService, IPromptService promptService, StateManager stateManager)
{
    public async Task<string> ExecuteAsync(SessionState state, string rawDescription)
    {
        state.RawDescription = rawDescription;

        var systemPrompt = await promptService.RenderAsync("stage1_intake_system.scriban", new { });

        var userPrompt = await promptService.RenderAsync("stage1_intake_user.scriban",
            new
            {
                raw_description = rawDescription
            });

        var summary = await llmService.GenerateAsync(
            stageName: "Stage1_Intake",
            systemPrompt: systemPrompt,
            userPrompt: userPrompt
        );

        state.IntakeSummary = summary.Trim();
        state.CurrentStage = WorkflowStage.Interview;

        await stateManager.SaveAsync(state);
        return state.IntakeSummary;
    }
}
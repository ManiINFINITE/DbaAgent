using System.Text.Json;
using DbaAgent.Core.Models;
using DbaAgent.Core.Services;

namespace DbaAgent.Core.Stages;

public sealed class Stage2Interview
{
    readonly private ILlmService _llmService;
    readonly private IPromptService _promptService;
    readonly private StateManager _stateManager;

    private const int MaxAllowedRounds = 4;
    private const int MinRequiredRounds = 2;

    public Stage2Interview(ILlmService llmService, IPromptService promptService, StateManager stateManager)
    {
        _llmService = llmService;
        _promptService = promptService;
        _stateManager = stateManager;
    }

    public async Task<string> GenerateQuestionsAsync(SessionState state)
    {
        var systemPrompt =
            "You are a professional Database Administrator conducting a structured requirements interview.";

        var userPrompt = await _promptService.RenderAsync("stage2_interview_questions.scriban",
            new
            {
                raw_description = state.RawDescription,
                intake_summary = state.IntakeSummary,
                interview_history = state.InterviewHistory
            });

        return await _llmService.GenerateAsync("Stage2_GenerateQuestions", systemPrompt, userPrompt);
    }

    public async Task<InterviewEvaluationResult> EvaluateSufficiencyAsync(SessionState state, int completedRounds)
    {
        if (completedRounds < MinRequiredRounds)
        {
            return new InterviewEvaluationResult(
                Status: "CONTINUE",
                Reasoning: $"At least {MinRequiredRounds} interview rounds are required by project rules."
            );
        }

        if (completedRounds >= MaxAllowedRounds)
        {
            return new InterviewEvaluationResult(
                Status: "COMPLETE",
                Reasoning: $"Reached maximum interview rounds threshold ({MaxAllowedRounds})."
            );
        }

        var systemPrompt =
            "You are a Database Architect evaluating requirement completeness. You respond strictly in valid JSON.";

        var userPrompt = await _promptService.RenderAsync("stage2_interview_evaluate.scriban",
            new
            {
                raw_description = state.RawDescription,
                intake_summary = state.IntakeSummary,
                interview_history = state.InterviewHistory,
                completed_rounds = completedRounds
            });

        var rawResponse = await _llmService.GenerateAsync("Stage2_EvaluateSufficiency", systemPrompt, userPrompt);

        try
        {
            var cleanedJson = ExtractJson(rawResponse);
            using var doc = JsonDocument.Parse(cleanedJson);
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetString() ?? "CONTINUE";
            var reasoning = root.GetProperty("reasoning").GetString() ?? string.Empty;

            return new InterviewEvaluationResult(status.ToUpperInvariant(), reasoning);
        }
        catch
        {
            // Fallback: If JSON parsing fails, continue if below 2 rounds, else complete
            var fallbackStatus = completedRounds < MinRequiredRounds ? "CONTINUE" : "COMPLETE";
            return new InterviewEvaluationResult(fallbackStatus, "Parsed via fallback evaluator heuristic.");
        }
    }

    public async Task<string> FinalizeRequirementsAsync(SessionState state)
    {
        var systemPrompt =
            "You are a Database Architect authoring formal database requirements documentation in Markdown.";

        var userPrompt = await _promptService.RenderAsync("stage2_synthesize_requirements.scriban",
            new
            {
                raw_description = state.RawDescription,
                intake_summary = state.IntakeSummary,
                interview_history = state.InterviewHistory
            });

        var requirementsMarkdown =
            await _llmService.GenerateAsync("Stage2_SynthesizeRequirements", systemPrompt, userPrompt);

        state.RequirementsDoc = requirementsMarkdown.Trim();
        state.CurrentStage = WorkflowStage.DbmsSelection;

        // Persist both the state JSON and the required requirements.md artifact
        await _stateManager.WriteArtifactAsync("requirements.md", state.RequirementsDoc);
        await _stateManager.SaveAsync(state);

        return state.RequirementsDoc;
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start != -1 && end > start ? text.Substring(start, end - start + 1) : text;
    }
}

public sealed record InterviewEvaluationResult(string Status, string Reasoning)
{
    public bool IsComplete => Status.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase);
}
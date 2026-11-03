using System.Text;
using System.Text.Json;
using DbaAgent.Core.Models;
using DbaAgent.Core.Services;
using DbaAgent.Core.Validation;

namespace DbaAgent.Core.Stages;

public sealed class Stage5RevisionLoop(ILlmService llmService, IPromptService promptService, StateManager stateManager)
{
    public async Task<RevisionExecutionResult> ApplyChangeAsync(
        SessionState state,
        string userRequest,
        Action<string> statusCallback
    )
    {
        statusCallback("Analyzing modification request and computing file impact...");

        // 1. Run Impact Analysis
        const string impactSystem =
            "You are a Database Architect evaluating change impacts. Respond strictly in valid JSON.";

        var impactUser = await promptService.RenderAsync("stage5_impact_analysis.scriban",
            new
            {
                selected_dbms = state.SelectedDbms,
                user_request = userRequest
            });

        var impactRaw = await llmService.GenerateAsync("Stage5_ImpactAnalysis", impactSystem, impactUser);
        var plan = ParseImpactPlan(impactRaw);

        // Handle DBMS switch if requested
        if (!string.IsNullOrWhiteSpace(plan.SwitchDbmsTo))
        {
            state.SelectedDbms = plan.SwitchDbmsTo;

            if (!plan.AffectedFiles.Contains("dbms-choice.md", StringComparer.OrdinalIgnoreCase))
            {
                plan.AffectedFiles.Add("dbms-choice.md");
            }

            if (!plan.AffectedFiles.Contains("logical-schema.md", StringComparer.OrdinalIgnoreCase))
            {
                plan.AffectedFiles.Add("logical-schema.md");
            }
        }

        // 2. Propagate updates across every affected file
        var updatedFiles = new List<string>();

        foreach (var fileName in plan.AffectedFiles)
        {
            var cleanName = fileName.Trim().ToLowerInvariant();
            var currentContent = GetFileContentFromState(state, cleanName);

            if (string.IsNullOrWhiteSpace(currentContent))
            {
                continue;
            }

            statusCallback($"Updating {cleanName} to reflect changes...");

            var updateSystem = "You are a Principal Database Administrator updating project documentation.";

            var updateUser = await promptService.RenderAsync("stage5_update_file.scriban",
                new
                {
                    selected_dbms = state.SelectedDbms,
                    file_name = cleanName,
                    change_summary = plan.Summary,
                    change_rationale = plan.Rationale,
                    current_file_content = currentContent
                });

            var revisedContent =
                await llmService.GenerateAsync($"Stage5_Update_{cleanName}", updateSystem, updateUser);

            // 1. Sanitize the generated content before validation
            var preparedContent = TextSanitizer.SanitizeMermaid(
                TextSanitizer.StripOuterCodeFence(revisedContent.Trim())
            );

            // 2. Validate Mermaid diagrams in the relevant files
            if (cleanName is "conceptual-model.md" or "logical-schema.md")
            {
                var validationResult = DocumentValidator.ValidateMermaidDiagram(preparedContent);

                if (!validationResult.IsValid)
                {
                    throw new InvalidOperationException(
                        $"Validation failed for {cleanName}:\n{validationResult.ErrorMessage}"
                    );
                }
            }

            // 3. Save only after validation succeeds
            var sanitizedContent = await stateManager.WriteArtifactAsync(cleanName, preparedContent);

            SetFileContentInState(state, cleanName, sanitizedContent);
            updatedFiles.Add(cleanName);
        }

        // 3. Record entry into changelog.md
        statusCallback("Recording change entry in changelog.md...");
        var changelogEntry = BuildChangelogEntry(plan.Summary, plan.Rationale, userRequest, updatedFiles);

        state.ChangelogDoc = string.IsNullOrWhiteSpace(state.ChangelogDoc)
            ? $"# Design Modification Changelog\n\n{changelogEntry}"
            : $"{state.ChangelogDoc.Trim()}\n\n---\n\n{changelogEntry}";

        await stateManager.WriteArtifactAsync("changelog.md", state.ChangelogDoc);
        await stateManager.SaveAsync(state);

        return new RevisionExecutionResult(plan.Summary, plan.Rationale, updatedFiles);
    }

    public async Task FinalizeApprovalAsync(SessionState state)
    {
        state.IsApproved = true;
        state.CurrentStage = WorkflowStage.SqlGeneration;

        // Ensure changelog.md exists even if zero revisions were requested
        if (string.IsNullOrWhiteSpace(state.ChangelogDoc))
        {
            state.ChangelogDoc =
                "# Design Modification Changelog\n\n*No revisions requested. Initial baseline design approved directly.*";

            await stateManager.WriteArtifactAsync("changelog.md", state.ChangelogDoc);
        }

        await stateManager.SaveAsync(state);
    }

    private static RevisionPlan ParseImpactPlan(string rawJson)
    {
        try
        {
            var start = rawJson.IndexOf('{');
            var end = rawJson.LastIndexOf('}');
            var cleaned = start != -1 && end > start ? rawJson.Substring(start, end - start + 1) : rawJson;

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var summary = root.GetProperty("summary").GetString() ?? "Schema adjustment";
            var rationale = root.GetProperty("rationale").GetString() ?? string.Empty;

            var affectedFiles = new List<string>();

            if (root.TryGetProperty("affected_files", out var filesElem) && filesElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in filesElem.EnumerateArray())
                {
                    var file = item.GetString();

                    if (!string.IsNullOrWhiteSpace(file))
                    {
                        affectedFiles.Add(file);
                    }
                }
            }

            string? switchDbms = null;

            if (root.TryGetProperty("switch_dbms_to", out var dbmsElem) && dbmsElem.ValueKind == JsonValueKind.String)
            {
                switchDbms = dbmsElem.GetString();
            }

            return new RevisionPlan(summary, rationale, affectedFiles, switchDbms);
        }
        catch
        {
            // Fallback: update core schema files if JSON parsing fails
            return new RevisionPlan(
                Summary: "Refactor requested by user",
                Rationale: "Iterative design refinement",
                AffectedFiles: ["conceptual-model.md", "logical-schema.md", "normalization.md", "assumptions.md"],
                SwitchDbmsTo: null
            );
        }
    }

    private static string GetFileContentFromState(SessionState state, string fileName) => fileName switch
    {
        "requirements.md" => state.RequirementsDoc,
        "dbms-choice.md" => state.DbmsChoiceDoc,
        "conceptual-model.md" => state.ConceptualModelDoc,
        "logical-schema.md" => state.LogicalSchemaDoc,
        "normalization.md" => state.NormalizationDoc,
        "assumptions.md" => state.AssumptionsDoc,
        _ => string.Empty
    };

    private static void SetFileContentInState(SessionState state, string fileName, string content)
    {
        switch (fileName)
        {
            case "requirements.md": state.RequirementsDoc = content; break;
            case "dbms-choice.md": state.DbmsChoiceDoc = content; break;
            case "conceptual-model.md": state.ConceptualModelDoc = content; break;
            case "logical-schema.md": state.LogicalSchemaDoc = content; break;
            case "normalization.md": state.NormalizationDoc = content; break;
            case "assumptions.md": state.AssumptionsDoc = content; break;
        }
    }

    private static string BuildChangelogEntry(
        string summary,
        string rationale,
        string rawPrompt,
        List<string> affectedFiles
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine($"### [{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] - {summary}");
        sb.AppendLine($"**User Request:** *\"{rawPrompt}\"*");
        sb.AppendLine($"**Rationale:** {rationale}");
        sb.AppendLine("**Affected Files:**");

        foreach (var file in affectedFiles)
        {
            sb.AppendLine($"- `{file}`");
        }

        return sb.ToString();
    }
}

public sealed record RevisionPlan(
    string Summary,
    string Rationale,
    List<string> AffectedFiles,
    string? SwitchDbmsTo
);

public sealed record RevisionExecutionResult(
    string Summary,
    string Rationale,
    List<string> UpdatedFiles
);
using DbaAgent.Core.Models;
using DbaAgent.Core.Services;

namespace DbaAgent.Core.Stages;

public sealed class Stage4DesignDocumentation(
    ILlmService llmService,
    IPromptService promptService,
    StateManager stateManager)
{
    public async Task ExecuteAsync(SessionState state, Action<string> progressCallback)
    {
        // 1. Conceptual Model
        progressCallback("Authoring conceptual model and Mermaid ER diagram (conceptual-model.md)...");

        const string conceptualSystem =
            "You are a Principal Database Designer generating conceptual ER models and Mermaid diagrams.";

        var conceptualPrompt = await promptService.RenderAsync("stage4_conceptual.scriban",
            new
            {
                requirements_doc = state.RequirementsDoc,
                selected_dbms = state.SelectedDbms
            });

        state.ConceptualModelDoc =
            (await llmService.GenerateAsync("Stage4_Conceptual", conceptualSystem, conceptualPrompt)).Trim();

        await stateManager.WriteArtifactAsync("conceptual-model.md", state.ConceptualModelDoc);
        await stateManager.SaveAsync(state);

        // 2. Logical Schema (uses conceptual model to ensure consistency)
        progressCallback("Translating to DBMS-specific logical schema (logical-schema.md)...");

        const string logicalSystem =
            "You are a Senior DBA designing relational schemas with specific constraints, indexes, and types.";

        var logicalPrompt = await promptService.RenderAsync("stage4_logical.scriban",
            new
            {
                requirements_doc = state.RequirementsDoc,
                conceptual_doc = state.ConceptualModelDoc,
                selected_dbms = state.SelectedDbms
            });

        state.LogicalSchemaDoc =
            (await llmService.GenerateAsync("Stage4_Logical", logicalSystem, logicalPrompt)).Trim();

        await stateManager.WriteArtifactAsync("logical-schema.md", state.LogicalSchemaDoc);
        await stateManager.SaveAsync(state);

        // 3. Normalization Analysis (uses logical schema)
        progressCallback("Performing 1NF-BCNF normalization & functional dependency analysis (normalization.md)...");
        const string normSystem = "You are a Database Theorist conducting formal normal form and dependency proofs.";

        var normPrompt = await promptService.RenderAsync("stage4_normalization.scriban",
            new
            {
                requirements_doc = state.RequirementsDoc,
                logical_doc = state.LogicalSchemaDoc
            });

        state.NormalizationDoc =
            (await llmService.GenerateAsync("Stage4_Normalization", normSystem, normPrompt)).Trim();

        await stateManager.WriteArtifactAsync("normalization.md", state.NormalizationDoc);
        await stateManager.SaveAsync(state);

        // 4. Assumptions & Ambiguities
        progressCallback("Documenting design assumptions and boundary conditions (assumptions.md)...");

        const string assumptionsSystem =
            "You are a Software Architect cataloging technical assumptions and ambiguities.";

        var assumptionsPrompt = await promptService.RenderAsync("stage4_assumptions.scriban",
            new
            {
                requirements_doc = state.RequirementsDoc,
                logical_doc = state.LogicalSchemaDoc
            });

        state.AssumptionsDoc =
            (await llmService.GenerateAsync("Stage4_Assumptions", assumptionsSystem, assumptionsPrompt)).Trim();

        await stateManager.WriteArtifactAsync("assumptions.md", state.AssumptionsDoc);

        // Advance stage
        state.CurrentStage = WorkflowStage.RevisionLoop;
        await stateManager.SaveAsync(state);
    }
}
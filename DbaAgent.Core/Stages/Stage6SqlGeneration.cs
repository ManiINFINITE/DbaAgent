using DbaAgent.Core.Models;
using DbaAgent.Core.Services;

namespace DbaAgent.Core.Stages;

public sealed class Stage6SqlGeneration(ILlmService llmService, IPromptService promptService, StateManager stateManager)
{
    public async Task ExecuteAsync(SessionState state, Action<string> progressCallback)
    {
        // 1. Generate schema.sql
        progressCallback($"Authoring DDL script (schema.sql) for {state.SelectedDbms}...");

        var schemaSystem =
            $"You are a Principal Database Administrator generating production DDL for {state.SelectedDbms}. Output pure SQL.";

        var schemaUser = await promptService.RenderAsync("stage6_schema.scriban",
            new
            {
                selected_dbms = state.SelectedDbms,
                logical_doc = state.LogicalSchemaDoc,
                assumptions_doc = state.AssumptionsDoc
            });

        var rawSchema = await llmService.GenerateAsync("Stage6_SchemaSql", schemaSystem, schemaUser);
        state.SchemaSql = TextSanitizer.StripOuterCodeFence(rawSchema).Trim();

        await stateManager.WriteArtifactAsync("schema.sql", state.SchemaSql);
        await stateManager.SaveAsync(state);

        // 2. Generate seed.sql using the finalized DDL to guarantee foreign key alignment
        progressCallback($"Synthesizing realistic seed dataset (seed.sql) for {state.SelectedDbms}...");

        var seedSystem =
            $"You are a Principal DBA authoring realistic production test datasets for {state.SelectedDbms}. Output pure SQL.";

        var seedUser = await promptService.RenderAsync("stage6_seed.scriban",
            new
            {
                selected_dbms = state.SelectedDbms,
                schema_sql = state.SchemaSql,
                requirements_doc = state.RequirementsDoc
            });

        var rawSeed = await llmService.GenerateAsync("Stage6_SeedSql", seedSystem, seedUser);
        state.SeedSql = TextSanitizer.StripOuterCodeFence(rawSeed).Trim();

        await stateManager.WriteArtifactAsync("seed.sql", state.SeedSql);

        // 3. Complete pipeline
        state.CurrentStage = WorkflowStage.Completed;
        await stateManager.SaveAsync(state);
    }
}
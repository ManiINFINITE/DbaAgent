using DbaAgent.Core.Models;
using DbaAgent.Core.Services;

namespace DbaAgent.Core.Stages;

public sealed class ValidationEngine(
    ILlmService llmService,
    IPromptService promptService,
    StateManager stateManager,
    LlmCallLogger logger,
    DbValidationConfig config)
{
    private const string RepairedSchemaDelimiter = "===REPAIRED_SCHEMA===";
    private const string RepairedSeedDelimiter = "===REPAIRED_SEED===";

    public async Task<bool> RunValidationLoopAsync(SessionState state, Action<string> statusCallback)
    {
        IDbValidator validator = state.SelectedDbms.ToLowerInvariant() switch
        {
            "postgresql" or "postgres" => new PostgresValidator(config.PostgresConnectionString),
            _ => throw new NotSupportedException(
                $"Automated live validation for '{state.SelectedDbms}' is not configured.")
        };

        var maxAttempts = config.MaxRepairAttempts;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            statusCallback(
                $"Running validation on real {validator.DbmsName} instance (Attempt {attempt}/{maxAttempts})...");

            var result = await validator.ValidateAsync(state.SchemaSql, state.SeedSql);

            if (result.Success)
            {
                await logger.LogAsync(new LlmCallLog(
                    Timestamp: DateTime.UtcNow,
                    Stage: "ValidationLoop_Success",
                    Attempt: attempt,
                    SystemPrompt: "N/A - Direct DB Execution",
                    UserPrompt: "Execute schema.sql and seed.sql",
                    Response: "SUCCESS: Both schema.sql and seed.sql executed without errors.",
                    DurationMs: 0,
                    Success: true,
                    ErrorMessage: null
                ));

                return true;
            }

            // If connection itself failed, abort immediately so user can check their DB/Docker
            if (result.FailedScriptName == "connection")
            {
                throw new InvalidOperationException(result.ErrorMessage);
            }

            statusCallback(
                $"Validation failed in {result.FailedScriptName}: {result.ErrorMessage}. Requesting LLM self-healing repair...");

            // Log the execution failure
            await logger.LogAsync(new LlmCallLog(
                Timestamp: DateTime.UtcNow,
                Stage: "ValidationLoop_Failure",
                Attempt: attempt,
                SystemPrompt: "N/A - Direct DB Execution",
                UserPrompt: $"Executing {result.FailedScriptName}",
                Response: string.Empty,
                DurationMs: 0,
                Success: false,
                ErrorMessage: $"{result.ErrorMessage} | {result.ErrorDetail}"
            ));

            if (attempt == maxAttempts)
            {
                break;
            }

            // Invoke LLM self-healing repair
            var systemPrompt = $"You are an expert {state.SelectedDbms} DBA repairing faulty SQL scripts.";

            var userPrompt = await promptService.RenderAsync("stage6_sql_repair.scriban",
                new
                {
                    selected_dbms = state.SelectedDbms,
                    failed_script_name = result.FailedScriptName,
                    error_message = $"{result.ErrorMessage} ({result.ErrorDetail})",
                    schema_sql = state.SchemaSql,
                    seed_sql = state.SeedSql
                });

            var repairOutput = await llmService.GenerateAsync("ValidationLoop_Repair", systemPrompt, userPrompt);
            var (repairedSchema, repairedSeed) = ParseRepairOutput(repairOutput, state.SchemaSql, state.SeedSql);

            state.SchemaSql = TextSanitizer.StripOuterCodeFence(repairedSchema).Trim();
            state.SeedSql = TextSanitizer.StripOuterCodeFence(repairedSeed).Trim();

            await stateManager.WriteArtifactAsync("schema.sql", state.SchemaSql);
            await stateManager.WriteArtifactAsync("seed.sql", state.SeedSql);
            await stateManager.SaveAsync(state);
        }

        return false;
    }

    private static (string RepairedSchema, string RepairedSeed) ParseRepairOutput(
        string rawOutput,
        string fallbackSchema,
        string fallbackSeed
    )
    {
        var schemaIndex = rawOutput.IndexOf(RepairedSchemaDelimiter, StringComparison.OrdinalIgnoreCase);
        var seedIndex = rawOutput.IndexOf(RepairedSeedDelimiter, StringComparison.OrdinalIgnoreCase);

        if (schemaIndex == -1 || seedIndex == -1)
        {
            return (fallbackSchema, fallbackSeed);
        }

        var schemaPart = rawOutput.Substring(
            schemaIndex + RepairedSchemaDelimiter.Length,
            seedIndex - (schemaIndex + RepairedSchemaDelimiter.Length)).Trim();

        var seedPart = rawOutput.Substring(seedIndex + RepairedSeedDelimiter.Length).Trim();

        return (schemaPart, seedPart);
    }
}
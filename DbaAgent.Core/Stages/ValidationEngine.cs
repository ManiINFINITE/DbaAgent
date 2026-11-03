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
        // 1. Resolve active scenario name dynamically from the output workspace directory
        var scenarioName = Path.GetFileName(
            stateManager.OutputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        );

        if (string.IsNullOrWhiteSpace(scenarioName))
        {
            scenarioName = "default";
        }

        // 2. Instantiate validator with the target scenario name for automated DB isolation
        IDbValidator validator = state.SelectedDbms.ToLowerInvariant() switch
        {
            "postgresql" or "postgres" => new PostgresValidator(config.PostgresConnectionString, scenarioName),
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

            // Abort immediately on environment/network failures to prevent burning tokens on SQL repairs
            if (result.FailedScriptName is "connection" or "provisioning")
            {
                throw new InvalidOperationException(
                    $"{result.ErrorMessage}\nDetails: {result.ErrorDetail}");
            }

            statusCallback(
                $"Validation failed in {result.FailedScriptName}: {result.ErrorMessage}. Requesting LLM self-healing repair...");

            // Log execution failure
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

        // Neither delimiter was returned: fallback
        if (schemaIndex == -1 && seedIndex == -1)
        {
            return (fallbackSchema, fallbackSeed);
        }

        var repairedSchema = fallbackSchema;
        var repairedSeed = fallbackSeed;

        if (schemaIndex != -1 && seedIndex != -1 && seedIndex > schemaIndex)
        {
            var schemaStart = schemaIndex + RepairedSchemaDelimiter.Length;
            repairedSchema = rawOutput.Substring(schemaStart, seedIndex - schemaStart).Trim();
            repairedSeed = rawOutput.Substring(seedIndex + RepairedSeedDelimiter.Length).Trim();
        }
        else if (schemaIndex != -1 && seedIndex == -1)
        {
            repairedSchema = rawOutput.Substring(schemaIndex + RepairedSchemaDelimiter.Length).Trim();
        }
        else if (seedIndex != -1 && schemaIndex == -1)
        {
            repairedSeed = rawOutput.Substring(seedIndex + RepairedSeedDelimiter.Length).Trim();
        }

        return (
            string.IsNullOrWhiteSpace(repairedSchema) ? fallbackSchema : repairedSchema,
            string.IsNullOrWhiteSpace(repairedSeed) ? fallbackSeed : repairedSeed
        );
    }
}
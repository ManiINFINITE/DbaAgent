using Npgsql;

namespace DbaAgent.Core.Services;

public sealed class PostgresValidator : IDbValidator
{
    readonly private string _connectionString;

    public string DbmsName => "PostgreSQL";

    public PostgresValidator(string connectionString) =>
        _connectionString = connectionString;

    public async Task<DbValidationResult> ValidateAsync(string schemaSql, string seedSql)
    {
        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // 1. Execute schema.sql
            try
            {
                await using var schemaCmd = new NpgsqlCommand(schemaSql, connection);
                await schemaCmd.ExecuteNonQueryAsync();
            }
            catch (PostgresException pex)
            {
                return new DbValidationResult(
                    Success: false,
                    FailedScriptName: "schema.sql",
                    ErrorMessage: $"PostgreSQL error in schema.sql: {pex.MessageText} (Code: {pex.SqlState})",
                    ErrorDetail: $"Line: {pex.Line}, Position: {pex.Position}, Where: {pex.Where}"
                );
            }
            catch (Exception ex)
            {
                return new DbValidationResult(
                    Success: false,
                    FailedScriptName: "schema.sql",
                    ErrorMessage: ex.Message,
                    ErrorDetail: ex.StackTrace
                );
            }

            // 2. Execute seed.sql
            try
            {
                await using var seedCmd = new NpgsqlCommand(seedSql, connection);
                await seedCmd.ExecuteNonQueryAsync();
            }
            catch (PostgresException pex)
            {
                return new DbValidationResult(
                    Success: false,
                    FailedScriptName: "seed.sql",
                    ErrorMessage: $"PostgreSQL error in seed.sql: {pex.MessageText} (Code: {pex.SqlState})",
                    ErrorDetail: $"Detail: {pex.Detail}, Table: {pex.TableName}, Constraint: {pex.ConstraintName}"
                );
            }
            catch (Exception ex)
            {
                return new DbValidationResult(
                    Success: false,
                    FailedScriptName: "seed.sql",
                    ErrorMessage: ex.Message,
                    ErrorDetail: ex.StackTrace
                );
            }

            return new DbValidationResult(
                Success: true,
                FailedScriptName: null,
                ErrorMessage: null,
                ErrorDetail: null
            );
        }
        catch (NpgsqlException ex)
        {
            return new DbValidationResult(
                Success: false,
                FailedScriptName: "connection",
                ErrorMessage: $"Cannot connect to PostgreSQL at '{_connectionString}': {ex.Message}",
                ErrorDetail: "Ensure the PostgreSQL database server or Docker container is running."
            );
        }
    }
}
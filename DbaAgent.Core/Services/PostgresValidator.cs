using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Npgsql;

namespace DbaAgent.Core.Services;

public sealed class PostgresValidator(string connectionString, string scenarioName = "default") : IDbValidator
{
    readonly private string _baseConnectionString = connectionString;
    readonly private string _scenarioName = scenarioName;

    private const int ContainerStartupTimeoutMs = 15000;
    private const string DefaultContainerName = "pg-test";

    public string DbmsName => "PostgreSQL";

    public async Task<DbValidationResult> ValidateAsync(string schemaSql, string seedSql)
    {
        try
        {
            // 1. Ensure Docker container is running and port is listening
            await EnsureDockerContainerRunningAsync();

            // 2. Provision isolated database for this scenario and get updated connection string
            var scenarioConnectionString = await EnsureScenarioDatabaseExistsAsync();

            // 3. Connect to the isolated scenario database
            await using var connection = new NpgsqlConnection(scenarioConnectionString);
            await connection.OpenAsync();

            // 4. Execute schema.sql
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

            // 5. Execute seed.sql
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
                ErrorMessage: $"Cannot connect to PostgreSQL at '{_baseConnectionString}': {ex.Message}",
                ErrorDetail: "Ensure Docker is running and PostgreSQL port is accessible."
            );
        }
        catch (Exception ex)
        {
            return new DbValidationResult(
                Success: false,
                FailedScriptName: "provisioning",
                ErrorMessage: $"Environment provisioning failed: {ex.Message}",
                ErrorDetail: ex.StackTrace
            );
        }
    }

    private async Task EnsureDockerContainerRunningAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(_baseConnectionString);
        var host = string.IsNullOrWhiteSpace(builder.Host) ? "localhost" : builder.Host;
        var port = builder.Port > 0 ? builder.Port : 5432;

        if (IsPortOpen(host, port))
            return;

        // Try starting an existing stopped container first
        var startProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"start {DefaultContainerName}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (startProcess is not null)
        {
            await startProcess.WaitForExitAsync();
        }

        // If starting failed (container does not exist), create and run a fresh container
        if (!IsPortOpen(host, port))
        {
            var runProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments =
                    $"run --name {DefaultContainerName} -e POSTGRES_PASSWORD=postgres -p {port}:5432 -d postgres:16-alpine",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (runProcess is not null)
            {
                await runProcess.WaitForExitAsync();
            }
        }

        // Poll until PostgreSQL accepts TCP connections
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < ContainerStartupTimeoutMs)
        {
            if (IsPortOpen(host, port))
            {
                await Task.Delay(1000); // Allow internal daemon to initialize
                return;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException(
            $"PostgreSQL container failed to bind port {port} within {ContainerStartupTimeoutMs / 1000} seconds.");
    }

    private async Task<string> EnsureScenarioDatabaseExistsAsync()
    {
        var cleanScenario = Regex.Replace(_scenarioName.Trim().ToLowerInvariant(), @"[^a-z0-9_]", "_");
        var targetDbName = $"dba_agent_{cleanScenario}";

        // Connect to the default maintenance 'postgres' database
        var adminBuilder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            Database = "postgres"
        };

        await using (var adminConnection = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await adminConnection.OpenAsync();

            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @dbname;",
                adminConnection);

            checkCmd.Parameters.AddWithValue("dbname", targetDbName);

            var dbExists = await checkCmd.ExecuteScalarAsync() is not null;

            if (!dbExists)
            {
                // CREATE DATABASE cannot execute inside a transaction block
                await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{targetDbName}\";", adminConnection);
                await createCmd.ExecuteNonQueryAsync();
            }
        }

        // Return connection string reconfigured for the isolated database
        var targetBuilder = new NpgsqlConnectionStringBuilder(_baseConnectionString)
        {
            Database = targetDbName
        };

        return targetBuilder.ConnectionString;
    }

    private static bool IsPortOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));

            if (!success)
            {
                return false;
            }

            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
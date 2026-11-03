using System.Diagnostics;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Npgsql;

namespace DbaAgent.Core.Validation;

public static class DatabaseProvisioner
{
    /// <summary>
    /// Ensures the Docker container is up and running on the target port.
    /// </summary>
    public async static Task EnsureDockerContainerRunningAsync(int port = 5433, string containerName = "pg-test")
    {
        if (IsPortOpen("localhost", port))
            return;

        // Try starting an existing stopped container first
        var startProcess = Process.Start(new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"start {containerName}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });

        await startProcess!.WaitForExitAsync();

        // If starting failed (container does not exist), create and run it
        if (!IsPortOpen("localhost", port))
        {
            var runProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments =
                    $"run --name {containerName} -e POSTGRES_PASSWORD=postgres -p {port}:5432 -d postgres:16-alpine",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            await runProcess!.WaitForExitAsync();
        }

        // Poll until PostgreSQL is accepting TCP connections (up to 15 seconds)
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < 15000)
        {
            if (IsPortOpen("localhost", port))
            {
                await Task.Delay(1000); // Allow internal Postgres daemon initialization
                return;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException(
            $"PostgreSQL Docker container failed to bind port {port} within 15 seconds.");
    }

    /// <summary>
    /// Connects to the admin 'postgres' database, creates the scenario database if missing, 
    /// and returns the connection string configured for that scenario.
    /// </summary>
    public static async Task<string> PrepareScenarioDatabaseAsync(string baseConnectionString, string scenarioName)
    {
        var cleanScenario = Regex.Replace(scenarioName.Trim().ToLowerInvariant(), @"[^a-z0-9_]", "_");
        var targetDbName = $"dba_agent_{cleanScenario}";

        // 1. Build an admin connection string pointing to the maintenance 'postgres' database
        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres"
        };

        // 2. Connect to admin DB and ensure target database exists
        await using (var conn = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await conn.OpenAsync();

            await using var checkCmd = new NpgsqlCommand(
                "SELECT 1 FROM pg_database WHERE datname = @dbname;",
                conn);

            checkCmd.Parameters.AddWithValue("dbname", targetDbName);

            var exists = await checkCmd.ExecuteScalarAsync() is not null;

            if (!exists)
            {
                // CREATE DATABASE cannot run within an ambient transaction block
                await using var createCmd = new NpgsqlCommand($"CREATE DATABASE \"{targetDbName}\";", conn);
                await createCmd.ExecuteNonQueryAsync();
            }
        }

        // 3. Return the specific scenario connection string (in-memory only, no file edits)
        var scenarioBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = targetDbName
        };

        return scenarioBuilder.ConnectionString;
    }

    private static bool IsPortOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
            if (!success) return false;

            client.EndConnect(result);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
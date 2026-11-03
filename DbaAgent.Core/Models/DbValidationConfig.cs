namespace DbaAgent.Core.Models;

public sealed record DbValidationConfig
{
    public string PostgresConnectionString { get; init; } =
        "Host=localhost;Port=5432;Database=dba_agent_test;Username=postgres;Password=postgres;";

    public int MaxRepairAttempts { get; init; } = 3;
}
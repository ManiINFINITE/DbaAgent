namespace DbaAgent.Core.Services;

public interface IDbValidator
{
    string DbmsName { get; }

    Task<DbValidationResult> ValidateAsync(string schemaSql, string seedSql);
}

public sealed record DbValidationResult(
    bool Success,
    string? FailedScriptName,
    string? ErrorMessage,
    string? ErrorDetail
);
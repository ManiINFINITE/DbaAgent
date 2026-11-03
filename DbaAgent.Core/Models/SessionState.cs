namespace DbaAgent.Core.Models;

public sealed class SessionState
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    public WorkflowStage CurrentStage { get; set; } = WorkflowStage.Intake;

    // Stage 1
    public string RawDescription { get; set; } = string.Empty;
    public string IntakeSummary { get; set; } = string.Empty;

    // Stage 2
    public List<InterviewExchange> InterviewHistory { get; set; } = [];
    public string RequirementsDoc { get; set; } = string.Empty;

    // Stage 3
    public string SelectedDbms { get; set; } = string.Empty;
    public string DbmsChoiceDoc { get; set; } = string.Empty;

    // Stage 4
    public string ConceptualModelDoc { get; set; } = string.Empty;
    public string LogicalSchemaDoc { get; set; } = string.Empty;
    public string NormalizationDoc { get; set; } = string.Empty;
    public string AssumptionsDoc { get; set; } = string.Empty;

    // Stage 5
    public string ChangelogDoc { get; set; } = string.Empty;
    public bool IsApproved { get; set; }

    // Stage 6
    public string SchemaSql { get; set; } = string.Empty;
    public string SeedSql { get; set; } = string.Empty;
}

public sealed record InterviewExchange(string Question, string Answer);
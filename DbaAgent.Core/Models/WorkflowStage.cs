namespace DbaAgent.Core.Models;

public enum WorkflowStage
{
    Intake = 1,
    Interview = 2,
    DbmsSelection = 3,
    DesignDocs = 4,
    RevisionLoop = 5,
    SqlGeneration = 6,
    Completed = 7
}
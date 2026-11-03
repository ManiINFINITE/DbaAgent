namespace DbaAgent.Core.Services;

public interface IPromptService
{
    Task<string> RenderAsync(string templateName, object model);
}
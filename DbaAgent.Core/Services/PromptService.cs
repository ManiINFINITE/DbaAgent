using Scriban;

namespace DbaAgent.Core.Services;

public sealed class PromptService : IPromptService
{
    readonly private string _promptsDirectoryName;

    public PromptService(string promptsDirectoryName = "prompts") =>
        _promptsDirectoryName = promptsDirectoryName;

    public async Task<string> RenderAsync(string templateName, object model)
    {
        var templatePath = ResolveTemplatePath(templateName);
        var templateText = await File.ReadAllTextAsync(templatePath);
        var template = Template.Parse(templateText);

        if (template.HasErrors)
        {
            var errors = string.Join("; ", template.Messages.Select(m => m.Message));
            throw new InvalidOperationException($"Template parsing error in '{templateName}': {errors}");
        }

        return await template.RenderAsync(model, member => member.Name);
    }

    private string ResolveTemplatePath(string templateName)
    {
        // 1. Check relative to current working directory
        var candidate = Path.Combine(Directory.GetCurrentDirectory(), _promptsDirectoryName, templateName);

        if (File.Exists(candidate))
        {
            return candidate;
        }

        // 2. Check relative to application binary directory (bin/Debug/net8.0/...)
        candidate = Path.Combine(AppContext.BaseDirectory, _promptsDirectoryName, templateName);

        if (File.Exists(candidate))
        {
            return candidate;
        }

        // 3. Search upward through parent directories (locates project root or solution root)
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            candidate = Path.Combine(directory.FullName, _promptsDirectoryName, templateName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            candidate = Path.Combine(directory.FullName, "src", "DbaAgent.Cli", _promptsDirectoryName, templateName);

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Prompt template '{templateName}' was not found. Searched in '{AppContext.BaseDirectory}', current directory, and all parent folders under '{_promptsDirectoryName}'.");
    }
}
using System.Collections.Generic;

namespace DbaAgent.Core.Validation
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public static class DocumentValidator
    {
        public static ValidationResult ValidateMermaidDiagram(string content)
        {
            var errors = new List<string>();

            // 1. Check opening fence
            if (!content.Contains("```mermaid"))
            {
                errors.Add("Missing opening '```mermaid' fence.");
            }

            // 2. Check closing fence
            int openingIndex = content.IndexOf("```mermaid", StringComparison.Ordinal);
            if (openingIndex != -1)
            {
                int closingIndex = content.IndexOf("```", openingIndex + 10, StringComparison.Ordinal);
                if (closingIndex == -1)
                {
                    errors.Add("Missing closing '```' fence.");
                }
            }

            // 3. Check for specific Mermaid ER diagram syntax
            if (!content.Contains("erDiagram"))
            {
                errors.Add("The diagram must start with the keyword 'erDiagram'.");
            }

            return new ValidationResult
            {
                IsValid = errors.Count == 0,
                ErrorMessage = errors.Count > 0 
                    ? "Your output failed structural validation. Fix the following errors:\n- " + string.Join("\n- ", errors) 
                    : null
            };
        }
    }
}
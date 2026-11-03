using System.Text.RegularExpressions;

namespace DbaAgent.Core.Services;

public static partial class TextSanitizer
{
    [GeneratedRegex(@"^```(?:markdown|md|sql)?\s*[\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFenceRegex();

    [GeneratedRegex(@"[\r\n]+```\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingFenceRegex();

    /// <summary>
    /// Strips outermost markdown/sql code fence wrappers while leaving inner code fences (e.g., ```mermaid) untouched.
    /// </summary>
    public static string StripOuterCodeFence(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        var trimmed = rawText.Trim();

        // Check if the response is wrapped by an outermost fence
        var leadingMatch = LeadingFenceRegex().Match(trimmed);
        var trailingMatch = TrailingFenceRegex().Match(trimmed);

        if (leadingMatch.Success && trailingMatch.Success)
        {
            // Remove the opening ```markdown/```md/```sql line
            var withoutLeading = trimmed[leadingMatch.Length..];

            // Remove the closing ``` line
            var lastFenceIndex = withoutLeading.LastIndexOf("```", StringComparison.Ordinal);

            if (lastFenceIndex != -1)
            {
                return withoutLeading[..lastFenceIndex].Trim();
            }
        }

        return trimmed;
    }
    
    /// <summary>
    /// Automatically cleans known LLM Mermaid syntax hallucinations before saving to disk.
    /// </summary>
    public static string SanitizeMermaid(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("```mermaid"))
            return text;

        // 1. Replace SQL "DEFAULT <val>" on entity attribute lines
        text = Regex.Replace(text, @"(\b(?:BOOLEAN|INT|VARCHAR|TEXT|DATE|TIME)\s+\w+)\s+DEFAULT\s+[^\r\n]+", "$1", RegexOptions.IgnoreCase);

        // 2. Replace SQL "UNIQUE" constraint with Mermaid "UK"
        text = Regex.Replace(text, @"(\b(?:BOOLEAN|INT|BIGINT|VARCHAR|TEXT|DATE|TIME)\s+\w+)\s+UNIQUE\b", "$1 UK", RegexOptions.IgnoreCase);

        return text;
    }
}
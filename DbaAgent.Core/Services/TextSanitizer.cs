using System.Text;
using System.Text.RegularExpressions;

namespace DbaAgent.Core.Services;

public static partial class TextSanitizer
{
    [GeneratedRegex(@"^```(?:markdown|md|sql)?\s*[\r\n]+", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFenceRegex();

    [GeneratedRegex(@"[\r\n]+```\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingFenceRegex();

    [GeneratedRegex(@"```mermaid[ \t]*\r?\n(.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex MermaidBlockRegex();

    [GeneratedRegex(@"^[A-Za-z][\w-]*\s*\{$")]
    private static partial Regex EntityOpenRegex();

    [GeneratedRegex(@"^([A-Za-z][\w-]*)\s+([|}][|o]\s*(?:--|\.\.)\s*[|o][|{])\s+([A-Za-z][\w-]*)\s*(?::\s*(.*))?$")]
    private static partial Regex RelationshipRegex();

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

    public static string SanitizeMermaid(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("```mermaid", StringComparison.OrdinalIgnoreCase))
            return text;

        return MermaidBlockRegex().Replace(text,
            m =>
            {
                var body = m.Groups[1].Value;

                if (!body.TrimStart().StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase))
                    return m.Value; // not an ER diagram, leave untouched

                return "```mermaid\n" + SanitizeErDiagram(body).TrimEnd() + "\n```";
            });
    }

    private static string SanitizeErDiagram(string body)
    {
        var sb = new StringBuilder();
        var inEntity = false;

        foreach (var rawLine in body.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = rawLine.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith("%%") ||
                trimmed.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(rawLine.TrimEnd()).Append('\n');
                continue;
            }

            var rel = RelationshipRegex().Match(trimmed);

            // Missing closing brace: a relationship line implicitly ends the entity block
            if (inEntity && rel.Success)
            {
                sb.Append("    }\n");
                inEntity = false;
            }

            if (inEntity)
            {
                if (trimmed == "}")
                {
                    inEntity = false;
                    sb.Append("    }\n");
                    continue;
                }

                var attr = NormalizeAttribute(trimmed);

                if (attr is not null)
                    sb.Append("        ").Append(attr).Append('\n');

                continue;
            }

            if (rel.Success)
            {
                var left = rel.Groups[1].Value;
                var card = Regex.Replace(rel.Groups[2].Value, @"\s+", "");
                var right = rel.Groups[3].Value;
                var label = rel.Groups[4].Value.Trim().Trim('"').Replace("\"", "'").Trim();
                if (label.Length == 0) label = "has";

                sb.Append($"    {left} {card} {right} : \"{label}\"\n");
                continue;
            }

            if (EntityOpenRegex().IsMatch(trimmed))
            {
                inEntity = true;
                sb.Append("    ").Append(trimmed).Append('\n');
                continue;
            }

            sb.Append(rawLine.TrimEnd()).Append('\n');
        }

        if (inEntity) sb.Append("    }\n");
        return sb.ToString();
    }

    private static string? NormalizeAttribute(string line)
    {
        // drop trailing "comment" strings
        line = Regex.Replace(line, "\"[^\"]*\"\\s*$", "");

        // multi-word types -> single token
        line = Regex.Replace(line,
            @"\bTIMESTAMP\s+WITH(?:OUT)?\s+TIME\s+ZONE\b",
            "TIMESTAMPTZ",
            RegexOptions.IgnoreCase);

        line = Regex.Replace(line, @"\bDOUBLE\s+PRECISION\b", "DOUBLE", RegexOptions.IgnoreCase);
        line = Regex.Replace(line, @"\bCHARACTER\s+VARYING\b", "VARCHAR", RegexOptions.IgnoreCase);

        // remove SQL noise
        line = Regex.Replace(line, @"\([^)]*\)", ""); // (255) (10,2)

        line = Regex.Replace(line,
            @"\bGENERATED\s+(?:ALWAYS|BY\s+DEFAULT)\s+AS\s+IDENTITY\b",
            "",
            RegexOptions.IgnoreCase);

        line = Regex.Replace(line, @"\b(?:DEFAULT|CHECK|REFERENCES)\b.*$", "", RegexOptions.IgnoreCase);

        line = Regex.Replace(line,
            @"\bNOT\s+NULL\b|\bNULL\b|\bAUTO_INCREMENT\b|\bSERIAL\b",
            "",
            RegexOptions.IgnoreCase);

        var tokens = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2) return null;

        var type = Regex.Replace(tokens[0], @"[^A-Za-z0-9_]", "");
        var name = Regex.Replace(tokens[1], @"[^A-Za-z0-9_]", "");
        if (type.Length == 0 || name.Length == 0) return null;

        var keys = new List<string>();

        foreach (var t in tokens.Skip(2))
        {
            var k = t.ToUpperInvariant() switch
            {
                "PK" or "PRIMARY" => "PK",
                "FK" or "FOREIGN" => "FK",
                "UK" or "UNIQUE" => "UK",
                _ => null
            };

            if (k is not null && !keys.Contains(k)) keys.Add(k);
        }

        return keys.Count > 0 ? $"{type} {name} {string.Join(", ", keys)}" : $"{type} {name}";
    }
}
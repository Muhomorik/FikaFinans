namespace FikaFinans.Application.Agents;

/// <summary>
/// Parses a prompt resource file consisting of an optional YAML-style frontmatter block
/// (<c>---\nName: ...\n---</c>) followed by the system-prompt body.
/// </summary>
/// <remarks>Copied verbatim from <c>KanelBrief.Core.Agents.PromptFileParser</c>.</remarks>
public static class PromptFileParser
{
    private const string Delimiter = "---";

    public static (string Name, string SystemPrompt) Parse(string content, string fallbackName)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackName);

        var lines = content.Replace("\r\n", "\n").Split('\n');

        if (lines.Length < 3 || lines[0].Trim() != Delimiter)
            return (fallbackName, content.Trim());

        var closingIndex = -1;
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim() == Delimiter)
            {
                closingIndex = i;
                break;
            }
        }

        if (closingIndex < 0)
            return (fallbackName, content.Trim());

        var name = fallbackName;
        for (var i = 1; i < closingIndex; i++)
        {
            var line = lines[i].Trim();
            if (line.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
            {
                name = line["Name:".Length..].Trim();
                break;
            }
        }

        var body = string.Join('\n', lines.Skip(closingIndex + 1)).Trim();
        return (name, body);
    }
}

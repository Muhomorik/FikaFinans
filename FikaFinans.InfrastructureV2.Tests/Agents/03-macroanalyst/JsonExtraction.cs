namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;

internal static class JsonExtraction
{
    // Returns the longest balanced { ... } block in raw, stripping any surrounding
    // prose or ```json fences. Models occasionally wrap their output in markdown
    // despite "JSON only" instructions.
    public static string ExtractFirstJsonObject(string raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(raw);

        var start = raw.IndexOf('{');
        if (start < 0)
            throw new InvalidOperationException(
                $"response contains no '{{' — cannot extract JSON object. Raw: {Truncate(raw, 500)}");

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < raw.Length; i++)
        {
            var c = raw[i];
            if (escaped) { escaped = false; continue; }
            if (c == '\\' && inString) { escaped = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return raw.Substring(start, i - start + 1);
            }
        }

        throw new InvalidOperationException(
            $"response has unbalanced braces — cannot extract JSON object. Raw: {Truncate(raw, 500)}");
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"... [+{s.Length - max} chars]";
}

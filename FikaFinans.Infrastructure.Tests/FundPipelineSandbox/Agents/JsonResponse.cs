using System.Text.Json;

namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents;

/// <summary>
/// JSON helpers for sandbox agent responses. Models occasionally wrap their
/// output in markdown code fences despite "JSON only" instructions — we strip
/// those defensively before deserializing.
/// </summary>
internal static class JsonResponse
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Returns the longest balanced <c>{ ... }</c> block in <paramref name="raw"/>,
    /// stripping any surrounding prose or <c>```json</c> fences. Throws if no
    /// balanced object is found — better to fail loudly with the raw text in
    /// the exception than silently swallow a bad response.
    /// </summary>
    public static string ExtractFirstJsonObject(string raw)
    {
        ArgumentException.ThrowIfNullOrEmpty(raw);

        var start = raw.IndexOf('{');
        if (start < 0)
            throw new InvalidOperationException(
                $"agent response contains no '{{' — cannot extract JSON object. Raw: {Truncate(raw, 500)}");

        // Walk forward tracking quote state + brace depth. Skips braces inside strings.
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < raw.Length; i++)
        {
            var c = raw[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }
            if (c == '"')
            {
                inString = !inString;
                continue;
            }
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
            $"agent response has unbalanced braces — cannot extract JSON object. Raw: {Truncate(raw, 500)}");
    }

    /// <summary>
    /// Re-emits <paramref name="rawJson"/> with two-space indentation. Goes through
    /// <see cref="JsonDocument"/> so field order from the model is preserved and we
    /// don't need a strongly-typed schema for every payload variant.
    /// </summary>
    public static string PrettyPrint(string rawJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(rawJson);
        using var doc = JsonDocument.Parse(rawJson);
        return JsonSerializer.Serialize(doc.RootElement, Options);
    }

    public static T DeserializeOrThrow<T>(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<T>(json, Options);
            return parsed
                ?? throw new InvalidOperationException(
                    $"deserialization returned null for {typeof(T).Name}. JSON: {Truncate(json, 500)}");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"failed to deserialize {typeof(T).Name}: {ex.Message}. JSON: {Truncate(json, 500)}", ex);
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"... [+{s.Length - max} chars]";
}

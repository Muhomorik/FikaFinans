namespace FikaFinans.Infrastructure.Tests.FundPipelineSandbox.Agents;

/// <summary>
/// Thrown when a sandbox agent's response can't be extracted to a balanced JSON
/// object or deserialized into the expected POCO. Carries the raw model output
/// (and the partially-extracted JSON, if extraction succeeded but deserialization
/// failed) so the caller can dump it to disk before re-throwing — otherwise the
/// 5-minute Code Interpreter run is gone with only a 500-char preview in the
/// inner exception's message.
/// </summary>
public sealed class SandboxResponseParseException : InvalidOperationException
{
    public SandboxResponseParseException(
        string message,
        string rawResponseText,
        string? extractedJson,
        Exception innerException)
        : base(message, innerException)
    {
        RawResponseText = rawResponseText;
        ExtractedJson = extractedJson;
    }

    /// <summary>Full text returned by the model — always populated.</summary>
    public string RawResponseText { get; }

    /// <summary>
    /// The balanced JSON object pulled from the raw text, if extraction
    /// succeeded. Null when the failure happened during extraction itself.
    /// </summary>
    public string? ExtractedJson { get; }
}

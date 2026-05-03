namespace FikaFinans.InfrastructureV2.Tests.Agents.MacroAnalyst;

public sealed class MacroAnalystValidationException : Exception
{
    public MacroAnalystValidationException(string message) : base(message) { }
}

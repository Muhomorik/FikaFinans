namespace FikaFinans.Infrastructure.Pipeline.Agents;

public sealed class MacroAnalystValidationException : Exception
{
    public MacroAnalystValidationException(string message) : base(message) { }
}

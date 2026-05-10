namespace FikaFinans.Infrastructure.Pipeline.Agents;

public sealed class DataLoaderHaltException : Exception
{
    public string Trigger { get; }

    public DataLoaderHaltException(string trigger, string message) : base(message)
    {
        Trigger = trigger;
    }
}

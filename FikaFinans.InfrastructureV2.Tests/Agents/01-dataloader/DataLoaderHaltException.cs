namespace FikaFinans.InfrastructureV2.Tests.Agents.DataLoader;

public sealed class DataLoaderHaltException : Exception
{
    public string Trigger { get; }

    public DataLoaderHaltException(string trigger, string message) : base(message)
    {
        Trigger = trigger;
    }
}

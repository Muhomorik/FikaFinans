namespace FikaFinans.Application.Pipeline;

public sealed record StepEvent(
    int StepNumber,
    string AgentName,
    StepEventKind Kind,
    string? Message = null,
    TimeSpan? Duration = null);

public enum StepEventKind
{
    Started,
    Succeeded,
    Failed,
}

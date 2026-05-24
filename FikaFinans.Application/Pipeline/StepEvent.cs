namespace FikaFinans.Application.Pipeline;

public sealed record StepEvent(
    StepId Step,
    StepEventKind Kind,
    string? Message = null,
    TimeSpan? Duration = null);

public enum StepEventKind
{
    Started,
    Succeeded,
    Failed,
}

namespace FikaFinans.Application.Pipeline;

/// <summary>
/// Where a step reports that it started, succeeded or failed, so whatever renders that
/// progress does not have to know which code ran the step.
/// </summary>
/// <seealso cref="StepEvent"/>
public interface IStepEventPublisher
{
    void Publish(StepEvent stepEvent);
}

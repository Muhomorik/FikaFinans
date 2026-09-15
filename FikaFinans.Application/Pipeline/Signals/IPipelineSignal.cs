namespace FikaFinans.Application.Pipeline.Signals;

/// <summary>
/// Implemented by every signal the pipeline sends — inbound and step-to-step — so all of
/// them are findable from one type.
/// </summary>
/// <seealso cref="NavChangeSignal"/>
public interface IPipelineSignal;

namespace FikaFinans.Application.Pipeline.Configs;

/// <summary>
/// The three prompt templates step 3 (MacroAnalyst) sends to the LLM, as one set. Step 3 has
/// no config file; these play the role <see cref="MetricsCalculatorConfig"/> plays for step 2.
/// </summary>
/// <param name="SystemPrompt">Sent as-is on both the first call and the retry.</param>
/// <param name="UserPromptTemplate">
/// Filled with the week, the three reports' run ids and JSON, and the category list.
/// </param>
/// <param name="RetryPromptTemplate">
/// Appended to the user prompt after an invalid response; <c>{error_message}</c> is replaced
/// with what was wrong.
/// </param>
public sealed record Step03MacroAnalystPrompts(
    string SystemPrompt,
    string UserPromptTemplate,
    string RetryPromptTemplate);

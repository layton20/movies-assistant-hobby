namespace MovieAssistant.Api.Exceptions;

/// <summary>
/// A tool threw while the model was using it. Wrapping it lets tracing tell a tool failure
/// apart from a failure of the model call itself.
/// </summary>
public class ToolFailureException(string toolName, Exception inner)
    : Exception($"Tool '{toolName}' failed: {inner.Message}", inner)
{
    public string ToolName { get; } = toolName;
}

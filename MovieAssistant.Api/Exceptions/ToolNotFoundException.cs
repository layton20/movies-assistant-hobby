namespace MovieAssistant.Api.Exceptions;

public class ToolNotFoundException(string toolName)
    : Exception($"No tool registered with name '{toolName}'");

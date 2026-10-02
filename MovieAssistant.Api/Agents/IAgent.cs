namespace MovieAssistant.Api.Agents;

public interface IAgent
{
    string Id { get; }
    string SystemPrompt { get; }
    string PromptVersion { get; }
    IReadOnlyList<string> EnabledToolNames { get; }
    float Temperature { get; }
    string FormatUserMessage(string content);
    int MaxOutputTokens { get; }
}

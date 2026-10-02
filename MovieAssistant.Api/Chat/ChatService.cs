using System;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using MovieAssistant.Api.Agents;
using Microsoft.Extensions.Options;
using MovieAssistant.Api.Safety;
using MovieAssistant.Api.Tools;
using MovieAssistant.Api.Tracing;
using static MovieAssistant.Api.Agents.AgentRegistry;

namespace MovieAssistant.Api.Chat;

public class ChatService(
    IChatClient chatClient,
    AgentRegistry agentRegistry,
    ToolRegistry toolRegistry,
    IRiskChecker riskChecker,
    IOptions<JevOptions> jevOptions,
    LangfuseClient langfuse) : IChatService
{
    public const string BlockedReply =
        "Sorry, I can't help with that. I can help you find movies by genre, year, rating, director or cast.";

    public async IAsyncEnumerable<SseEvent> StreamAsync(
    string agentId,
    IList<ChatRequestMessage> history,
    bool isEval = false,
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        IAgent? agent = null;
        string? resolveError = null;

        try
        {
            agent = agentRegistry.Resolve(agentId);
        }
        catch (AgentNotFoundException ex)
        {
            resolveError = ex.Message;
        }

        if (agent is null)
        {
            yield return new SseEvent(SseEventTypes.Error, Error: resolveError);
            yield break;
        }

        // One id for the whole request, so evals can attach scores to the trace it produced
        var traceId = LangfuseClient.NewTraceId();
        yield return new SseEvent(SseEventTypes.Trace, TraceId: traceId);

        RiskResult? risk = null;

        // Only the latest (untrusted) user turn goes to Jev, never history or tool output
        if (jevOptions.Value.Enabled)
        {
            risk = await riskChecker.CheckAsync(
                UserInputSanitiser.Clean(history[^1].Content), cancellationToken);

            if (risk.Verdict == RiskVerdict.Block
                || (risk.Verdict == RiskVerdict.Unavailable && !jevOptions.Value.FailOpen))
            {
                langfuse.RecordError(TraceError.From(
                    TraceError.RiskBlocked,
                    $"Blocked by Jev (probability {risk.Probability?.ToString("0.00") ?? "n/a"}).",
                    countsAsError: false), traceId, isEval);

                yield return new SseEvent(SseEventTypes.Chunk, Delta: BlockedReply);
                yield return new SseEvent(SseEventTypes.Done);
                yield break;
            }

            if (risk.Verdict == RiskVerdict.Unavailable)
                langfuse.RecordError(TraceError.From(
                    TraceError.RiskCheckUnavailable, "Jev check unavailable; failing open.", countsAsError: false));
        }

        var agentTools = toolRegistry.GetToolsForAgent(agent);

        var messages = BuildMessages(agent, history);

        var options = new ChatOptions
        {
            Temperature = agent.Temperature,
            MaxOutputTokens = agent.MaxOutputTokens,
            Tools = [.. agentTools.Select(t => t.AsAIFunction())],
            AdditionalProperties = new()
            {
                ["promptVersion"] = agent.PromptVersion,
                ["traceId"] = traceId,
                ["isEval"] = isEval
            }
        };

        // Read by LangfuseTracingChatClient so the verdict lands on the same trace as the generation
        if (risk is not null)
        {
            options.AdditionalProperties["riskVerdict"] = risk.Verdict.ToString();
            options.AdditionalProperties["riskProbability"] = risk.Probability;
        }

        // IChatClient is configured with UseFunctionInvocation() middleware in Program.cs.
        // That middleware intercepts tool call responses, executes the tools, appends the
        // results to the conversation, and re-submits - all transparently. By the time
        // tokens reach this foreach, tool calls are already resolved and we are streaming
        // the final user-facing response.
        await foreach (var update in chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
                yield return new SseEvent(SseEventTypes.Chunk, Delta: update.Text);
        }

        yield return new SseEvent(SseEventTypes.Done);
    }

    private static List<ChatMessage> BuildMessages(
        IAgent agent,
        IList<ChatRequestMessage> history)
    {
        var messages = new List<ChatMessage>(history.Count + 1)
        {
            new(ChatRole.System, agent.SystemPrompt)
        };

        messages.AddRange(history.Select(m => new ChatMessage(
            m.Role == "user" ? ChatRole.User : ChatRole.Assistant,
            m.Role == "user"
                ? agent.FormatUserMessage(m.Content)
                : m.Content)));

        return messages;
    }

}

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace MovieAssistant.Api.Tracing;

public class LangfuseTracingChatClient(
    IChatClient innerClient,
    LangfuseClient langfuse,
    ILogger<LangfuseTracingChatClient>? logger = null)
    : DelegatingChatClient(innerClient)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var traceId = options?.AdditionalProperties?.TryGetValue("traceId", out var id) == true && id is string given
            ? given
            : LangfuseClient.NewTraceId();
        var isEval = options?.AdditionalProperties?.TryGetValue("isEval", out var eval) == true && eval is true;
        var startTime = DateTimeOffset.UtcNow;
        var collectedOutput = new StringBuilder();
        var responseModel = (string?)null;
        long inputTokens = 0, outputTokens = 0, totalTokens = 0;
        DateTimeOffset? firstTokenTime = null;
        var promptVersion = options?.AdditionalProperties?.TryGetValue("promptVersion", out var version) == true
            ? version?.ToString()
            : null;
        var riskVerdict = options?.AdditionalProperties?.TryGetValue("riskVerdict", out var verdict) == true
            ? verdict?.ToString()
            : null;
        var riskProbability = options?.AdditionalProperties?.TryGetValue("riskProbability", out var probability) == true
            ? probability as double?
            : null;

        Exception? failure = null;
        var completed = false;

        await using var updates = base.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);

        try
        {
            while (true)
            {
                // yield can't sit inside a try/catch, so the failure is captured here and rethrown below
                try
                {
                    if (!await updates.MoveNextAsync())
                        break;
                }
                catch (Exception ex)
                {
                    failure = ex;
                    break;
                }

                var update = updates.Current;

                if (!string.IsNullOrEmpty(update.Text))
                {
                    // First user-visible text. With tool calls this is after the tool loop, not the first model call
                    firstTokenTime ??= DateTimeOffset.UtcNow;
                    collectedOutput.Append(update.Text);
                }

                responseModel ??= update.ModelId;

                // Token counts arrive as UsageContent, once per model call. The tool loop can make
                // several calls per chat, so sum them
                foreach (var usage in update.Contents.OfType<UsageContent>())
                {
                    inputTokens += usage.Details.InputTokenCount ?? 0;
                    outputTokens += usage.Details.OutputTokenCount ?? 0;
                    totalTokens += usage.Details.TotalTokenCount
                        ?? (usage.Details.InputTokenCount ?? 0) + (usage.Details.OutputTokenCount ?? 0);
                }

                yield return update;
            }

            completed = failure is null;

            if (failure is not null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }
        finally
        {
            // Runs on success, on failure, and when the caller stops reading early (client disconnect).
            var error = failure is not null
                ? TraceError.Classify(failure, cancellationToken)
                : completed
                    ? null
                    : TraceError.Classify(new OperationCanceledException(), new CancellationToken(canceled: true));

            var endTime = DateTimeOffset.UtcNow;
            // Langfuse derives cost from the model name, so prefer what the provider reported
            var model = responseModel ?? options?.ModelId ?? "unknown";
            var output = collectedOutput.ToString();
            var messages = chatMessages.ToList();
            var (input, outputCount, total) = (inputTokens, outputTokens, totalTokens);

            // With jev-router this is the model it actually picked, as reported by the provider
            logger?.LogInformation(
                "Model responded: {Model} ({InputTokens} in / {OutputTokens} out tokens, {ElapsedMs:0} ms, {Outcome})",
                model, input, outputCount, (endTime - startTime).TotalMilliseconds, error?.Category ?? "ok");

            // Builds the spans in memory; the OTLP exporter sends them in the background
            langfuse.RecordGeneration(
                traceId, model, messages,
                output, startTime, endTime, firstTokenTime, input, outputCount, total, promptVersion, error,
                riskVerdict, riskProbability, isEval);
        }
    }
}

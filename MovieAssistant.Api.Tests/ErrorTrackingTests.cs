using System.ClientModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using MovieAssistant.Api.Exceptions;
using MovieAssistant.Api.Tools;
using MovieAssistant.Api.Tracing;

namespace MovieAssistant.Api.Tests;

public class TraceErrorClassifyTests
{
    private static readonly CancellationToken Live = CancellationToken.None;
    private static readonly CancellationToken Cancelled = new(canceled: true);

    [Fact]
    public void ToolFailure() =>
        Assert.Equal(TraceError.ToolFailure,
            TraceError.Classify(new ToolFailureException("search_movies", new InvalidOperationException("boom")), Live).Category);

    [Fact]
    public void ToolFailure_WinsOverInnerTimeout() =>
        Assert.Equal(TraceError.ToolFailure,
            TraceError.Classify(new ToolFailureException("t", new TimeoutException()), Live).Category);

    [Fact]
    public void TimeoutException_IsTimeout() =>
        Assert.Equal(TraceError.Timeout, TraceError.Classify(new TimeoutException(), Live).Category);

    [Fact]
    public void UnrequestedCancellation_IsTimeout() =>
        Assert.Equal(TraceError.Timeout, TraceError.Classify(new TaskCanceledException(), Live).Category);

    [Fact]
    public void RequestedCancellation_IsClientDisconnect_AndNotAnError()
    {
        var error = TraceError.Classify(new OperationCanceledException(), Cancelled);

        Assert.Equal(TraceError.ClientDisconnected, error.Category);
        Assert.False(error.CountsAsError);
    }

    [Fact]
    public void WrappedTimeout_IsFoundThroughInnerExceptions() =>
        Assert.Equal(TraceError.Timeout,
            TraceError.Classify(new HttpRequestException("x", new TimeoutException()), Live).Category);

    [Theory]
    [InlineData(429, TraceError.ProviderRateLimit)]
    [InlineData(408, TraceError.Timeout)]
    [InlineData(504, TraceError.Timeout)]
    [InlineData(500, TraceError.ModelError)]
    public void ProviderStatusCodes(int status, string expected) =>
        Assert.Equal(expected, TraceError.Classify(new ClientResultException(new StubResponse(status)), Live).Category);

    [Fact]
    public void Unknown_IsModelError_AndCountsAsError()
    {
        var error = TraceError.Classify(new InvalidOperationException("nope"), Live);

        Assert.Equal(TraceError.ModelError, error.Category);
        Assert.True(error.CountsAsError);
    }

    [Fact]
    public void LongMessages_AreTruncated() =>
        Assert.Equal(500, TraceError.From(TraceError.ModelError, new string('x', 5000)).Message.Length);

    private sealed class StubResponse(int status) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "stub";
        public override Stream? ContentStream { get => null; set { } }
        public override BinaryData Content => BinaryData.Empty;
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(BinaryData.Empty);
        public override void Dispose() { }
    }
}

public class ToolFailureHandlingTests
{
    [Fact]
    public async Task ThrowingTool_SurfacesAsToolFailureException()
    {
        var tool = AIFunctionFactory.Create(string () => throw new InvalidOperationException("db down"), "search_movies");
        var client = new FunctionInvokingChatClient(new ToolCallingClient())
        {
            FunctionInvoker = ToolFailureHandling.InvokeAsync
        };

        var ex = await Assert.ThrowsAsync<ToolFailureException>(() =>
            client.GetResponseAsync("hi", new ChatOptions { Tools = [tool] }));

        Assert.Equal("search_movies", ex.ToolName);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    private sealed class ToolCallingClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("1", "search_movies")])));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

public class TracingErrorTests
{
    private static async Task Drain(IChatClient client, string traceId, Dictionary<string, object?>? extra = null)
    {
        var tracing = new LangfuseTracingChatClient(client, new LangfuseClient());
        var props = new AdditionalPropertiesDictionary { ["traceId"] = traceId };
        foreach (var (k, v) in extra ?? []) props[k] = v;

        await foreach (var _ in tracing.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "hi")], new ChatOptions { AdditionalProperties = props })) { }
    }

    [Fact]
    public async Task ModelTimeout_IsTaggedAsErrorAndRethrown()
    {
        using var capture = new SpanCapture();
        var id = LangfuseClient.NewTraceId();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            Drain(new ScriptedClient(throwAfterFirstChunk: new TimeoutException("slow")), id));

        var (root, generation) = capture.Trace(id);
        Assert.Contains("error:timeout", Tags(root));
        Assert.Equal("ERROR", generation.GetTagItem("langfuse.observation.level"));
        Assert.Equal(ActivityStatusCode.Error, generation.Status);
        Assert.Equal("partial", generation.GetTagItem("langfuse.observation.output")); // what streamed before the failure is kept
    }

    [Fact]
    public async Task ToolFailure_IsTaggedAsToolFailure()
    {
        using var capture = new SpanCapture();
        var id = LangfuseClient.NewTraceId();
        var failure = new ToolFailureException("search_movies", new InvalidOperationException("db down"));

        await Assert.ThrowsAsync<ToolFailureException>(() => Drain(new ScriptedClient(throwAfterFirstChunk: failure), id));

        Assert.Contains("error:tool_failure", Tags(capture.Trace(id).Root));
    }

    [Fact]
    public async Task ClientDisconnect_IsTracedAsWarning_NotAsError()
    {
        using var capture = new SpanCapture();
        var id = LangfuseClient.NewTraceId();
        var tracing = new LangfuseTracingChatClient(new ScriptedClient(), new LangfuseClient());
        var options = new ChatOptions { AdditionalProperties = new() { ["traceId"] = id } };

        // Read one chunk, then stop, as the endpoint does when the client goes away
        await using (var updates = tracing.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")], options).GetAsyncEnumerator())
            await updates.MoveNextAsync();

        var (root, generation) = capture.Trace(id);
        Assert.Equal(["client_disconnected"], Tags(root));
        Assert.Equal("WARNING", generation.GetTagItem("langfuse.observation.level"));
        Assert.NotEqual(ActivityStatusCode.Error, generation.Status);
    }

    [Fact]
    public async Task Success_HasNoErrorTags()
    {
        using var capture = new SpanCapture();
        var id = LangfuseClient.NewTraceId();

        await Drain(new ScriptedClient(), id);

        var (root, generation) = capture.Trace(id);
        Assert.Empty(Tags(root));
        Assert.Null(generation.GetTagItem("langfuse.observation.level"));
        Assert.Null(generation.GetTagItem("langfuse.observation.status_message"));
    }

    [Fact]
    public async Task Spans_form_one_trace_with_a_true_root()
    {
        using var capture = new SpanCapture();
        var id = LangfuseClient.NewTraceId();

        await Drain(new ScriptedClient(), id);

        var (root, generation) = capture.Trace(id);
        Assert.Equal(id, root.TraceId.ToHexString());
        Assert.Equal(default, root.ParentSpanId); // no phantom parent: Langfuse must see a root observation
        Assert.Equal(root.SpanId, generation.ParentSpanId);
        Assert.Equal("generation", generation.GetTagItem("langfuse.observation.type"));
    }

    [Fact]
    public async Task Root_carries_overall_input_and_output()
    {
        using var capture = new SpanCapture();
        var id = LangfuseClient.NewTraceId();

        await Drain(new ScriptedClient(), id);

        var root = capture.Trace(id).Root;
        Assert.Contains("hi", (string)root.GetTagItem("langfuse.observation.input")!);
        Assert.Equal("partial reply", root.GetTagItem("langfuse.observation.output"));
    }

    [Fact]
    public async Task Trace_context_is_copied_to_every_span()
    {
        using var capture = new SpanCapture();
        var id = LangfuseClient.NewTraceId();

        await Drain(new ScriptedClient(), id, new() { ["promptVersion"] = "v7", ["riskVerdict"] = "Allow", ["isEval"] = true });

        var (root, generation) = capture.Trace(id);
        foreach (var span in new[] { root, generation })
        {
            Assert.Equal("movie-chat", span.GetTagItem("langfuse.trace.name"));
            Assert.Equal("v7", span.GetTagItem("langfuse.version"));
            Assert.Equal(["risk:allow", "eval"], Tags(span));
            Assert.Equal("Allow", span.GetTagItem("langfuse.trace.metadata.riskVerdict"));
        }
    }

    [Fact]
    public void Generation_reports_model_usage_and_first_token_time()
    {
        using var capture = new SpanCapture();
        var id = LangfuseClient.NewTraceId();
        var start = DateTimeOffset.UtcNow.AddSeconds(-3);

        new LangfuseClient().RecordGeneration(
            id, "openai/gpt-4o-mini", [new ChatMessage(ChatRole.User, "hi")], "ok",
            start, start.AddSeconds(2), start.AddSeconds(1), 10, 5, 15, null, null);

        var (root, generation) = capture.Trace(id);
        Assert.Equal("openai/gpt-4o-mini", generation.GetTagItem("langfuse.observation.model.name"));
        Assert.Equal("""{"input":10,"output":5,"total":15}""", generation.GetTagItem("langfuse.observation.usage_details"));
        Assert.NotNull(generation.GetTagItem("langfuse.observation.completion_start_time"));
        Assert.Equal(start.UtcDateTime, generation.StartTimeUtc, TimeSpan.FromMilliseconds(1));
        Assert.Equal(TimeSpan.FromSeconds(2), root.Duration);
    }

    [Fact]
    public void Validation_TraceIsTaggedAndHasNoGeneration()
    {
        using var capture = new SpanCapture();
        var id = LangfuseClient.NewTraceId();

        new LangfuseClient().RecordError(
            TraceError.From(TraceError.Validation, "Message role must be 'user' or 'assistant'."), id);

        var spans = capture.Spans(id);
        Assert.Equal(2, spans.Count);
        Assert.Contains("error:validation", Tags(spans.Single(s => s.ParentSpanId == default)));
        Assert.Equal("event", spans.Single(s => s.ParentSpanId != default).GetTagItem("langfuse.observation.type"));
        Assert.DoesNotContain(spans, s => (string?)s.GetTagItem("langfuse.observation.type") == "generation");
    }

    [Fact]
    public void RateLimitFlood_IsThrottledToOneTrace()
    {
        using var capture = new SpanCapture();
        var langfuse = new LangfuseClient();

        for (var i = 0; i < 50; i++)
            langfuse.RecordRateLimit();

        var roots = capture.All.Where(s => s.ParentSpanId == default && Tags(s).Contains("error:rate_limit")).ToList();
        Assert.Single(roots);
    }

    private static string[] Tags(Activity span) => span.GetTagItem("langfuse.trace.tags") as string[] ?? [];

    /// <summary>Collects spans the Langfuse source emits; the listener is process-wide, so tests filter by trace ID.</summary>
    private sealed class SpanCapture : IDisposable
    {
        private readonly ConcurrentQueue<Activity> _spans = new();
        private readonly ActivityListener _listener;

        public SpanCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == LangfuseClient.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = _spans.Enqueue
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public IReadOnlyCollection<Activity> All => _spans;

        public List<Activity> Spans(string traceId) =>
            _spans.Where(s => s.TraceId.ToHexString() == traceId).ToList();

        public (Activity Root, Activity Generation) Trace(string traceId)
        {
            var spans = Spans(traceId);
            return (spans.Single(s => s.ParentSpanId == default), spans.Single(s => s.ParentSpanId != default));
        }

        public void Dispose() => _listener.Dispose();
    }

    private sealed class ScriptedClient(Exception? throwAfterFirstChunk = null) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "partial");
            await Task.Yield();

            if (throwAfterFirstChunk is not null)
                throw throwAfterFirstChunk;

            yield return new ChatResponseUpdate(ChatRole.Assistant, " reply");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}

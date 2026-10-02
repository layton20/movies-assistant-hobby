using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace MovieAssistant.Api.Tracing;

/// <summary>
/// Records finished chat requests as OpenTelemetry spans in Langfuse's v4 observations-first shape.
/// The OTLP exporter configured in Program.cs ships them; with no exporter nothing listens to
/// <see cref="SourceName"/> and every method is a cheap no-op.
/// </summary>
/// <remarks>
/// Each operation is built locally and exported once, complete: Langfuse v4 does not deduplicate
/// re-sent span IDs. Trace-wide context (name, tags, version, metadata) is copied onto every span
/// because v4 filters observations, not traces. Overall input/output sits on the root span.
/// </remarks>
public class LangfuseClient
{
    public const string SourceName = "MovieAssistant.Langfuse";
    private const string TraceName = "movie-chat";

    private static readonly ActivitySource Source = new(SourceName);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly TimeSpan RateLimitTraceInterval = TimeSpan.FromSeconds(1);
    private static long _lastRateLimitTraceTicks;
    private static int _suppressedRateLimitTraces;

    /// <summary>The ID Langfuse shows for a trace: 32 hex characters, shared by every span in it.</summary>
    public static string NewTraceId() => ActivityTraceId.CreateRandom().ToHexString();

    public void RecordGeneration(
        string traceId,
        string model,
        IList<ChatMessage> inputMessages,
        string output,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        DateTimeOffset? firstTokenTime,
        long inputTokens,
        long outputTokens,
        long totalTokens,
        string? promptVersion,
        TraceError? error,
        string? riskVerdict = null,
        double? riskProbability = null,
        bool isEval = false)
    {
        var previous = Activity.Current;
        try
        {
            using var root = StartRoot(traceId, startTime);
            if (root is null) return; // no exporter configured

            var context = new TraceContext(
                promptVersion, TagsFor(error, riskVerdict, isEval), error?.Category, riskVerdict, riskProbability);
            var input = JsonSerializer.Serialize(
                inputMessages.Select(m => new { role = m.Role.Value, content = m.Text }), JsonOptions);

            context.ApplyTo(root);
            ApplyObservation(root, "span", error, input, output);

            using var generation = StartSpan("chat-completion", root.Context, startTime);
            if (generation is null) return;

            context.ApplyTo(generation);
            ApplyObservation(generation, "generation", error, input, output);
            generation.SetTag("langfuse.observation.model.name", model);
            // Langfuse multiplies these by the model's price to compute cost
            generation.SetTag("langfuse.observation.usage_details",
                JsonSerializer.Serialize(new { input = inputTokens, output = outputTokens, total = totalTokens }, JsonOptions));
            // Langfuse derives time-to-first-token from this
            if (firstTokenTime is { } first)
                generation.SetTag("langfuse.observation.completion_start_time", first.UtcDateTime.ToString("O"));

            generation.SetEndTime(endTime.UtcDateTime);
            root.SetEndTime(endTime.UtcDateTime);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    /// <summary>
    /// Records a request that failed before reaching the model (validation, rate limit),
    /// so it still counts in the error rate.
    /// </summary>
    public void RecordError(TraceError error, string? traceId = null, bool isEval = false)
    {
        var previous = Activity.Current;
        try
        {
            var now = DateTimeOffset.UtcNow;

            using var root = StartRoot(traceId ?? NewTraceId(), now);
            if (root is null) return;

            var context = new TraceContext(null, TagsFor(error, null, isEval), error.Category, null, null);
            context.ApplyTo(root);
            ApplyObservation(root, "span", error);

            using var failure = StartSpan(error.Category, root.Context, now);
            if (failure is null) return;

            context.ApplyTo(failure);
            ApplyObservation(failure, "event", error);

            failure.SetEndTime(now.UtcDateTime);
            root.SetEndTime(now.UtcDateTime);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    /// <summary>
    /// Rate-limit rejections are traced at most once per second, with the number suppressed in
    /// between, so a flood of rejected requests cannot turn into a flood of spans.
    /// </summary>
    public void RecordRateLimit()
    {
        var now = DateTime.UtcNow.Ticks;
        var last = Interlocked.Read(ref _lastRateLimitTraceTicks);

        if (now - last < RateLimitTraceInterval.Ticks
            || Interlocked.CompareExchange(ref _lastRateLimitTraceTicks, now, last) != last)
        {
            Interlocked.Increment(ref _suppressedRateLimitTraces);
            return;
        }

        var suppressed = Interlocked.Exchange(ref _suppressedRateLimitTraces, 0);
        var message = suppressed == 0
            ? "Request rejected by the rate limiter."
            : $"Request rejected by the rate limiter (+{suppressed} more since the last trace).";

        RecordError(TraceError.From(TraceError.RateLimit, message));
    }

    /// <summary>
    /// The root span takes the trace ID we already told the caller about. A parent context with an
    /// empty span ID gives that trace ID without a phantom parent, so Langfuse sees a true root.
    /// </summary>
    private static Activity? StartRoot(string traceId, DateTimeOffset startTime) =>
        Source.StartActivity(
            TraceName,
            ActivityKind.Internal,
            new ActivityContext(ActivityTraceId.CreateFromString(traceId), default, ActivityTraceFlags.Recorded),
            startTime: startTime);

    private static Activity? StartSpan(string name, ActivityContext parent, DateTimeOffset startTime) =>
        Source.StartActivity(name, ActivityKind.Internal, parent, startTime: startTime);

    private static void ApplyObservation(
        Activity span, string type, TraceError? error, string? input = null, string? output = null)
    {
        span.SetTag("langfuse.observation.type", type);

        if (input is not null) span.SetTag("langfuse.observation.input", input);
        if (output is not null) span.SetTag("langfuse.observation.output", output);

        if (LevelFor(error) is { } level) span.SetTag("langfuse.observation.level", level);
        if (error is not null) span.SetTag("langfuse.observation.status_message", error.Message);
        if (error is { CountsAsError: true }) span.SetStatus(ActivityStatusCode.Error, error.Message);
    }

    private static string[]? TagsFor(TraceError? error, string? riskVerdict = null, bool isEval = false)
    {
        string[] errorTags = error switch
        {
            null => [],
            { CountsAsError: true } => ["error", $"error:{error.Category}"],
            _ => [error.Category]
        };

        string[] tags = riskVerdict is null ? errorTags : [.. errorTags, $"risk:{riskVerdict.ToLowerInvariant()}"];
        if (isEval) tags = [.. tags, "eval"];
        return tags.Length == 0 ? null : tags;
    }

    private static string? LevelFor(TraceError? error) => error switch
    {
        null => null,
        { CountsAsError: true } => "ERROR",
        _ => "WARNING"
    };

    /// <summary>Trace-wide attributes, set on every span so v4 can filter and aggregate observations by them.</summary>
    private sealed record TraceContext(
        string? Version, string[]? Tags, string? ErrorCategory, string? RiskVerdict, double? RiskProbability)
    {
        public void ApplyTo(Activity span)
        {
            span.SetTag("langfuse.trace.name", TraceName);
            if (Version is not null) span.SetTag("langfuse.version", Version);
            if (Tags is not null) span.SetTag("langfuse.trace.tags", Tags);
            if (ErrorCategory is not null) span.SetTag("langfuse.trace.metadata.errorCategory", ErrorCategory);
            if (RiskVerdict is not null) span.SetTag("langfuse.trace.metadata.riskVerdict", RiskVerdict);
            if (RiskProbability is not null)
                span.SetTag("langfuse.trace.metadata.riskProbability", RiskProbability.Value.ToString("0.####",
                    System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}

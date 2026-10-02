using System.ClientModel;
using MovieAssistant.Api.Exceptions;

namespace MovieAssistant.Api.Tracing;

/// <param name="Category">Stable slug used as a Langfuse tag, e.g. "timeout".</param>
/// <param name="CountsAsError">False for outcomes worth seeing but not part of the error rate.</param>
public record TraceError(string Category, string Message, bool CountsAsError = true)
{
    public const string Validation = "validation";
    public const string RateLimit = "rate_limit";
    public const string ProviderRateLimit = "provider_rate_limit";
    public const string Timeout = "timeout";
    public const string ToolFailure = "tool_failure";
    public const string ModelError = "model_error";
    public const string ClientDisconnected = "client_disconnected";
    public const string RiskBlocked = "risk_blocked";
    public const string RiskCheckUnavailable = "risk_check_unavailable";

    private const int MaxMessageLength = 500;

    public static TraceError From(string category, string message, bool countsAsError = true) =>
        new(category, message.Length > MaxMessageLength ? message[..MaxMessageLength] : message, countsAsError);

    /// <summary>Maps an exception from the chat pipeline to a category.</summary>
    /// <param name="cancellationToken">The request's token, used to tell a client disconnect from a timeout.</param>
    public static TraceError Classify(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        ToolFailureException tool => From(ToolFailure, tool.Message),

        OperationCanceledException when cancellationToken.IsCancellationRequested =>
            From(ClientDisconnected, "Client disconnected before the response completed.", countsAsError: false),

        ClientResultException { Status: 429 } e => From(ProviderRateLimit, e.Message),
        ClientResultException { Status: 408 or 504 } e => From(Timeout, e.Message),

        // A cancellation we did not ask for is the HttpClient/SDK timeout firing
        TimeoutException or OperationCanceledException => From(Timeout, exception.Message),

        _ when exception.InnerException is { } inner => Classify(inner, cancellationToken) is { Category: not ModelError } known
            ? known
            : From(ModelError, $"{exception.GetType().Name}: {exception.Message}"),

        _ => From(ModelError, $"{exception.GetType().Name}: {exception.Message}")
    };
}

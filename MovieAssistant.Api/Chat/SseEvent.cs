using System.Text.Json.Serialization;

namespace MovieAssistant.Api.Chat;

public record SseEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("delta")] string? Delta = null,
    [property: JsonPropertyName("error")] string? Error = null,
    [property: JsonPropertyName("traceId")] string? TraceId = null
);

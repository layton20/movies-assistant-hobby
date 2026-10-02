using System.Text.Json.Serialization;

namespace MovieAssistant.Api.Chat;

public record ChatRequestMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content
);
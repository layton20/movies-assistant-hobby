using System;
using System.Text.Json.Serialization;

namespace MovieAssistant.Api.Chat;

public record ChatRequest(
    [property: JsonPropertyName("agentId")] string AgentId,
    [property: JsonPropertyName("history")] List<ChatRequestMessage> History
);

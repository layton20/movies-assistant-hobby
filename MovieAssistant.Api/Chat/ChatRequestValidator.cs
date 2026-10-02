namespace MovieAssistant.Api.Chat;

public static class ChatRequestValidator
{
    public const int MaxMessages = 1000;
    public const int MaxMessageLength = 2000;

    /// <summary>Returns an error message, or null when the request is acceptable.</summary>
    public static string? Validate(ChatRequest? request)
    {
        if (request?.History is not { Count: > 0 } history)
            return "History must contain at least one message.";

        if (history.Count > MaxMessages)
            return $"History is limited to {MaxMessages} messages.";

        foreach (var message in history)
        {
            if (message is null || message.Role is not ("user" or "assistant"))
                return "Message role must be 'user' or 'assistant'.";

            if (string.IsNullOrWhiteSpace(message.Content))
                return "Message content must not be empty.";

            if (message.Content.Length > MaxMessageLength)
                return $"Messages are limited to {MaxMessageLength} characters.";
        }

        if (history[^1].Role != "user")
            return "The last message must be from the user.";

        return null;
    }
}

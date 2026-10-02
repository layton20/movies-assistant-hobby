namespace MovieAssistant.Api.Chat;

public static class SseEventTypes
{
    public const string Chunk = "chunk";
    public const string Done = "done";
    public const string Error = "error";
    public const string Trace = "trace";
}

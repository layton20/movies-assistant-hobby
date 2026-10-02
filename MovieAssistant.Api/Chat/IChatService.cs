namespace MovieAssistant.Api.Chat;

public interface IChatService
{
    IAsyncEnumerable<SseEvent> StreamAsync(
        string agentId,
        IList<ChatRequestMessage> history,
        bool isEval = false,
        CancellationToken cancellationToken = default);
}

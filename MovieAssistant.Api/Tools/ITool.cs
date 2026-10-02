using Microsoft.Extensions.AI;

namespace MovieAssistant.Api.Tools;

public interface ITool
{
    string Name { get; }
    AIFunction AsAIFunction();
}

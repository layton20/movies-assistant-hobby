using MovieAssistant.Api.Agents;
using MovieAssistant.Api.Exceptions;

namespace MovieAssistant.Api.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name);
    }

    public IReadOnlyList<ITool> GetToolsForAgent(IAgent agent)
    {
        return agent.EnabledToolNames
            .Select(name => _tools.TryGetValue(name, out var tool)
                ? tool
                : throw new ToolNotFoundException(name))
            .ToList();
    }
}

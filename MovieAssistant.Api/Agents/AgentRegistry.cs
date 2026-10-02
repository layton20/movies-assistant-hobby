namespace MovieAssistant.Api.Agents;

public class AgentRegistry
{
    private readonly Dictionary<string, IAgent> Agents;

    public AgentRegistry(IEnumerable<IAgent> agents)
    {
        Agents = agents.ToDictionary(a => a.Id);
    }

    public IAgent Resolve(string agentId)
    {
        if (Agents.TryGetValue(agentId, out var agent))
            return agent;

        throw new AgentNotFoundException(agentId);
    }

    public class AgentNotFoundException(string agentId) : Exception($"No agent registered with ID '{agentId}'");
}

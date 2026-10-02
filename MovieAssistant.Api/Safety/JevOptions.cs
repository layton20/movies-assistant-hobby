namespace MovieAssistant.Api.Safety;

public class JevOptions
{
    public string BaseUrl { get; init; } = "https://openrouter.ai";
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Pinned so behaviour doesn't shift under us; avoid ~typesafe/jev-latest.</summary>
    public string Model { get; init; } = "typesafe/jev-1.13";

    /// <summary>Block when the malicious probability is at or above this. Tune on our own evals.</summary>
    public double BlockThreshold { get; init; } = 0.5;

    public int TimeoutMs { get; init; } = 1500;

    /// <summary>When Jev is unreachable or returns garbage: let the request through (true) or block it.</summary>
    public bool FailOpen { get; init; } = true;

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);
}

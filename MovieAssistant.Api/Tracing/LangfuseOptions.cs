using System;

namespace MovieAssistant.Api.Tracing;

public class LangfuseOptions
{
    public string BaseUrl { get; init; } = "https://cloud.langfuse.com";
    public string PublicKey { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) &&
        !string.IsNullOrWhiteSpace(SecretKey);
}

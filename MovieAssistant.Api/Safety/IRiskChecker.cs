namespace MovieAssistant.Api.Safety;

public enum RiskVerdict
{
    Allow,
    Block,
    /// <summary>Check could not be completed; the caller decides via <see cref="JevOptions.FailOpen"/>.</summary>
    Unavailable
}

public record RiskResult(RiskVerdict Verdict, double? Probability = null);

public interface IRiskChecker
{
    Task<RiskResult> CheckAsync(string userMessage, CancellationToken cancellationToken);
}

using Microsoft.Extensions.AI;
using MovieAssistant.Api.Exceptions;

namespace MovieAssistant.Api.Tools;

public static class ToolFailureHandling
{
    /// <summary>
    /// Replaces the default tool invocation in UseFunctionInvocation. Behaves the same, except a
    /// tool exception is wrapped in <see cref="ToolFailureException"/> so it can be tracked separately.
    /// </summary>
    public static async ValueTask<object?> InvokeAsync(
        FunctionInvocationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.Function.InvokeAsync(context.Arguments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation is not a tool failure
        }
        catch (Exception ex)
        {
            throw new ToolFailureException(context.Function.Name, ex);
        }
    }
}

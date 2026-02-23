using Serilog;

namespace SOUP.Helpers;

/// <summary>
/// Extension methods for fire-and-forget async operations with error logging.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Safely executes a fire-and-forget task, logging any exceptions instead of silently swallowing them.
    /// Use this instead of <c>_ = SomeMethodAsync();</c> to ensure failures are observable in logs.
    /// </summary>
    /// <param name="task">The task to observe.</param>
    /// <param name="callerContext">Optional context string for log messages (e.g., the calling method name).</param>
    public static async void SafeFireAndForget(this Task task, string? callerContext = null)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown — don't log
        }
        catch (Exception ex)
        {
            if (callerContext is not null)
                Log.Warning(ex, "Fire-and-forget task failed in {Context}", callerContext);
            else
                Log.Warning(ex, "Fire-and-forget task failed");
        }
    }
}

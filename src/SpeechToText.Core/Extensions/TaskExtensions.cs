using Microsoft.Extensions.Logging;

namespace Olbrasoft.SpeechToText.Core.Extensions;

/// <summary>
/// Extension methods for Task operations.
/// </summary>
public static class TaskExtensions
{
    /// <summary>
    /// Executes a task without awaiting, but ensures exceptions are logged.
    /// Use this instead of discarding tasks with _ = Task.Run(...).
    /// </summary>
    /// <param name="task">The task to execute.</param>
    /// <param name="logger">Logger for error reporting.</param>
    /// <param name="context">Optional context description for error messages.</param>
    public static void FireAndForget(this Task task, ILogger logger, string? context = null)
    {
        task.ContinueWith(
            t =>
            {
                var message = string.IsNullOrEmpty(context)
                    ? "Unhandled exception in fire-and-forget task"
                    : $"Unhandled exception in fire-and-forget task: {context}";

                logger.LogError(t.Exception, message);
            },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Wraps an action in Task.Run and ensures exceptions are logged.
    /// Replaces pattern: _ = Task.Run(() => SomeMethod());
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="logger">Logger for error reporting.</param>
    /// <param name="context">Optional context description for error messages.</param>
    public static void RunFireAndForget(Action action, ILogger logger, string? context = null)
    {
        Task.Run(action).FireAndForget(logger, context);
    }

    /// <summary>
    /// Wraps an async action in Task.Run and ensures exceptions are logged.
    /// Replaces pattern: _ = Task.Run(async () => await SomeMethodAsync());
    /// </summary>
    /// <param name="asyncAction">The async action to execute.</param>
    /// <param name="logger">Logger for error reporting.</param>
    /// <param name="context">Optional context description for error messages.</param>
    public static void RunFireAndForget(Func<Task> asyncAction, ILogger logger, string? context = null)
    {
        Task.Run(asyncAction).FireAndForget(logger, context);
    }
}

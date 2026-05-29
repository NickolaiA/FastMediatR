namespace FastMediatR;

/// <summary>
/// <see cref="INotificationPublisher"/> implementation that invokes all registered
/// <see cref="INotificationHandler{TNotification}"/> instances concurrently via
/// <see cref="Task.WhenAll(Task[])"/> and surfaces every handler failure.
/// </summary>
/// <remarks>
/// <para>
/// This is the <b>default</b> notification publisher strategy. All handlers start in parallel.
/// If one or more handlers throw, all exceptions are collected and re-thrown as an
/// <see cref="AggregateException"/> once every handler has completed — no handler is
/// cancelled because another failed.
/// </para>
/// <para>
/// To ensure all handler exceptions are surfaced (not just the first one, which is the
/// behavior of <c>await Task.WhenAll(...)</c>), this implementation re-throws the
/// underlying <c>Task.Exception</c> after awaiting the joined task:
/// <code>
/// try { await whenAll; }
/// catch { if (whenAll.Exception is { } ex) throw ex; else throw; }
/// </code>
/// This is the idiomatic BCL pattern for "all errors" semantics. When the joined task is
/// merely cancelled (no handler faulted), <c>Task.Exception</c> is <see langword="null"/>,
/// so the original <see cref="OperationCanceledException"/> is allowed to propagate.
/// </para>
/// <para>
/// To switch strategies, set <c>cfg.NotificationPublisherStrategy</c> in
/// <c>AddFastMediatR&lt;TMarker&gt;</c>, or replace the <see cref="INotificationPublisher"/>
/// service directly:
/// <code>
/// services.Replace(ServiceDescriptor.Singleton&lt;INotificationPublisher, MyPublisher&gt;());
/// </code>
/// </para>
/// </remarks>
public sealed class TaskWhenAllPublisher : INotificationPublisher
{
    /// <summary>
    /// Invokes all <paramref name="handlers"/> concurrently and surfaces all failures
    /// via <see cref="AggregateException"/>.
    /// </summary>
    /// <param name="handlers">
    /// Pre-bound handler executors emitted by the source generator. Order is not significant
    /// for this strategy since all handlers run in parallel.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="notification">The notification to broadcast. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token forwarded to every handler.</param>
    /// <returns>A task that completes when all handlers have finished.</returns>
    /// <exception cref="AggregateException">
    /// Thrown when one or more handlers throw. All handler exceptions are aggregated.
    /// </exception>
    public async Task Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlers,
        INotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(notification);

        if (handlers.Count == 0)
        {
            return;
        }

        var tasks = new Task[handlers.Count];

        for (int i = 0; i < handlers.Count; i++)
        {
            tasks[i] = handlers[i].Handler(notification, cancellationToken);
        }

        var whenAll = Task.WhenAll(tasks);

        try
        {
            await whenAll.ConfigureAwait(false);
        }
        catch
        {
            // When one or more handlers faulted, re-throw the AggregateException so ALL handler
            // errors are surfaced, not just the first one that Task.WhenAll would normally propagate.
            // When the joined task was merely cancelled (no fault), Task.Exception is null — in that
            // case let the original OperationCanceledException propagate instead of dereferencing null.
            if (whenAll.Exception is { } aggregate)
            {
                throw aggregate;
            }

            throw;
        }
    }
}

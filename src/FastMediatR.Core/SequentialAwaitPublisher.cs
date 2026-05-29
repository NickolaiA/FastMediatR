namespace FastMediatR;

/// <summary>
/// <see cref="INotificationPublisher"/> implementation that invokes each registered
/// <see cref="INotificationHandler{TNotification}"/> one at a time in DI registration order.
/// </summary>
/// <remarks>
/// <para>
/// Handlers execute sequentially. If any handler throws, the exception propagates
/// immediately and no subsequent handlers are invoked — the exception is propagated
/// unwrapped (as a single exception, not wrapped in <see cref="AggregateException"/>).
/// </para>
/// <para>
/// The cancellation token is checked between every handler invocation so that a long
/// sequence of handlers can be aborted promptly when the caller cancels.
/// </para>
/// <para>
/// Use this strategy when:
/// <list type="bullet">
///   <item>Handler execution order is significant.</item>
///   <item>A failing handler should prevent subsequent handlers from running.</item>
/// </list>
/// Use <see cref="TaskWhenAllPublisher"/> (the default) when handlers are independent and
/// you want all failures surfaced, not just the first.
/// </para>
/// <para>
/// To switch strategies, set <c>cfg.NotificationPublisherStrategy</c> in
/// <c>AddFastMediatR&lt;TMarker&gt;</c>, or replace the <see cref="INotificationPublisher"/>
/// service directly:
/// <code>
/// services.Replace(ServiceDescriptor.Singleton&lt;INotificationPublisher, SequentialAwaitPublisher&gt;());
/// </code>
/// </para>
/// </remarks>
public sealed class SequentialAwaitPublisher : INotificationPublisher
{
    /// <summary>
    /// Invokes each handler in <paramref name="handlers"/> sequentially, in order.
    /// Stops and propagates the exception if any handler throws.
    /// </summary>
    /// <param name="handlers">
    /// Pre-bound handler executors emitted by the source generator. Handlers are invoked
    /// in the order provided.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="notification">The notification to broadcast. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">
    /// Token forwarded to every handler. Checked before each handler invocation
    /// (including before the first handler) so that a cancelled token prevents any
    /// handler from running.
    /// </param>
    /// <returns>A task that completes when all handlers have been invoked successfully.</returns>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is cancelled between handler invocations.</exception>
    /// <exception cref="Exception">
    /// Any exception thrown by a handler propagates directly, stopping execution of subsequent handlers.
    /// </exception>
    public async Task Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlers,
        INotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(notification);

        for (int i = 0; i < handlers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await handlers[i].Handler(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}

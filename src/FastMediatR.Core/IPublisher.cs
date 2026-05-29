namespace FastMediatR;

/// <summary>
/// Publishes a notification to every registered <see cref="INotificationHandler{TNotification}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Handler sequencing and exception semantics are determined by the configured
/// <see cref="INotificationPublisher"/>. The default,
/// <c>TaskWhenAllPublisher</c>, invokes all handlers concurrently and surfaces
/// every failure as an <see cref="System.AggregateException"/>.
/// </para>
/// <para>
/// Prefer <see cref="IPublisher"/> over <see cref="IMediator"/> when a class only needs to
/// publish notifications.
/// </para>
/// </remarks>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    /// <param name="notification">The notification to broadcast. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token propagated to every handler.</param>
    /// <returns>A task completing when all handlers have completed per the active publisher strategy.</returns>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="notification"/> is <see langword="null"/>.
    /// </exception>
    Task Publish(
        INotification notification,
        CancellationToken cancellationToken = default);
}

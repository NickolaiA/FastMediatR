namespace FastMediatR;

/// <summary>
/// Handles a notification of type <typeparamref name="TNotification"/>.
/// </summary>
/// <typeparam name="TNotification">The concrete notification type this handler observes.</typeparam>
/// <remarks>
/// <para>
/// Multiple handlers may exist for the same notification. The configured
/// <see cref="INotificationPublisher"/> determines whether they are invoked concurrently
/// (<c>TaskWhenAllPublisher</c>) or sequentially (<c>SequentialAwaitPublisher</c>).
/// </para>
/// </remarks>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>
    /// Processes the notification.
    /// </summary>
    /// <param name="notification">The notification payload. Never <see langword="null"/>.</param>
    /// <param name="cancellationToken">
    /// Token observed by the handler to cooperatively cancel the operation.
    /// </param>
    /// <returns>A task representing the asynchronous handler invocation.</returns>
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}

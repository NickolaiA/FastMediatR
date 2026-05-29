namespace FastMediatR;

/// <summary>
/// Strategy contract for invoking the set of <see cref="INotificationHandler{TNotification}"/>
/// instances that observe a given notification.
/// </summary>
/// <remarks>
/// <para>
/// FastMediatR ships two implementations:
/// <list type="bullet">
///   <item>
///     <description>
///     <c>TaskWhenAllPublisher</c> (default) — invokes all handlers concurrently
///     and surfaces every failure via <see cref="System.AggregateException"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///     <c>SequentialAwaitPublisher</c> — invokes handlers one at a time in
///     registration order; the first failure stops the publish and propagates unwrapped.
///     </description>
///   </item>
/// </list>
/// Consumers may register a custom implementation by replacing the
/// <see cref="INotificationPublisher"/> service in DI.
/// </para>
/// </remarks>
public interface INotificationPublisher
{
    /// <summary>
    /// Invokes every handler in <paramref name="handlers"/> for the given
    /// <paramref name="notification"/> according to the strategy's semantics.
    /// </summary>
    /// <param name="handlers">
    /// The pre-bound handler executors emitted by the source generator. Order is
    /// significant for sequential strategies. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="notification">The notification to broadcast. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token forwarded to every handler.</param>
    /// <returns>A task completing when all handlers have completed per the strategy's contract.</returns>
    Task Publish(
        IReadOnlyList<NotificationHandlerExecutor> handlers,
        INotification notification,
        CancellationToken cancellationToken);
}

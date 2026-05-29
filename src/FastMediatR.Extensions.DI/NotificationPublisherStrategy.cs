namespace FastMediatR.Extensions.DI;

/// <summary>
/// Specifies which <see cref="FastMediatR.INotificationPublisher"/> implementation is registered
/// by <see cref="ServiceCollectionExtensions.AddFastMediatR{TMarker}"/>.
/// </summary>
/// <remarks>
/// Choose a strategy via <see cref="FastMediatRConfig.NotificationPublisherStrategy"/>:
/// <code>
/// builder.Services.AddFastMediatR&lt;Program&gt;(cfg =&gt;
///     cfg.NotificationPublisherStrategy = NotificationPublisherStrategy.Sequential);
/// </code>
/// You can always replace the registered <see cref="FastMediatR.INotificationPublisher"/>
/// with a custom implementation after calling <c>AddFastMediatR</c>.
/// </remarks>
public enum NotificationPublisherStrategy
{
    /// <summary>
    /// All handlers run concurrently via <c>Task.WhenAll</c>. Every failure is surfaced
    /// via <see cref="System.AggregateException"/>. This is the default.
    /// </summary>
    /// <seealso cref="FastMediatR.TaskWhenAllPublisher"/>
    ParallelWhenAll = 0,

    /// <summary>
    /// Handlers run one at a time in DI registration order. The first failure stops the
    /// publish and the exception propagates unwrapped.
    /// </summary>
    /// <seealso cref="FastMediatR.SequentialAwaitPublisher"/>
    Sequential = 1,
}

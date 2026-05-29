namespace FastMediatR;

/// <summary>
/// Marker interface for a notification (event) broadcast to every registered
/// <see cref="INotificationHandler{TNotification}"/> via <see cref="IPublisher.Publish"/>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="IRequest{TResponse}"/>, a notification may have zero, one, or many
/// handlers. The configured <see cref="INotificationPublisher"/> determines how handlers
/// are sequenced (concurrent fan-out by default, or sequential).
/// </para>
/// </remarks>
public interface INotification
{
}

using FastMediatR;

namespace FastMediatR.Sample.Api.Notifications;

/// <summary>
/// Demonstrates <see cref="INotification"/> — a broadcast event with zero or more handlers.
/// Notifications are fire-and-forget at the call site; the chosen <see cref="INotificationPublisher"/>
/// strategy decides how the handlers are coordinated (default: <c>TaskWhenAllPublisher</c>).
/// </summary>
/// <param name="UserId">The id of the newly-created user.</param>
/// <param name="Email">The user's email address.</param>
public sealed record UserCreatedNotification(Guid UserId, string Email) : INotification;

/// <summary>Sends a welcome email when a user is created.</summary>
public sealed class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    /// <summary>Handles the notification.</summary>
    /// <param name="notification">The event payload.</param>
    /// <param name="cancellationToken">Token observed by the handler.</param>
    /// <returns>A completed task.</returns>
    public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        // Placeholder for actual email integration.
        return Task.CompletedTask;
    }
}

/// <summary>Writes an audit-log entry when a user is created (demonstrates multiple handlers per notification).</summary>
public sealed class AuditUserCreationHandler : INotificationHandler<UserCreatedNotification>
{
    /// <summary>Handles the notification.</summary>
    /// <param name="notification">The event payload.</param>
    /// <param name="cancellationToken">Token observed by the handler.</param>
    /// <returns>A completed task.</returns>
    public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        // Placeholder for audit-log integration.
        return Task.CompletedTask;
    }
}

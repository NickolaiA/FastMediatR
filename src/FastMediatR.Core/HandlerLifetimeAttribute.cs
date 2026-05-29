using Microsoft.Extensions.DependencyInjection;

namespace FastMediatR;

/// <summary>
/// Overrides the DI lifetime FastMediatR uses when registering this handler.
/// </summary>
/// <remarks>
/// <para>
/// By default every <see cref="IRequestHandler{TRequest, TResponse}"/> and
/// <see cref="INotificationHandler{TNotification}"/> is registered as
/// <see cref="ServiceLifetime.Scoped"/>. Apply this attribute to a concrete handler class
/// to register a single handler under a different lifetime — typically
/// <see cref="ServiceLifetime.Transient"/> for stateless handlers or
/// <see cref="ServiceLifetime.Singleton"/> for handlers that depend only on singletons.
/// </para>
/// <para>
/// The source generator emits diagnostic <c>FM003</c> when a handler's
/// <see cref="HandlerLifetimeAttribute"/> deviates from the configured default, so the
/// non-default lifetime is visible in the build output.
/// </para>
/// </remarks>
/// <param name="lifetime">The DI lifetime to use when registering the decorated handler.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class HandlerLifetimeAttribute(ServiceLifetime lifetime) : Attribute
{
    /// <summary>
    /// The DI lifetime to use when registering the decorated handler.
    /// </summary>
    public ServiceLifetime Lifetime { get; } = lifetime;
}

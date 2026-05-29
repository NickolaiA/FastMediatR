// Copyright FastMediatR contributors
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.DependencyInjection;

namespace FastMediatR.Telemetry;

/// <summary>
/// Extension methods on <see cref="IServiceCollection"/> for registering FastMediatR
/// telemetry services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TracingBehavior{TRequest,TResponse}"/> as an open-generic
    /// <see cref="IPipelineBehavior{TRequest,TResponse}"/> singleton so that every
    /// request dispatched through FastMediatR is wrapped in an OpenTelemetry span.
    /// </summary>
    /// <param name="services">
    /// The <see cref="IServiceCollection"/> to register the behavior into.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The same <see cref="IServiceCollection"/> instance for fluent chaining.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="services"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Call this method <em>before</em> <c>AddFastMediatR&lt;TMarker&gt;</c> so that
    /// <see cref="TracingBehavior{TRequest,TResponse}"/> is placed at the outermost
    /// position in the pipeline and its span therefore covers the full dispatch latency.
    /// </para>
    /// <para>
    /// <see cref="TracingBehavior{TRequest,TResponse}"/> is registered as a singleton
    /// because it carries no per-request or per-scope state — all mutable state lives
    /// in the <see cref="System.Diagnostics.Activity"/> which is a per-call BCL type.
    /// </para>
    /// <para>
    /// To export the emitted spans to an OpenTelemetry back-end, also call
    /// <see cref="TracerProviderBuilderExtensions.AddFastMediatRInstrumentation"/> when
    /// configuring the OpenTelemetry SDK:
    /// <code>
    /// builder.Services.AddOpenTelemetry()
    ///     .WithTracing(tracing =&gt; tracing.AddFastMediatRInstrumentation());
    /// </code>
    /// </para>
    /// </remarks>
    public static IServiceCollection AddFastMediatRTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(TracingBehavior<,>));

        return services;
    }
}

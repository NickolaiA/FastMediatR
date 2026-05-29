// Copyright FastMediatR contributors
// SPDX-License-Identifier: MIT

using OpenTelemetry.Trace;

namespace FastMediatR.Telemetry;

/// <summary>
/// Extension methods on <see cref="TracerProviderBuilder"/> for wiring the FastMediatR
/// instrumentation source into the OpenTelemetry SDK.
/// </summary>
public static class TracerProviderBuilderExtensions
{
    /// <summary>
    /// Subscribes the <c>"FastMediatR"</c> <see cref="System.Diagnostics.ActivitySource"/>
    /// to the <see cref="TracerProviderBuilder"/> so that spans emitted by
    /// <see cref="TracingBehavior{TRequest,TResponse}"/> are captured and forwarded to
    /// the configured exporter(s).
    /// </summary>
    /// <param name="builder">
    /// The <see cref="TracerProviderBuilder"/> to configure. Must not be
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The same <see cref="TracerProviderBuilder"/> for fluent chaining.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="builder"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// This is semantically equivalent to calling
    /// <c>builder.AddSource("FastMediatR")</c> and is provided as a named extension
    /// so the intent is explicit and discoverable via IDE auto-complete.
    /// </remarks>
    public static TracerProviderBuilder AddFastMediatRInstrumentation(
        this TracerProviderBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddSource(FastMediatRActivitySource.ActivitySourceName);
    }
}

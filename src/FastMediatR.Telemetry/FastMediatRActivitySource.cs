// Copyright FastMediatR contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Reflection;

namespace FastMediatR.Telemetry;

/// <summary>
/// Holds the shared <see cref="ActivitySource"/> instance for FastMediatR telemetry.
/// </summary>
/// <remarks>
/// <para>
/// Centralised so that <see cref="TracingBehavior{TRequest,TResponse}"/> and
/// <see cref="TracerProviderBuilderExtensions"/> reference the same source-name constant,
/// preventing typos and making it easy to subscribe via <c>.AddSource("FastMediatR")</c>.
/// </para>
/// <para>
/// The <see cref="ActivitySource"/> is created once at class-initialisation time,
/// before any request is dispatched through the pipeline.
/// </para>
/// </remarks>
internal static class FastMediatRActivitySource
{
    /// <summary>
    /// The well-known name of the <see cref="ActivitySource"/> emitted by FastMediatR.
    /// Consumers should subscribe using <c>tracerProviderBuilder.AddSource(<see cref="ActivitySourceName"/>)</c>
    /// or the helper extension <c>tracerProviderBuilder.AddFastMediatRInstrumentation()</c>.
    /// </summary>
    internal const string ActivitySourceName = "FastMediatR";

    /// <summary>
    /// The shared <see cref="ActivitySource"/> instance used to start spans.
    /// All spans emitted by <see cref="TracingBehavior{TRequest,TResponse}"/> originate from this source.
    /// The assembly informational version is attached so exporters can distinguish package versions.
    /// </summary>
    internal static readonly ActivitySource Source = new(
        ActivitySourceName,
        typeof(FastMediatRActivitySource).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0");
}

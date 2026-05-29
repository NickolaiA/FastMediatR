using System.ComponentModel.DataAnnotations;

namespace FastMediatR;

/// <summary>
/// Runtime configuration options for FastMediatR, bound from the <c>"FastMediatR"</c> section
/// of <c>appsettings.json</c> (or any registered <c>IConfiguration</c> source).
/// </summary>
/// <remarks>
/// <para>
/// Options are registered via <c>AddOptions&lt;FastMediatROptions&gt;().BindConfiguration("FastMediatR")</c>
/// inside <c>ServiceCollectionExtensions.AddFastMediatR&lt;TMarker&gt;</c>. The instance is validated
/// with data annotations on application start (<c>ValidateOnStart</c>).
/// </para>
/// <para>
/// Example <c>appsettings.json</c> section:
/// <code>
/// "FastMediatR": {
///   "PerformanceThresholdMs": 300,
///   "EnableLogging": true,
///   "EnablePayloadLogging": false
/// }
/// </code>
/// </para>
/// </remarks>
public sealed class FastMediatROptions
{
    /// <summary>
    /// The key under which FastMediatR options are expected in <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "FastMediatR";

    /// <summary>
    /// Gets or sets the elapsed-time threshold in milliseconds above which a slow-request
    /// warning is emitted by <see cref="PerformanceBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <value>
    /// Must be a positive integer. Defaults to <c>500</c> ms.
    /// </value>
    [Range(1, int.MaxValue, ErrorMessage = "PerformanceThresholdMs must be a positive integer (minimum 1).")]
    public int PerformanceThresholdMs { get; set; } = 500;

    /// <summary>
    /// Gets or sets a value indicating whether <see cref="LoggingBehavior{TRequest,TResponse}"/>
    /// emits log entries. Set to <see langword="false"/> to disable logging globally without
    /// removing the behavior from the pipeline.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether request payloads are included in the structured
    /// log entries emitted by <see cref="LoggingBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <value>
    /// <see langword="true"/> to log request payloads; <see langword="false"/> (default) to suppress them.
    /// Be cautious when enabling in production — payloads may contain PII.
    /// </value>
    public bool EnablePayloadLogging { get; set; }
}

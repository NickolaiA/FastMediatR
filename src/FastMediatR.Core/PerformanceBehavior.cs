using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FastMediatR;

/// <summary>
/// Pipeline behavior that measures the wall-clock elapsed time of each request and logs a
/// warning when the elapsed time exceeds <see cref="FastMediatROptions.PerformanceThresholdMs"/>.
/// </summary>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Uses <see cref="TimeProvider.GetTimestamp"/> and <see cref="TimeProvider.GetElapsedTime(long)"/>
/// for monotonic, mockable timing (DotNet.md §1 — use <c>TimeProvider</c> for testability).
/// </para>
/// <para>
/// The timer wraps the call to <c>next</c> in a <c>try/finally</c> block so that slow requests
/// are logged on both the success and failure paths — a slow failing request is equally
/// actionable information.
/// </para>
/// <para>
/// A typical multi-behavior registration order (outermost first):
/// <code>
/// cfg.AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;));      // outermost — logs full lifecycle
/// cfg.AddOpenBehavior(typeof(PerformanceBehavior&lt;,&gt;)); // measures handler + validation latency
/// cfg.AddOpenBehavior(typeof(ValidationBehavior&lt;,&gt;));  // innermost (before handler)
/// </code>
/// </para>
/// </remarks>
public sealed class PerformanceBehavior<TRequest, TResponse>(
    TimeProvider timeProvider,
    IOptions<FastMediatROptions> options,
    ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Cached request type name used in every log message to avoid per-call reflection.</summary>
    private static readonly string _requestName = typeof(TRequest).Name;

    /// <summary>Options snapshot injected at construction time.</summary>
    private readonly FastMediatROptions _options = options.Value;

    /// <summary>
    /// Measures the elapsed time of the inner pipeline and logs a warning if it exceeds
    /// <see cref="FastMediatROptions.PerformanceThresholdMs"/>.
    /// </summary>
    /// <param name="request">The current request. Never <see langword="null"/>.</param>
    /// <param name="next">The next step in the pipeline.</param>
    /// <param name="cancellationToken">Token forwarded to <paramref name="next"/>.</param>
    /// <returns>The response produced by the inner pipeline.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        long startTs = timeProvider.GetTimestamp();

        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            long elapsedMs = (long)timeProvider.GetElapsedTime(startTs).TotalMilliseconds;
            int threshold = _options.PerformanceThresholdMs;

            if (elapsedMs > threshold)
            {
                PerformanceLog.Slow(logger, _requestName, elapsedMs, threshold);
            }
        }
    }
}

/// <summary>
/// Source-generated, zero-allocation log methods for <see cref="PerformanceBehavior{TRequest,TResponse}"/>.
/// </summary>
internal static partial class PerformanceLog
{
    /// <summary>Logs a slow-request warning.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="requestType">The short name of the request type.</param>
    /// <param name="elapsedMs">Actual elapsed time in milliseconds.</param>
    /// <param name="threshold">Configured threshold in milliseconds.</param>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Slow request: {RequestType} took {ElapsedMs}ms (threshold: {Threshold}ms)")]
    public static partial void Slow(ILogger logger, string requestType, long elapsedMs, int threshold);
}

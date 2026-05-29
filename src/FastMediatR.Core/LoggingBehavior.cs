using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FastMediatR;

/// <summary>
/// Pipeline behavior that logs the start, completion, and failure of every request flowing
/// through the pipeline using source-generated, zero-allocation log methods.
/// </summary>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Behavior:
/// <list type="bullet">
///   <item>Logs "Handling {RequestType}" (+ payload if <see cref="FastMediatROptions.EnablePayloadLogging"/> is <see langword="true"/>) at <c>Information</c> level on entry.</item>
///   <item>Logs "Handled {RequestType} in {ElapsedMs}ms" at <c>Information</c> level on success.</item>
///   <item>Logs "Failed {RequestType} after {ElapsedMs}ms" at <c>Error</c> level on exception.</item>
///   <item>When <see cref="FastMediatROptions.EnableLogging"/> is <see langword="false"/>, all logging is suppressed and the behavior is a transparent pass-through.</item>
/// </list>
/// </para>
/// <para>
/// Register via <c>cfg.AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;))</c> inside
/// <c>AddFastMediatR&lt;TMarker&gt;</c>.
/// </para>
/// <para>
/// <b>PII warning:</b> <see cref="FastMediatROptions.EnablePayloadLogging"/> defaults to
/// <see langword="false"/>. Enabling it will log the full request payload via structured
/// destructuring (<c>{@Request}</c>). Only enable after auditing every request type for
/// sensitive fields.
/// </para>
/// </remarks>
public sealed class LoggingBehavior<TRequest, TResponse>(
    IOptions<FastMediatROptions> options,
    TimeProvider timeProvider,
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Cached request type name used in every log message to avoid per-call reflection.</summary>
    private static readonly string _requestName = typeof(TRequest).Name;

    /// <summary>Options snapshot injected at construction time.</summary>
    private readonly FastMediatROptions _options = options.Value;

    /// <summary>
    /// Logs the request entry, delegates to <paramref name="next"/>, and logs completion or failure.
    /// Skips all logging when <see cref="FastMediatROptions.EnableLogging"/> is <see langword="false"/>.
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

        if (!_options.EnableLogging)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        long startTs = timeProvider.GetTimestamp();

        if (_options.EnablePayloadLogging)
        {
            LoggingBehaviorLog.HandlingWithPayload(logger, _requestName, request);
        }
        else
        {
            LoggingBehaviorLog.Handling(logger, _requestName);
        }

        try
        {
            TResponse response = await next(cancellationToken).ConfigureAwait(false);
            long elapsedMs = (long)timeProvider.GetElapsedTime(startTs).TotalMilliseconds;
            LoggingBehaviorLog.Handled(logger, _requestName, elapsedMs);
            return response;
        }
        catch (Exception ex)
        {
            // try/catch (not try/finally) so failure logs are distinct from success logs.
            long elapsedMs = (long)timeProvider.GetElapsedTime(startTs).TotalMilliseconds;
            LoggingBehaviorLog.Failed(logger, ex, _requestName, elapsedMs);
            throw;
        }
    }
}

/// <summary>
/// Source-generated, zero-allocation log methods for <see cref="LoggingBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <remarks>
/// All log methods use <c>[LoggerMessage]</c> to generate the logging calls at compile time,
/// eliminating boxing and string formatting on the hot path (DotNet.md §15).
/// </remarks>
internal static partial class LoggingBehaviorLog
{
    /// <summary>Logs a handling-started message without the request payload.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="requestType">The short name of the request type.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestType}")]
    public static partial void Handling(ILogger logger, string requestType);

    /// <summary>
    /// Logs a handling-started message including the full request payload.
    /// Only invoked when <see cref="FastMediatROptions.EnablePayloadLogging"/> is <see langword="true"/>.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="requestType">The short name of the request type.</param>
    /// <param name="request">The request payload, destructured via <c>{@Request}</c>.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestType} {@Request}")]
    public static partial void HandlingWithPayload(ILogger logger, string requestType, object request);

    /// <summary>Logs a successfully completed request.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="requestType">The short name of the request type.</param>
    /// <param name="elapsedMs">Wall-clock elapsed time in milliseconds.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestType} in {ElapsedMs}ms")]
    public static partial void Handled(ILogger logger, string requestType, long elapsedMs);

    /// <summary>Logs a request that completed with an unhandled exception.</summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception that caused the failure.</param>
    /// <param name="requestType">The short name of the request type.</param>
    /// <param name="elapsedMs">Wall-clock elapsed time in milliseconds.</param>
    [LoggerMessage(Level = LogLevel.Error, Message = "Failed {RequestType} after {ElapsedMs}ms")]
    public static partial void Failed(ILogger logger, Exception ex, string requestType, long elapsedMs);
}

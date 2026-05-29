// Copyright FastMediatR contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;

namespace FastMediatR.Telemetry;

/// <summary>
/// Pipeline behavior that wraps each <c>IMediator.Send</c> operation in an
/// <see cref="Activity"/> span emitted by the <c>"FastMediatR"</c>
/// <see cref="ActivitySource"/>.
/// </summary>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// The activity name is the short type name of <typeparamref name="TRequest"/>
/// (e.g. <c>"CreateUserCommand"</c>) so spans are readable in Jaeger or Zipkin
/// without requiring tag drill-down.
/// </para>
/// <para>
/// If no listener is subscribed to the <c>"FastMediatR"</c> <see cref="ActivitySource"/>
/// (i.e. <see cref="ActivitySource.StartActivity(string, ActivityKind)"/> returns
/// <see langword="null"/>), the behavior short-circuits with zero overhead — the only
/// cost is a single null check per request.
/// </para>
/// <para>
/// <b>PII safety:</b> Only the fully-qualified type names of the request and response
/// are recorded as tags — never the request payload. Consumers who need payload
/// enrichment should add a separate <see cref="IPipelineBehavior{TRequest,TResponse}"/>
/// that writes to <see cref="Activity.Current"/>.
/// </para>
/// <para>
/// <b>Exception detail caution:</b> When the inner pipeline throws, this behavior records the
/// exception message via <see cref="Activity.SetStatus(ActivityStatusCode, string?)"/> and a
/// full <c>exception</c> event (including the stack trace) via
/// <see cref="Activity.AddException(Exception, in System.Diagnostics.TagList, System.DateTimeOffset)"/>.
/// This follows OpenTelemetry semantic conventions, but exception messages can contain sensitive
/// data in some applications. In PII-sensitive environments, configure your exporter or a
/// span processor to scrub or redact the <c>exception.message</c> / <c>exception.stacktrace</c>
/// attributes before export.
/// </para>
/// <para>
/// Register via <see cref="ServiceCollectionExtensions.AddFastMediatRTelemetry"/>
/// <em>before</em> calling <c>AddFastMediatR&lt;TMarker&gt;</c> so that this behavior
/// wraps the entire pipeline (outermost position).
/// </para>
/// </remarks>
public sealed class TracingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Cached short name of <typeparamref name="TRequest"/> used as the activity operation name.
    /// Computed once per generic instantiation to avoid per-call reflection on the hot path.
    /// </summary>
    private static readonly string _requestName = typeof(TRequest).Name;

    /// <summary>
    /// Cached fully-qualified name of <typeparamref name="TRequest"/> for the
    /// <c>mediator.request_type</c> tag. Falls back to the short name when
    /// <see cref="Type.FullName"/> is <see langword="null"/> (e.g. for anonymous types).
    /// </summary>
    private static readonly string _requestTypeFull = typeof(TRequest).FullName ?? typeof(TRequest).Name;

    /// <summary>
    /// Cached fully-qualified name of <typeparamref name="TResponse"/> for the
    /// <c>mediator.response_type</c> tag. Falls back to the short name when
    /// <see cref="Type.FullName"/> is <see langword="null"/>.
    /// </summary>
    private static readonly string _responseTypeFull = typeof(TResponse).FullName ?? typeof(TResponse).Name;

    /// <summary>
    /// Starts an <see cref="Activity"/> span, invokes the next pipeline step, then
    /// marks the span <c>Ok</c> on success or <c>Error</c> on exception.
    /// </summary>
    /// <param name="request">The incoming request. Never <see langword="null"/>.</param>
    /// <param name="next">
    /// The next step in the pipeline — either the handler or the next inner behavior.
    /// Must not be <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Propagated unchanged to <paramref name="next"/>.</param>
    /// <returns>The response returned by the inner pipeline.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="next"/> is <see langword="null"/>.
    /// </exception>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        // StartActivity returns null when no ActivityListener is subscribed to the
        // "FastMediatR" source. This is the zero-overhead fast path.
        using Activity? activity = FastMediatRActivitySource.Source
            .StartActivity(_requestName, ActivityKind.Internal);

        if (activity is null)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        // Fully-qualified type names give unambiguous tag values when the same short
        // name appears in multiple assemblies. Static cached fields avoid per-call
        // typeof() reflection on the hot path.
        activity.SetTag("mediator.request_type", _requestTypeFull);
        activity.SetTag("mediator.response_type", _responseTypeFull);

        try
        {
            TResponse response = await next(cancellationToken).ConfigureAwait(false);
            activity.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (Exception ex)
        {
            // Set status and add an exception event before rethrowing so the span is
            // complete regardless of whether the caller inspects Activity.Current.
            // Activity.AddException (BCL, .NET 9+) follows OTel semantic conventions
            // and writes an event named "exception" with the standard attributes.
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.AddException(ex);
            throw;
        }
    }
}

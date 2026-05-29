namespace FastMediatR;

/// <summary>
/// Pipeline behavior that runs all <see cref="IRequestPreProcessor{TRequest}"/> instances
/// registered for <typeparamref name="TRequest"/> before invoking the next step in the pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// All pre-processors run sequentially in DI registration order. If a pre-processor throws,
/// execution stops and the exception propagates up through surrounding pipeline behaviors —
/// the handler is not invoked.
/// </para>
/// <para>
/// This behavior is auto-registered as an open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/>
/// by <c>AddFastMediatR&lt;TMarker&gt;</c> <b>after</b> all user-supplied <c>AddOpenBehavior</c>
/// calls, guaranteeing it lands innermost (closest to the handler) in the pipeline.
/// </para>
/// <para>
/// If no <see cref="IRequestPreProcessor{TRequest}"/> instances are registered for the request
/// type, this behavior is a transparent pass-through with no overhead.
/// </para>
/// </remarks>
public sealed class RequestPreProcessorBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Pre-processor instances resolved from DI for this request type.</summary>
    private readonly IEnumerable<IRequestPreProcessor<TRequest>> _processors;

    /// <summary>
    /// Initializes a new instance of <see cref="RequestPreProcessorBehavior{TRequest,TResponse}"/>
    /// with the pre-processors resolved from the DI container.
    /// </summary>
    /// <param name="processors">
    /// All <see cref="IRequestPreProcessor{TRequest}"/> implementations registered for
    /// <typeparamref name="TRequest"/>. An empty enumerable is valid (behavior becomes a no-op).
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="processors"/> is <see langword="null"/>.</exception>
    public RequestPreProcessorBehavior(IEnumerable<IRequestPreProcessor<TRequest>> processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        _processors = processors;
    }

    /// <summary>
    /// Runs all pre-processors sequentially then delegates to <paramref name="next"/>.
    /// </summary>
    /// <param name="request">The current request. Never <see langword="null"/>.</param>
    /// <param name="next">The next step in the pipeline.</param>
    /// <param name="cancellationToken">Token forwarded to each pre-processor and to <paramref name="next"/>.</param>
    /// <returns>The response produced by the inner pipeline.</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        foreach (IRequestPreProcessor<TRequest> processor in _processors)
        {
            await processor.Process(request, cancellationToken).ConfigureAwait(false);
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}

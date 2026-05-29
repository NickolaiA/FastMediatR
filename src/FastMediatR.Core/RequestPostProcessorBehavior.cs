namespace FastMediatR;

/// <summary>
/// Pipeline behavior that invokes the next step in the pipeline first, then runs all
/// <see cref="IRequestPostProcessor{TRequest,TResponse}"/> instances registered for
/// <typeparamref name="TRequest"/> and <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// All post-processors run sequentially in DI registration order after the handler completes
/// successfully. If the handler throws, post-processors are <b>not</b> invoked — the exception
/// propagates up through surrounding behaviors.
/// </para>
/// <para>
/// This behavior is auto-registered as an open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/>
/// by <c>AddFastMediatR&lt;TMarker&gt;</c> <b>after</b> all user-supplied <c>AddOpenBehavior</c>
/// calls and after <see cref="RequestPreProcessorBehavior{TRequest,TResponse}"/>, guaranteeing it
/// lands innermost in the pipeline.
/// </para>
/// <para>
/// If no <see cref="IRequestPostProcessor{TRequest,TResponse}"/> instances are registered for the
/// request type, this behavior is a transparent pass-through with no overhead.
/// </para>
/// </remarks>
public sealed class RequestPostProcessorBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Post-processor instances resolved from DI for this request/response pair.</summary>
    private readonly IEnumerable<IRequestPostProcessor<TRequest, TResponse>> _processors;

    /// <summary>
    /// Initializes a new instance of <see cref="RequestPostProcessorBehavior{TRequest,TResponse}"/>
    /// with the post-processors resolved from the DI container.
    /// </summary>
    /// <param name="processors">
    /// All <see cref="IRequestPostProcessor{TRequest,TResponse}"/> implementations registered for
    /// the current request/response pair. An empty enumerable is valid (behavior becomes a no-op).
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="processors"/> is <see langword="null"/>.</exception>
    public RequestPostProcessorBehavior(IEnumerable<IRequestPostProcessor<TRequest, TResponse>> processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        _processors = processors;
    }

    /// <summary>
    /// Invokes <paramref name="next"/>, captures the response, then runs all post-processors sequentially.
    /// </summary>
    /// <param name="request">The current request. Never <see langword="null"/>.</param>
    /// <param name="next">The next step in the pipeline.</param>
    /// <param name="cancellationToken">Token forwarded to each post-processor and to <paramref name="next"/>.</param>
    /// <returns>The response produced by the inner pipeline (passed through unchanged).</returns>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        TResponse response = await next(cancellationToken).ConfigureAwait(false);

        foreach (IRequestPostProcessor<TRequest, TResponse> processor in _processors)
        {
            await processor.Process(request, response, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }
}

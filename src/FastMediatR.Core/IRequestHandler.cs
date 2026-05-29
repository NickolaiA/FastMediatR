namespace FastMediatR;

/// <summary>
/// Handles a request of type <typeparamref name="TRequest"/> and produces a response
/// of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The concrete request type this handler processes.</typeparam>
/// <typeparam name="TResponse">The response type returned to the caller.</typeparam>
/// <remarks>
/// <para>
/// Exactly one handler must be registered per request type. Handler implementations are
/// resolved from the DI container with <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped"/>
/// lifetime by default; the lifetime can be overridden per-handler with
/// <see cref="HandlerLifetimeAttribute"/>.
/// </para>
/// </remarks>
public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Processes the request and returns the response.
    /// </summary>
    /// <param name="request">The request payload. Never <see langword="null"/>.</param>
    /// <param name="cancellationToken">
    /// Token observed by the handler to cooperatively cancel the operation.
    /// </param>
    /// <returns>A task that completes with the handler's response.</returns>
    Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a void-returning request of type <typeparamref name="TRequest"/>.
/// </summary>
/// <typeparam name="TRequest">The concrete <see cref="IRequest"/> type this handler processes.</typeparam>
/// <remarks>
/// <para>
/// This is a convenience interface for handlers of <see cref="IRequest"/> (void) commands.
/// It is a strict refinement of <see cref="IRequestHandler{TRequest, TResponse}"/> over
/// <see cref="Unit"/> so that a single dispatch and pipeline-behavior model covers both
/// the void and the value-returning cases. Implementations should return <see cref="Unit.Task"/>
/// to avoid allocating a fresh completed task on every call.
/// </para>
/// </remarks>
public interface IRequestHandler<in TRequest> : IRequestHandler<TRequest, Unit>
    where TRequest : IRequest<Unit>
{
}

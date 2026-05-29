namespace FastMediatR;

/// <summary>
/// Sends a request through the pipeline to its single registered handler.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ISender"/> is the narrower, command/query-only entry point. Prefer it over
/// <see cref="IMediator"/> when a class only needs to send requests, so the dependency
/// graph stays minimal.
/// </para>
/// </remarks>
public interface ISender
{
    /// <summary>
    /// Typed dispatch. Resolves <see cref="IRequestHandler{TRequest, TResponse}"/> for the
    /// runtime request type and executes all registered
    /// <see cref="IPipelineBehavior{TRequest, TResponse}"/> instances in registration order
    /// (first-registered = outermost).
    /// </summary>
    /// <typeparam name="TResponse">The response type declared by the request.</typeparam>
    /// <param name="request">The request to dispatch. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token propagated to every behavior and the handler.</param>
    /// <returns>A task completing with the handler's response.</returns>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Untyped dispatch. Resolves the handler dynamically and returns the response boxed
    /// as <see cref="object"/>. Useful when the request type is known only at runtime
    /// (e.g. payloads deserialised from a message bus).
    /// </summary>
    /// <param name="request">
    /// The request to dispatch. Must implement <see cref="IRequest{TResponse}"/> for
    /// some <c>TResponse</c>. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="cancellationToken">Token propagated to every behavior and the handler.</param>
    /// <returns>
    /// A task completing with the handler's response boxed as <see cref="object"/>.
    /// For void requests the value is <see cref="Unit.Value"/>.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">
    /// Thrown when <paramref name="request"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.ArgumentException">
    /// Thrown when <paramref name="request"/> does not implement
    /// <see cref="IRequest{TResponse}"/>.
    /// </exception>
    Task<object?> Send(object request, CancellationToken cancellationToken = default);
}

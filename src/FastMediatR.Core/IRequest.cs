namespace FastMediatR;

/// <summary>
/// Marker interface for a request that produces a response of type
/// <typeparamref name="TResponse"/> when dispatched through <see cref="ISender"/>.
/// </summary>
/// <typeparam name="TResponse">
/// The response type returned by the corresponding
/// <see cref="IRequestHandler{TRequest, TResponse}"/>.
/// </typeparam>
/// <remarks>
/// <para>
/// Exactly one handler must be registered per request type; the source generator emits
/// <c>FM001</c> if a request is sent with no handler and <c>FM002</c> if more than one
/// handler is registered for the same request.
/// </para>
/// </remarks>
public interface IRequest<out TResponse> : IBaseRequest
{
}

/// <summary>
/// Marker interface for a void-returning request (fire-and-execute command).
/// </summary>
/// <remarks>
/// <para>
/// Handlers return <see cref="System.Threading.Tasks.Task{TResult}"/> of <see cref="Unit"/>,
/// allowing pipeline behaviors to be expressed uniformly over a single
/// <see cref="IRequestHandler{TRequest, TResponse}"/> contract.
/// </para>
/// </remarks>
public interface IRequest : IRequest<Unit>
{
}

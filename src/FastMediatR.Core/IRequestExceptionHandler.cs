namespace FastMediatR;

/// <summary>
/// Handles an exception thrown while processing a request of type <typeparamref name="TRequest"/>,
/// optionally marking it handled and supplying a fallback <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TRequest">The request type whose handling threw the exception.</typeparam>
/// <typeparam name="TResponse">The response type the request would have produced.</typeparam>
/// <typeparam name="TException">The exception type this handler observes.</typeparam>
/// <remarks>
/// <para>
/// Exception handlers are resolved by the auto-registered
/// <c>RequestExceptionProcessorBehavior</c> and matched against the runtime exception type and
/// every base type up the inheritance chain (most-derived first). A handler that calls
/// <see cref="RequestExceptionHandlerState{TResponse}.SetHandled(TResponse)"/> stops further
/// matching and short-circuits the exception: the pipeline returns the supplied response instead
/// of rethrowing.
/// </para>
/// <para>
/// Multiple handlers may be registered for the same request/exception pair; they run in DI
/// registration order until one marks the exception handled.
/// </para>
/// <para>
/// If a handler <em>itself</em> throws, that fault propagates (unwrapped) instead of the request
/// exception, and the original exception is attached to the fault's <see cref="Exception.Data"/>
/// under the <c>"FastMediatR.OriginalException"</c> key so neither failure is lost.
/// </para>
/// </remarks>
public interface IRequestExceptionHandler<in TRequest, TResponse, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>
    /// Processes the thrown exception.
    /// </summary>
    /// <param name="request">The request that was being handled when the exception was thrown.</param>
    /// <param name="exception">The exception instance.</param>
    /// <param name="state">
    /// Carries the handled flag and fallback response. Call
    /// <see cref="RequestExceptionHandlerState{TResponse}.SetHandled(TResponse)"/> to suppress the
    /// exception and return a response from the pipeline.
    /// </param>
    /// <param name="cancellationToken">
    /// Token observed by the handler. Note this is the <em>request's</em> token and may already be
    /// cancelled when the observed failure is a cancellation; handlers that must run to completion
    /// should not observe it.
    /// </param>
    /// <returns>A task representing the asynchronous handler invocation.</returns>
    Task Handle(
        TRequest request,
        TException exception,
        RequestExceptionHandlerState<TResponse> state,
        CancellationToken cancellationToken);
}

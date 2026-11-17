namespace FastMediatR;

/// <summary>
/// Performs a side-effecting action (logging, metrics, etc.) when an exception of type
/// <typeparamref name="TException"/> is thrown while processing a request of type
/// <typeparamref name="TRequest"/>.
/// </summary>
/// <typeparam name="TRequest">The request type whose handling threw the exception.</typeparam>
/// <typeparam name="TException">The exception type this action observes.</typeparam>
/// <remarks>
/// <para>
/// Unlike <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/>, an action cannot
/// suppress the exception or supply a response — it only reacts to it. After all matching actions
/// run, the exception continues to propagate.
/// </para>
/// <para>
/// Actions are resolved by the auto-registered <c>RequestExceptionActionProcessorBehavior</c> and
/// matched against the runtime exception type and every base type up the inheritance chain. Whether
/// actions run for <em>all</em> exceptions or only <em>unhandled</em> ones is controlled by
/// <see cref="RequestExceptionActionProcessorStrategy"/>.
/// </para>
/// </remarks>
public interface IRequestExceptionAction<in TRequest, in TException>
    where TRequest : notnull
    where TException : Exception
{
    /// <summary>
    /// Executes the side-effecting action for the thrown exception.
    /// </summary>
    /// <param name="request">The request that was being handled when the exception was thrown.</param>
    /// <param name="exception">The exception instance.</param>
    /// <param name="cancellationToken">
    /// Always a non-cancellable token (<see cref="CancellationToken.None"/>), <em>not</em> the request's
    /// token: actions are guaranteed side-effect observers and must complete even when the observed
    /// failure is itself a cancellation of the request.
    /// </param>
    /// <returns>A task representing the asynchronous action invocation.</returns>
    Task Execute(TRequest request, TException exception, CancellationToken cancellationToken);
}

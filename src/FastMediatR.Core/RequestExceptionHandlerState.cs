namespace FastMediatR;

/// <summary>
/// Carries the outcome of exception handling for a single request: whether an
/// <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> handled the exception and,
/// if so, the fallback response to return from the pipeline.
/// </summary>
/// <typeparam name="TResponse">The response type the request would have produced.</typeparam>
public sealed class RequestExceptionHandlerState<TResponse>
{
    /// <summary>
    /// Gets a value indicating whether a handler has marked the exception handled via
    /// <see cref="SetHandled(TResponse)"/>.
    /// </summary>
    public bool Handled { get; private set; }

    /// <summary>
    /// Gets the fallback response supplied by the handler that marked the exception handled.
    /// Meaningful only when <see cref="Handled"/> is <see langword="true"/>.
    /// </summary>
    public TResponse? Response { get; private set; }

    /// <summary>
    /// Marks the exception handled and records the response to return from the pipeline instead of
    /// rethrowing.
    /// </summary>
    /// <param name="response">The fallback response to return to the caller.</param>
    public void SetHandled(TResponse response)
    {
        Handled = true;
        Response = response;
    }
}

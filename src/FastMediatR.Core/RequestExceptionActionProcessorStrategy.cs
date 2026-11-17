namespace FastMediatR;

/// <summary>
/// Controls when <see cref="IRequestExceptionAction{TRequest,TException}"/> instances run relative
/// to <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> processing.
/// </summary>
public enum RequestExceptionActionProcessorStrategy
{
    /// <summary>
    /// Actions run only when no exception handler marks the exception handled (the default).
    /// The action-processor behavior is placed <em>outside</em> the handler-processor behavior, so
    /// actions execute after handlers have declined to handle the exception.
    /// </summary>
    ApplyForUnhandledExceptions = 0,

    /// <summary>
    /// Actions always run when an exception is thrown, regardless of whether a handler later
    /// suppresses it. The action-processor behavior is placed <em>inside</em> the handler-processor
    /// behavior, so actions execute before handlers decide the outcome.
    /// </summary>
    ApplyForAllExceptions = 1,
}

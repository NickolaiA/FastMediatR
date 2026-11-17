using System.Diagnostics.CodeAnalysis;

namespace FastMediatR.Extensions.DI;

/// <summary>
/// Pipeline behavior that catches exceptions thrown by the inner pipeline and offers them to every
/// registered <see cref="IRequestExceptionHandler{TRequest,TResponse,TException}"/> matching the
/// runtime exception type (and its base types). If a handler marks the exception handled, the
/// pipeline returns the supplied fallback response instead of rethrowing.
/// </summary>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Auto-registered by <c>AddFastMediatR</c> between the user-supplied behaviors and the
/// pre/post-processor behaviors, so it wraps the handler and the pre/post processors. Handlers are
/// matched most-derived exception type first; the first handler to call
/// <see cref="RequestExceptionHandlerState{TResponse}.SetHandled(TResponse)"/> wins and stops the search.
/// </para>
/// <para>
/// When no handler is registered (or none handles the exception), the original exception is rethrown
/// with its stack trace preserved.
/// </para>
/// <para>
/// When an exception handler <em>itself</em> throws, that fault propagates unwrapped and the original
/// request exception is attached to it under the <c>"FastMediatR.OriginalException"</c> key of
/// <see cref="Exception.Data"/>, so the replaced exception remains discoverable.
/// </para>
/// </remarks>
public sealed class RequestExceptionProcessorBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>The DI provider used to resolve exception handlers for the runtime exception type.</summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="RequestExceptionProcessorBehavior{TRequest,TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The DI provider. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public RequestExceptionProcessorBehavior(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Exception handling is part of the reflection-based AddFastMediatR opt-in path, which is already annotated RequiresUnreferencedCode.")]
    [UnconditionalSuppressMessage(
        "AOT",
        "IL3050:RequiresDynamicCode",
        Justification = "Exception handling is part of the reflection-based AddFastMediatR opt-in path, which is already annotated RequiresDynamicCode.")]
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        try
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var state = new RequestExceptionHandlerState<TResponse>();

            bool handled;
            try
            {
                handled = await ExceptionPipeline
                    .TryHandleAsync(_serviceProvider, typeof(TRequest), request, exception, state, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception handlerFault) when (!ReferenceEquals(handlerFault, exception))
            {
                // The faulty handler is about to replace the request exception; keep the original
                // reachable via Data so the underlying failure is not lost silently.
                ExceptionPipeline.TryAttachOriginalException(handlerFault, exception);
                throw;
            }

            if (!handled)
            {
                throw;
            }

            return state.Response!;
        }
    }
}

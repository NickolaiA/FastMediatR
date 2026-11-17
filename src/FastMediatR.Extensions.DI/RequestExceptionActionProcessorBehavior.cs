using System.Diagnostics.CodeAnalysis;

namespace FastMediatR.Extensions.DI;

/// <summary>
/// Pipeline behavior that runs every registered <see cref="IRequestExceptionAction{TRequest,TException}"/>
/// matching the runtime exception type (and its base types) when the inner pipeline throws, then
/// rethrows the original exception. Actions are side-effects only — they cannot suppress the exception.
/// </summary>
/// <typeparam name="TRequest">The request type flowing through the pipeline.</typeparam>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Auto-registered by <c>AddFastMediatR</c>. Its position relative to
/// <see cref="RequestExceptionProcessorBehavior{TRequest,TResponse}"/> is controlled by
/// <see cref="RequestExceptionActionProcessorStrategy"/>:
/// <list type="bullet">
///   <item>
///     <description>
///     <see cref="RequestExceptionActionProcessorStrategy.ApplyForUnhandledExceptions"/> (default) —
///     placed outside the processor behavior, so actions run only when no handler suppresses the exception.
///     </description>
///   </item>
///   <item>
///     <description>
///     <see cref="RequestExceptionActionProcessorStrategy.ApplyForAllExceptions"/> — placed inside the
///     processor behavior, so actions always run when an exception is thrown.
///     </description>
///   </item>
/// </list>
/// </para>
/// <para>
/// Actions are invoked with a non-cancellable <see cref="CancellationToken"/>
/// (<see cref="CancellationToken.None"/>) regardless of the request token, guaranteeing that
/// side-effect telemetry completes even when the observed failure is itself a cancellation.
/// </para>
/// <para>
/// When an action <em>itself</em> throws, that fault propagates unwrapped and the original request
/// exception is attached to it under the <c>"FastMediatR.OriginalException"</c> key of
/// <see cref="Exception.Data"/>, so the replaced exception remains discoverable.
/// </para>
/// </remarks>
public sealed class RequestExceptionActionProcessorBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>The DI provider used to resolve exception actions for the runtime exception type.</summary>
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="RequestExceptionActionProcessorBehavior{TRequest,TResponse}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The DI provider. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public RequestExceptionActionProcessorBehavior(IServiceProvider serviceProvider)
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
            try
            {
                await ExceptionPipeline
                    .RunActionsAsync(_serviceProvider, typeof(TRequest), request, exception)
                    .ConfigureAwait(false);
            }
            catch (Exception actionFault) when (!ReferenceEquals(actionFault, exception))
            {
                // The faulty action is about to replace the request exception; keep the original
                // reachable via Data so the underlying failure is not lost silently.
                ExceptionPipeline.TryAttachOriginalException(actionFault, exception);
                throw;
            }

            throw;
        }
    }
}

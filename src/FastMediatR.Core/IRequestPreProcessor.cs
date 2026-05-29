namespace FastMediatR;

/// <summary>
/// Runs before the handler is invoked, as part of the auto-registered
/// <c>RequestPreProcessorBehavior</c> at the innermost-but-one position of the pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type this pre-processor observes.</typeparam>
/// <remarks>
/// <para>
/// All registered pre-processors for a request type run sequentially in DI registration
/// order before the handler executes. A pre-processor that throws aborts the pipeline:
/// the handler is not invoked and the exception propagates up through the surrounding
/// pipeline behaviors.
/// </para>
/// </remarks>
public interface IRequestPreProcessor<in TRequest>
    where TRequest : notnull
{
    /// <summary>
    /// Executes pre-handler logic for the given request.
    /// </summary>
    /// <param name="request">The request about to be handled. Never <see langword="null"/>.</param>
    /// <param name="cancellationToken">Token observed by the pre-processor.</param>
    /// <returns>A task representing the asynchronous pre-processor invocation.</returns>
    Task Process(TRequest request, CancellationToken cancellationToken);
}

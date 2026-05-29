namespace FastMediatR;

/// <summary>
/// Runs after the handler returns successfully, as part of the auto-registered
/// <c>RequestPostProcessorBehavior</c> at the innermost-but-one position of the pipeline.
/// </summary>
/// <typeparam name="TRequest">The request type this post-processor observes.</typeparam>
/// <typeparam name="TResponse">The response type produced by the handler.</typeparam>
/// <remarks>
/// <para>
/// All registered post-processors for a request type run sequentially in DI registration
/// order after the handler completes. If the handler throws, post-processors are NOT
/// invoked. A post-processor that throws masks the handler's successful response with
/// the post-processor's exception.
/// </para>
/// </remarks>
public interface IRequestPostProcessor<in TRequest, in TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Executes post-handler logic for the given request/response pair.
    /// </summary>
    /// <param name="request">The original request. Never <see langword="null"/>.</param>
    /// <param name="response">The response produced by the handler.</param>
    /// <param name="cancellationToken">Token observed by the post-processor.</param>
    /// <returns>A task representing the asynchronous post-processor invocation.</returns>
    Task Process(TRequest request, TResponse response, CancellationToken cancellationToken);
}

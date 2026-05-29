namespace FastMediatR;

/// <summary>
/// Wraps a request handler with cross-cutting logic (validation, logging, transactions, etc.).
/// </summary>
/// <typeparam name="TRequest">The request type this behavior applies to.</typeparam>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Behaviors are invoked in the order they are registered: the first-registered behavior
/// is the outermost, and the handler executes at the innermost position. A behavior MUST
/// invoke <c>next</c> at most once; calling it zero times short-circuits the
/// pipeline (e.g. validation failure) and calling it more than once is undefined behavior.
/// </para>
/// <para>
/// Behaviors are resolved per-request from the DI container with the same lifetime as the
/// handler (scoped by default) and must propagate the supplied
/// <see cref="System.Threading.CancellationToken"/> to <c>next</c>.
/// </para>
/// </remarks>
public interface IPipelineBehavior<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>
    /// Invokes the behavior around the inner pipeline step.
    /// </summary>
    /// <param name="request">The current request. Never <see langword="null"/>.</param>
    /// <param name="next">
    /// The next step in the pipeline (another behavior or the terminal handler).
    /// The implementation must forward the supplied cancellation token.
    /// </param>
    /// <param name="cancellationToken">Token observed by the behavior.</param>
    /// <returns>A task completing with the response produced by the inner pipeline.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "'next' matches the MediatR API surface; renaming would break drop-in compatibility.")]
    Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

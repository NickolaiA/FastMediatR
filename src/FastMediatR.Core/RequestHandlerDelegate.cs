namespace FastMediatR;

/// <summary>
/// Delegate representing the next step in a request pipeline.
/// </summary>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <param name="cancellationToken">
/// The cancellation token to forward to the next behavior or handler. Accepting the token
/// as a parameter (rather than closing over an outer one) matches the MediatR 12+ API and
/// prevents stale-token bugs when behaviors re-enter the pipeline with a derived token.
/// </param>
/// <returns>A task completing with the response from the inner pipeline step.</returns>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "'RequestHandlerDelegate' matches the MediatR API surface; renaming would break drop-in compatibility.")]
public delegate Task<TResponse> RequestHandlerDelegate<TResponse>(CancellationToken cancellationToken);

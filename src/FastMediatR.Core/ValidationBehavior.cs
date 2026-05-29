using FluentValidation;
using FluentValidation.Results;

namespace FastMediatR;

/// <summary>
/// Pipeline behavior that runs all <see cref="IValidator{TRequest}"/> instances registered
/// for <typeparamref name="TRequest"/> before the request reaches the handler.
/// </summary>
/// <typeparam name="TRequest">The request type to validate.</typeparam>
/// <typeparam name="TResponse">The response type produced by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// All validators run concurrently via <c>Task.WhenAll</c>. If any validator reports
/// failures, a <see cref="ValidationException"/> is thrown with all collected
/// <see cref="FluentValidation.Results.ValidationFailure"/> instances before
/// <c>next</c> is invoked — the handler never executes.
/// </para>
/// <para>
/// If no validators are registered for <typeparamref name="TRequest"/>, the behavior
/// is a transparent pass-through with no overhead.
/// </para>
/// <para>
/// Register validators alongside the behavior:
/// <code>
/// builder.Services.AddValidatorsFromAssemblyContaining&lt;Program&gt;();
/// builder.Services.AddFastMediatR&lt;Program&gt;(cfg =&gt;
///     cfg.AddOpenBehavior(typeof(ValidationBehavior&lt;,&gt;)));
/// </code>
/// </para>
/// </remarks>
public sealed class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Validator instances resolved from DI for this request type.</summary>
    private readonly IValidator<TRequest>[] _validators;

    /// <summary>
    /// Initializes a new instance of <see cref="ValidationBehavior{TRequest,TResponse}"/>
    /// with the validators resolved from the DI container.
    /// </summary>
    /// <param name="validators">
    /// All <see cref="IValidator{TRequest}"/> implementations registered for
    /// <typeparamref name="TRequest"/>. An empty enumerable is valid (behavior becomes a no-op).
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="validators"/> is <see langword="null"/>.</exception>
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        ArgumentNullException.ThrowIfNull(validators);
        _validators = validators as IValidator<TRequest>[] ?? validators.ToArray();
    }

    /// <summary>
    /// Validates the <paramref name="request"/> concurrently against all registered validators,
    /// then delegates to <paramref name="next"/> if validation succeeds.
    /// </summary>
    /// <param name="request">The current request. Never <see langword="null"/>.</param>
    /// <param name="next">The next step in the pipeline, invoked only when validation passes.</param>
    /// <param name="cancellationToken">Token forwarded to each validator and to <paramref name="next"/>.</param>
    /// <returns>The response produced by the inner pipeline.</returns>
    /// <exception cref="ValidationException">
    /// Thrown when one or more validators report failures. Contains all
    /// <see cref="FluentValidation.Results.ValidationFailure"/> instances from all validators.
    /// </exception>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (_validators.Length == 0)
        {
            return await next(cancellationToken).ConfigureAwait(false);
        }

        ValidationResult[] results = await Task
            .WhenAll(_validators.Select(v => v.ValidateAsync(new ValidationContext<TRequest>(request), cancellationToken)))
            .ConfigureAwait(false);

        var failures = results
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }
}

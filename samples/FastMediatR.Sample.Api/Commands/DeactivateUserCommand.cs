using FastMediatR;
using Microsoft.Extensions.DependencyInjection;

namespace FastMediatR.Sample.Api.Commands;

/// <summary>
/// Demonstrates the void-command form <see cref="IRequest"/> (returns <see cref="Unit"/>).
/// Use this shape when an action has side effects but no meaningful return value.
/// </summary>
/// <param name="UserId">The id of the user to deactivate.</param>
public sealed record DeactivateUserCommand(Guid UserId) : IRequest;

/// <summary>
/// Reference handler for <see cref="DeactivateUserCommand"/>.
/// Decorated with <see cref="HandlerLifetimeAttribute"/> to demonstrate how any handler can opt out
/// of the global default lifetime (Scoped) in favour of Transient.
/// </summary>
[HandlerLifetime(ServiceLifetime.Transient)]
public sealed class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand>
{
    /// <summary>Handles the command and returns the canonical <see cref="Unit.Value"/>.</summary>
    /// <param name="request">The command instance.</param>
    /// <param name="cancellationToken">Token observed by the handler.</param>
    /// <returns>A pre-completed task wrapping <see cref="Unit.Value"/>.</returns>
    public Task<Unit> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // No-op in the sample.
        return Unit.Task;
    }
}

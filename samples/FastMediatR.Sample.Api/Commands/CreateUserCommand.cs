using FastMediatR;

namespace FastMediatR.Sample.Api.Commands;

/// <summary>
/// Demonstrates <see cref="IRequest{TResponse}"/> — a command that returns a value (the new user's id).
/// Phase 1: this type illustrates the core abstraction only; dispatch is wired in Phase 3.
/// </summary>
/// <param name="Email">The new user's email address.</param>
/// <param name="DisplayName">The display name shown in the UI.</param>
public sealed record CreateUserCommand(string Email, string DisplayName) : IRequest<Guid>;

/// <summary>
/// Reference implementation of <see cref="IRequestHandler{TRequest, TResponse}"/> for the sample.
/// Returns a deterministic GUID derived from the email so the sample is reproducible.
/// </summary>
public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Guid>
{
    /// <summary>Handles the <see cref="CreateUserCommand"/>.</summary>
    /// <param name="request">The command instance carrying the payload.</param>
    /// <param name="cancellationToken">Token observed by the handler.</param>
    /// <returns>The id of the newly-created user.</returns>
    public Task<Guid> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        // Deterministic id keeps the sample idempotent.
        var id = new Guid(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(request.Email)).AsSpan(0, 16));

        return Task.FromResult(id);
    }
}

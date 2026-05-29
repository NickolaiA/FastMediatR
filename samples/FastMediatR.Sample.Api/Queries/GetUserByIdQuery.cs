using FastMediatR;

namespace FastMediatR.Sample.Api.Queries;

/// <summary>
/// Demonstrates a query — a request that returns data without side effects.
/// Same <see cref="IRequest{TResponse}"/> shape as a command: FastMediatR makes no
/// command/query distinction at the type level (intent is conveyed by naming convention).
/// </summary>
/// <param name="UserId">The id of the user to load.</param>
public sealed record GetUserByIdQuery(Guid UserId) : IRequest<UserDto?>;

/// <summary>Read-model returned by <see cref="GetUserByIdQuery"/>.</summary>
/// <param name="Id">The user id.</param>
/// <param name="Email">The user's email.</param>
/// <param name="DisplayName">The user's display name.</param>
public sealed record UserDto(Guid Id, string Email, string DisplayName);

/// <summary>Reference handler for <see cref="GetUserByIdQuery"/>.</summary>
public sealed class GetUserByIdQueryHandler : IRequestHandler<GetUserByIdQuery, UserDto?>
{
    /// <summary>Handles the query against the in-memory sample store.</summary>
    /// <param name="request">The query instance.</param>
    /// <param name="cancellationToken">Token observed by the handler.</param>
    /// <returns>The user, or <see langword="null"/> when not found.</returns>
    public Task<UserDto?> Handle(GetUserByIdQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        UserDto? dto = request.UserId == Guid.Empty
            ? null
            : new UserDto(request.UserId, "sample@example.com", "Sample User");

        return Task.FromResult(dto);
    }
}

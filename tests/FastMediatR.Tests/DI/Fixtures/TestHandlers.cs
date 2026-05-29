// Test handler and processor types used by scanner and DI tests.
// All types are concrete, non-generic, and implement the correct FastMediatR interfaces.

using Microsoft.Extensions.DependencyInjection;

namespace FastMediatR.Tests.DI.Fixtures;

// ── Request/Response types ──────────────────────────────────────────────────

/// <summary>Stub request used by scanner tests.</summary>
internal sealed record TestRequest(string Value) : IRequest<string>;

/// <summary>Stub void request used by scanner tests.</summary>
internal sealed record TestCommand : IRequest;

/// <summary>Stub notification used by scanner tests.</summary>
internal sealed record TestNotification(string Payload) : INotification;

// ── Handler types ───────────────────────────────────────────────────────────

/// <summary>Handler for <see cref="TestRequest"/>. Uses the default lifetime (Scoped by convention).</summary>
internal sealed class TestRequestHandler : IRequestHandler<TestRequest, string>
{
    public Task<string> Handle(TestRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Value);
}

/// <summary>Handler for <see cref="TestCommand"/>. Uses the default lifetime.</summary>
internal sealed class TestCommandHandler : IRequestHandler<TestCommand>
{
    public Task<Unit> Handle(TestCommand request, CancellationToken cancellationToken) =>
        Unit.Task;
}

/// <summary>Notification handler for <see cref="TestNotification"/>. Uses the default lifetime.</summary>
internal sealed class TestNotificationHandler : INotificationHandler<TestNotification>
{
    public Task Handle(TestNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>
/// Second notification handler for <see cref="TestNotification"/>.
/// Tests that multiple notification handlers are all registered independently.
/// </summary>
internal sealed class TestNotificationHandler2 : INotificationHandler<TestNotification>
{
    public Task Handle(TestNotification notification, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>Pre-processor for <see cref="TestRequest"/>. Uses the default lifetime.</summary>
internal sealed class TestRequestPreProcessor : IRequestPreProcessor<TestRequest>
{
    public Task Process(TestRequest request, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>Post-processor for <see cref="TestRequest"/>. Uses the default lifetime.</summary>
internal sealed class TestRequestPostProcessor : IRequestPostProcessor<TestRequest, string>
{
    public Task Process(TestRequest request, string response, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

// ── Lifetime override type ──────────────────────────────────────────────────

/// <summary>
/// Handler decorated with <see cref="HandlerLifetimeAttribute"/> (Singleton).
/// Tests that the scanner respects the attribute over the default lifetime.
/// </summary>
[HandlerLifetime(ServiceLifetime.Singleton)]
internal sealed class SingletonHandler : IRequestHandler<TestRequest, string>
{
    public Task<string> Handle(TestRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Value);
}

// ── Open-generic behavior (for AddOpenBehavior tests) ───────────────────────

/// <summary>
/// Open-generic no-op behavior used in <see cref="AddFastMediatRTests"/> to verify
/// that <c>FastMediatRConfig.AddOpenBehavior</c> is respected.
/// </summary>
internal sealed class NopBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => next(cancellationToken);
}

// ── Marker for AddFastMediatR<TMarker> round-trip tests ────────────────────

/// <summary>
/// Marker type used as the <c>TMarker</c> argument to <c>AddFastMediatR&lt;TMarker&gt;</c>
/// during round-trip tests. Its assembly is the test assembly which contains
/// <c>FastMediatR.Generated.FastMediatRGeneratedRegistration</c>.
/// </summary>
internal sealed class TestMarker;

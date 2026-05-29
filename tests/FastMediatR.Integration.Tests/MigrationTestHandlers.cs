// Migration compatibility test support types.
// These types live in the integration-test assembly.  The FastMediatR source generator
// discovers them at compile time and emits a FastMediatRDispatcher + FastMediatRGeneratedRegistration
// for this assembly.  MigrationCompatibilityTests then boots a standalone ServiceCollection
// (without WebApplicationFactory) by calling AddFastMediatR<MigrationTestMarker>(), which
// finds the generated registration and wires everything up via the generated dispatcher —
// proving that the library's public API is a drop-in replacement for MediatR's API surface.
//
// Before (MediatR):
//   using MediatR;
//   services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MigrationTestMarker>());
//
// After (FastMediatR — only the using + registration change):
//   using FastMediatR;
//   using FastMediatR.Extensions.DI;
//   services.AddFastMediatR<MigrationTestMarker>(cfg => cfg.RegisterHandlersFromAssemblyContaining<MigrationTestMarker>());

using FastMediatR;
using FluentValidation;

namespace FastMediatR.Integration.Tests.Migration;

// ── Marker type ──────────────────────────────────────────────────────────────

/// <summary>Assembly marker used by <c>AddFastMediatR&lt;MigrationTestMarker&gt;</c> to scope DI scanning.</summary>
public sealed class MigrationTestMarker;

// ── IRequest<TResponse> — command that returns a value ───────────────────────

/// <summary>Simple echo command — mirrors a typical MediatR <c>IRequest&lt;TResponse&gt;</c>.</summary>
/// <param name="Message">The message to echo back.</param>
public sealed record EchoCommand(string Message) : IRequest<string>;

/// <summary>Handler for <see cref="EchoCommand"/>.</summary>
public sealed class EchoCommandHandler : IRequestHandler<EchoCommand, string>
{
    /// <summary>Returns the message unchanged, demonstrating the happy-path.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The echoed message.</returns>
    public Task<string> Handle(EchoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(request.Message);
    }
}

// ── IRequest (void command) ───────────────────────────────────────────────────

/// <summary>Void command — mirrors a typical MediatR <c>IRequest</c> (returns <c>Unit</c>).</summary>
public sealed record VoidEchoCommand(string Message) : IRequest;

/// <summary>Handler for <see cref="VoidEchoCommand"/>.</summary>
public sealed class VoidEchoCommandHandler : IRequestHandler<VoidEchoCommand>
{
    /// <summary>No-op handler used to verify void-command dispatch.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed <see cref="Unit"/> task.</returns>
    public Task<Unit> Handle(VoidEchoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Unit.Task;
    }
}

// ── INotification — fan-out to multiple handlers ─────────────────────────────

/// <summary>Notification broadcast to multiple handlers — mirrors MediatR <c>INotification</c>.</summary>
/// <param name="Text">The notification payload.</param>
public sealed record EchoNotification(string Text) : INotification;

/// <summary>Tracks notification receipt for assertion purposes.</summary>
public static class NotificationTracker
{
    /// <summary>List of received notification payloads; cleared between tests via <see cref="Clear"/>.</summary>
    public static List<string> Received { get; } = [];

    /// <summary>Clears all tracked receipts.</summary>
    public static void Clear() => Received.Clear();
}

/// <summary>First handler for <see cref="EchoNotification"/>.</summary>
public sealed class EchoNotificationHandlerA : INotificationHandler<EchoNotification>
{
    /// <summary>Appends "A:{text}" to <see cref="NotificationTracker.Received"/>.</summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task Handle(EchoNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        NotificationTracker.Received.Add($"A:{notification.Text}");
        return Task.CompletedTask;
    }
}

/// <summary>Second handler for <see cref="EchoNotification"/> — demonstrates multiple handlers per notification.</summary>
public sealed class EchoNotificationHandlerB : INotificationHandler<EchoNotification>
{
    /// <summary>Appends "B:{text}" to <see cref="NotificationTracker.Received"/>.</summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task Handle(EchoNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        NotificationTracker.Received.Add($"B:{notification.Text}");
        return Task.CompletedTask;
    }
}

// ── Validator — FluentValidation compatibility ────────────────────────────────

/// <summary>FluentValidation validator for <see cref="EchoCommand"/> — exercises ValidationBehavior migration.</summary>
public sealed class EchoCommandValidator : AbstractValidator<EchoCommand>
{
    /// <summary>Requires <see cref="EchoCommand.Message"/> to be non-empty.</summary>
    public EchoCommandValidator()
    {
        RuleFor(c => c.Message)
            .NotEmpty()
            .WithMessage("Message must not be empty.");
    }
}

// ── Pipeline tracking behavior — verifies order is preserved after migration ──

/// <summary>
/// Test-only behavior that records its own name in <see cref="PipelineOrderTracker.Steps"/>
/// both before and after calling the next step.
/// Used by <see cref="MigrationCompatibilityTests"/> to assert that behavior execution
/// order is identical to what MediatR users expect.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
public sealed class TrackingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <summary>Invokes the next step and records entry/exit positions.</summary>
    /// <param name="request">The request.</param>
    /// <param name="next">The next pipeline step.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response from the inner pipeline.</returns>
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        PipelineOrderTracker.Steps.Add($"before:{typeof(TRequest).Name}");
        var response = await next(cancellationToken).ConfigureAwait(false);
        PipelineOrderTracker.Steps.Add($"after:{typeof(TRequest).Name}");
        return response;
    }
}

/// <summary>Shared step recorder used by <see cref="TrackingBehavior{TRequest,TResponse}"/>.</summary>
public static class PipelineOrderTracker
{
    /// <summary>Ordered list of steps recorded during pipeline execution.</summary>
    public static List<string> Steps { get; } = [];

    /// <summary>Clears all recorded steps.</summary>
    public static void Clear() => Steps.Clear();
}

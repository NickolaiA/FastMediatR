// AOT smoke test handlers.
// Types defined here are discovered by the FastMediatR source generator at compile time.
// The generator emits FastMediatRGeneratedRegistration for this assembly, which is
// then called directly from Program.cs to avoid the reflection-based AddFastMediatR<> path.

using FastMediatR;

namespace FastMediatR.Aot.SmokeTest;

// ── IRequest<TResponse> ───────────────────────────────────────────────────────

/// <summary>Simple smoke-test command that echoes the input with a "SMOKE:" prefix.</summary>
/// <param name="Input">The input string to echo.</param>
public sealed record SmokeTestCommand(string Input) : IRequest<string>;

/// <summary>Handler for <see cref="SmokeTestCommand"/>.</summary>
public sealed class SmokeTestCommandHandler : IRequestHandler<SmokeTestCommand, string>
{
    /// <summary>Returns "SMOKE:{Input}".</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The echoed string.</returns>
    public Task<string> Handle(SmokeTestCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult($"SMOKE:{request.Input}");
    }
}

// ── IRequest (void) ───────────────────────────────────────────────────────────

/// <summary>Void smoke-test command to verify <c>IRequest</c> (Unit) dispatch in AOT.</summary>
public sealed record SmokeTestVoidCommand(string Label) : IRequest;

/// <summary>Handler for <see cref="SmokeTestVoidCommand"/>.</summary>
public sealed class SmokeTestVoidCommandHandler : IRequestHandler<SmokeTestVoidCommand>
{
    /// <summary>No-op handler.</summary>
    /// <param name="request">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Unit.</returns>
    public Task<Unit> Handle(SmokeTestVoidCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Unit.Task;
    }
}

// ── INotification (fan-out) ───────────────────────────────────────────────────

/// <summary>Smoke-test notification to verify fan-out dispatch in AOT.</summary>
/// <param name="Message">The notification payload.</param>
public sealed record SmokeTestNotification(string Message) : INotification;

/// <summary>Tracks whether notification handlers were invoked.</summary>
public static class SmokeNotificationTracker
{
    /// <summary>Set to <see langword="true"/> by <see cref="SmokeTestNotificationHandlerA"/> on invocation.</summary>
    public static bool HandlerAInvoked { get; set; }

    /// <summary>Set to <see langword="true"/> by <see cref="SmokeTestNotificationHandlerB"/> on invocation.</summary>
    public static bool HandlerBInvoked { get; set; }
}

/// <summary>First notification handler — sets <see cref="SmokeNotificationTracker.HandlerAInvoked"/>.</summary>
public sealed class SmokeTestNotificationHandlerA : INotificationHandler<SmokeTestNotification>
{
    /// <summary>Marks handler A as invoked.</summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task Handle(SmokeTestNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        SmokeNotificationTracker.HandlerAInvoked = true;
        return Task.CompletedTask;
    }
}

/// <summary>Second notification handler — verifies fan-out by setting <see cref="SmokeNotificationTracker.HandlerBInvoked"/>.</summary>
public sealed class SmokeTestNotificationHandlerB : INotificationHandler<SmokeTestNotification>
{
    /// <summary>Marks handler B as invoked.</summary>
    /// <param name="notification">The notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A completed task.</returns>
    public Task Handle(SmokeTestNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();
        SmokeNotificationTracker.HandlerBInvoked = true;
        return Task.CompletedTask;
    }
}

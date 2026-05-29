using FastMediatR.Extensions.DI;
using FastMediatR.Integration.Tests.Migration;
using FluentAssertions;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FastMediatR.Integration.Tests;

/// <summary>
/// Demonstrates and verifies that FastMediatR is a drop-in replacement for MediatR for the
/// common API surface.  Each test boots a standalone <see cref="ServiceCollection"/> (no HTTP,
/// no WebApplicationFactory) and exercises the generated dispatcher directly.
///
/// The handlers and types used here are defined in <c>MigrationTestHandlers.cs</c> in the
/// <c>FastMediatR.Integration.Tests.Migration</c> namespace.  Because the FastMediatR source
/// generator runs on this project's compilation, it emits <c>FastMediatRGeneratedRegistration</c>
/// for those handler types, which <c>AddFastMediatR&lt;MigrationTestMarker&gt;()</c> discovers
/// at runtime — no reflection needed beyond the single startup-time lookup.
///
/// Migration recipe (only two changes from MediatR):
///   1. Replace <c>using MediatR;</c>  →  <c>using FastMediatR;</c>
///   2. Replace <c>services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining&lt;Marker&gt;())</c>
///      →  <c>services.AddFastMediatR&lt;Marker&gt;(cfg => cfg.RegisterHandlersFromAssemblyContaining&lt;Marker&gt;())</c>
/// </summary>
public sealed class MigrationCompatibilityTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a fresh <see cref="IServiceProvider"/> wired with <c>AddFastMediatR&lt;MigrationTestMarker&gt;</c>
    /// so every test starts with a clean container.
    /// </summary>
    /// <returns>A built service provider scoped to the migration test handlers.</returns>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

#pragma warning disable IL2026 // test assembly — AOT not required
        services.AddFastMediatR<MigrationTestMarker>(cfg =>
            cfg.RegisterHandlersFromAssemblyContaining<MigrationTestMarker>());
#pragma warning restore IL2026

        return services.BuildServiceProvider();
    }

    // ── IRequest<TResponse> ───────────────────────────────────────────────────

    /// <summary>
    /// <c>IMediator.Send&lt;TResponse&gt;(request, ct)</c> dispatches to <see cref="EchoCommandHandler"/>
    /// and returns the expected value — identical to the MediatR API.
    /// </summary>
    [Fact]
    public async Task Send_EchoCommand_ReturnsMirroredMessage()
    {
        // Arrange
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.Send(new EchoCommand("hello"), TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be("hello");
    }

    /// <summary>
    /// The untyped overload <c>IMediator.Send(object request, ct)</c> also works after migration.
    /// </summary>
    [Fact]
    public async Task Send_UntypedOverload_ReturnsMirroredMessage()
    {
        // Arrange
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        object request = new EchoCommand("world");

        // Act
        var result = await mediator.Send(request, TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be("world");
    }

    // ── IRequest (void) ───────────────────────────────────────────────────────

    /// <summary>
    /// A void command (<c>IRequest</c>) sends successfully and returns <see cref="Unit.Value"/>.
    /// </summary>
    [Fact]
    public async Task Send_VoidCommand_ReturnsUnit()
    {
        // Arrange
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var result = await mediator.Send(new VoidEchoCommand("nothing"), TestContext.Current.CancellationToken);

        // Assert
        result.Should().Be(Unit.Value);
    }

    // ── INotification (fan-out) ───────────────────────────────────────────────

    /// <summary>
    /// <c>IMediator.Publish(notification, ct)</c> fans out to all registered handlers —
    /// <see cref="EchoNotificationHandlerA"/> and <see cref="EchoNotificationHandlerB"/> both run.
    /// </summary>
    [Fact]
    public async Task Publish_NotificationWithTwoHandlers_BothHandlersRun()
    {
        // Arrange
        NotificationTracker.Clear();
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.Publish(new EchoNotification("ping"), TestContext.Current.CancellationToken);

        // Assert
        NotificationTracker.Received.Should().HaveCount(2);
        NotificationTracker.Received.Should().Contain("A:ping");
        NotificationTracker.Received.Should().Contain("B:ping");
    }

    // ── ISender / IPublisher interface segregation ────────────────────────────

    /// <summary>
    /// The resolved <see cref="IMediator"/> also satisfies <see cref="ISender"/> and
    /// <see cref="IPublisher"/> — the same interface segregation as MediatR.
    /// </summary>
    [Fact]
    public async Task ResolvedMediator_ImplementsISenderAndIPublisher()
    {
        // Arrange
        await using var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act — use via ISender
        var sender = (ISender)mediator;
        var senderResult = await sender.Send(new EchoCommand("via ISender"), TestContext.Current.CancellationToken);

        // Act — use via IPublisher
        NotificationTracker.Clear();
        var publisher = (IPublisher)mediator;
        await publisher.Publish(new EchoNotification("via IPublisher"), TestContext.Current.CancellationToken);

        // Assert
        senderResult.Should().Be("via ISender");
        NotificationTracker.Received.Should().Contain("A:via IPublisher");
    }

    // ── ValidationBehavior compatibility ─────────────────────────────────────

    /// <summary>
    /// When <see cref="EchoCommandValidator"/> is registered and <c>ValidationBehavior&lt;,&gt;</c>
    /// is added as an open behavior, an invalid request throws <see cref="ValidationException"/> —
    /// matching the behavior MediatR users expect from FluentValidation pipeline integration.
    /// </summary>
    [Fact]
    public async Task Send_InvalidCommand_ValidationBehaviorThrowsValidationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<EchoCommandValidator>();
        services.AddSingleton<IValidator<EchoCommand>>(sp => sp.GetRequiredService<EchoCommandValidator>());

#pragma warning disable IL2026
        services.AddFastMediatR<MigrationTestMarker>(cfg =>
            cfg.RegisterHandlersFromAssemblyContaining<MigrationTestMarker>()
               .AddOpenBehavior(typeof(ValidationBehavior<,>)));
#pragma warning restore IL2026

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var act = async () => await mediator.Send(new EchoCommand(""), TestContext.Current.CancellationToken);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Message must not be empty*");
    }

    // ── Pipeline behavior ordering ────────────────────────────────────────────

    /// <summary>
    /// Behaviors registered via <c>AddOpenBehavior</c> wrap the handler in outermost-first order,
    /// producing the same before/after execution pattern MediatR users expect.
    /// </summary>
    [Fact]
    public async Task Send_WithTrackingBehavior_RecordsBeforeAndAfterSteps()
    {
        // Arrange
        PipelineOrderTracker.Clear();
        var services = new ServiceCollection();

#pragma warning disable IL2026
        services.AddFastMediatR<MigrationTestMarker>(cfg =>
            cfg.RegisterHandlersFromAssemblyContaining<MigrationTestMarker>()
               .AddOpenBehavior(typeof(TrackingBehavior<,>)));
#pragma warning restore IL2026

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        await mediator.Send(new EchoCommand("ordered"), TestContext.Current.CancellationToken);

        // Assert
        PipelineOrderTracker.Steps.Should().ContainInOrder(
            "before:EchoCommand",
            "after:EchoCommand");
    }
}

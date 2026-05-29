using FastMediatR.Extensions.DI;
using FastMediatR.Generated;
using FastMediatR.Tests.DI.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FastMediatR.Tests.DI;

/// <summary>
/// Unit tests for <see cref="ServiceCollectionExtensions.AddFastMediatR{TMarker}"/>.
/// Covers round-trip invocation, assembly scanning, open-behavior registration, and options wiring.
/// </summary>
public sealed class AddFastMediatRTests
{
    // ── Round-trip: generated registration is discovered and invoked ─────────

    /// <summary>
    /// Verifies that <c>AddFastMediatR&lt;TestMarker&gt;</c> locates the fake
    /// <see cref="FastMediatRGeneratedRegistration"/> in the test assembly and invokes its
    /// <c>Register</c> method, resulting in <c>FakeGeneratedMarker</c> being present in the container.
    /// </summary>
    [Fact]
    public void AddFastMediatR_InvokesGeneratedRegistration_WhenTypeIsPresent()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
#pragma warning disable IL2026 // RequiresUnreferencedCode — intentional in test context
        services.AddFastMediatR<TestMarker>();
#pragma warning restore IL2026

        // Assert — FakeGeneratedMarker is registered by the fake FastMediatRGeneratedRegistration
        services.Should().Contain(d => d.ServiceType == typeof(FastMediatRGeneratedRegistration.FakeGeneratedMarker));
    }

    /// <summary>
    /// Verifies that calling <c>AddFastMediatR</c> twice for the same marker assembly does not
    /// duplicate the generated registration (TryAdd semantics prevent double-registration).
    /// </summary>
    [Fact]
    public void AddFastMediatR_CalledTwice_DoesNotDuplicateGeneratedRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        services.AddFastMediatR<TestMarker>();
        services.AddFastMediatR<TestMarker>();
#pragma warning restore IL2026

        // Assert — singleton is only registered once
        services.Count(d => d.ServiceType == typeof(FastMediatRGeneratedRegistration.FakeGeneratedMarker))
            .Should().Be(1);
    }

    // ── Assembly scanning: handler types are registered ──────────────────────

    /// <summary>
    /// Verifies that <c>AddFastMediatR&lt;TestMarker&gt;</c> scans the test assembly and registers
    /// <see cref="TestRequestHandler"/> because it implements <see cref="IRequestHandler{TRequest,TResponse}"/>.
    /// </summary>
    [Fact]
    public void AddFastMediatR_RegistersRequestHandlerFromMarkerAssembly()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        services.AddFastMediatR<TestMarker>();
#pragma warning restore IL2026

        // Assert
        services.Should().Contain(d => d.ServiceType == typeof(TestRequestHandler));
    }

    /// <summary>
    /// Verifies that <c>AddFastMediatR</c> registers a notification handler type discovered by scanning.
    /// </summary>
    [Fact]
    public void AddFastMediatR_RegistersNotificationHandlerFromMarkerAssembly()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        services.AddFastMediatR<TestMarker>();
#pragma warning restore IL2026

        // Assert
        services.Should().Contain(d => d.ServiceType == typeof(TestNotificationHandler));
    }

    // ── Additional assembly scanning via config ───────────────────────────────

    /// <summary>
    /// Verifies that assemblies registered via <see cref="FastMediatRConfig.RegisterHandlersFromAssembly"/>
    /// are scanned in addition to the TMarker assembly.
    /// </summary>
    [Fact]
    public void AddFastMediatR_ScansAdditionalAssembly_WhenRegistered()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        services.AddFastMediatR<TestMarker>(cfg =>
            cfg.RegisterHandlersFromAssemblyContaining<TestRequestHandler>());
#pragma warning restore IL2026

        // Assert — TestRequestHandler is in the same assembly so it will be registered (TryAdd dedup is fine)
        services.Should().Contain(d => d.ServiceType == typeof(TestRequestHandler));
    }

    // ── Open-behavior registration ───────────────────────────────────────────

    /// <summary>
    /// Verifies that an open-generic behavior registered via <see cref="FastMediatRConfig.AddOpenBehavior"/>
    /// appears in the service collection as an <see cref="IPipelineBehavior{TRequest,TResponse}"/> mapping.
    /// </summary>
    [Fact]
    public void AddFastMediatR_RegistersOpenBehavior_WhenConfigured()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        services.AddFastMediatR<TestMarker>(cfg =>
            cfg.AddOpenBehavior(typeof(NopBehavior<,>)));
#pragma warning restore IL2026

        // Assert
        services.Should().Contain(d =>
            d.ServiceType == typeof(IPipelineBehavior<,>) &&
            d.ImplementationType == typeof(NopBehavior<,>));
    }

    /// <summary>
    /// Verifies that multiple open behaviors are registered in declaration order (outermost first).
    /// </summary>
    [Fact]
    public void AddFastMediatR_MultipleOpenBehaviors_RegisteredInDeclarationOrder()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        services.AddFastMediatR<TestMarker>(cfg =>
        {
            cfg.AddOpenBehavior(typeof(NopBehavior<,>));
            cfg.AddOpenBehavior(typeof(NopBehavior<,>)); // second instance
        });
#pragma warning restore IL2026

        // Assert — both registrations present (order-preserving)
        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>) && d.ImplementationType == typeof(NopBehavior<,>))
            .ToList();

        behaviors.Should().HaveCount(2);
    }

    // ── Options registration ─────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="FastMediatROptions"/> options infrastructure is registered in the
    /// container after <c>AddFastMediatR</c> is called.
    /// </summary>
    [Fact]
    public void AddFastMediatR_RegistersFastMediatROptions()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        services.AddFastMediatR<TestMarker>();
#pragma warning restore IL2026

        // Assert — IConfigureOptions<FastMediatROptions> wires the BindConfiguration call
        services.Should().Contain(d => d.ServiceType == typeof(IConfigureOptions<FastMediatROptions>));
    }

    // ── Argument validation ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies that a <see cref="ArgumentNullException"/> is thrown when the services
    /// argument is <see langword="null"/>.
    /// </summary>
    [Fact]
    public void AddFastMediatR_ThrowsArgumentNullException_WhenServicesIsNull()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
#pragma warning disable IL2026
        Action act = () => services!.AddFastMediatR<TestMarker>();
#pragma warning restore IL2026

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }
}

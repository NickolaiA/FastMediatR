using System.Diagnostics.CodeAnalysis;
using FastMediatR.Extensions.DI;
using FastMediatR.Tests.DI.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FastMediatR.Tests.DI;

/// <summary>
/// Unit tests for <see cref="AssemblyScanner"/>.
/// Covers handler discovery, notification handler fan-out, pre/post processor interface mapping,
/// and <see cref="HandlerLifetimeAttribute"/> override.
/// </summary>
public sealed class AssemblyScannerTests
{
    // ── Request handler discovery ────────────────────────────────────────────

    /// <summary>
    /// Verifies that a class implementing <see cref="IRequestHandler{TRequest,TResponse}"/> is
    /// registered as a scoped service (default lifetime) by the scanner.
    /// </summary>
    [Fact]
    public void Scan_RegistersRequestHandler_WithDefaultScopedLifetime()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
#pragma warning disable IL2026
        AssemblyScanner.Scan(services, typeof(TestRequestHandler).Assembly, ServiceLifetime.Scoped);
#pragma warning restore IL2026

        // Assert
        ServiceDescriptor? descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TestRequestHandler));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    /// <summary>
    /// Verifies that a class implementing <see cref="IRequestHandler{TRequest}"/> (void command) is
    /// discovered and registered correctly.
    /// </summary>
    [Fact]
    public void Scan_RegistersVoidCommandHandler()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        AssemblyScanner.Scan(services, typeof(TestCommandHandler).Assembly, ServiceLifetime.Scoped);
#pragma warning restore IL2026

        // Assert
        services.Should().Contain(d => d.ServiceType == typeof(TestCommandHandler));
    }

    // ── Notification handler discovery ───────────────────────────────────────

    /// <summary>
    /// Verifies that both <see cref="TestNotificationHandler"/> and <see cref="TestNotificationHandler2"/>
    /// are registered independently, allowing fan-out publish.
    /// </summary>
    [Fact]
    public void Scan_RegistersMultipleNotificationHandlers_Independently()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        AssemblyScanner.Scan(services, typeof(TestNotificationHandler).Assembly, ServiceLifetime.Scoped);
#pragma warning restore IL2026

        // Assert
        services.Should().Contain(d => d.ServiceType == typeof(TestNotificationHandler));
        services.Should().Contain(d => d.ServiceType == typeof(TestNotificationHandler2));
    }

    // ── Pre/post processor discovery and interface mapping ───────────────────

    /// <summary>
    /// Verifies that a <see cref="IRequestPreProcessor{TRequest}"/> implementation is registered
    /// both as its concrete type and as the closed <see cref="IRequestPreProcessor{TRequest}"/> interface.
    /// </summary>
    [Fact]
    public void Scan_RegistersPreProcessor_AsBothConcreteAndInterface()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        AssemblyScanner.Scan(services, typeof(TestRequestPreProcessor).Assembly, ServiceLifetime.Scoped);
#pragma warning restore IL2026

        // Assert — concrete type registered
        services.Should().Contain(d => d.ServiceType == typeof(TestRequestPreProcessor));

        // Assert — interface mapping registered for pipeline behavior resolution
        services.Should().Contain(d => d.ServiceType == typeof(IRequestPreProcessor<TestRequest>));
    }

    /// <summary>
    /// Verifies that a <see cref="IRequestPostProcessor{TRequest,TResponse}"/> implementation is
    /// registered both as its concrete type and as the closed interface.
    /// </summary>
    [Fact]
    public void Scan_RegistersPostProcessor_AsBothConcreteAndInterface()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        AssemblyScanner.Scan(services, typeof(TestRequestPostProcessor).Assembly, ServiceLifetime.Scoped);
#pragma warning restore IL2026

        // Assert — concrete type registered
        services.Should().Contain(d => d.ServiceType == typeof(TestRequestPostProcessor));

        // Assert — interface mapping registered
        services.Should().Contain(d => d.ServiceType == typeof(IRequestPostProcessor<TestRequest, string>));
    }

    // ── HandlerLifetimeAttribute override ────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="HandlerLifetimeAttribute"/> on a handler type overrides the
    /// caller-supplied default lifetime argument.
    /// </summary>
    [Fact]
    public void Scan_RespectsHandlerLifetimeAttribute_OverridingDefaultLifetime()
    {
        // Arrange — default is Scoped but SingletonHandler is decorated with [HandlerLifetime(Singleton)]
        var services = new ServiceCollection();

#pragma warning disable IL2026
        AssemblyScanner.Scan(services, typeof(SingletonHandler).Assembly, ServiceLifetime.Scoped);
#pragma warning restore IL2026

        // Assert
        ServiceDescriptor? descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(SingletonHandler));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    // ── Abstract / interface / open-generic types are excluded ───────────────

    /// <summary>
    /// Verifies that abstract classes and interfaces are NOT registered by the scanner.
    /// </summary>
    [Fact]
    public void Scan_DoesNotRegister_AbstractOrInterfaceTypes()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        AssemblyScanner.Scan(services, typeof(IRequestHandler<,>).Assembly, ServiceLifetime.Scoped);
#pragma warning restore IL2026

        // Assert — the interface itself must not appear as a service type for its own concrete registration
        services.Should().NotContain(d => d.ServiceType == typeof(IRequestHandler<,>) && d.ImplementationType == typeof(IRequestHandler<,>));
    }

    // ── Transient lifetime ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies that passing <see cref="ServiceLifetime.Transient"/> as the default results in
    /// transient handler registrations (when no attribute is present).
    /// </summary>
    [Fact]
    public void Scan_UsesTransientLifetime_WhenDefaultLifetimeIsTransient()
    {
        // Arrange
        var services = new ServiceCollection();

#pragma warning disable IL2026
        AssemblyScanner.Scan(services, typeof(TestNotificationHandler).Assembly, ServiceLifetime.Transient);
#pragma warning restore IL2026

        // Assert — TestNotificationHandler has no attribute so transient applies
        ServiceDescriptor? descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TestNotificationHandler));
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Transient);
    }
}



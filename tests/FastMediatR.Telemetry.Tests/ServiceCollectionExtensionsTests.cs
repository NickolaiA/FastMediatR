using FastMediatR;
using FastMediatR.Telemetry;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FastMediatR.Telemetry.Tests;

/// <summary>
/// Unit tests for <see cref="ServiceCollectionExtensions.AddFastMediatRTelemetry"/>.
/// </summary>
public sealed class ServiceCollectionExtensionsTests
{
    /// <summary>
    /// Verifies that <see cref="ServiceCollectionExtensions.AddFastMediatRTelemetry"/>
    /// throws <see cref="ArgumentNullException"/> when the services collection is
    /// <see langword="null"/>.
    /// </summary>
    [Fact]
    public void AddFastMediatRTelemetry_NullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddFastMediatRTelemetry();

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    /// <summary>
    /// Verifies that <see cref="ServiceCollectionExtensions.AddFastMediatRTelemetry"/>
    /// registers <see cref="TracingBehavior{TRequest,TResponse}"/> as a singleton
    /// open-generic <see cref="IPipelineBehavior{TRequest,TResponse}"/>.
    /// </summary>
    [Fact]
    public void AddFastMediatRTelemetry_RegistersTracingBehaviorAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFastMediatRTelemetry();

        // Assert
        services.Should().ContainSingle(sd =>
            sd.ServiceType == typeof(IPipelineBehavior<,>) &&
            sd.ImplementationType == typeof(TracingBehavior<,>) &&
            sd.Lifetime == ServiceLifetime.Singleton);
    }

    /// <summary>
    /// Verifies that <see cref="ServiceCollectionExtensions.AddFastMediatRTelemetry"/>
    /// returns the same <see cref="IServiceCollection"/> instance for fluent chaining.
    /// </summary>
    [Fact]
    public void AddFastMediatRTelemetry_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var returned = services.AddFastMediatRTelemetry();

        // Assert
        returned.Should().BeSameAs(services);
    }
}

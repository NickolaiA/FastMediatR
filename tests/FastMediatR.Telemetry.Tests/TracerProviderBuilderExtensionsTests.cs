using FastMediatR.Telemetry;
using FluentAssertions;
using OpenTelemetry.Trace;
using Xunit;

namespace FastMediatR.Telemetry.Tests;

/// <summary>
/// Unit tests for <see cref="TracerProviderBuilderExtensions.AddFastMediatRInstrumentation"/>.
/// </summary>
public sealed class TracerProviderBuilderExtensionsTests
{
    /// <summary>
    /// Verifies that <see cref="TracerProviderBuilderExtensions.AddFastMediatRInstrumentation"/>
    /// throws <see cref="ArgumentNullException"/> when the builder is <see langword="null"/>.
    /// </summary>
    [Fact]
    public void AddFastMediatRInstrumentation_NullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        TracerProviderBuilder? builder = null;

        // Act
        var act = () => builder!.AddFastMediatRInstrumentation();

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }
}

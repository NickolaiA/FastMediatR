using FastMediatR;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FastMediatR.Tests.Core;

/// <summary>
/// Unit tests for <see cref="HandlerLifetimeAttribute"/>.
/// Verifies the attribute is single-use, sealed, class-only, and stores the supplied lifetime.
/// </summary>
public sealed class HandlerLifetimeAttributeTests
{
    /// <summary>The lifetime supplied at construction must round-trip through the property.</summary>
    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public void Lifetime_RoundTrips(ServiceLifetime lifetime)
    {
        new HandlerLifetimeAttribute(lifetime).Lifetime.Should().Be(lifetime);
    }

    /// <summary>Attribute must only target classes, not allow multiple, and not inherit.</summary>
    [Fact]
    public void AttributeUsage_Is_Class_Only_And_Single_Use()
    {
        var usage = (AttributeUsageAttribute[])typeof(HandlerLifetimeAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), inherit: false);

        usage.Should().ContainSingle();
        usage[0].ValidOn.Should().Be(AttributeTargets.Class);
        usage[0].AllowMultiple.Should().BeFalse();
        usage[0].Inherited.Should().BeFalse();
    }

    /// <summary>The attribute must be sealed (no subclass can change behavior).</summary>
    [Fact]
    public void Attribute_Is_Sealed()
    {
        typeof(HandlerLifetimeAttribute).IsSealed.Should().BeTrue();
    }
}

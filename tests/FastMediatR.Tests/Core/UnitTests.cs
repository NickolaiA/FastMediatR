using FastMediatR;
using FluentAssertions;
using Xunit;

namespace FastMediatR.Tests.Core;

/// <summary>
/// Unit tests for <see cref="Unit"/>.
/// Covers task 1.T from the Phase 1 task breakdown: equality / ToString / Value behavior
/// of the void-return marker type.
/// </summary>
public sealed class UnitTests
{
    /// <summary>Two default <see cref="Unit"/> values must compare equal.</summary>
    [Fact]
    public void Two_Default_Units_Are_Equal()
    {
        var a = default(Unit);
        var b = default(Unit);

        a.Equals(b).Should().BeTrue();
        (a == b).Should().BeTrue();
        (a != b).Should().BeFalse();
    }

    /// <summary><see cref="Unit.Value"/> must equal <see langword="default"/>(<see cref="Unit"/>).</summary>
    [Fact]
    public void Value_Equals_Default()
    {
        Unit.Value.Should().Be(default(Unit));
    }

    /// <summary>Hash codes must be stable across instances.</summary>
    [Fact]
    public void GetHashCode_Is_Stable_Across_Instances()
    {
        Unit.Value.GetHashCode().Should().Be(default(Unit).GetHashCode());
    }

    /// <summary><see cref="Unit.ToString"/> must return the canonical "()" literal.</summary>
    [Fact]
    public void ToString_Returns_Canonical_Literal()
    {
        Unit.Value.ToString().Should().Be("()");
    }

    /// <summary><see cref="Unit"/> must be a <c>readonly record struct</c> (zero-allocation void marker).</summary>
    [Fact]
    public void Unit_Is_Readonly_Record_Struct()
    {
        var type = typeof(Unit);

        type.IsValueType.Should().BeTrue();
        // record struct ⇒ compiler-generated EqualityContract + Equals(Unit) + op_Equality/op_Inequality
        type.GetMethod("op_Equality").Should().NotBeNull();
        type.GetMethod("op_Inequality").Should().NotBeNull();
    }
}

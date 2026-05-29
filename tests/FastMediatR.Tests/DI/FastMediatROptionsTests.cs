using System.ComponentModel.DataAnnotations;
using FastMediatR;
using FluentAssertions;
using Xunit;

namespace FastMediatR.Tests.DI;

/// <summary>
/// Unit tests for <see cref="FastMediatROptions"/>.
/// Covers default values, validation rules, and the expected section name constant.
/// </summary>
public sealed class FastMediatROptionsTests
{
    // ── Default values ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the default <see cref="FastMediatROptions"/> instance has all expected default values.
    /// </summary>
    [Fact]
    public void FastMediatROptions_DefaultInstance_HasExpectedDefaults()
    {
        // Arrange & Act
        var options = new FastMediatROptions();

        // Assert
        options.PerformanceThresholdMs.Should().Be(500);
        options.EnablePayloadLogging.Should().BeFalse();
        options.EnableLogging.Should().BeTrue();
    }

    // ── SectionName constant ─────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="FastMediatROptions.SectionName"/> is the expected constant value
    /// used to bind the section from <c>IConfiguration</c>.
    /// </summary>
    [Fact]
    public void FastMediatROptions_SectionName_IsExpectedValue()
    {
        // Assert
        FastMediatROptions.SectionName.Should().Be("FastMediatR");
    }

    // ── Validation: PerformanceThresholdMs ───────────────────────────────────

    /// <summary>
    /// Verifies that <c>PerformanceThresholdMs = 0</c> fails validation because the
    /// <c>[Range(1, int.MaxValue)]</c> attribute requires a positive integer.
    /// </summary>
    [Fact]
    public void FastMediatROptions_PerformanceThresholdMs_Zero_FailsValidation()
    {
        // Arrange
        var options = new FastMediatROptions { PerformanceThresholdMs = 0 };
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);

        // Act
        bool isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
        results.Should().ContainSingle()
            .Which.MemberNames.Should().Contain(nameof(FastMediatROptions.PerformanceThresholdMs));
    }

    /// <summary>
    /// Verifies that a negative <c>PerformanceThresholdMs</c> also fails validation.
    /// </summary>
    [Fact]
    public void FastMediatROptions_PerformanceThresholdMs_Negative_FailsValidation()
    {
        // Arrange
        var options = new FastMediatROptions { PerformanceThresholdMs = -1 };
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);

        // Act
        bool isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <c>PerformanceThresholdMs = 1</c> (the minimum allowed value) passes validation.
    /// </summary>
    [Fact]
    public void FastMediatROptions_PerformanceThresholdMs_One_PassesValidation()
    {
        // Arrange
        var options = new FastMediatROptions { PerformanceThresholdMs = 1 };
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);

        // Act
        bool isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
        results.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the default <c>PerformanceThresholdMs = 500</c> passes validation.
    /// </summary>
    [Fact]
    public void FastMediatROptions_DefaultValue_PassesValidation()
    {
        // Arrange
        var options = new FastMediatROptions();
        var results = new List<ValidationResult>();
        var context = new ValidationContext(options);

        // Act
        bool isValid = Validator.TryValidateObject(options, context, results, validateAllProperties: true);

        // Assert
        isValid.Should().BeTrue();
    }
}

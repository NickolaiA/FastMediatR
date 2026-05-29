using FastMediatR;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Xunit;

namespace FastMediatR.Tests.Behaviors;

// ── Shared test types ─────────────────────────────────────────────────────────

/// <summary>Minimal request used across validation behavior tests.</summary>
internal sealed record ValidationTestRequest(string Value) : IRequest<string>;

/// <summary>
/// Concrete validator that always passes (no rules configured).
/// </summary>
internal sealed class AlwaysPassValidator : AbstractValidator<ValidationTestRequest>
{
    /// <summary>Initialises a validator with no rules — always passes.</summary>
    public AlwaysPassValidator() { }
}

/// <summary>
/// Concrete validator that always fails with one configurable failure.
/// </summary>
internal sealed class AlwaysFailValidator : AbstractValidator<ValidationTestRequest>
{
    /// <summary>Initialises a validator that reports a single failure.</summary>
    /// <param name="propertyName">Property name for the failure.</param>
    /// <param name="errorMessage">Error message text.</param>
    public AlwaysFailValidator(string propertyName, string errorMessage)
    {
        RuleFor(r => r.Value)
            .Must(_ => false).WithName(propertyName).WithMessage(errorMessage);
    }
}

/// <summary>
/// Concrete validator that captures the <see cref="CancellationToken"/> passed to
/// <c>ValidateAsync</c> so cancellation-token forwarding can be verified.
/// </summary>
internal sealed class CapturingValidator : AbstractValidator<ValidationTestRequest>
{
    /// <summary>The last <see cref="CancellationToken"/> passed to validation.</summary>
    public CancellationToken CapturedToken { get; private set; }

    /// <summary>Validates asynchronously while recording the cancellation token.</summary>
    public override Task<ValidationResult> ValidateAsync(
        ValidationContext<ValidationTestRequest> context,
        CancellationToken cancellation = default)
    {
        CapturedToken = cancellation;
        return base.ValidateAsync(context, cancellation);
    }
}

/// <summary>
/// Unit tests for <see cref="ValidationBehavior{TRequest,TResponse}"/>.
/// Verifies pass-through with no validators, success with passing validators, failure
/// aggregation from multiple validators, and cancellation token forwarding.
/// </summary>
public sealed class ValidationBehaviorTests
{
    // ── Happy-path ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when no validators are registered the behavior transparently delegates
    /// to <c>next</c> without any validation cost.
    /// </summary>
    [Fact]
    public async Task Handle_WithNoValidators_PassesThrough()
    {
        // Arrange
        RequestHandlerDelegate<string> next = _ => Task.FromResult("pass");
        var sut = new ValidationBehavior<ValidationTestRequest, string>([]);

        // Act
        string result = await sut.Handle(new ValidationTestRequest("x"), next, CancellationToken.None);

        // Assert
        result.Should().Be("pass");
    }

    /// <summary>
    /// Verifies that when all registered validators pass, <c>next</c> is invoked and its
    /// response is returned unchanged.
    /// </summary>
    [Fact]
    public async Task Handle_WithPassingValidator_InvokesNext()
    {
        // Arrange
        IValidator<ValidationTestRequest> validator = new AlwaysPassValidator();
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");
        var sut = new ValidationBehavior<ValidationTestRequest, string>([validator]);

        // Act
        string result = await sut.Handle(new ValidationTestRequest("valid"), next, CancellationToken.None);

        // Assert
        result.Should().Be("ok");
    }

    // ── Validation failure ────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when a validator reports failures a <see cref="ValidationException"/>
    /// is thrown and <c>next</c> is not invoked.
    /// </summary>
    [Fact]
    public async Task Handle_WithFailingValidator_ThrowsValidationException()
    {
        // Arrange
        bool nextCalled = false;
        RequestHandlerDelegate<string> next = _ => { nextCalled = true; return Task.FromResult("unreachable"); };

        IValidator<ValidationTestRequest> validator = new AlwaysFailValidator("Value", "must not be empty");
        var sut = new ValidationBehavior<ValidationTestRequest, string>([validator]);

        // Act
        Func<Task> act = () => sut.Handle(new ValidationTestRequest(""), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
        nextCalled.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that failures from multiple validators are all aggregated into a single
    /// <see cref="ValidationException"/> (not just the first validator's failures).
    /// </summary>
    [Fact]
    public async Task Handle_WithMultipleValidators_AggregatesAllFailures()
    {
        // Arrange
        RequestHandlerDelegate<string> next = _ => Task.FromResult("unreachable");

        IValidator<ValidationTestRequest> v1 = new AlwaysFailValidator("Value", "error A");
        IValidator<ValidationTestRequest> v2 = new AlwaysFailValidator("Value", "error B");
        var sut = new ValidationBehavior<ValidationTestRequest, string>([v1, v2]);

        // Act
        Func<Task> act = () => sut.Handle(new ValidationTestRequest("bad"), next, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().HaveCount(2);
        ex.Which.Errors.Select(e => e.ErrorMessage).Should().Contain("error A").And.Contain("error B");
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the cancellation token is forwarded to each validator's
    /// <c>ValidateAsync</c> call.
    /// </summary>
    [Fact]
    public async Task Handle_WithCancellation_PropagatesToken()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var validator = new CapturingValidator();

        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");
        var sut = new ValidationBehavior<ValidationTestRequest, string>([validator]);

        // Act
        await sut.Handle(new ValidationTestRequest("x"), next, cts.Token);

        // Assert
        validator.CapturedToken.Should().Be(cts.Token);
    }

    // ── Null guards ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that passing <see langword="null"/> to the constructor throws
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void Constructor_NullValidators_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => _ = new ValidationBehavior<ValidationTestRequest, string>(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("validators");
    }

    /// <summary>
    /// Verifies that passing a <see langword="null"/> <c>next</c> delegate throws
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_NullNext_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = new ValidationBehavior<ValidationTestRequest, string>([]);

        // Act
        Func<Task> act = () => sut.Handle(new ValidationTestRequest("x"), null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("next");
    }
}


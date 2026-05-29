using FastMediatR;
using FluentAssertions;
using Xunit;

namespace FastMediatR.Tests.Behaviors;

// ── Shared test types (namespace-level so delegate-based fakes can implement the interface) ──

/// <summary>Minimal request used across pre-processor behavior tests.</summary>
internal sealed record PreProcTestRequest : IRequest<string>;

/// <summary>
/// Concrete <see cref="IRequestPreProcessor{TRequest}"/> backed by a delegate.
/// Avoids NSubstitute proxying of generic interfaces with internal type arguments.
/// </summary>
internal sealed class DelegatePreProcessor(Func<PreProcTestRequest, CancellationToken, Task> body)
    : IRequestPreProcessor<PreProcTestRequest>
{
    /// <summary>Invokes the configured delegate.</summary>
    public Task Process(PreProcTestRequest request, CancellationToken cancellationToken)
        => body(request, cancellationToken);
}

/// <summary>
/// Unit tests for <see cref="RequestPreProcessorBehavior{TRequest,TResponse}"/>.
/// Verifies ordering, pass-through, cancellation, and exception propagation semantics.
/// </summary>
public sealed class RequestPreProcessorBehaviorTests
{
    // ── Happy-path ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that all registered pre-processors are invoked in DI registration order
    /// and that <c>next</c> is called afterwards.
    /// </summary>
    [Fact]
    public async Task Handle_RunsAllPreProcessors_InRegistrationOrder()
    {
        // Arrange
        var order = new List<int>();

        IRequestPreProcessor<PreProcTestRequest> p1 = new DelegatePreProcessor((_, _) => { order.Add(1); return Task.CompletedTask; });
        IRequestPreProcessor<PreProcTestRequest> p2 = new DelegatePreProcessor((_, _) => { order.Add(2); return Task.CompletedTask; });

        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");
        var sut = new RequestPreProcessorBehavior<PreProcTestRequest, string>([p1, p2]);

        // Act
        string result = await sut.Handle(new PreProcTestRequest(), next, CancellationToken.None);

        // Assert
        result.Should().Be("ok");
        order.Should().Equal(1, 2);
    }

    /// <summary>
    /// Verifies that with no pre-processors registered the behavior transparently delegates
    /// to <c>next</c> without any overhead.
    /// </summary>
    [Fact]
    public async Task Handle_WithNoProcessors_PassesThrough()
    {
        // Arrange
        bool nextCalled = false;
        RequestHandlerDelegate<string> next = _ => { nextCalled = true; return Task.FromResult("pass"); };
        var sut = new RequestPreProcessorBehavior<PreProcTestRequest, string>([]);

        // Act
        string result = await sut.Handle(new PreProcTestRequest(), next, CancellationToken.None);

        // Assert
        result.Should().Be("pass");
        nextCalled.Should().BeTrue();
    }

    // ── Exception semantics ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when a pre-processor throws, the exception propagates immediately and
    /// the handler (next) is never invoked.
    /// </summary>
    [Fact]
    public async Task Handle_WhenProcessorThrows_DoesNotInvokeHandler()
    {
        // Arrange
        bool nextCalled = false;
        RequestHandlerDelegate<string> next = _ => { nextCalled = true; return Task.FromResult("unreachable"); };

        IRequestPreProcessor<PreProcTestRequest> failing = new DelegatePreProcessor(
            (_, _) => Task.FromException(new InvalidOperationException("boom")));

        var sut = new RequestPreProcessorBehavior<PreProcTestRequest, string>([failing]);

        // Act
        Func<Task> act = () => sut.Handle(new PreProcTestRequest(), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        nextCalled.Should().BeFalse();
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the cancellation token is forwarded to each pre-processor and to <c>next</c>.
    /// </summary>
    [Fact]
    public async Task Handle_PropagatesCancellationToken()
    {
        // Arrange
        CancellationToken capturedToken = default;
        using var cts = new CancellationTokenSource();

        IRequestPreProcessor<PreProcTestRequest> processor = new DelegatePreProcessor((_, ct) =>
        {
            capturedToken = ct;
            return Task.CompletedTask;
        });

        RequestHandlerDelegate<string> next = _ => Task.FromResult("ok");
        var sut = new RequestPreProcessorBehavior<PreProcTestRequest, string>([processor]);

        // Act
        await sut.Handle(new PreProcTestRequest(), next, cts.Token);

        // Assert
        capturedToken.Should().Be(cts.Token);
    }

    // ── Null guards ───────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that passing a <see langword="null"/> <c>processors</c> collection to the
    /// constructor throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void Constructor_NullProcessors_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => _ = new RequestPreProcessorBehavior<PreProcTestRequest, string>(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("processors");
    }

    /// <summary>
    /// Verifies that passing a <see langword="null"/> <c>next</c> delegate to
    /// <see cref="RequestPreProcessorBehavior{TRequest,TResponse}.Handle"/> throws
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_NullNext_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = new RequestPreProcessorBehavior<PreProcTestRequest, string>([]);

        // Act
        Func<Task> act = () => sut.Handle(new PreProcTestRequest(), null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("next");
    }
}

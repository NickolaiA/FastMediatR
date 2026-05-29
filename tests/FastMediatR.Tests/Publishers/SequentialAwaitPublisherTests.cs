using FastMediatR;
using FluentAssertions;
using Xunit;

namespace FastMediatR.Tests.Publishers;

/// <summary>
/// Unit tests for <see cref="SequentialAwaitPublisher"/>.
/// Verifies in-order execution, exception propagation that stops subsequent handlers, and
/// cancellation between handlers.
/// </summary>
public sealed class SequentialAwaitPublisherTests
{
    // ── Test stubs ────────────────────────────────────────────────────────────

    /// <summary>Minimal notification used across all tests.</summary>
    private sealed record TestNotification : INotification;

    /// <summary>Creates a <see cref="NotificationHandlerExecutor"/> that invokes the given delegate.</summary>
    /// <param name="handler">The handler body to wrap.</param>
    /// <returns>A <see cref="NotificationHandlerExecutor"/> wrapping <paramref name="handler"/>.</returns>
    private static NotificationHandlerExecutor MakeExecutor(Func<INotification, CancellationToken, Task> handler)
        => new(new object(), handler);

    private static readonly SequentialAwaitPublisher Sut = new();

    // ── Happy-path ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that publishing with an empty handler list completes without throwing.
    /// </summary>
    [Fact]
    public async Task Publish_WithNoHandlers_CompletesSuccessfully()
    {
        // Act
        Func<Task> act = () => Sut.Publish([], new TestNotification(), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    /// <summary>
    /// Verifies that multiple handlers are invoked in registration order.
    /// </summary>
    [Fact]
    public async Task Publish_WithMultipleHandlers_InvokesInOrder()
    {
        // Arrange
        var order = new List<int>();
        var executors = new[]
        {
            MakeExecutor((_, _) => { order.Add(1); return Task.CompletedTask; }),
            MakeExecutor((_, _) => { order.Add(2); return Task.CompletedTask; }),
            MakeExecutor((_, _) => { order.Add(3); return Task.CompletedTask; }),
        };

        // Act
        await Sut.Publish(executors, new TestNotification(), CancellationToken.None);

        // Assert
        order.Should().Equal(1, 2, 3);
    }

    // ── Exception semantics ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when a handler throws, the exception propagates immediately (unwrapped)
    /// and subsequent handlers are not invoked.
    /// </summary>
    [Fact]
    public async Task Publish_WhenHandlerThrows_StopsExecution_AndPropagatesUnwrapped()
    {
        // Arrange
        bool secondInvoked = false;
        var executors = new[]
        {
            MakeExecutor((_, _) => Task.FromException(new InvalidOperationException("stop"))),
            MakeExecutor((_, _) => { secondInvoked = true; return Task.CompletedTask; }),
        };

        // Act
        Func<Task> act = () => Sut.Publish(executors, new TestNotification(), CancellationToken.None);

        // Assert — propagated as a plain InvalidOperationException, NOT AggregateException.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("stop");
        secondInvoked.Should().BeFalse();
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that if the cancellation token is already cancelled before <c>Publish</c>
    /// is called, no handlers are invoked and <see cref="OperationCanceledException"/> is
    /// thrown immediately.
    /// </summary>
    [Fact]
    public async Task Publish_WhenAlreadyCancelled_ThrowsImmediately_WithNoHandlerInvoked()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        bool handlerInvoked = false;
        var executors = new[]
        {
            MakeExecutor((_, _) => { handlerInvoked = true; return Task.CompletedTask; }),
        };

        // Act
        Func<Task> act = () => Sut.Publish(executors, new TestNotification(), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        handlerInvoked.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that cancelling the token between handler invocations stops execution
    /// and propagates <see cref="OperationCanceledException"/>.
    /// </summary>
    [Fact]
    public async Task Publish_RespectsCancellationBetweenHandlers()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        bool secondInvoked = false;

        var executors = new[]
        {
            MakeExecutor((_, _) => { cts.Cancel(); return Task.CompletedTask; }),
            MakeExecutor((_, _) => { secondInvoked = true; return Task.CompletedTask; }),
        };

        // Act
        Func<Task> act = () => Sut.Publish(executors, new TestNotification(), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        secondInvoked.Should().BeFalse();
    }

    // ── Null guards ───────────────────────────────────────────────────────────

    /// <summary>Verifies that a <see langword="null"/> handlers list throws <see cref="ArgumentNullException"/>.</summary>
    [Fact]
    public async Task Publish_NullHandlers_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = () => Sut.Publish(null!, new TestNotification(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("handlers");
    }

    /// <summary>Verifies that a <see langword="null"/> notification throws <see cref="ArgumentNullException"/>.</summary>
    [Fact]
    public async Task Publish_NullNotification_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = () => Sut.Publish([], null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("notification");
    }
}

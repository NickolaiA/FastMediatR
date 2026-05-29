using FastMediatR;
using FluentAssertions;
using Xunit;

namespace FastMediatR.Tests.Publishers;

/// <summary>
/// Unit tests for <see cref="TaskWhenAllPublisher"/>.
/// Verifies concurrent execution, empty-handler semantics, and that all handler
/// exceptions are aggregated into an <see cref="AggregateException"/>.
/// </summary>
public sealed class TaskWhenAllPublisherTests
{
    // ── Test stubs ────────────────────────────────────────────────────────────

    /// <summary>Minimal notification used across all tests.</summary>
    private sealed record TestNotification : INotification;

    /// <summary>Creates a <see cref="NotificationHandlerExecutor"/> that invokes the given delegate.</summary>
    /// <param name="handler">The handler body to wrap.</param>
    /// <returns>A <see cref="NotificationHandlerExecutor"/> wrapping <paramref name="handler"/>.</returns>
    private static NotificationHandlerExecutor MakeExecutor(Func<INotification, CancellationToken, Task> handler)
        => new(new object(), handler);

    private static readonly TaskWhenAllPublisher Sut = new();

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
    /// Verifies that a single registered handler is invoked.
    /// </summary>
    [Fact]
    public async Task Publish_WithOneHandler_InvokesIt()
    {
        // Arrange
        bool invoked = false;
        var executor = MakeExecutor((_, _) => { invoked = true; return Task.CompletedTask; });

        // Act
        await Sut.Publish([executor], new TestNotification(), CancellationToken.None);

        // Assert
        invoked.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that all handlers are invoked when multiple are registered. Execution order
    /// is unspecified (concurrent), but all handlers must complete.
    /// </summary>
    [Fact]
    public async Task Publish_WithMultipleHandlers_InvokesAll()
    {
        // Arrange
        var invoked = new HashSet<int>();
        var executors = Enumerable.Range(1, 3)
            .Select(i => MakeExecutor((_, _) => { lock (invoked) { invoked.Add(i); } return Task.CompletedTask; }))
            .ToList();

        // Act
        await Sut.Publish(executors, new TestNotification(), CancellationToken.None);

        // Assert
        invoked.Should().BeEquivalentTo([1, 2, 3]);
    }

    // ── Exception aggregation ─────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when one handler throws an <see cref="AggregateException"/> is propagated.
    /// </summary>
    [Fact]
    public async Task Publish_WhenOneHandlerThrows_ThrowsAggregateException()
    {
        // Arrange
        var executor = MakeExecutor((_, _) => Task.FromException(new InvalidOperationException("fail")));

        // Act
        Func<Task> act = () => Sut.Publish([executor], new TestNotification(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AggregateException>();
    }

    /// <summary>
    /// Verifies that when multiple handlers throw, all exceptions are captured inside a
    /// single <see cref="AggregateException"/> (not just the first one).
    /// </summary>
    [Fact]
    public async Task Publish_WhenMultipleHandlersThrow_AggregatesAllExceptions()
    {
        // Arrange
        var executors = new[]
        {
            MakeExecutor((_, _) => Task.FromException(new InvalidOperationException("error A"))),
            MakeExecutor((_, _) => Task.FromException(new ArgumentException("error B"))),
        };

        // Act
        Func<Task> act = () => Sut.Publish(executors, new TestNotification(), CancellationToken.None);

        // Assert
        var aggEx = await act.Should().ThrowAsync<AggregateException>();
        aggEx.Which.InnerExceptions.Should().HaveCount(2);
        aggEx.Which.InnerExceptions.Select(e => e.Message)
            .Should().Contain("error A").And.Contain("error B");
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that the cancellation token is forwarded to every handler delegate.
    /// </summary>
    [Fact]
    public async Task Publish_ForwardsCancellationTokenToHandlers()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        CancellationToken capturedToken = default;

        var executor = MakeExecutor((_, ct) =>
        {
            capturedToken = ct;
            return Task.CompletedTask;
        });

        // Act
        await Sut.Publish([executor], new TestNotification(), cts.Token);

        // Assert
        capturedToken.Should().Be(cts.Token);
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

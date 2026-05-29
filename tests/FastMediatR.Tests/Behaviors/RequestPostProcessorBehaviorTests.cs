using FastMediatR;
using FluentAssertions;
using Xunit;

namespace FastMediatR.Tests.Behaviors;

// ── Shared test types ─────────────────────────────────────────────────────────

/// <summary>Minimal request used across post-processor behavior tests.</summary>
internal sealed record PostProcTestRequest : IRequest<string>;

/// <summary>
/// Concrete <see cref="IRequestPostProcessor{TRequest,TResponse}"/> backed by a delegate.
/// Avoids NSubstitute proxying of generic interfaces with internal type arguments.
/// </summary>
internal sealed class DelegatePostProcessor(Func<PostProcTestRequest, string, CancellationToken, Task> body)
    : IRequestPostProcessor<PostProcTestRequest, string>
{
    /// <summary>Tracks how many times this processor was invoked.</summary>
    public int InvocationCount { get; private set; }

    /// <summary>Invokes the configured delegate and increments <see cref="InvocationCount"/>.</summary>
    public Task Process(PostProcTestRequest request, string response, CancellationToken cancellationToken)
    {
        InvocationCount++;
        return body(request, response, cancellationToken);
    }
}

/// <summary>
/// Unit tests for <see cref="RequestPostProcessorBehavior{TRequest,TResponse}"/>.
/// Verifies that post-processors run after the handler, ordering is preserved, and
/// exceptions from the handler stop post-processor execution.
/// </summary>
public sealed class RequestPostProcessorBehaviorTests
{
    // ── Happy-path ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that all registered post-processors are invoked after the handler completes,
    /// in DI registration order, and that the original handler response is returned.
    /// </summary>
    [Fact]
    public async Task Handle_RunsAllPostProcessors_AfterHandler()
    {
        // Arrange
        var order = new List<string>();

        RequestHandlerDelegate<string> next = _ => { order.Add("handler"); return Task.FromResult("result"); };

        IRequestPostProcessor<PostProcTestRequest, string> p1 =
            new DelegatePostProcessor((_, _, _) => { order.Add("p1"); return Task.CompletedTask; });
        IRequestPostProcessor<PostProcTestRequest, string> p2 =
            new DelegatePostProcessor((_, _, _) => { order.Add("p2"); return Task.CompletedTask; });

        var sut = new RequestPostProcessorBehavior<PostProcTestRequest, string>([p1, p2]);

        // Act
        string result = await sut.Handle(new PostProcTestRequest(), next, CancellationToken.None);

        // Assert
        result.Should().Be("result");
        order.Should().Equal("handler", "p1", "p2");
    }

    /// <summary>
    /// Verifies that with no post-processors registered the behavior transparently delegates
    /// to <c>next</c> and returns the handler response unchanged.
    /// </summary>
    [Fact]
    public async Task Handle_WithNoProcessors_PassesThrough()
    {
        // Arrange
        RequestHandlerDelegate<string> next = _ => Task.FromResult("pass");
        var sut = new RequestPostProcessorBehavior<PostProcTestRequest, string>([]);

        // Act
        string result = await sut.Handle(new PostProcTestRequest(), next, CancellationToken.None);

        // Assert
        result.Should().Be("pass");
    }

    // ── Exception semantics ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when the handler (<c>next</c>) throws, the exception propagates and no
    /// post-processors are executed.
    /// </summary>
    [Fact]
    public async Task Handle_WhenHandlerThrows_DoesNotRunPostProcessors()
    {
        // Arrange
        RequestHandlerDelegate<string> next = _ => Task.FromException<string>(new InvalidOperationException("handler fail"));

        var postProcessor = new DelegatePostProcessor((_, _, _) => Task.CompletedTask);
        var sut = new RequestPostProcessorBehavior<PostProcTestRequest, string>([postProcessor]);

        // Act
        Func<Task> act = () => sut.Handle(new PostProcTestRequest(), next, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler fail");
        postProcessor.InvocationCount.Should().Be(0);
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
        Action act = () => _ = new RequestPostProcessorBehavior<PostProcTestRequest, string>(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("processors");
    }

    /// <summary>
    /// Verifies that passing a <see langword="null"/> <c>next</c> delegate throws
    /// <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public async Task Handle_NullNext_ThrowsArgumentNullException()
    {
        // Arrange
        var sut = new RequestPostProcessorBehavior<PostProcTestRequest, string>([]);

        // Act
        Func<Task> act = () => sut.Handle(new PostProcTestRequest(), null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("next");
    }
}
